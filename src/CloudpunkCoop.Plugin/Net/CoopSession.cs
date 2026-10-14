using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using BepInEx.Logging;
using CloudpunkCoop.Game;
using CloudpunkCoop.Protocol;
using CloudpunkCoop.UI;
using UnityEngine;

namespace CloudpunkCoop.Net
{
    /// <summary>What this process is doing in a session.</summary>
    public enum CoopRole
    {
        Off = 0,
        Host = 1,
        Guest = 2,
    }

    /// <summary>
    /// Owns the one network link and the two things that flow over it.
    ///
    /// Socket acceptance/connection runs on one lifecycle worker, which publishes pending
    /// links only. Pump adopts links and resets native presentation on the Unity main thread.
    /// CoopConnection owns the socket reader/writer and never touches the game.
    ///
    /// Host: listens, accepts one guest, and streams the authoritative car pose at 20 Hz from
    /// the real <c>PlayerCar</c> Rigidbody. Guest: connects, sends HELLO, and keeps the last
    /// pose it received.
    ///
    /// Physics authority never moves. Only input will move, and that is iteration 7.
    /// </summary>
    public static class CoopSession
    {
        /// <summary>Bumped whenever the wire format changes in a way a peer must notice.</summary>
        public const string ProtocolVersion = ProtocolConstants.ProtocolVersion;

        /// <summary>Snapshot rate the host streams at. The plan calls for 19-21 Hz.</summary>
        private const float SnapshotIntervalSeconds = 1f / 20f;

        /// <summary>Messages drained per frame. Bounded so a burst cannot stall a render tick.</summary>
        private const int MaxMessagesPerFrame = 64;

        private static volatile CoopConnection _connection;
        private static TcpListener _listener;
        private static Thread _acceptThread;
        private static Thread _connectThread;
        private static readonly object TransportGate = new object();
        private static long _lifecycleGeneration;
        private static CoopConnection _pendingConnection;
        private static bool _retiringConnection;
        private static string _workerStatus, _closedReason;
        private static string _guestHost;
        private static int _guestPort;
        private const int RetryMilliseconds = 3000;
        private static float _handshakeDeadline;
        private static CoopRole _role = CoopRole.Off;
        private static string _status = "off";
        private static float _nextSnapshotAt;
        private static uint _snapshotSequence;
        private static CarStateMessage _remoteCar;
        private static bool _haveRemoteCar;
        private static bool _peerAccepted;
        private static bool _helloWaiting;
        private static bool _guestPresentationReady;
        private static bool _reportedReady;
        private static SeatExchange _seatAuthority = new SeatExchange();
        public static SeatStateMessage Seats { get; private set; } = new SeatStateMessage(0, 1, 0, false, 0);
        private static float _nextSeatStateAt, _nextInputAt;
        private static uint _driverInputSequence;
        public static long DriverInputsSent { get; private set; }
        public static long NonzeroDriverInputsSent { get; private set; }
        public static bool PeerOnline => IsConnected && _peerAccepted;
        public static bool GuestSeated => PeerOnline && !GuestMobility.OnFoot &&
            (_role == CoopRole.Guest ? CarProxy.Ready : _guestPresentationReady);

        public static CoopRole Role => _role;
        public static string Status => _status;

        public static bool IsConnected
        {
            get
            {
                CoopConnection connection = _connection;
                return connection != null && connection.IsConnected;
            }
        }

        internal static void DropTransportForTest()
        {
            if(!Bootstrapper.DiagnosticsEnabled ||
                !string.Equals(Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST"),"true",StringComparison.OrdinalIgnoreCase) ||
                !PeerOnline)throw new InvalidOperationException("An admitted developer test peer is required.");
            // Exercise the real retirement/reconnect worker without Stop(), which
            // intentionally starts a new session and clears its campaign ledger.
            _connection.Close();
        }

        public static bool HaveRemoteCar => _haveRemoteCar;
        internal static void HostCampaignChanging()
        {
            if(_role!=CoopRole.Host)return;
            _peerAccepted=false;_helloWaiting=false;
            _connection?.SendByeAndClose(ByeReason.LocalShutdown);
            CampaignReplication.Reset();
        }
        public static CarStateMessage RemoteCar => _remoteCar;

        public static long SnapshotsSent { get; private set; }
        public static long SnapshotsReceived { get; private set; }
        public static long MessagesReceived { get; private set; }

        /// <summary>Distance the remote car has moved since the previous snapshot, in metres.</summary>
        public static float RemoteStepMetres { get; private set; }

        /// <summary>Snapshot rate actually observed, averaged over the last second.</summary>
        public static float RemoteSnapshotRate { get; private set; }

        private static float _rateWindowStart;
        private static long _rateWindowCount;
        private static Vector3 _lastRemotePosition;

        public static bool StartHost(int port, ManualLogSource log)
        {
            if(CampaignReplication.GuestSaveIsolated)
            {
                _status="Restart the game before hosting: this process has protected guest saves.";
                return false;
            }
            Stop();
            CampaignReplication.Install();

            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
            }
            catch (Exception ex)
            {
                _status = "listen failed: " + ex.Message;
                log.LogError("coop: cannot listen on port " + port + ": " + ex.Message);
                _listener = null;
                return false;
            }

            _role = CoopRole.Host;
            _status = "listening on port " + port;
            log.LogInfo("coop: host listening on port " + port + " (all interfaces)");

            EnsureTransportWorker();
            return true;
        }

