using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using InControl;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>Overrides composed native axes only for the duration of PlayerCar.Inputs.</summary>
    public static class NativeDrivingInput
    {
        private static ManualLogSource _log;
        private static bool _installed;
        private static float _probeStarted = -1;
        private static float _probeHeight;
        private static DriverInputMessage _remote;
        private static float _remoteAt = float.NegativeInfinity;
        private static bool _haveRemote;
        public static long RemoteInputs { get; private set; }
        public static void Reset() { _haveRemote = false; _remoteAt = float.NegativeInfinity; _probeStarted = -1; }
        public static void Receive(DriverInputMessage input)
        {
            bool independent=IndependentCoop.Enabled;
            if (!CoopSession.PeerOnline || (independent
                ? IndependentCoop.State.DriverOf(IndependentCoop.LocalPlayer)!=IndependentCoop.PeerPlayer || input.Revision!=IndependentCoop.State.Revision
                : CoopSession.Role != CoopRole.Host || CoopSession.Seats.Driver != 2 || input.Revision != CoopSession.Seats.Revision)) return;
            if (_haveRemote && input.Revision == _remote.Revision &&
                unchecked((int)(input.Sequence - _remote.Sequence)) <= 0) return;
            _remote = input; _remoteAt = Time.realtimeSinceStartup; _haveRemote = true;
            RemoteInputs++;
        }
        public static bool ProbeActive => _probeStarted >= 0;
        public static long Hits { get; private set; }
        public static long BackgroundHits { get; private set; }
        public static string ProbeResult { get; private set; } = "not tested";

        public static void Install(ManualLogSource log)
        {
            if (_installed) return;
            _log = log;
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            new Harmony("dev.andre.cloudpunk-coop.native-driving").Patch(
                AccessTools.Method(typeof(PlayerCar), "Inputs", new[] { typeof(float) }),
                prefix: new HarmonyMethod(typeof(NativeDrivingInput).GetMethod(nameof(Prefix), flags)),
                finalizer: new HarmonyMethod(typeof(NativeDrivingInput).GetMethod(nameof(Finalizer), flags)));
            new Harmony("dev.andre.cloudpunk-coop.native-interact").Patch(
                AccessTools.Method(typeof(PlayerCar),"Update"),
                prefix:new HarmonyMethod(typeof(NativeDrivingInput).GetMethod(nameof(InteractPrefix),flags)),
                finalizer:new HarmonyMethod(typeof(NativeDrivingInput).GetMethod(nameof(InteractFinalizer),flags)));
            _installed = true;
            log.LogInfo("native driving: PlayerCar.Inputs axis seam installed (inactive)");
        }

        public static void StartProbe()
        {
            if (!_installed || GameRefs.PlayerCar == null || GameRefs.Global.IsInMenu()) return;
            if(IndependentCoop.Enabled && CoopSession.PeerOnline &&
                (IndependentCoop.LocalVehicle!=IndependentCoop.LocalPlayer || IndependentCoop.State.DriverOf(IndependentCoop.LocalPlayer)!=IndependentCoop.LocalPlayer)) return;
            RemoteInputOverride.SetActive(false);
            Autopilot.SetEnabled(false);
            _probeStarted = Time.realtimeSinceStartup;
            _probeHeight = GameRefs.PlayerCar.rBody.position.y;
            Hits = BackgroundHits = 0;
            ProbeResult = "zero 2s, lift 1s, zero 2s";
            _log.LogInfo("native driving probe: start height=" + _probeHeight);
        }

        public static void Tick()
        {
            if (!ProbeActive) return;
            float elapsed = Time.realtimeSinceStartup - _probeStarted;
            if (elapsed < 5f && GameRefs.PlayerCar != null) return;
            float height = GameRefs.PlayerCar == null ? _probeHeight : GameRefs.PlayerCar.rBody.position.y;
            ProbeResult = FormattableString.Invariant($"dy={height - _probeHeight:0.000}; hits={Hits}; backgroundHits={BackgroundHits}");
            _probeStarted = -1;
            _log.LogInfo("native driving probe: " + ProbeResult);
        }

        private sealed class SavedAxes
        {
            public PlayerOneAxisAction Drive, Steer, Altitude, Strafe;
            public InputControlState D, S, A, T;
            public PlayerCar Car;
            public bool LogSample;
        }

        private static unsafe void Prefix(PlayerCar __instance, out SavedAxes __state)
        {
            __state = null;
            bool remoteDriver = CoopSession.Role == CoopRole.Host && CoopSession.PeerOnline &&
                CoopSession.Seats.BothSeated && CoopSession.Seats.Driver == 2;
            bool independent=IndependentCoop.Enabled && CoopSession.PeerOnline;
            bool suppressLocal=independent && (IndependentCoop.State.DriverOf(IndependentCoop.LocalPlayer)!=IndependentCoop.LocalPlayer ||
                IndependentCoop.LocalVehicle!=IndependentCoop.LocalPlayer);
            if(independent) remoteDriver=IndependentCoop.State.DriverOf(IndependentCoop.LocalPlayer)==IndependentCoop.PeerPlayer;
            if ((!ProbeActive && !remoteDriver && !suppressLocal) || GameRefs.PlayerCar == null || __instance.Pointer != GameRefs.PlayerCar.Pointer ||
                GameRefs.Global.IsInMenu() || GameRefs.Global.isLoadingSaveGame) return;
            float elapsed = Time.realtimeSinceStartup - _probeStarted;
            if (ProbeActive && (elapsed >= 5f || elapsed < 0)) return;
            var actions = GameInput.Instance.playerActions;
            if (actions == null || (!independent && (!actions.Drive.enabled || !actions.Drive.ownerEnabled ||
                !actions.Altitude.enabled || !actions.Altitude.ownerEnabled))) return;
            __state = new SavedAxes { Drive = actions.Drive, Steer = actions.Steer,
                Altitude = actions.Altitude, Strafe = actions.Strafe,
                D = actions.Drive.thisState, S = actions.Steer.thisState,
                A = actions.Altitude.thisState, T = actions.Strafe.thisState };
            __state.Car = __instance;
            __state.LogSample = ProbeActive && (Hits == 0 || (elapsed >= 2f && elapsed < 2.03f));
            actions.Drive.thisState = default;
            actions.Steer.thisState = default;
            actions.Strafe.thisState = default;
            float lift = elapsed >= 2f && elapsed < 3f &&
                __instance.rBody.position.y < _probeHeight + 5f ? 1f : 0f;
            actions.Altitude.thisState = new InputControlState { State = lift != 0, Value = lift, RawValue = lift };
            if (remoteDriver && !ProbeActive)
            {
                bool fresh = _haveRemote && _remote.Revision == (independent ? IndependentCoop.State.Revision : CoopSession.Seats.Revision) &&
                    Time.realtimeSinceStartup - _remoteAt < 0.3f;
                actions.Drive.thisState = Axis(fresh ? _remote.Drive : 0);
                actions.Steer.thisState = Axis(fresh ? _remote.Steer : 0);
                actions.Altitude.thisState = Axis(fresh ? _remote.Altitude : 0);
                actions.Strafe.thisState = Axis(fresh ? _remote.Strafe : 0);
            }
            Hits++;
            if (!Application.isFocused) BackgroundHits++;
        }

        private static unsafe Exception Finalizer(Exception __exception, SavedAxes __state)
        {
            if (__state != null)
            {
                if (__state.LogSample)
                    _log.LogInfo("native driving probe output: gas=" + __state.Car.gasInput +
                        " alt=" + __state.Altitude.thisState.Value + " rising=" + __state.Car.isRising +
                        " floor=" + __state.Car.currentFloorLevel + " ceiling=" + __state.Car.layerMaxY +
                        " frozen=" + __state.Car.controlsFrozen + " routine=" + (__state.Car.currentRoutine != null));
                __state.Drive.thisState = __state.D;
                __state.Steer.thisState = __state.S;
                __state.Altitude.thisState = __state.A;
                __state.Strafe.thisState = __state.T;
            }
            return __exception;
        }

        private static InputControlState Axis(float value) => new InputControlState
            { State = value != 0, Value = value, RawValue = value };

        private sealed class SavedInteract
        {
            public InControl.PlayerAction Action;
            public InputControlState Current;
            public PlayerCar Car;
            public ParkVolume Volume;
        }
        private static void InteractPrefix(PlayerCar __instance,out SavedInteract __state)
        {
            __state=null;
            if(!IndependentCoop.Enabled || !CoopSession.PeerOnline || GameRefs.PlayerCar==null ||
                __instance.Pointer!=GameRefs.PlayerCar.Pointer || GameInput.Instance==null) return;
            var action=GameInput.Instance.playerActions.Interact;
            __state=new SavedInteract { Action=action, Current=action.thisState, Car=__instance };
            bool occupied=ParkingAuthority.OccupiedForLocalCar(__instance.parkingVolume);
            ParkingAuthority.SetPromptHidden(occupied);
            if(occupied)
            {
                __state.Volume=__instance.parkingVolume;
                // Native Update hides the exterior and cockpit landing prompts when no
                // available volume is present. Restore it after native UI processing.
                __instance.parkingVolume=null;
            }
            action.thisState=default;
        }
        private static Exception InteractFinalizer(Exception __exception,SavedInteract __state)
        {
            if(__state!=null)
            {
                __state.Action.thisState=__state.Current;
                if(__state.Volume!=null && __state.Car!=null) __state.Car.parkingVolume=__state.Volume;
            }
            return __exception;
        }
    }
}
