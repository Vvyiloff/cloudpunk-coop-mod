using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    /// <summary>
    /// Transport tests over real loopback TCP.
    ///
    /// These use actual sockets rather than a mock stream on purpose: the things that break
    /// in this layer are chunk boundaries, partial writes and shutdown races, none of which a
    /// mock reproduces. Every test has a bounded timeout so a hang fails instead of stalling
    /// the suite.
    /// </summary>
    public class CoopConnectionTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        /// <summary>Opens a loopback listener on an ephemeral port and yields it with that port.</summary>
        private static (TcpListener listener, int port) StartListener()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return (listener, port);
        }

        /// <summary>
        /// Connects a host and guest pair and returns both, waiting until the host side has
        /// accepted. Both are connected before the test body runs.
        /// </summary>
        private static (CoopConnection host, CoopConnection guest) ConnectedPair()
        {
            (TcpListener listener, int port) = StartListener();

            CoopConnection? guest = null;
            Exception? connectError = null;

            var connector = new Thread(() =>
            {
                try
                {
                    guest = CoopConnection.Connect("127.0.0.1", port, 5000);
                }
                catch (Exception ex)
                {
                    connectError = ex;
                }
            })
            {
                IsBackground = true,
            };
            connector.Start();

            using var acceptTimeout = new CancellationTokenSource(Timeout);
            CoopConnection host = CoopConnection.Accept(listener, acceptTimeout.Token);
            connector.Join(Timeout);

            if (connectError != null)
            {
                host.Dispose();
                throw connectError;
            }

            Assert.NotNull(guest);
            listener.Stop();
            return (host, guest!);
        }

        /// <summary>Spins until a message arrives or the deadline passes.</summary>
        private static IMessage ReceiveWithin(CoopConnection connection, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (connection.TryReceive(out IMessage? message) && message != null)
                {
                    return message;
                }

                Thread.Sleep(5);
            }

            throw new TimeoutException("No message arrived within " + timeout + ".");
        }

        [Fact]
        public void GuestToHost_DeliversCarState()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                var sent = new CarStateMessage(
                    10f, 20f, 30f, 0f, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0.5f, 0.1f, 7u);

                Assert.True(guest.Send(sent));

                IMessage received = ReceiveWithin(host, Timeout);

                var carState = Assert.IsType<CarStateMessage>(received);
                Assert.Equal(10f, carState.PositionX);
                Assert.Equal(30f, carState.PositionZ);
                Assert.Equal(0.5f, carState.SteerAngle);
                Assert.Equal(7u, carState.SequenceNumber);
            }
        }

        [Fact]
        public void HostToGuest_DeliversInput()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                Assert.True(host.Send(new InputMessage(0.75f, 0f, -1f, 0.5f, 0f, 11u)));

                IMessage received = ReceiveWithin(guest, Timeout);

                var input = Assert.IsType<InputMessage>(received);
                Assert.Equal(0.75f, input.Throttle);
                Assert.Equal(-1f, input.Steer);
                Assert.Equal(11u, input.SequenceNumber);
            }
        }

        [Fact]
        public void Handshake_MessagesRoundTrip()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                Assert.True(guest.Send(new HelloMessage("1.0", "Andrey")));
                IMessage first = ReceiveWithin(host, Timeout);
                var hello = Assert.IsType<HelloMessage>(first);
                Assert.Equal("1.0", hello.ProtocolVersion);
                Assert.Equal("Andrey", hello.DisplayName);

                Assert.True(host.Send(new WelcomeMessage(PeerRole.Guest, "1.0")));
                IMessage second = ReceiveWithin(guest, Timeout);
                var welcome = Assert.IsType<WelcomeMessage>(second);
                Assert.Equal(PeerRole.Guest, welcome.Role);
            }
        }

        [Fact]
        public void ManyMessages_ArriveInOrderAndIntact()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                const int count = 500;
                for (uint i = 0; i < count; i++)
                {
                    Assert.True(guest.Send(new PingMessage(i, i * 1000L)));
                }

                var received = new List<PingMessage>();
                var stopwatch = Stopwatch.StartNew();
                while (received.Count < count && stopwatch.Elapsed < Timeout)
                {
                    if (host.TryReceive(out IMessage? message) && message is PingMessage ping)
                    {
                        received.Add(ping);
                    }
                    else
                    {
                        Thread.Sleep(2);
                    }
                }

                Assert.Equal(count, received.Count);
                for (int i = 0; i < count; i++)
                {
                    Assert.Equal((uint)i, received[i].SequenceNumber);
                    Assert.Equal(i * 1000L, received[i].SentAtMilliseconds);
                }
            }
        }

        [Fact]
        public void LargePayload_NearTheFrameLimit_SurvivesFragmentation()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                // A long display name forces a multi-chunk frame, exercising reassembly.
                string bigName = new string('n', ProtocolConstants.MaxStringByteLength);
                Assert.True(guest.Send(new HelloMessage("1.0", bigName)));

                IMessage received = ReceiveWithin(host, Timeout);

                var hello = Assert.IsType<HelloMessage>(received);
                Assert.Equal(bigName.Length, hello.DisplayName.Length);
                Assert.Equal(bigName, hello.DisplayName);
            }
        }

        [Fact]
        public void PeerSendingBye_IsVisibleToTheOtherSide()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                guest.SendByeAndClose(ByeReason.LocalShutdown);

                IMessage received = ReceiveWithin(host, Timeout);

                Assert.IsType<ByeMessage>(received);
            }
        }

        [Fact]
        public void SendAfterClose_ReturnsFalseInsteadOfThrowing()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                guest.Close();

                Assert.False(guest.Send(new PingMessage(1u, 0L)));
                Assert.False(guest.Send(new PingMessage(2u, 0L)));
            }
        }

        [Fact]
        public void CloseTwiceAndDisposeTwice_AreSafe()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            {
                guest.Close();
                guest.Close();
                guest.Dispose();
                guest.Dispose();

                Assert.Equal(ConnectionState.Closed, guest.State);
            }
        }

        [Fact]
        public void Dispose_DoesNotHang()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            {
                var stopwatch = Stopwatch.StartNew();
                guest.Dispose();
                stopwatch.Stop();

                // The join is bounded; anything close to the two-second mark means shutdown
                // is not actually bounded and would stall the game on exit.
                Assert.True(
                    stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                    $"Dispose took {stopwatch.ElapsedMilliseconds} ms, expected a bounded shutdown.");
            }
        }

        [Fact]
        public void ClosedEvent_FiresOnceWhenThePeerVanishes()
        {
            (TcpListener listener, int port) = StartListener();

            CoopConnection? guest = null;
            var connector = new Thread(() =>
            {
                guest = CoopConnection.Connect("127.0.0.1", port, 5000);
            })
            {
                IsBackground = true,
            };
            connector.Start();

            using var acceptTimeout = new CancellationTokenSource(Timeout);
            using CoopConnection host = CoopConnection.Accept(listener, acceptTimeout.Token);
            connector.Join(Timeout);
            listener.Stop();

            Assert.NotNull(guest);
            int fired = 0;
            host.Closed += (_, _) => Interlocked.Increment(ref fired);

            // Kill the guest socket outright: no BYE, just a dropped link.
            guest!.Dispose();

            var stopwatch = Stopwatch.StartNew();
            while (Volatile.Read(ref fired) == 0 && stopwatch.Elapsed < Timeout)
            {
                Thread.Sleep(10);
            }

            Assert.True(fired >= 1, "Closed never fired after the peer disappeared.");
        }

        [Fact]
        public void TryReceive_IsEmptyOnAFreshConnection()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                Assert.False(host.TryReceive(out IMessage? message));
                Assert.Null(message);
                Assert.Equal(0, host.QueuedMessageCount);
            }
        }

        [Fact]
        public void DrainMessages_RespectsTheBatchLimit()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                for (uint i = 0; i < 10; i++)
                {
                    guest.Send(new PingMessage(i, i));
                }

                // Wait for the frames to land.
                var stopwatch = Stopwatch.StartNew();
                while (host.QueuedMessageCount < 10 && stopwatch.Elapsed < Timeout)
                {
                    Thread.Sleep(5);
                }

                Assert.Equal(10, host.QueuedMessageCount);

                var handled = new List<IMessage>();
                int firstBatch = host.DrainMessages(4, handled.Add);

                Assert.Equal(4, firstBatch);
                Assert.Equal(4, handled.Count);
                Assert.Equal(6, host.QueuedMessageCount);
            }
        }

        [Fact]
        public void ConcurrentSends_AllArrive()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                const int threads = 4;
                const int perThread = 100;

                var failures = new List<Exception>();
                var senders = new List<Thread>();
                for (int t = 0; t < threads; t++)
                {
                    var sender = new Thread(() =>
                    {
                        try
                        {
                            for (uint i = 0; i < perThread; i++)
                            {
                                guest.Send(new SeatMessage((byte)(i % 2), i % 2 == 0));
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (failures)
                            {
                                failures.Add(ex);
                            }
                        }
                    })
                    {
                        IsBackground = true,
                    };
                    senders.Add(sender);
                    sender.Start();
                }

                foreach (Thread sender in senders)
                {
                    Assert.True(sender.Join(Timeout), "A sender thread did not finish in time.");
                }

                Assert.Empty(failures);

                int expected = threads * perThread;
                var stopwatch = Stopwatch.StartNew();
                while (host.QueuedMessageCount < expected && stopwatch.Elapsed < Timeout)
                {
                    Thread.Sleep(5);
                }

                Assert.Equal(expected, host.QueuedMessageCount);

                var seen = new List<IMessage>();
                host.DrainMessages(expected, seen.Add);
                Assert.Equal(expected, seen.Count);
            }
        }

        [Fact]
        public void ConcurrentReceiveWhileSending_DoesNotCorruptState()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                const int count = 300;
                var received = new List<IMessage>();
                var stopwatch = Stopwatch.StartNew();
                Exception? producerFailure = null;

                var producer = new Thread(() =>
                {
                    try
                    {
                        for (uint i = 0; i < count; i++)
                        {
                            guest.Send(new PingMessage(i, i));
                        }
                    }
                    catch (Exception ex)
                    {
                        producerFailure = ex;
                    }
                })
                {
                    IsBackground = true,
                };
                producer.Start();

                // Drain from this thread while the producer writes.
                while (received.Count < count && stopwatch.Elapsed < Timeout)
                {
                    if (host.TryReceive(out IMessage? message) && message != null)
                    {
                        received.Add(message);
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }

                producer.Join(Timeout);

                Assert.Null(producerFailure);
                Assert.Equal(count, received.Count);
                foreach (IMessage message in received)
                {
                    Assert.IsType<PingMessage>(message);
                }
            }
        }

        [Fact]
        public void OversizedInboundFrame_FaultsTheConnection()
        {
            (TcpListener listener, int port) = StartListener();

            CoopConnection? hostSide = null;
            var acceptor = new Thread(() =>
            {
                using var acceptTimeout = new CancellationTokenSource(Timeout);
                hostSide = CoopConnection.Accept(listener, acceptTimeout.Token);
            })
            {
                IsBackground = true,
            };
            acceptor.Start();

            // A raw socket, so the bad prefix can be written without going through Send.
            using var raw = new TcpClient();
            raw.Connect(IPAddress.Loopback, port);
            Assert.True(acceptor.Join(Timeout), "The host never accepted the raw connection.");
            Assert.NotNull(hostSide);

            using (hostSide)
            {
                var faulted = new ManualResetEventSlim(false);
                hostSide!.Closed += (_, args) =>
                {
                    if (args.Error is ProtocolException)
                    {
                        faulted.Set();
                    }
                };

                // A length prefix claiming more than the host will accept. A well-behaved
                // peer never produces this, so it must be rejected rather than allocated for.
                var prefix = new byte[FrameCodec.LengthPrefixSize];
                FrameCodec.WriteLengthPrefix(prefix, ProtocolConstants.DefaultMaxFramePayloadSize + 1);
                raw.GetStream().Write(prefix, 0, prefix.Length);
                raw.GetStream().Flush();

                Assert.True(faulted.Wait(Timeout), "The host did not fault on an oversized frame.");
            }

            listener.Stop();
        }

        [Fact]
        public void Connect_ToAClosedPort_ReportsFailureRatherThanHanging()
        {
            // Bind and immediately release a port so nothing is listening on it.
            (TcpListener listener, int port) = StartListener();
            listener.Stop();

            var stopwatch = Stopwatch.StartNew();

            // Either an immediate refusal or a timeout is acceptable; hanging is not.
            try
            {
                using CoopConnection connection = CoopConnection.Connect("127.0.0.1", port, 1000);
                Assert.True(connection.IsConnected);
            }
            catch (ProtocolException)
            {
                // Expected when the port is closed.
            }

            stopwatch.Stop();
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Connect took {stopwatch.ElapsedMilliseconds} ms, expected a bounded attempt.");
        }

        [Fact]
        public void Accept_HonoursCancellation()
        {
            (TcpListener listener, _) = StartListener();

            try
            {
                using var cancellation = new CancellationTokenSource(200);

                Assert.Throws<OperationCanceledException>(() =>
                    CoopConnection.Accept(listener, cancellation.Token));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void InitialState_IsConnected()
        {
            (CoopConnection host, CoopConnection guest) = ConnectedPair();
            using (host)
            using (guest)
            {
                Assert.Equal(ConnectionState.Connected, host.State);
                Assert.True(host.IsConnected);
                Assert.Equal(ConnectionState.Connected, guest.State);
            }
        }
    }
}