        public static bool StartGuest(string host, int port, ManualLogSource log)
        {
            Stop();
            CampaignReplication.Install();

            _role = CoopRole.Guest;
            CampaignReplication.IsolateGuestSave();
            _guestHost = host;
            _guestPort = port;
            _status = "connecting to " + host + ":" + port;
            log.LogInfo("coop: guest connecting to " + host + ":" + port);

            EnsureTransportWorker();
            return true;
        }

        public static void Stop()
        {
            CoopInventory.FlushPersonalEffects(Time.realtimeSinceStartup,true);
            // Invalidate before any native cleanup. A connect completing after this point
            // must dispose its own link and cannot publish into the next lifecycle.
            CoopConnection connection, pending;
            TcpListener listener;
            lock (TransportGate)
            {
                _lifecycleGeneration = checked(_lifecycleGeneration + 1);
                connection = _connection; pending = _pendingConnection; listener = _listener;
                _connection = _pendingConnection = null; _listener = null;
                _workerStatus = _closedReason = null;
                _role = CoopRole.Off;
            }
            if (listener != null) { try { listener.Stop(); } catch (Exception) { } }
            DisposeLink(pending);
            if (connection != null)
            {
                try { connection.SendByeAndClose(ByeReason.LocalShutdown); } catch (Exception) { }
                DisposeLink(connection);
            }

            CampaignReplication.Reset();
            RegionReplication.Reset();
            NativeDrivingInput.Reset();
            IndependentCoop.Reset();
            TrafficReplication.Stop();
            WorldNpcReplication.Stop();
            RemoteFootAvatar.Clear();
            GuestMobility.Reset();
            CarProxy.Stop(BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop"));
            _role = CoopRole.Off;
            _status = "off";
            _haveRemoteCar = false;
            _peerAccepted = false;
            _helloWaiting=false;
            _snapshotSequence = 0;
            _worldSequence=0;_nextWorldAt=0;
            _guestPresentationReady = false;
            _reportedReady = false;
            _seatAuthority = new SeatExchange();
            Seats = new SeatStateMessage(0, 1, 0, false, 0);
            _nextSeatStateAt = _nextInputAt = 0;
            _driverInputSequence = 0;
        }

        /// <summary>Drains inbound messages and, on the host, emits the car snapshot.</summary>
        public static void Pump(float now, ManualLogSource log)
        {
            using var timing=CoopPerformance.Measure(CoopPerformance.Stage.Network);
            MaintainTransport(now, log);
            CoopConnection connection = _connection;
            if (connection == null)
            {
                CampaignReplication.Tick(now);
                WriteStatusFile(now);
                return;
            }

            connection.DrainMessages(MaxMessagesPerFrame, message => Handle(message, log));
            if(_role==CoopRole.Host && _helloWaiting)
            {
                if(HostSaveIdentity.Ready){_helloWaiting=false;_peerAccepted=true;SendWelcome(log);}
                else _status=HostSaveIdentity.LastError.Length==0 ? "waiting for host campaign load" : HostSaveIdentity.LastError;
            }
            RegionReplication.Tick(now);
            CampaignReplication.Tick(now);
            StreamWorldState(now,connection);
            if (PeerOnline && RegionReplication.LocalReady && now >= _nextActorAt)
            {
                _nextActorAt = now + SnapshotIntervalSeconds;
                Send(RemoteFootAvatar.Capture(_actorSequence++));
            }

            if (!connection.IsConnected)
            {
                MaintainTransport(now, log);
                WriteStatusFile(now);
                return;
            }

            if (!IndependentCoop.Enabled && _role == CoopRole.Guest && _peerAccepted && _reportedReady != CarProxy.Ready)
            {
                Send(new SeatMessage(CarProxy.Ready ? (byte)1 : byte.MaxValue, false));
                _reportedReady = CarProxy.Ready;
            }

            if (IndependentCoop.Enabled && PeerOnline)
            {
                StreamCarState(now,connection,log);
                byte vehicle=IndependentCoop.LocalVehicle;
                if(vehicle!=0 && vehicle!=IndependentCoop.LocalPlayer &&
                    IndependentCoop.State.DriverOf(vehicle)==IndependentCoop.LocalPlayer && now>=_nextInputAt && GameInput.Instance!=null)
                {
                    _nextInputAt=now+SnapshotIntervalSeconds;
                    var a=GameInput.Instance.playerActions;
                    bool driving=IndependentCoop.Playing && IndependentCoop.RemoteSeat && IndependentCoop.PeerCarFresh && !CoopConnectionMenu.CapturesInput;
                    Send(new DriverInputMessage(IndependentCoop.State.Revision,_driverInputSequence++,
                        driving ? Mathf.Clamp(a.Drive.thisState.Value,-1,1) : 0,driving ? Mathf.Clamp(a.Steer.thisState.Value,-1,1) : 0,
                        driving ? Mathf.Clamp(a.Altitude.thisState.Value,-1,1) : 0,driving ? Mathf.Clamp(a.Strafe.thisState.Value,-1,1) : 0));
                    DriverInputsSent++;
                }
            }
            else if (_role == CoopRole.Host)
            {
                bool changed = _seatAuthority.SetBothSeated(GuestSeated && GameRefs.Resolved &&
                    GameRefs.Player != null && !GameRefs.Player.IsActive() &&
                    !GameRefs.Global.IsInMenu() && !GameRefs.Global.isLoadingSaveGame);
                changed |= _seatAuthority.Expire(now);
                if (changed || now >= _nextSeatStateAt) PublishSeats(now);
                StreamCarState(now, connection, log);
            }
            else if (_role == CoopRole.Guest && PeerOnline && Seats.Driver == 2 &&
                Seats.BothSeated && CarProxy.Ready && now >= _nextInputAt && GameInput.Instance != null)
            {
                _nextInputAt = now + SnapshotIntervalSeconds;
                unsafe
                {
                    var a = GameInput.Instance.playerActions;
                    if (a != null)
                    {
                        float drive = Mathf.Clamp(a.Drive.thisState.Value, -1f, 1f);
                        float steer = Mathf.Clamp(a.Steer.thisState.Value, -1f, 1f);
                        float altitude = Mathf.Clamp(a.Altitude.thisState.Value, -1f, 1f);
                        float strafe = Mathf.Clamp(a.Strafe.thisState.Value, -1f, 1f);
                        if(CoopConnectionMenu.CapturesInput)drive=steer=altitude=strafe=0;
                        Send(new DriverInputMessage(Seats.Revision, _driverInputSequence++, drive, steer, altitude, strafe));
                        DriverInputsSent++;
                        if (drive != 0 || steer != 0 || altitude != 0 || strafe != 0) NonzeroDriverInputsSent++;
                    }
                }
            }

            WriteStatusFile(now);
        }

        public static void RequestSeatExchange()
        {
            if(IndependentCoop.Enabled)
            {
                if(!PeerOnline) return;
                if(_role==CoopRole.Host) IndependentCoop.PressSwap(IndependentCoop.State.Revision,1);
                else Send(new SeatRequestMessage(IndependentCoop.State.Revision));
                return;
            }
            if (!PeerOnline || !Seats.BothSeated) return;
            if (_role == CoopRole.Host)
            {
                if (_seatAuthority.Press(1, Seats.Revision, Time.realtimeSinceStartup))
                    PublishSeats(Time.realtimeSinceStartup);
            }
            else Send(new SeatRequestMessage(Seats.Revision));
        }

        private static void PublishSeats(float now)
        {
            byte previousDriver = Seats.Driver;
            Seats = new SeatStateMessage(_seatAuthority.Revision, _seatAuthority.Driver,
                _seatAuthority.Requester, _seatAuthority.BothSeated,
                _seatAuthority.Requester == 0 ? 0f : Mathf.Clamp((float)(_seatAuthority.ExpiresAt - now), 0f, 10f));
            if (previousDriver != Seats.Driver)
            {
                Autopilot.SetEnabled(false); RemoteInputOverride.SetActive(false);
            }
            if (PeerOnline) Send(Seats);
            _nextSeatStateAt = now + 1f;
        }

        /// <summary>
        /// Publishes this process's view of the session to its own file.
        ///
        /// Two copies of the game on one machine write to the same BepInEx log file, so the log
        /// cannot be used to tell the host's account of a session from the guest's. Each process
        /// gets its own status file instead, named by role.
        /// </summary>
        private static void WriteStatusFile(float now)
        {
            if(!Bootstrapper.DiagnosticsEnabled) return;
            if (now - _nextStatusWrite < 1f)
            {
                return;
            }
            _nextStatusWrite = now;

            string path;
            try
            {
                string role = _role == CoopRole.Host ? "host" : "guest";
                path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "coop-status-" + role + ".txt");
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                string text =
                    "role=" + _role +
                    ";scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex +
                    ";status=" + _status +
                    ";connected=" + IsConnected +
                    ";peerOnline=" + PeerOnline + ";guestSeated=" + GuestSeated +
                    ";campaign=" + CampaignReplication.Status + ";campaignRevision=" + CampaignReplication.Revision +
                    ";saveIdentityReady="+HostSaveIdentity.Ready+";saveIdentityError="+HostSaveIdentity.LastError+
                    ";campaignApplied=" + CampaignReplication.SnapshotsApplied + ";campaignSuppressed=" + CampaignReplication.SuppressedMutations +
                    ";campaignHostCompletions=" + CampaignReplication.HostCompletions +
                    ";campaignProjectionPending=" + CampaignSnapshot.PresentationPending +
                    ";campaignProjectionError=" + CampaignSnapshot.LastPresentationError +
                    ";dialogClips=" + CampaignPresentation.ClipsPlayed + ";dialogMissingClips=" + CampaignPresentation.MissingClips +
                    ";dialogPortraitFrames=" + CampaignPresentation.PortraitFrames + ";dialogPresentationError=" + CampaignPresentation.LastError +
                    ";driver=" + Seats.Driver + ";seatRevision=" + Seats.Revision + ";seatRequester=" + Seats.Requester +
                    ";driverInputs=" + DriverInputsSent + ";nonzeroDriverInputs=" + NonzeroDriverInputsSent +
                    ";nativeInputHits=" + NativeDrivingInput.Hits + ";nativeRemoteInputs=" + NativeDrivingInput.RemoteInputs +
                    ";sent=" + SnapshotsSent +
                    ";recv=" + SnapshotsReceived +
                    ";haveRemote=" + _haveRemoteCar +
                    ";remoteRate=" + RemoteSnapshotRate.ToString("0.0", CultureInfo.InvariantCulture) +
                    ";remoteStep=" + RemoteStepMetres.ToString("0.00", CultureInfo.InvariantCulture) +
                    ";remotePos=" + (_haveRemoteCar
                        ? _remoteCar.PositionX.ToString("0.0", CultureInfo.InvariantCulture) + "," +
                          _remoteCar.PositionY.ToString("0.0", CultureInfo.InvariantCulture) + "," +
                          _remoteCar.PositionZ.ToString("0.0", CultureInfo.InvariantCulture)
                        : "-") +
                    ";seq=" + (_haveRemoteCar ? _remoteCar.SequenceNumber.ToString() : "-");

                text += ";proxy=" + CarProxy.Ready + ";proxyStatus=" + CarProxy.Status +
                    ";peerScene=" + RegionReplication.PeerScene + ";sameRegion=" + RegionReplication.SameRegion +
                    ";regionReady=" + RegionReplication.LocalReady +
                    ";ownCarScene=" + IndependentCoop.OwnScene +
                    ";proxyPos=" + CarProxy.PositionText + ";proxyMeshes=" + CarProxy.MeshCount +
                    ";proxyBuffer=" + CarProxy.Buffered + ";proxyRejected=" + CarProxy.Rejected +
                    ";proxyUnderruns=" + CarProxy.Underruns + ";proxyTeleports=" + CarProxy.Teleports +
                    ";proxyTravel=" + CarProxy.Travelled.ToString("0.00", CultureInfo.InvariantCulture) +
                    ";proxyMaxStep=" + CarProxy.MaxFrameStep.ToString("0.000", CultureInfo.InvariantCulture) +
                    ";localSuppressed=" + LocalCarSuppress.Active + ";camera=" + RemoteCameraRig.LiveCameraName +
                    ";fxTemplates=" + CarVisuals.Count + ";fxEmitting=" + CarVisuals.Emitting +
                    ";fxDynamic="+CarVisuals.DynamicGroupCount+";fxDynamicCreated="+CarVisuals.DynamicGroupsCreated+";fxContactSource="+CarVisuals.CapturedContactParticles+
                    ";fxApplied=" + CarVisuals.Applied + ";fxMissing=" + CarVisuals.MissingTemplates +
                    ";fxTrails=" + CarVisuals.TrailCount + ";fxStatus=" + CarVisuals.Status +
                    ";traffic=" + TrafficReplication.Status + ";trafficActive=" + TrafficReplication.ActiveVehicles +
                    ";trafficPool=" + TrafficReplication.PoolCount + ";trafficSeq=" + TrafficReplication.Sequence +
                    ";npcs=" + WorldNpcReplication.Status + ";npcCount=" + WorldNpcReplication.Count +
                    ";npcActive=" + WorldNpcReplication.ActiveCount + ";" + WorldNpcReplication.AnimationDiagnostics + ";" + RemoteFootAvatar.AnimationDiagnostics + ";mobility=" + GuestMobility.Status +
                    ";guestOnFoot=" + GuestMobility.OnFoot +
                    ";independentCars=" + IndependentCoop.Enabled + ";peerCar=" + PeerCarProxy.Ready +
                    ";peerCarPos=" + PeerCarProxy.PositionText + ";peerCarStatus=" + PeerCarProxy.Status +
                    ";occupancyRevision=" + IndependentCoop.State.Revision +
                    ";p1Vehicle=" + IndependentCoop.State.Player1Vehicle + ";p2Vehicle=" + IndependentCoop.State.Player2Vehicle +
                    ";car1Driver=" + IndependentCoop.State.Driver1 + ";car2Driver=" + IndependentCoop.State.Driver2 +
                    ";peerCarColliders=" + PeerCarProxy.ColliderCount + ";carOverlapCorrections=" + PeerCarProxy.OverlapCorrections +
                    ";carPenetration=" + PeerCarProxy.LastPenetration.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture) +
                    ";"+CoopPerformance.Diagnostics()+"parking="+ParkingAuthority.Status+";parkingDenied="+ParkingAuthority.Denied+";parkingGranted="+ParkingAuthority.Granted+";parkingPromptHidden="+ParkingAuthority.PromptHidden+
                    ";parkingVolumeAvailable="+(GameRefs.PlayerCar!=null && GameRefs.PlayerCar.parkingVolume!=null)+
                    ";parkingUiActive="+(GameRefs.PlayerCar!=null && GameRefs.PlayerCar.parkingUI!=null && GameRefs.PlayerCar.parkingUI.gameObject.activeInHierarchy)+
                    ";boarding=" + IndependentCoop.Status +
                    ";" + RemoteCameraRig.Diagnostics;

                string temporary=path+".tmp";
                System.IO.File.WriteAllText(temporary, text);
                System.IO.File.Move(temporary,path,true);
            }
            catch (Exception)
            {
                // Diagnostics must never take the session down.
            }
        }

