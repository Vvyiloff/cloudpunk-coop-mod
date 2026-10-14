using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using CloudpunkCoop.Game;
using CloudpunkCoop.UI;

namespace CloudpunkCoop
{
    /// <summary>
    /// Plugin entry point.
    /// </summary>
    [BepInPlugin(Plugin.Guid, Plugin.Name, Plugin.Version)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "dev.andre.cloudpunk-coop";
        public const string Name = "Cloudpunk Coop";
        public const string Version = "0.1.0";
        private DiskLogListener _testLog;
        private UnityEngine.Application.LogCallback _testUnityLog;
        private bool _forwardingUnityLog;
        private readonly System.Collections.Generic.HashSet<string> _nativeErrors=new();

        public override void Load()
        {
            Log.LogInfo("=== " + Name + " " + Version + " loading ===");
            Log.LogInfo("unity=" + UnityEngine.Application.unityVersion +
                        " platform=" + UnityEngine.Application.platform +
                        " product=" + UnityEngine.Application.productName);

            ConfigureDiagnostics();
            ConfigureAutomation();
            ConfigureNetwork();
            ConfigureTestLog();

            try
            {
                Bootstrapper bootstrapper = AddComponent<Bootstrapper>();
                Log.LogInfo("Bootstrapper attached: " + (bootstrapper != null));
            }
            catch (System.Exception ex)
            {
                Log.LogError("failed to attach Bootstrapper: " + ex);
            }
        }

        private void ConfigureTestLog()
        {
            if(!Bootstrapper.DiagnosticsEnabled || !string.Equals(System.Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST"),
                "true",System.StringComparison.OrdinalIgnoreCase)) return;
            string directory=System.Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST_DIR");
            if(string.IsNullOrWhiteSpace(directory) || !System.IO.Path.IsPathFullyQualified(directory) ||
                !System.IO.Directory.Exists(directory)) return;
            try
            {
                string role=(Bootstrapper.CoopRoleSetting ?? "off").ToLowerInvariant();
                if(role!="host" && role!="guest" && role!="off") return;
                _testLog=new DiskLogListener(System.IO.Path.Combine(directory,"native-"+role+".log"),LogLevel.All,false,true,1);
                BepInEx.Logging.Logger.Listeners.Add(_testLog);
                _testUnityLog=(System.Action<string,string,UnityEngine.LogType>)((message,stack,type)=>
                {
                    if(_forwardingUnityLog || type!=UnityEngine.LogType.Error && type!=UnityEngine.LogType.Exception &&
                        type!=UnityEngine.LogType.Assert || _nativeErrors.Count>=64 || !_nativeErrors.Add(message+stack)) return;
                    _forwardingUnityLog=true;
                    if(type==UnityEngine.LogType.Exception) Bootstrapper.NativeTestException=message+"\n"+stack;
                    try {Log.LogError("Native Unity "+type+": "+message+"\n"+stack);}
                    finally {_forwardingUnityLog=false;}
                });
                UnityEngine.Application.add_logMessageReceived(_testUnityLog);
                Log.LogInfo("Separate native test log enabled for "+role);
            }
            catch(System.Exception ex){Log.LogWarning("Could not create native test log: "+ex.Message);}
        }

        /// <summary>
        /// Developer tools are opt-in, including per-process test diagnostics.
        /// </summary>
        private void ConfigureDiagnostics()
        {
            Bootstrapper.DiagnosticsEnabled = false;
            try
            {
                Bootstrapper.DiagnosticsEnabled = Config.Bind("Developer", "EnableDiagnostics", false,
                    "Enable diagnostic F-keys, probes, demo controls and debug panels. Beta players should leave false.").Value;
            }
            catch (System.Exception ex)
            {
                Log.LogWarning("could not read developer settings, diagnostics default to disabled: " + ex.Message);
            }

            if (bool.TryParse(System.Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_DIAGNOSTICS"),
                out bool diagnosticsOverride)) Bootstrapper.DiagnosticsEnabled = diagnosticsOverride;
            Log.LogInfo("developer diagnostics: " + (Bootstrapper.DiagnosticsEnabled ? "enabled" : "disabled"));
        }

        /// <summary>Scripted gameplay requires both developer diagnostics and an explicit demo opt-in.</summary>
        private void ConfigureAutomation()
        {
            try
            {
                ConfigEntry<bool> automation = Config.Bind(
                    "Automation",
                    "RunScriptedDemo",
                    false,
                    "Automatically seat a passenger, put the player in the car and drive it a " +
                    "few seconds after the world loads. Required for unattended gate verification. " +
                    "Requires [Developer] EnableDiagnostics. Default false.");

                Bootstrapper.AutomationEnabled = automation.Value;
                // Per-process test override, without changing the shared BepInEx config.
                if (bool.TryParse(System.Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_DEMO"),
                    out bool demoOverride)) Bootstrapper.AutomationEnabled = demoOverride;
                Bootstrapper.AutomationEnabled = Bootstrapper.DiagnosticsEnabled && Bootstrapper.AutomationEnabled;
                Log.LogInfo("scripted demo: " + (Bootstrapper.AutomationEnabled ? "enabled" : "disabled") +
                            " (change [Automation] RunScriptedDemo in BepInEx\\config\\" +
                            Plugin.Guid + ".cfg)");
            }
            catch (System.Exception ex)
            {
                Bootstrapper.AutomationEnabled = false;
                Log.LogWarning("could not read the automation setting, defaulting to disabled: " + ex.Message);
            }
        }

        /// <summary>
        /// Network settings. Role defaults to Off, so the plugin never opens a listening
        /// socket unless someone asks for it: an internet-facing port is a decision the
        /// operator makes, not a side effect of installing the mod.
        /// </summary>
        private void ConfigureNetwork()
        {
            try
            {
                ConfigEntry<string> role = Config.Bind(
                    "Network",
                    "Role",
                    "Off",
                    "Off, Host or Guest. Host opens a listener on Port and streams the car. " +
                    "Guest connects to Address:Port. Default Off so nothing listens unasked.");

                ConfigEntry<int> port = Config.Bind(
                    "Network",
                    "Port",
                    27050,
                    "TCP port used by both roles.");

                ConfigEntry<string> address = Config.Bind(
                    "Network",
                    "Address",
                    "127.0.0.1",
                    "Host address a Guest connects to. Ignored by Host.");

                Bootstrapper.CoopRoleSetting = role.Value;
                Bootstrapper.CoopPort = port.Value;
                Bootstrapper.CoopAddress = address.Value;
                CampaignReplication.ConfigureCampaign(Config.Bind("Campaign","Id","campaign-1",
                    "Host campaign identity for the separate Player 2 inventory. A new game creates a new identity."));
                IndependentCoop.Enabled=Config.Bind("Network","IndependentCars",true,
                    "Each player keeps an individual native vehicle. Both vehicles are visible to both players.").Value;
                if(bool.TryParse(System.Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_INDEPENDENT_CARS"),out bool independentOverride))
                    IndependentCoop.Enabled=independentOverride;

                // Environment overrides. Two copies of the game on one machine share a single
                // BepInEx config file, so without this both would take the same role and the
                // second one could never be the guest. The env var is per-process, which is
                // exactly the granularity needed.
                string envRole = System.Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_ROLE");
                if (!string.IsNullOrWhiteSpace(envRole))
                {
                    Bootstrapper.CoopRoleSetting = envRole.Trim();
                }

                string envPort = System.Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_PORT");
                if (int.TryParse(envPort, out int parsedPort))
                {
                    Bootstrapper.CoopPort = parsedPort;
                }

                string envAddress = System.Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_ADDRESS");
                if (!string.IsNullOrWhiteSpace(envAddress))
                {
                    Bootstrapper.CoopAddress = envAddress.Trim();
                }

                Log.LogInfo("network: role=" + Bootstrapper.CoopRoleSetting +
                            " port=" + Bootstrapper.CoopPort +
                            " address=" + Bootstrapper.CoopAddress +
                            (string.IsNullOrWhiteSpace(envRole) ? " (from config)" : " (from CLOUDPUNK_COOP_ROLE)"));
            }
            catch (System.Exception ex)
            {
                Bootstrapper.CoopRoleSetting = "Off";
                Log.LogWarning("could not read the network settings, staying off: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Main-thread host for the mod. Resolves the game world, samples the input map every
    /// frame and draws the debug panel.
    /// </summary>
    public sealed class Bootstrapper : UnityEngine.MonoBehaviour
    {
        internal static string NativeTestException="";
        private static readonly ManualLogSource Log = Logger.CreateLogSource(Plugin.Name);

        /// <summary>
        /// Set from the plugin config before this component is attached. When false, no
        /// scripted gameplay runs on its own. Gameplay keys remain available.
        /// </summary>
        public static bool AutomationEnabled { get; set; } = false;
        public static bool DiagnosticsEnabled { get; set; } = false;

        /// <summary>Network role from the config: Off, Host or Guest.</summary>
        public static string CoopRoleSetting { get; set; } = "Off";

        public static int CoopPort { get; set; } = 27050;
        public static string CoopAddress { get; set; } = "127.0.0.1";

        private const float PollIntervalSeconds = 0.5f;
        private const float GiveUpAfterSeconds = 120f;
        private const float ActivityLogIntervalSeconds = 15f;
        private const float AutoSeatDelaySeconds = 6f;
        private const float EnterCarDelaySeconds = 8f;
        private const float DemoStartSeconds = 10f;
        private const float DemoEndSeconds = 150f;
        private const float TelemetryIntervalSeconds = 2f;

        private float _nextPollAt;
        private float _startedAt;
        private float _nextActivityLogAt;
        private float _seatRigAt;
        private float _enterCarAt;
        private float _demoStartAt;
        private float _demoEndAt;
        private float _nextTelemetryAt;
        private UnityEngine.Vector3 _lastCarPosition;
        private bool _haveLastPosition;
        private bool _gaveUp;

        public void Awake()
        {
            _startedAt = UnityEngine.Time.realtimeSinceStartup;
            _nextPollAt = 0f;
            _nextActivityLogAt = float.PositiveInfinity;

            CoopMenuInput.Install();
            HostSaveIdentity.Install();
            StartCoopSession();
        }

        /// <summary>
        /// Starts the configured role. Done in Awake rather than once the world resolves, so a
        /// guest can connect and hand-shake while the host is still loading its save.
        /// </summary>
        private void StartCoopSession()
        {
            string role = (CoopRoleSetting ?? "Off").Trim();

            if (string.Equals(role, "Host", System.StringComparison.OrdinalIgnoreCase))
            {
                Net.CoopSession.StartHost(CoopPort, Log);
            }
            else if (string.Equals(role, "Guest", System.StringComparison.OrdinalIgnoreCase))
            {
                Net.CoopSession.StartGuest(CoopAddress, CoopPort, Log);
            }
            else
            {
                Log.LogInfo("coop: role Off, no socket opened");
            }
        }

        public void FixedUpdate()
        {
            using var timing=CoopPerformance.Measure(CoopPerformance.Stage.Collision);
            if(IndependentCoop.Enabled) PeerCarProxy.PhysicsTick();
        }

        public void Update()
        {
            GameRefs.Refresh();
            CoopConnectionMenu.Tick();
            if(IndependentCoop.Playing) CoopPerformance.Frame(UnityEngine.Time.unscaledDeltaTime);
            float now = UnityEngine.Time.realtimeSinceStartup;

            // Networking runs every frame, including before the world resolves: a guest must be
            // able to connect and hand-shake while the host is still loading its save.
            Net.CoopSession.Pump(now, Log);
            CoopPersonalEffects.Tick(now);
            WorldNpcReplication.PrewarmLoadedRegion();
            CampaignTestDriver.Tick(now);
            CoopPresence.Tick();
            NativeDrivingInput.Tick();

            if (DiagnosticsEnabled && DebugOverlay.TogglePressed())
            {
                DebugOverlay.Toggle();
                Log.LogInfo("debug overlay " + (DebugOverlay.Visible ? "shown" : "hidden"));
            }

            if (!_gaveUp && !GameRefs.Resolved)
            {
                if (now >= _nextPollAt)
                {
                    _nextPollAt = now + PollIntervalSeconds;
                    try
                    {
                        if (GameRefs.Resolve(Log))
                        {
                            GameInputMap.Initialize(Log);
                            SeatRig.Initialize(Log);
                            RemoteInputOverride.Install(Log);
                            NativeDrivingInput.Install(Log);
                            CoopMenuInput.Install();
                            IndependentCoop.Install();

                            if (DiagnosticsEnabled && AutomationEnabled && Net.CoopSession.Role != Net.CoopRole.Guest)
                            {
                                _seatRigAt = now + AutoSeatDelaySeconds;
                                _enterCarAt = now + EnterCarDelaySeconds;
                                _demoStartAt = now + DemoStartSeconds;
                                _demoEndAt = now + DemoEndSeconds;
                            }
                            else
                            {
                                Log.LogInfo("scripted demo is off; gameplay controls remain available");
                            }

                            _nextActivityLogAt = now + ActivityLogIntervalSeconds;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        // A half-initialised world is normal while loading; keep retrying.
                        Log.LogWarning("resolution attempt failed, retrying: " +
                                       ex.GetType().Name + ": " + ex.Message);
                    }

                    if (!GameRefs.Resolved && now - _startedAt > GiveUpAfterSeconds)
                    {
                        _gaveUp = true;
                        Log.LogError("gave up waiting for the game world; iteration 1 gate will fail");
                    }
                }
                return;
            }

            if (GameInputMap.Ready && !CoopConnectionMenu.CapturesInput)
            {
                if (DebugOverlay.Pressed(UnityEngine.KeyCode.F8)) Net.CoopSession.RequestSeatExchange();
                if (Net.CoopSession.Role == Net.CoopRole.Guest && DebugOverlay.Pressed(UnityEngine.KeyCode.E))
                { if(!IndependentCoop.Enabled) GuestMobility.RequestToggle(); }
                if(IndependentCoop.Enabled && DebugOverlay.Pressed(UnityEngine.KeyCode.E))
                    IndependentCoop.RequestToggle(UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift));
                if (DiagnosticsEnabled && DebugOverlay.Pressed(UnityEngine.KeyCode.F12)) TrafficReplication.ShowLabels = !TrafficReplication.ShowLabels;
                if (DiagnosticsEnabled && DebugOverlay.Pressed(UnityEngine.KeyCode.F10))
                    VisualProbe.Capture(Log, System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
                        "coop-visual-" + Net.CoopSession.Role.ToString().ToLowerInvariant() + ".txt"));
                if (DiagnosticsEnabled && DebugOverlay.Pressed(UnityEngine.KeyCode.F11))
                    NativeCampaignProbe.Capture(Log, System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
                        "coop-campaign-" + Net.CoopSession.Role.ToString().ToLowerInvariant() + ".jsonl"));
                if (DiagnosticsEnabled && Net.CoopSession.Role == Net.CoopRole.Host && DebugOverlay.Pressed(UnityEngine.KeyCode.F2))
                    NativeDrivingInput.StartProbe();
                GameInputMap.Sample();

                if (DiagnosticsEnabled && now >= _nextActivityLogAt)
                {
                    _nextActivityLogAt = now + ActivityLogIntervalSeconds;
                    GameInputMap.LogActivity(Log);
                }

                if (DiagnosticsEnabled && Net.CoopSession.Role != Net.CoopRole.Guest)
                {
                    HandleSeatKeys(now);
                    HandleAutopilot(now);
                    LogTelemetry(now);
                }
            }
        }

        public void LateUpdate()
        {
            using var timing=CoopPerformance.Measure(CoopPerformance.Stage.Presentation);
            if (!IndependentCoop.Enabled && Net.CoopSession.Role == Net.CoopRole.Guest)
                CarProxy.Tick(UnityEngine.Time.realtimeSinceStartup, Net.CoopSession.IsConnected, Log);
            if(IndependentCoop.Enabled)
            {
                PeerCarProxy.Tick(UnityEngine.Time.realtimeSinceStartup,Net.CoopSession.PeerOnline,IndependentCoop.PeerPlayer);
                IndependentCoop.Tick(UnityEngine.Time.realtimeSinceStartup);
                ParkingAuthority.Tick();
            }
            RemoteMapMarker.Tick();
            TrafficReplication.Tick(UnityEngine.Time.realtimeSinceStartup);
            if(!IndependentCoop.Enabled) GuestMobility.Tick();
            RemoteFootAvatar.Tick(UnityEngine.Time.realtimeSinceStartup);
            WorldNpcReplication.Tick(UnityEngine.Time.realtimeSinceStartup);
        }

        public void OnDestroy()
        {
            CarProxy.Stop(Log);
            RemoteMapMarker.Clear();
            Net.CoopSession.Stop();
        }

        /// <summary>
        /// F6 runs the autopilot: it lifts off, holds an altitude band, probes left/centre/
        /// right with raycasts and steers around whatever is in the way while heading for the
        /// waypoint. It is the honest replacement for the earlier fixed-throttle driver, which
        /// drove straight into a wall and made the car look broken.
        /// </summary>
        private void HandleAutopilot(float now)
        {
            if (DebugOverlay.Pressed(UnityEngine.KeyCode.F6))
            {
                bool enable = !Autopilot.Enabled;
                Autopilot.SetEnabled(enable);
                RemoteInputOverride.SetActive(enable);
                Log.LogInfo("autopilot active=" + enable);
                _nextTelemetryAt = 0f;
            }

            // Scripted demo window, so the gate can be verified from the log with nobody at
            // the keyboard. It overrides nothing; F6 still works inside it.
            if (_demoStartAt > 0f && now >= _demoStartAt)
            {
                _demoStartAt = 0f;
                _nextTelemetryAt = 0f;
                if (!Autopilot.Enabled)
                {
                    Autopilot.SetEnabled(true);
                    RemoteInputOverride.SetActive(true);
                    Log.LogInfo("demo: autopilot engaged");
                }
            }

            if (_demoEndAt > 0f && now >= _demoEndAt)
            {
                _demoEndAt = 0f;
                if (Autopilot.Enabled)
                {
                    Autopilot.SetEnabled(false);
                    RemoteInputOverride.SetActive(false);
                    Log.LogInfo("demo: autopilot disengaged");
                }
            }

            if (!Autopilot.Enabled)
            {
                return;
            }

            Autopilot.Update(now);
        }

        /// <summary>
        /// Samples the car's position and speed on a timer, whenever the player is in the
        /// vehicle. Kept separate from the autopilot so the same numbers can be read while a
        /// human is driving, which is the only way to tell a broken override from a car that
        /// simply does not move.
        /// </summary>
        private void LogTelemetry(float now)
        {
            if (!GameRefs.InVehicle || GameRefs.PlayerCar == null || now < _nextTelemetryAt)
            {
                return;
            }
            _nextTelemetryAt = now + TelemetryIntervalSeconds;

            unsafe
            {
                UnityEngine.Vector3 position = GameRefs.PlayerCar.transform.position;
                float speed = GameRefs.PlayerCar.currentSpeed;
                bool cockpit = GameRefs.Global != null && Global.IsInVehicleCockpit;

                string deltaText;
                if (_haveLastPosition)
                {
                    UnityEngine.Vector3 delta = position - _lastCarPosition;
                    deltaText = "len=" + delta.magnitude.ToString("0.000");
                }
                else
                {
                    // First sample: a delta here would be the distance from the world origin and
                    // read as a teleport that never happened.
                    deltaText = "first-sample";
                    _haveLastPosition = true;
                }

                _lastCarPosition = position;

                Log.LogInfo("car telemetry: pos(" + Fmt(position) + ") " + deltaText +
                            " speed=" + speed.ToString("0.00") +
                            " cockpit=" + (cockpit ? "yes" : "no") +
                            " override=" + (RemoteInputOverride.Active ? "on" : "off") +
                            " throttle=" + RemoteInputOverride.Throttle.ToString("0.00") +
                            " ascend=" + RemoteInputOverride.AscendValue.ToString("0.00") +
                            " steer=" + RemoteInputOverride.Steer.ToString("0.00") +
                            " wp=" + Autopilot.DistanceToWaypoint.ToString("0") +
                            " clear=" + Autopilot.LeftClearance.ToString("0") + "/" +
                                        Autopilot.CenterClearance.ToString("0") + "/" +
                                        Autopilot.RightClearance.ToString("0") +
                            " near=" + Autopilot.NearClearance.ToString("0") +
                            " stuck=" + (Autopilot.Stuck ? "yes" : "no") +
                            " progress=" + Autopilot.ProgressPerSecond.ToString("0.0") +
                            " pilot=" + Autopilot.Decision);
            }

            // Read the game's own actions back, so "the car did not move" can be separated from
            // "the key never arrived". These are the values InControl is reporting right now.
            Log.LogInfo("car input: forward=" + GameInputMap.ValueOf("DriveForward").ToString("0.00") +
                        " backward=" + GameInputMap.ValueOf("DriveBackward").ToString("0.00") +
                        " steerLeft=" + GameInputMap.ValueOf("SteerLeft").ToString("0.00") +
                        " steerRight=" + GameInputMap.ValueOf("SteerRight").ToString("0.00") +
                        " ascend=" + GameInputMap.ValueOf("Ascend").ToString("0.00") +
                        " sink=" + GameInputMap.ValueOf("Sink").ToString("0.00") +
                        " patchHits=" + RemoteInputOverride.PrefixHits +
                        " pointerHits=" + RemoteInputOverride.PointerHits +
                        " unmatched=" + RemoteInputOverride.UnmatchedHits +
                        " external=" + RemoteInputOverride.ExternalHits +
                        " ptrs=[" + string.Join(", ", RemoteInputOverride.PointerSamples.ToArray()) + "]");
        }

        private static string Fmt(UnityEngine.Vector3 v)
        {
            return v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00");
        }

        /// <summary>
        /// Seat controls for iteration 3. The avatar is also seated automatically a few
        /// seconds after load, so the gate can be verified without anyone touching the keys.
        /// F7 forces the player into the driver seat; the scripted demo also does this at
        /// T+8s so iteration 4 can prove synthetic input moves the car.
        /// </summary>
        private void HandleSeatKeys(float now)
        {
            if (DebugOverlay.Pressed(UnityEngine.KeyCode.F3))
            {
                SeatRig.Sit(SeatKind.Driver, Log);
            }
            else if (DebugOverlay.Pressed(UnityEngine.KeyCode.F4))
            {
                SeatRig.Sit(SeatKind.Passenger, Log);
            }
            else if (DebugOverlay.Pressed(UnityEngine.KeyCode.F5))
            {
                SeatRig.Leave(Log);
            }
            else if (DebugOverlay.Pressed(UnityEngine.KeyCode.F7))
            {
                GameRefs.EnterVehicle(Log);
            }

            if (_seatRigAt > 0f && now >= _seatRigAt)
            {
                _seatRigAt = 0f;
                if (SeatRig.Occupant == SeatKind.None)
                {
                    Log.LogInfo("auto-seating an avatar in the passenger seat");
                    SeatRig.Sit(SeatKind.Passenger, Log);
                }
            }

            if (_enterCarAt > 0f && now >= _enterCarAt)
            {
                _enterCarAt = 0f;
                if (!GameRefs.InVehicle)
                {
                    Log.LogInfo("auto: entering the vehicle for the drive test");
                    GameRefs.EnterVehicle(Log);
                }
            }
        }

        public void OnGUI()
        {
            CoopPresence.Draw();
            CoopConnectionMenu.Draw();
            if (!DiagnosticsEnabled) return;

            if (!_onGuiLogged)
            {
                _onGuiLogged = true;
                Log.LogInfo("OnGUI is running: the debug overlay is being drawn");
            }

            TrafficReplication.DrawLabels();
            WorldNpcReplication.DrawLabels();
            DebugOverlay.Draw();
        }

        private bool _onGuiLogged;
    }
}
