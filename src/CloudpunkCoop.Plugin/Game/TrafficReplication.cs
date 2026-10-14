using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    /// <summary>Host-owned ambient traffic; guest reuses a verified native pool, never random spawns.</summary>
    public static class TrafficReplication
    {
        private sealed class Original
        {
            public Vehicle Car;
            public bool Active, Enabled, Kinematic;
            public Vector3 Position, Velocity, Angular;
            public Quaternion Rotation;
            public Renderer[] Renderers;
            public bool[] RendererEnabled;
            public readonly CarSnapshotBuffer Timeline = new CarSnapshotBuffer();
            public bool NetworkActive;
        }
        private static readonly List<Original> Originals = new List<Original>();
        private static readonly List<(TrafficMessage Message, float Received)> Pending = new List<(TrafficMessage, float)>(32);
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Traffic");
        private static Global _global;
        private static MEC.CoroutineHandle _routine;
        private static bool _paused, _haveSequence, _failed;
        private static uint _sequence;
        private static float _lastReceived;
        private static IntPtr _manifestPool;
        private static string _fingerprint;
        private static int _renderScene = -1;
        private static int _receiveScene = -1;
        public static bool Active => Originals.Count > 0;
        public static string Status { get; private set; } = "waiting";
        public static int ActiveVehicles { get; private set; }
        public static int PoolCount { get; private set; }
        public static uint Sequence => _sequence;
        public static bool ShowLabels;
        private static bool _captureFailureLogged;
        public static void Fail(Exception ex)
        {
            Status = "failed: " + ex.Message;
            if (!_captureFailureLogged) { _captureFailureLogged = true; Log.LogWarning("traffic capture: " + ex); }
        }

        private static string Fingerprint(Global global)
        {
            var pool = global.vehicles;
            if (pool == null) return null;
            if (_manifestPool == pool.Pointer && _fingerprint != null) return _fingerprint;
            var text = new StringBuilder();
            for (int i = 0; i < pool.Length; i++)
            {
                Vehicle car = pool[i];
                text.Append(i).Append(':').Append(car == null ? "null" : car.name).Append('|');
                if (car != null)
                    foreach (var filter in car.GetComponentsInChildren<MeshFilter>(true))
                        text.Append(filter.sharedMesh == null ? "null" : filter.sharedMesh.name).Append(';');
                text.Append('\n');
            }
            _manifestPool = pool.Pointer;
            _fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
            if(Bootstrapper.DiagnosticsEnabled)
                System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
                    "coop-traffic-manifest-" + CoopSession.Role.ToString().ToLowerInvariant() + ".txt"), text.ToString());
            return _fingerprint;
        }

        public static TrafficMessage Capture(uint sequence)
        {
            if (!RegionReplication.LocalReady)
            { _manifestPool = IntPtr.Zero; _fingerprint = null; return null; }
            var g = Global.Instance;
            if (g == null || g.vehicles == null || g.IsInMenu() || g.isLoadingSaveGame) return null;
            var pool = g.vehicles;
            if (pool.Length > TrafficMessage.MaxVehicles)
                throw new InvalidOperationException("Traffic pool exceeds wire limit: " + pool.Length);
            var states = new TrafficVehicleState[pool.Length];
            ActiveVehicles = 0; PoolCount = pool.Length;
            for (int i = 0; i < pool.Length; i++)
            {
                var car = pool[i];
                Vector3 p = car == null ? Vector3.zero : car.transform.position;
                Quaternion q = car == null ? Quaternion.identity : car.transform.rotation;
                bool active = car != null && car.gameObject.activeInHierarchy;
                if (active) ActiveVehicles++;
                states[i] = new TrafficVehicleState((ushort)i, active, p.x, p.y, p.z, q.x, q.y, q.z, q.w);
            }
            Status = "authoritative";
            _sequence = sequence;
            return new TrafficMessage(sequence, SceneManager.GetActiveScene().buildIndex, Fingerprint(g), states);
        }

        public static void Receive(TrafficMessage message, float now)
        {
            // Foreign/loading frames must not consume the local sequence window.
            if (!RegionReplication.LocalReady || !RegionReplication.SameRegion ||
                message.Scene != SceneManager.GetActiveScene().buildIndex) return;
            if (_receiveScene != -1 && _receiveScene != message.Scene) Stop();
            if (_haveSequence && unchecked((int)(message.SequenceNumber - _sequence)) <= 0) return;
            _receiveScene = message.Scene;
            _haveSequence = true; _sequence = message.SequenceNumber; _lastReceived = now;
            if (Pending.Count == 32) Pending.RemoveAt(0);
            Pending.Add((message, now));
        }

        private static void Begin(Global g)
        {
            _renderScene = SceneManager.GetActiveScene().buildIndex;
            _global = g; _routine = g.vehicleRoutine;
            int paused = MEC.Timing.PauseCoroutines(_routine);
            if (paused == 0) throw new InvalidOperationException("Native traffic routine could not be paused.");
            _paused = true;
            for (int i = 0; i < g.vehicles.Length; i++)
            {
                var car = g.vehicles[i];
                var saved = new Original { Car = car };
                Originals.Add(saved);
                if (car == null) continue;
                saved.Active = car.gameObject.activeSelf; saved.Enabled = car.enabled;
                saved.Position = car.transform.position; saved.Rotation = car.transform.rotation;
                var renderers = car.GetComponentsInChildren<Renderer>(true);
                saved.Renderers = new Renderer[renderers.Length]; saved.RendererEnabled = new bool[renderers.Length];
                for (int r = 0; r < renderers.Length; r++)
                { saved.Renderers[r] = renderers[r]; saved.RendererEnabled[r] = renderers[r].enabled; }
                if (car.rBody != null)
                {
                    saved.Kinematic = car.rBody.isKinematic;
                    saved.Velocity = car.rBody.velocity; saved.Angular = car.rBody.angularVelocity;
                    car.rBody.isKinematic = true;
                }
                car.enabled = false;
                car.gameObject.SetActive(false);
            }
            Log.LogInfo("guest traffic: paused native spawning; bound " + Originals.Count + " verified pool vehicles");
        }

        public static void Tick(float now)
        {
            if (CoopSession.Role != CoopRole.Guest) return;
            if (!RegionReplication.LocalReady || !RegionReplication.SameRegion)
            { Stop(); return; }
            int scene = SceneManager.GetActiveScene().buildIndex;
            if ((_renderScene != -1 && _renderScene != scene) || (_receiveScene != -1 && _receiveScene != scene))
            { Stop(); return; }
            if (!CoopSession.PeerOnline || (_haveSequence && now - _lastReceived > 2f))
            { Stop(); return; }
            var g = Global.Instance;
            if (g == null || g.IsInMenu() || g.isLoadingSaveGame)
            { if (Active) Stop(); return; }
            if (_failed || Pending.Count == 0 && !Active) return;
            try
            {
                if (_global != null && _global.Pointer != g.Pointer) Stop();
                while (Pending.Count > 0)
                {
                    var packet = Pending[0]; Pending.RemoveAt(0);
                    // A queued frame can outlive the scene in which it was received.
                    if (packet.Message.Scene != SceneManager.GetActiveScene().buildIndex) continue;
                    var states = packet.Message.Vehicles;
                    if (g.vehicles == null ||
                        states.Length != g.vehicles.Length || packet.Message.PoolFingerprint != Fingerprint(g))
                        throw new InvalidOperationException("Traffic scene or pool fingerprint mismatch.");
                    foreach (var state in states)
                        if (state.Id >= states.Length) throw new InvalidOperationException("Traffic ID outside pool.");
                    if (!Active) Begin(g);
                    PoolCount = states.Length;
                    foreach (var state in states)
                    {
                        Original saved = Originals[state.Id];
                        if (saved.Car == null) continue;
                        if (saved.NetworkActive != state.Active) saved.Timeline.Clear();
                        saved.NetworkActive = state.Active;
                        if (!state.Active) { saved.Car.gameObject.SetActive(false); continue; }
                        saved.Timeline.Add(new CarStateMessage(state.X, state.Y, state.Z,
                            state.Qx, state.Qy, state.Qz, state.Qw, 0,0,0,0,0,0,0,0, packet.Message.SequenceNumber), packet.Received);
                    }
                }
                ActiveVehicles = 0;
                foreach (var saved in Originals)
                {
                    if (saved.Car == null || !saved.NetworkActive) continue;
                    if (!saved.Timeline.TrySample(now, out var from, out var to, out float t, out _)) continue;
                    Vector3 p = Vector3.Lerp(new Vector3(from.PositionX, from.PositionY, from.PositionZ),
                        new Vector3(to.PositionX, to.PositionY, to.PositionZ), t);
                    Quaternion q = Quaternion.Slerp(new Quaternion(from.RotationX, from.RotationY, from.RotationZ, from.RotationW),
                        new Quaternion(to.RotationX, to.RotationY, to.RotationZ, to.RotationW), t);
                    saved.Car.transform.SetPositionAndRotation(p, q);
                    saved.Car.enabled = false;
                    if (saved.Car.rBody != null) saved.Car.rBody.isKinematic = true;
                    saved.Car.gameObject.SetActive(true);
                    foreach (Renderer renderer in saved.Renderers) if (renderer != null) renderer.enabled = true;
                    ActiveVehicles++;
                }
                Status = "replicating";
            }
            catch (Exception ex)
            { Stop(); _failed = true; Status = "failed: " + ex.Message; Log.LogWarning("traffic replication: " + ex); }
        }

        public static void Stop()
        {
            foreach (var saved in Originals)
            {
                if (saved.Car == null) continue;
                RestoreNative(() => saved.Car.gameObject.SetActive(false));
                RestoreNative(() => saved.Car.transform.SetPositionAndRotation(saved.Position, saved.Rotation));
                RestoreNative(() =>
                {
                    if (saved.Car.rBody == null) return;
                    saved.Car.rBody.isKinematic = saved.Kinematic;
                    if (!saved.Kinematic) { saved.Car.rBody.velocity = saved.Velocity; saved.Car.rBody.angularVelocity = saved.Angular; }
                });
                RestoreNative(() => saved.Car.enabled = saved.Enabled);
                for (int i = 0; saved.Renderers != null && i < saved.Renderers.Length; i++)
                {
                    int index = i;
                    RestoreNative(() => { if (saved.Renderers[index] != null) saved.Renderers[index].enabled = saved.RendererEnabled[index]; });
                }
                RestoreNative(() => saved.Car.gameObject.SetActive(saved.Active));
            }
            Originals.Clear(); Pending.Clear();
            if (_paused) { RestoreNative(() => MEC.Timing.ResumeCoroutines(_routine)); _paused = false; }
            _global = null; _haveSequence = false; ActiveVehicles = 0; Status = "waiting";
            _renderScene = _receiveScene = -1; _manifestPool = IntPtr.Zero; _fingerprint = null;
            _failed = false;
        }

        private static void RestoreNative(Action restore)
        {
            try { restore(); }
            catch (Exception ex) { Log.LogWarning("traffic restore: " + ex.Message); }
        }

        public static void DrawLabels()
        {
            if (!ShowLabels || !GameRefs.Resolved || !CoopSession.PeerOnline || GameRefs.Global.mainCamera == null) return;
            var g = Global.Instance;
            if (g == null || g.vehicles == null) return;
            for (int i = 0; i < g.vehicles.Length; i++)
            {
                var car = g.vehicles[i];
                if (car == null || !car.gameObject.activeInHierarchy) continue;
                Vector3 p = g.mainCamera.WorldToScreenPoint(car.transform.position + Vector3.up * 2f);
                if (p.z > 0 && p.z < 150f && p.x >= 0 && p.x < Screen.width)
                    GUI.Box(new Rect(p.x - 35, Screen.height - p.y, 70, 22), "NPC " + i);
            }
        }
    }
}