        private static float _nextStatusWrite;
        private static float _nextActorAt;
        private static uint _actorSequence;

        private static void Handle(IMessage message, ManualLogSource log)
        {
            MessagesReceived++;

            switch (message)
            {
                case CampaignChunkMessage campaign:
                    CampaignReplication.Receive(campaign);
                    break;
                case CampaignPresentationMessage presentation:
                    CampaignReplication.Receive(presentation);
                    break;
                case CampaignActionMessage action:
                    CampaignReplication.Handle(action);
                    break;
                case CampaignReplyMessage outcome:
                    CampaignReplication.Receive(outcome);
                    break;
                case HelloMessage hello:
                    log.LogInfo("coop: HELLO from '" + hello.DisplayName +
                                "' speaking protocol " + hello.ProtocolVersion);
                    if (_role == CoopRole.Host)
                    {
                        if (hello.ProtocolVersion != ProtocolVersion)
                        {
                            RejectVersion(log);
                            break;
                        }
                        if(HostSaveIdentity.Ready){_peerAccepted=true;SendWelcome(log);}
                        else {_helloWaiting=true;_status="waiting for host campaign load";}
                    }
                    break;

                case WelcomeMessage welcome:
                    if (welcome.ProtocolVersion != ProtocolVersion)
                    {
                        RejectVersion(log);
                        break;
                    }
                    _peerAccepted = true;
                    _status = "connected as " + welcome.Role;
                    log.LogInfo("coop: WELCOME, role=" + welcome.Role +
                                ", protocol=" + welcome.ProtocolVersion);
                    break;

                case CarStateMessage carState:
                    ApplyCarState(carState);
                    break;
                case OwnedCarMessage owned:
                    if(IndependentCoop.ReceiveCar(owned,Time.realtimeSinceStartup)) RecordCar(owned.Pose);
                    break;
                case OccupancyRequestMessage boarding:
                    if(IndependentCoop.Enabled && _role==CoopRole.Host && PeerOnline)
                        IndependentCoop.HandleRequest(boarding,2);
                    break;
                case OccupancyStateMessage occupancy:
                    if(IndependentCoop.Enabled) IndependentCoop.ReceiveState(occupancy);
                    break;
                case VehicleParkMessage parking:
                    if(IndependentCoop.Enabled && PeerOnline) IndependentCoop.HandleParking(parking);
                    break;

                case CarVisualMessage visual:
                    if (_peerAccepted && (_role == CoopRole.Guest || IndependentCoop.Enabled)) CarVisuals.Receive(visual);
                    break;

                case CarTrailMessage trails:
                    if (_peerAccepted && (_role == CoopRole.Guest || IndependentCoop.Enabled)) CarVisuals.ReceiveTrails(trails);
                    break;

                case SeatRequestMessage request:
                    if(IndependentCoop.Enabled) { IndependentCoop.PressSwap(request.Revision,2); break; }
                    if (_role == CoopRole.Host && PeerOnline)
                    {
                        _seatAuthority.Press(2, request.Revision, Time.realtimeSinceStartup);
                        PublishSeats(Time.realtimeSinceStartup);
                    }
                    break;

                case SeatStateMessage state:
                    if (_role == CoopRole.Guest && PeerOnline && unchecked((int)(state.Revision - Seats.Revision)) >= 0)
                        Seats = state;
                    break;

                case DriverInputMessage input:
                    NativeDrivingInput.Receive(input);
                    break;

                case TrafficMessage traffic:
                    if (_role == CoopRole.Guest && PeerOnline)
                        TrafficReplication.Receive(traffic, Time.realtimeSinceStartup);
                    break;

                case RegionPresenceMessage region:
                    if (PeerOnline) RegionReplication.Receive(region);
                    break;
                case PersonalClockMessage clock:
                    CoopPersonalEffects.Receive(clock);
                    break;

                case MobilityRequestMessage request:
                    GuestMobility.HandleRequest(request);
                    break;
                case MobilityStateMessage state:
                    GuestMobility.Receive(state);
                    break;
                case FootPoseMessage pose:
                    if (PeerOnline) RemoteFootAvatar.Receive(pose, Time.realtimeSinceStartup);
                    break;

                case ParkingSlotMessage slot:
                    if(PeerOnline && (slot.Kind==0 || slot.Kind==ParkingSlotMessage.SpawnRequest) && _role==CoopRole.Host) ParkingAuthority.HandleRequest(slot,2);
                    else if(PeerOnline && slot.Kind==3 && _role==CoopRole.Host) ParkingAuthority.HandleCancel(slot,2);
                    else if(PeerOnline && slot.Kind!=0 && _role==CoopRole.Guest) ParkingAuthority.Receive(slot);
                    break;
                case NpcMessage npcs:
                    if (_role == CoopRole.Guest && PeerOnline) WorldNpcReplication.Receive(npcs, Time.realtimeSinceStartup);
                    break;

                case SeatMessage seat:
                    if (_role == CoopRole.Host && _peerAccepted)
                        _guestPresentationReady = seat.SeatId == 1 && !seat.IsDriving;
                    log.LogInfo("coop: SEAT id=" + seat.SeatId + " driving=" + seat.IsDriving);
                    break;

                case PingMessage ping:
                    Send(new PongMessage(ping.SequenceNumber, ping.SentAtMilliseconds));
                    break;

                case PongMessage pong:
                    log.LogInfo("coop: PONG seq=" + pong.SequenceNumber);
                    break;

                case ByeMessage bye:
                    log.LogInfo("coop: peer said BYE (" + bye.Reason + ")");
                    break;

                default:
                    log.LogWarning("coop: unhandled message " + message.Opcode);
                    break;
            }
        }

