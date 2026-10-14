using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CloudpunkCoop.Protocol
{
    /// <summary>Lifecycle state of a <see cref="CoopConnection"/>.</summary>
    public enum ConnectionState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Closing = 3,
        Closed = 4,
        Faulted = 5,
    }

    /// <summary>
    /// Why a connection stopped, so a caller can tell a clean BYE from a dropped socket.
    /// </summary>
    public sealed class ConnectionClosedEventArgs : EventArgs
    {
        public ConnectionClosedEventArgs(ConnectionState state, string reason, Exception? error)
        {
            State = state;
            Reason = reason;
            Error = error;
        }

        /// <summary>State the connection settled into.</summary>
        public ConnectionState State { get; }

        /// <summary>Human readable cause, never null.</summary>
        public string Reason { get; }

        /// <summary>The socket or protocol error that ended the connection, if any.</summary>
        public Exception? Error { get; }

        /// <summary>True when the link ended without an error, for example on a BYE.</summary>
        public bool WasClean => Error == null;
    }

    /// <summary>
    /// One TCP link to a peer, with all socket work on background threads.
    ///
    /// The Unity main thread must never block on a socket, and IL2CPP will not tolerate a
    /// background thread touching UnityEngine objects. So this class does exactly one thing:
    /// it moves decoded protocol messages between the socket and a thread-safe queue. The
    /// caller drains <see cref="TryReceive"/> from the main thread and does all game work
    /// there. Nothing in this file references UnityEngine.
    ///
    /// Reads and writes live on separate threads. A single thread cannot do both correctly:
    /// the only way to notice a blocking read should stop is for the read to return, and
    /// <c>NetworkStream.DataAvailable</c> stays false on a peer that has closed the socket, so
    /// polling it never detects an orderly disconnect. A dedicated blocking reader sees EOF
    /// immediately, and a dedicated writer keeps outbound latency off the read path.
    ///
    /// Shutdown is bounded: <see cref="Dispose"/> closes the socket and joins both workers
    /// with a timeout, so a wedged peer cannot hang the game on exit.
    /// </summary>
    public sealed class CoopConnection : IDisposable
    {
        private const int ReadChunkSize = 8192;

        private readonly TcpClient _client;
        private readonly Thread _readThread;
        private readonly Thread _writeThread;
        private readonly ConcurrentQueue<IMessage> _inbound = new ConcurrentQueue<IMessage>();
        private readonly ConcurrentQueue<byte[]> _outbound = new ConcurrentQueue<byte[]>();
        private readonly ManualResetEventSlim _outboundReady = new ManualResetEventSlim(false);
        private readonly FrameReader _reader;
        private readonly int _maxQueuedMessages;
        private readonly object _stateLock = new object();
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

        private volatile ConnectionState _state = ConnectionState.Connecting;
        private volatile bool _disposed;
        private int _pendingWrites;

        public CoopConnection(
            TcpClient client,
            int maxPayloadSize = ProtocolConstants.DefaultMaxFramePayloadSize,
            int maxQueuedMessages = ProtocolConstants.DefaultMaxQueuedMessages)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _reader = new FrameReader(maxPayloadSize);
            _maxQueuedMessages = maxQueuedMessages;

            _client.NoDelay = true;
            _state = ConnectionState.Connected;

            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "CloudpunkCoop.Transport.Read",
            };
            _writeThread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "CloudpunkCoop.Transport.Write",
            };

            _readThread.Start();
            _writeThread.Start();
        }

        /// <summary>Raised once, on the reader thread, when the connection ends.</summary>
        public event EventHandler<ConnectionClosedEventArgs>? Closed;

        public ConnectionState State => _state;

        /// <summary>True between a successful connect and the start of shutdown.</summary>
        public bool IsConnected => _state == ConnectionState.Connected;

        /// <summary>Messages decoded and waiting for the main thread.</summary>
        public int QueuedMessageCount => _inbound.Count;

        /// <summary>Frames handed to the writer but not yet written.</summary>
        public int PendingSendCount => Volatile.Read(ref _pendingWrites);

        /// <summary>Payloads dropped because the inbound queue was full.</summary>
        public long DroppedMessages { get; private set; }

        /// <summary>
        /// Connects to a host. Blocking, so call it from a thread the game can afford to wait
        /// on; the connect timeout bounds how long it can take.
        /// </summary>
        public static CoopConnection Connect(
            string host,
            int port,
            int connectTimeoutMilliseconds = 8000,
            int maxPayloadSize = ProtocolConstants.DefaultMaxFramePayloadSize)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Host must not be empty.", nameof(host));
            }

            var client = new TcpClient();
            IAsyncResult async = client.BeginConnect(host, port, null, null);
            if (!async.AsyncWaitHandle.WaitOne(connectTimeoutMilliseconds))
            {
                client.Close();
                throw new ProtocolException(
                    ProtocolErrorKind.ConnectTimeout,
                    $"Connecting to {host}:{port} timed out after {connectTimeoutMilliseconds} ms.");
            }

            try
            {
                client.EndConnect(async);
            }
            catch (Exception ex)
            {
                client.Close();
                throw new ProtocolException(
                    ProtocolErrorKind.ConnectionClosed,
                    $"Connecting to {host}:{port} failed: {ex.Message}", ex);
            }

            return new CoopConnection(client, maxPayloadSize);
        }

        /// <summary>Accepts one pending client, blocking until one arrives or the token fires.</summary>
        public static CoopConnection Accept(
            TcpListener listener,
            CancellationToken cancellationToken = default,
            int maxPayloadSize = ProtocolConstants.DefaultMaxFramePayloadSize)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            while (!listener.Pending())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(25);
            }

            TcpClient client = listener.AcceptTcpClient();
            return new CoopConnection(client, maxPayloadSize);
        }

        /// <summary>
        /// Queues a message for delivery. Non-blocking and safe to call from the main thread;
        /// returns false once the connection is no longer usable.
        /// </summary>
        public bool Send(IMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            lock (_stateLock)
            {
                if (_state != ConnectionState.Connected)
                {
                    return false;
                }
            }

            byte[] payload = MessageCodec.Encode(message);
            lock (_stateLock)
            {
                if (_state != ConnectionState.Connected) return false;
                Interlocked.Increment(ref _pendingWrites);
                _outbound.Enqueue(FrameCodec.EncodeFrame(payload));
                _outboundReady.Set();
            }
            return true;
        }

        /// <summary>Queues a BYE and then closes, so the peer sees an orderly disconnect.</summary>
        public void SendByeAndClose(ByeReason reason, int flushTimeoutMilliseconds = 500)
        {
            Send(new ByeMessage(reason));

            // Give the writer a moment to flush the BYE before the socket goes away.
            var deadline = Stopwatch.StartNew();
            while (PendingSendCount > 0 && deadline.ElapsedMilliseconds < flushTimeoutMilliseconds)
            {
                Thread.Sleep(5);
            }

            Close();
        }

        /// <summary>Takes one decoded message. Call from the Unity main thread each frame.</summary>
        public bool TryReceive(out IMessage? message) => _inbound.TryDequeue(out message);

        /// <summary>Drains up to <paramref name="max"/> messages, invoking the callback for each.</summary>
        public int DrainMessages(int max, Action<IMessage> onMessage)
        {
            if (onMessage == null)
            {
                throw new ArgumentNullException(nameof(onMessage));
            }

            int handled = 0;
            while (handled < max && _inbound.TryDequeue(out IMessage? message))
            {
                if (message != null)
                {
                    onMessage(message);
                    handled++;
                }
            }

            return handled;
        }

        /// <summary>Begins an orderly shutdown. Safe to call more than once.</summary>
        public void Close()
        {
            lock (_stateLock)
            {
                if (_state == ConnectionState.Closed || _state == ConnectionState.Closing)
                {
                    return;
                }

                _state = ConnectionState.Closing;
            }

            try
            {
                _shutdown.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already cancelled during Dispose.
            }

            _outboundReady.Set();

            // Closing the socket is what unblocks the reader; a graceful shutdown first lets
            // anything already written reach the peer.
            try
            {
                if (_client.Connected)
                {
                    _client.Client.Shutdown(SocketShutdown.Both);
                }
            }
            catch (Exception)
            {
                // The peer is already gone; closing below is what matters.
            }

            _client.Close();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Close();

            // Bounded waits: a stuck socket must not hold the game open on exit. If a join
            // times out the thread is left to die with the process rather than blocking here.
            if (_readThread.IsAlive)
            {
                _readThread.Join(500);
            }

            if (_writeThread.IsAlive)
            {
                _writeThread.Join(500);
            }

            _outboundReady.Dispose();
            _shutdown.Dispose();

            lock (_stateLock)
            {
                if (_state != ConnectionState.Closed)
                {
                    _state = ConnectionState.Closed;
                }
            }
        }

        /// <summary>
        /// The reader: a blocking <c>Stream.Read</c> loop. EOF (zero bytes) means the peer
        /// closed, which a polling loop on <c>DataAvailable</c> could never observe.
        /// Every exit path reports through <see cref="Closed"/> exactly once.
        /// </summary>
        private void ReadLoop()
        {
            string reason = "peer closed the connection";
            Exception? error = null;
            ConnectionState finalState = ConnectionState.Closed;

            try
            {
                NetworkStream stream = _client.GetStream();
                var chunk = new byte[ReadChunkSize];

                while (true)
                {
                    int read = stream.Read(chunk, 0, chunk.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    _reader.Append(chunk.AsSpan(0, read));

                    bool byeReceived = false;
                    while (_reader.TryReadFrame(out ReadOnlySpan<byte> payload))
                    {
                        if (!MessageCodec.TryDecode(payload, out IMessage? message) || message == null)
                        {
                            throw new ProtocolException(
                                ProtocolErrorKind.UnknownOpcode,
                                "A frame arrived that does not decode to a known message.");
                        }

                        if (message is ByeMessage bye)
                        {
                            // A control message must not be dropped, so it bypasses the
                            // queue-length guard that ordinary traffic goes through.
                            _inbound.Enqueue(bye);
                            reason = "peer sent BYE (" + bye.Reason + ")";
                            byeReceived = true;
                            break;
                        }

                        Enqueue(message);
                    }

                    if (byeReceived)
                    {
                        break;
                    }
                }
            }
            catch (ProtocolException ex)
            {
                error = ex;
                reason = "protocol error: " + ex.Message;
                finalState = ConnectionState.Faulted;
            }
            catch (ObjectDisposedException)
            {
                reason = "connection closed locally";
            }
            catch (Exception ex) when (ex is SocketException || ex is System.IO.IOException)
            {
                // A local Close() tears the socket out from under a blocking read; that is a
                // requested shutdown, not a fault.
                if (_shutdown.IsCancellationRequested)
                {
                    reason = "connection closed locally";
                }
                else
                {
                    error = ex;
                    reason = "socket error: " + ex.Message;
                    finalState = ConnectionState.Faulted;
                }
            }
            catch (Exception ex)
            {
                error = ex;
                reason = "unexpected transport failure: " + ex.Message;
                finalState = ConnectionState.Faulted;
            }

            lock (_stateLock)
            {
                if (_state != ConnectionState.Faulted)
                {
                    _state = finalState;
                }
            }

            Closed?.Invoke(this, new ConnectionClosedEventArgs(_state, reason, error));
        }

        /// <summary>
        /// The writer: waits for queued frames and writes them in order. A send failure is
        /// swallowed here because the reader will observe the same dead socket and is the
        /// single authority on why the connection ended.
        /// </summary>
        private void WriteLoop()
        {
            try
            {
                NetworkStream stream = _client.GetStream();

                while (!_shutdown.IsCancellationRequested)
                {
                    _outboundReady.Wait(50);
                    _outboundReady.Reset();

                    while (_outbound.TryDequeue(out byte[]? frame))
                    {
                        if (frame == null)
                        {
                            continue;
                        }

                        try
                        {
                            stream.Write(frame, 0, frame.Length);
                            stream.Flush();
                        }
                        finally { Interlocked.Decrement(ref _pendingWrites); }
                    }
                }
            }
            catch (Exception)
            {
                // The reader reports the failure; there is nothing to add here and nothing
                // useful to do, since an unhandled exception would take the process down.
            }
        }

        /// <summary>
        /// Queues a decoded message, dropping traffic when the caller is not draining.
        /// Dropping is deliberate: an unbounded queue would let a fast peer exhaust memory in
        /// a process that is also rendering a game.
        /// </summary>
        private void Enqueue(IMessage message)
        {
            if (_inbound.Count >= _maxQueuedMessages)
            {
                DroppedMessages++;
                return;
            }

            _inbound.Enqueue(message);
        }
    }
}
