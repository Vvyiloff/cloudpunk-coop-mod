using System;
using BepInEx.Logging;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    /// <summary>Host confirms guest occupancy; local walking uses the native Player activation path.</summary>
    public static class GuestMobility
    {
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Mobility");
        private static MobilityStateMessage _state = new MobilityStateMessage(0, false, true, "E: exit / board at parking", 0, 0,0,0,0);
        private static bool _pending, _localFoot, _applied;
        private static float _pendingAt;
        private static GameObject _spawnObject;
        public static bool OnFoot => _state.OnFoot;
        public static bool LocalOnFoot => _localFoot;
        public static uint Revision => _state.Revision;
        public static string Status { get; private set; } = "E: exit / board at parking";
        private static ParkVolume _lastParking;

        public static void RequestToggle()
        {
            if (CoopSession.Role != CoopRole.Guest || !CoopSession.PeerOnline || !CarProxy.Ready || _pending) return;
            _pending = true; _pendingAt = Time.realtimeSinceStartup;
            Status = "Waiting for host boarding confirmation";
            CoopSession.SendMessage(new MobilityRequestMessage(_state.Revision, !OnFoot));
        }

        public static void HandleRequest(MobilityRequestMessage request)
        {
            if (CoopSession.Role != CoopRole.Host || !CoopSession.PeerOnline || !GameRefs.Resolved) return;
            var car = GameRefs.PlayerCar;
            if (car == null) return;
            Vector3 parkedPosition = car.parkObject != null && car.parkObject.activeInHierarchy
                ? car.parkObject.transform.position : car.transform.position;
            if (car.parkingVolume != null) _lastParking = car.parkingVolume;
            string denied = null;
            Vector3 destination = Vector3.zero;
            float yaw = 0;
            if (request.Revision != _state.Revision) denied = "Boarding state changed; press E again";
            else if (GameRefs.Global.IsInMenu() || GameRefs.Global.isLoadingSaveGame) denied = "World is loading";
            else if (request.OnFoot)
            {
                // Native parking also activates the host's pedestrian. Do not drop a passenger
                // from a flying HOVA or invent a ground point based on the camera position.
                if (GameRefs.Player == null || !GameRefs.Player.IsActive()) denied = "Driver must park the car first";
                else
                {
                    if (_lastParking == null || _lastParking.characterSpawnPoint == null)
                    {
                        foreach (var park in UnityEngine.Object.FindObjectsOfType<ParkVolume>())
                            if (park.characterSpawnPoint != null &&
                                Vector3.Distance(park.transform.position, parkedPosition) < 25f)
                            { _lastParking = park; break; }
                    }
                    if (_lastParking == null || _lastParking.characterSpawnPoint == null ||
                        Vector3.Distance(_lastParking.transform.position, parkedPosition) > 30f)
                        denied = "No valid parking exit beside this car";
                    else
                    {
                        var spawn = _lastParking.characterSpawnPoint.transform;
                        destination = spawn.position; yaw = spawn.eulerAngles.y;
                    }
                }
            }
            else if (!RemoteFootAvatar.HavePose || !RemoteFootAvatar.OnFoot ||
                RemoteFootAvatar.Scene != SceneManager.GetActiveScene().buildIndex ||
                Vector3.Distance(RemoteFootAvatar.LatestPosition, parkedPosition) > 8f)
                denied = "Move beside the shared car before boarding";

            if (denied == null)
            {
                _state = new MobilityStateMessage(_state.Revision + 1, request.OnFoot, true,
                    request.OnFoot ? "On foot; E beside the car to board" : "Passenger; F8 to request driving",
                    SceneManager.GetActiveScene().buildIndex, destination.x, destination.y, destination.z, yaw);
                Log.LogInfo("guest mobility committed: revision=" + _state.Revision + " foot=" + OnFoot);
                CoopSession.SendMessage(_state);
            }
            else CoopSession.SendMessage(new MobilityStateMessage(_state.Revision, _state.OnFoot, false, denied,
                SceneManager.GetActiveScene().buildIndex, _state.X, _state.Y, _state.Z, _state.Yaw));
        }

        public static void Receive(MobilityStateMessage state)
        {
            if (CoopSession.Role != CoopRole.Guest || !CoopSession.PeerOnline ||
                unchecked((int)(state.Revision - _state.Revision)) < 0) return;
            _pending = false; Status = state.Reason;
            if (!state.Accepted) return;
            if (_state.Revision != state.Revision || _state.OnFoot != state.OnFoot) _applied = false;
            _state = state;
        }

        public static void Tick()
        {
            if (CoopSession.Role == CoopRole.Host && GameRefs.PlayerCar != null && GameRefs.PlayerCar.parkingVolume != null)
                _lastParking = GameRefs.PlayerCar.parkingVolume;
            if (CoopSession.Role != CoopRole.Guest) return;
            if (!CoopSession.PeerOnline || !CarProxy.Ready) { _pending = false; return; }
            if (_pending && Time.realtimeSinceStartup - _pendingAt > 5f)
            { _pending = false; Status = "Boarding request timed out; press E again"; }
            if (_applied) return;
            try
            {
                if (_state.OnFoot)
                {
                    if (_state.Scene != SceneManager.GetActiveScene().buildIndex) return;
                    _spawnObject = new GameObject("CloudpunkCoop_GuestExitPoint");
                    _spawnObject.SetActive(false);
                    _spawnObject.transform.SetPositionAndRotation(new Vector3(_state.X, _state.Y, _state.Z),
                        Quaternion.Euler(0, _state.Yaw, 0));
                    var spawn = _spawnObject.AddComponent(Il2CppType.Of<CharacterSpawnPoint>()).Cast<CharacterSpawnPoint>();
                    spawn.registerAsLevelEntry = false;
                    spawn.dependingTriggerName = "";
                    spawn.action = new UnityEvent(); // No duplicate mission/reward callbacks on guest exit.
                    _spawnObject.SetActive(true);
                    _localFoot = true;
                    RemoteCameraRig.Stop();
                    SeatRig.Leave(Log);
                    GameRefs.Player.Activate(spawn, false, false, 0f);
                    GameRefs.Global.OnChangeVehicleState(0f);
                    if (GameRefs.Global.gdoc != null) GameRefs.Global.gdoc.referenceObject = GameRefs.Player.transform;
                }
                else if (_localFoot)
                {
                    _localFoot = false;
                    Player.Deactivate(0f);
                    GameRefs.Player.gameObject.SetActive(false);
                    GameRefs.Global.OnChangeVehicleState(0f);
                    RemoteCameraRig.Begin(CarProxy.Target);
                    SeatRig.Sit(SeatKind.Passenger, Log);
                    if (_spawnObject != null) UnityEngine.Object.Destroy(_spawnObject);
                    _spawnObject = null;
                }
                _applied = true;
                Log.LogInfo("guest mobility applied: revision=" + _state.Revision + " foot=" + _localFoot);
            }
            catch (Exception ex)
            { _applied = true; Status = "Native boarding transition failed"; Log.LogWarning("guest mobility: " + ex); }
        }

        public static void Reset()
        {
            _pending = _localFoot = _applied = false;
            _state = new MobilityStateMessage(0, false, true, "E: exit / board at parking", 0, 0,0,0,0);
            Status = _state.Reason; _lastParking = null;
            if (_spawnObject != null) UnityEngine.Object.Destroy(_spawnObject);
            _spawnObject = null;
        }
    }
}