        private static void ApplyCarState(CarStateMessage carState)
        {
            if (_role != CoopRole.Guest || !_peerAccepted ||
                !CarProxy.Receive(carState, Time.realtimeSinceStartup))
            {
                // Duplicates, stale sequences, invalid poses and sequence wrap are handled by
                // the bounded render timeline, which only accepts authoritative guest poses.
                return;
            }
            RecordCar(carState);
        }
        private static void RecordCar(CarStateMessage carState)
        {
            var position = new Vector3(carState.PositionX, carState.PositionY, carState.PositionZ);
            if (_haveRemoteCar)
            {
                RemoteStepMetres = (position - _lastRemotePosition).magnitude;
            }

            _lastRemotePosition = position;
            _remoteCar = carState;
            _haveRemoteCar = true;
            SnapshotsReceived++;
            _rateWindowCount++;

            if (_rateWindowStart <= 0f)
            {
                _rateWindowStart = Time.realtimeSinceStartup;
            }
            else
            {
                float window = Time.realtimeSinceStartup - _rateWindowStart;
                if (window >= 1f)
                {
                    RemoteSnapshotRate = _rateWindowCount / window;
                    _rateWindowStart = Time.realtimeSinceStartup;
                    _rateWindowCount = 0;
                }
            }
        }

        private static void SendWelcome(ManualLogSource log)
        {
            if (Send(new WelcomeMessage(PeerRole.Guest, ProtocolVersion)))
            {
                _status = "guest connected";
                log.LogInfo("coop: guest accepted, streaming car state");
            }
        }

        private static void RejectVersion(ManualLogSource log)
        {
            _peerAccepted = false;
            _status = "protocol mismatch: expected " + ProtocolVersion;
            log.LogWarning("coop: " + _status + "; both players need the same mod build");
            _connection?.SendByeAndClose(ByeReason.ProtocolError);
        }

        private static bool Send(IMessage message)
        {
            CoopConnection connection = _connection;
            if (connection == null)
            {
                return false;
            }

            try
            {
                return connection.Send(message);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool SendMessage(IMessage message) => Send(message);

        /// <summary>
        /// Samples the real car and sends it. Runs on the main thread, so reading the
        /// Rigidbody here is safe; the actual write happens on the transport's writer thread.
        /// </summary>
        private static void StreamCarState(float now, CoopConnection connection, ManualLogSource log)
        {
            using var timing=CoopPerformance.Measure(CoopPerformance.Stage.Snapshot);
            if(!RegionReplication.LocalReady) return;
            if(GameRefs.Global==null || GameRefs.Global.IsInMenu() || GameRefs.Global.isLoadingSaveGame) return;
            if(IndependentCoop.Enabled && !IndependentCoop.Playing) return;
            if (!_peerAccepted || now < _nextSnapshotAt)
            {
                return;
            }

            // Accumulate rather than reschedule from "now": rescheduling quantises every
            // interval up to the next frame, which measured 18.3 Hz instead of 20. The clamp
            // stops a long stall (a loading hitch) from turning into a burst of catch-up sends.
            _nextSnapshotAt += SnapshotIntervalSeconds;
            if (_nextSnapshotAt < now - 0.25f)
            {
                _nextSnapshotAt = now + SnapshotIntervalSeconds;
            }

            if(IndependentCoop.Enabled && IndependentCoop.TryCaptureParkedAway(_snapshotSequence,out var parkedAway))
            {
                _snapshotSequence++;
                if(connection.Send(parkedAway)) SnapshotsSent++;
                return;
            }
            if (GameRefs.PlayerCar == null)
            {
                return;
            }

            try
            {
                unsafe
                {
                    Transform carTransform = GameRefs.PlayerCar.transform;
                    bool parked=IndependentCoop.OwnParked;
                    if (parked) carTransform = GameRefs.PlayerCar.parkObject.transform;
                    Vector3 position = carTransform.position;
                    Quaternion rotation = carTransform.rotation;

                    Vector3 velocity = Vector3.zero;
                    Vector3 angularVelocity = Vector3.zero;

                    Rigidbody body = GameRefs.PlayerCar.rBody;
                    if (body != null)
                    {
                        velocity = body.velocity;
                        angularVelocity = body.angularVelocity;
                    }

                    var message = new CarStateMessage(
                        position.x, position.y, position.z,
                        rotation.x, rotation.y, rotation.z, rotation.w,
                        velocity.x, velocity.y, velocity.z,
                        angularVelocity.x, angularVelocity.y, angularVelocity.z,
                        // The vehicle exposes no steering-angle or damage scalar; the plan
                        // listed them as visual state for later. Zero means "unknown" for now,
                        // not "undamaged and straight".
                        0f, 0f,
                        _snapshotSequence++);

                    bool sent;
                    if(IndependentCoop.Enabled)
                    {
                        var owned=new OwnedCarMessage(IndependentCoop.LocalPlayer,UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex,parked,message);
                        IndependentCoop.RememberOwnCar(owned);sent=connection.Send(owned);
                    }
                    else sent=connection.Send(message);
                    if (sent)
                    {
                        SnapshotsSent++;
                        try
                        {
                            connection.Send(CarVisuals.Capture(message.SequenceNumber));
                            connection.Send(CarVisuals.CaptureTrails(message.SequenceNumber));
                        }
                        catch (Exception ex) { CarVisuals.Fail(ex, log); }
                    }
                }
            }
            catch (Exception ex)
            {
                // Logged once: a broken snapshot must not fill the log at 20 Hz.
                if (!_snapshotErrorLogged)
                {
                    _snapshotErrorLogged = true;
                    log.LogError("coop: snapshot failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static bool _snapshotErrorLogged;
        private static float _nextWorldAt;
        private static uint _worldSequence;
        private static void StreamWorldState(float now,CoopConnection connection)
        {
            // Ambient state must continue while the host walks with its parked car
            // in another region, or when no local car body is currently loaded.
            if(_role!=CoopRole.Host || !PeerOnline || !RegionReplication.LocalReady ||
                !RegionReplication.SameRegion || now<_nextWorldAt) return;
            _nextWorldAt+=0.1f;
            if(_nextWorldAt<now-0.25f)_nextWorldAt=now+0.1f;
            uint sequence=_worldSequence++;
            try
            {
                var traffic=TrafficReplication.Capture(sequence);
                if(traffic!=null)connection.Send(traffic);
            }
            catch(Exception ex){TrafficReplication.Fail(ex);}
            WorldNpcReplication.CaptureAndSend(sequence);
        }

        // Called only on the main thread. Retain old worker handles until they actually
        // finish: Stop's invalidation is not proof a bounded Connect has returned yet.
        private static void EnsureTransportWorker()
        {
            lock (TransportGate)
            {
                if (_role == CoopRole.Off || _acceptThread != null && _acceptThread.IsAlive ||
                    _connectThread != null && _connectThread.IsAlive) return;
                long generation = _lifecycleGeneration;
                if (_role == CoopRole.Host)
                {
                    TcpListener listener = _listener;
                    if (listener == null) return;
                    _acceptThread = new Thread(() => AcceptLoop(generation, listener))
                        { IsBackground = true, Name = "CloudpunkCoop.Accept" };
                    _acceptThread.Start();
                }
                else
                {
                    string host = _guestHost; int port = _guestPort;
                    _connectThread = new Thread(() => ConnectLoop(generation, host, port))
                        { IsBackground = true, Name = "CloudpunkCoop.Connect" };
                    _connectThread.Start();
                }
            }
        }

        private static bool CurrentLifecycle(long generation)
        {
            lock (TransportGate) return generation == _lifecycleGeneration;
        }

        private static bool WaitForTransportSlot(long generation)
        {
            while (true)
            {
                lock (TransportGate)
                {
                    if (generation != _lifecycleGeneration) return false;
                    if (_connection == null && _pendingConnection == null && !_retiringConnection) return true;
                }
                Thread.Sleep(50);
            }
        }

        private static bool RetryDelay(long generation)
        {
            for (int elapsed = 0; elapsed < RetryMilliseconds; elapsed += 50)
            {
                if (!CurrentLifecycle(generation)) return false;
                Thread.Sleep(50);
            }
            return CurrentLifecycle(generation);
        }

        private static void PublishWorkerStatus(long generation, string status)
        {
            lock (TransportGate)
                if (generation == _lifecycleGeneration && _connection == null && _pendingConnection == null &&
                    !_retiringConnection) _workerStatus = status;
        }

        private static void PublishPending(long generation, CoopConnection connection)
        {
            connection.Closed += (_, args) =>
            {
                // Reader callback carries identity. It never updates public status or Unity.
                lock (TransportGate)
                    if (generation == _lifecycleGeneration &&
                        (ReferenceEquals(connection, _connection) || ReferenceEquals(connection, _pendingConnection)))
                        _closedReason = args.Reason;
            };
            lock (TransportGate)
            {
                if (generation == _lifecycleGeneration && _connection == null && _pendingConnection == null &&
                    !_retiringConnection)
                {
                    _pendingConnection = connection; _workerStatus = null;
                    return;
                }
            }
            DisposeLink(connection); // Including a connect that completed after Stop.
        }

        private static void AcceptLoop(long generation, TcpListener listener)
        {
            while (WaitForTransportSlot(generation))
            {
                try
                {
                    PublishWorkerStatus(generation, "listening for guest / reconnect");
                    PublishPending(generation, CoopConnection.Accept(listener));
                    // No second accept until Pump disposes the prior active/pending link.
                }
                catch (Exception ex)
                {
                    if (!CurrentLifecycle(generation)) return;
                    PublishWorkerStatus(generation, "accept failed, retrying: " + ex.Message);
                    if (!RetryDelay(generation)) return;
                }
            }
        }

        private static void ConnectLoop(long generation, string host, int port)
        {
            while (WaitForTransportSlot(generation))
            {
                try
                {
                    PublishWorkerStatus(generation, "connecting to " + host + ":" + port);
                    PublishPending(generation, CoopConnection.Connect(host, port, RetryMilliseconds));
                }
                catch (Exception ex)
                {
                    if (!CurrentLifecycle(generation)) return;
                    PublishWorkerStatus(generation, "connect failed, retrying: " + ex.Message);
                }
                if (!WaitForTransportSlot(generation) || !RetryDelay(generation)) return;
            }
        }

        private static void MaintainTransport(float now, ManualLogSource log)
        {
            EnsureTransportWorker();
            CoopConnection retired = null, adopted = null;
            string reason = null;
            lock (TransportGate)
            {
                if (_connection != null && !_peerAccepted && now >= _handshakeDeadline)
                {
                    _closedReason = "handshake timeout";
                    _connection.Close();
                }
                if (_connection != null && !_connection.IsConnected)
                {
                    retired = _connection; _connection = null;
                    reason = _closedReason ?? "transport closed";
                    _closedReason = null; _retiringConnection = true;
                }
                else if (_connection == null && _pendingConnection != null)
                {
                    adopted = _pendingConnection; _pendingConnection = null;
                    _retiringConnection = true; _closedReason = null;
                }
                else if (_connection == null && _workerStatus != null)
                {
                    _status = _workerStatus; _workerStatus = null;
                }
            }
            if (retired != null)
            {
                try
                {
                    DisposeLink(retired);
                    ResetTransportPresentation(now, log);
                    _status = "closed: " + reason + "; awaiting reconnect";
                    log.LogInfo("coop: " + _status);
                }
                finally { lock (TransportGate) _retiringConnection = false; }
            }
            if (adopted != null)
            {
                bool installed = false;
                try
                {
                    if (!adopted.IsConnected) return;
                    ResetTransportPresentation(now, log);
                    CampaignReplication.TransportReconnected();
                    // Assignment and all handshake dispatch are on the main thread.
                    lock (TransportGate) _connection = adopted;
                    installed = true; _handshakeDeadline = now + 10f;
                    _status = _role == CoopRole.Host ? "guest connected, awaiting HELLO" : "connected, awaiting WELCOME";
                    if (_role == CoopRole.Guest && !Send(new HelloMessage(ProtocolVersion, Environment.MachineName)))
                        adopted.Close();
                    log.LogInfo("coop: " + _status);
                }
                finally
                {
                    if (!installed) DisposeLink(adopted);
                    lock (TransportGate) _retiringConnection = false;
                }
            }
        }

        private static void ResetTransportPresentation(float now, ManualLogSource log)
        {
            _peerAccepted = false; _haveRemoteCar = false;
            _helloWaiting=false;
            _guestPresentationReady = _reportedReady = false;
            NativeDrivingInput.Reset();
            // Preserve host occupancy of its own car; Tick's offline branch evicts only
            // the vanished peer and restores any local seat in that peer's vehicle.
            if (IndependentCoop.Enabled && _role == CoopRole.Host) IndependentCoop.Tick(now);
            else IndependentCoop.Reset();
            PeerCarProxy.Clear();
            TrafficReplication.Stop(); WorldNpcReplication.Stop();
            RegionReplication.Reset();
            RemoteFootAvatar.Clear(); GuestMobility.Reset(); CarProxy.Stop(log);
            RemoteMapMarker.Clear();
            _seatAuthority = new SeatExchange();
            Seats = new SeatStateMessage(0, 1, 0, false, 0);
            _snapshotSequence = _driverInputSequence = _actorSequence = 0;
            _worldSequence=0;_nextWorldAt=0;
            _nextSnapshotAt = _nextActorAt = _nextInputAt = _nextSeatStateAt = 0;
            _rateWindowStart = now; _rateWindowCount = 0;
            RemoteSnapshotRate = RemoteStepMetres = 0;
            _snapshotErrorLogged = false;
            // No CampaignReplication.Reset here: role/guest save guards, host campaign
            // identity, ledger and deduplicated request outcomes survive disconnection.
        }

        private static void DisposeLink(CoopConnection connection)
        {
            if (connection == null) return;
            try { connection.Dispose(); } catch (Exception) { }
        }
    }
}
