using System;
using System.Globalization;
using BepInEx.Logging;
using CloudpunkCoop.Protocol;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>Guest-only inert visual replica. Never instantiate a native PlayerCar script.</summary>
    public static class CarProxy
    {
        private static readonly CarSnapshotBuffer Buffer = new CarSnapshotBuffer();
        private static GameObject _root;
        private static Rigidbody _body;
        private static float _retryAt;
        private static float _lastSnapshotAt;
        private static bool _errorLogged, _wasUnderrun;
        private static Vector3 _lastPosition;
        private static bool _havePosition;
        public static bool Ready => _root != null;
        public static Transform Target => _root == null ? null : _root.transform;
        public static int MeshCount { get; private set; }
        public static int Buffered => Buffer.Count;
        public static long Rejected => Buffer.Rejected;
        public static long Teleports => Buffer.Teleports;
        public static long Underruns { get; private set; }
        public static float Travelled { get; private set; }
        public static float MaxFrameStep { get; private set; }
        public static string Status { get; private set; } = "waiting";

        public static bool Receive(CarStateMessage state, float now)
        {
            if (!Buffer.Add(state, now)) return false;
            _lastSnapshotAt = now;
            return true;
        }

        public static void Tick(float now, bool connected, ManualLogSource log)
        {
            if (!connected || (Buffer.Count > 0 && now - _lastSnapshotAt > 2f))
            {
                Stop(log);
                return;
            }
            if (!GameRefs.Resolved || GameRefs.PlayerCar == null || GameRefs.Player == null ||
                Buffer.Count == 0) return;
            // Global and the parked car exist before Continue is pressed. In that phase the
            // main camera lens is still the menu's, not a gameplay lens.
            if (GameRefs.Global.isLoadingSaveGame || GameRefs.Global.IsInMenu())
            {
                if (Ready) Stop(log);
                return;
            }
            if (!Ready && (GameRefs.Global.cameraBrain == null ||
                GameRefs.Global.cameraBrain.ActiveVirtualCamera == null ||
                GameRefs.Global.mainCamera == null || !GameRefs.Global.mainCamera.enabled)) return;
            try
            {
                if (!Ready)
                {
                    if (now < _retryAt) return;
                    _retryAt = now + 2f;
                    Create(log);
                }
                if (!Buffer.TrySample(now, out CarStateMessage from, out CarStateMessage to,
                    out float blend, out bool underrun)) return;

                Vector3 position = Vector3.Lerp(Position(from), Position(to), blend);
                Quaternion rotation = Quaternion.Slerp(Rotation(from), Rotation(to), blend);
                if (_havePosition)
                {
                    float step = (position - _lastPosition).magnitude;
                    // Teleports are intentionally snapped, not counted as driving.
                    if (step < 100f) { Travelled += step; MaxFrameStep = Math.Max(MaxFrameStep, step); }
                }
                _lastPosition = position;
                _havePosition = true;
                if (underrun && !_wasUnderrun) Underruns++;
                _wasUnderrun = underrun;
                _root.transform.SetPositionAndRotation(position, rotation);
                try { CarVisuals.Apply(from.SequenceNumber); }
                catch (Exception ex) { CarVisuals.Fail(ex, log); }
                LocalCarSuppress.Follow(position, rotation);
                RemoteCameraRig.UpdatePose(position, rotation);
                Status = underrun ? "latest (buffer underrun)" : "interpolating (100 ms)";
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    log.LogError("proxy: " + ex);
                }
                Stop(log);
                Status = "failed: " + ex.GetType().Name;
            }
        }

        private static void Create(ManualLogSource log)
        {
            Transform source = GameRefs.PlayerCar.transform;
            _root = new GameObject("CloudpunkCoop_RemoteCar");
            _root.SetActive(false);
            _root.transform.SetPositionAndRotation(source.position, source.rotation);
            _root.transform.localScale = source.lossyScale;
            MeshCount = 0;
            CopyVisualTree(source, _root.transform, true);
            try { CarVisuals.Create(source, _root.transform, log); }
            catch (Exception ex) { CarVisuals.Fail(ex, log); }
            if (MeshCount == 0) throw new InvalidOperationException("car has no mesh renderers");
            _body = _root.AddComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.detectCollisions = false;

            SeatRig.AttachToProxy(_root.transform, log);
            RemoteCameraRig.Begin(_root.transform);
            LocalCarSuppress.Begin();
            _root.SetActive(true);
            log.LogInfo("proxy: created visual-only car with " + MeshCount +
                " meshes, kinematic body, passenger and existing Cinemachine brain");
        }

        private static void CopyVisualTree(Transform source, Transform destination, bool root)
        {
            if (!root)
            {
                destination.localPosition = source.localPosition;
                destination.localRotation = source.localRotation;
                destination.localScale = source.localScale;
                destination.gameObject.layer = source.gameObject.layer;
            }
            MeshFilter filter = source.GetComponent<MeshFilter>();
            MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            if (filter != null && filter.sharedMesh != null && renderer != null)
            {
                destination.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer copy = destination.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                // The native occlusion system may have disabled the source renderer while
                // the save was loading. That transient culling result is not visual state.
                copy.enabled = true;
                copy.shadowCastingMode = renderer.shadowCastingMode;
                copy.receiveShadows = renderer.receiveShadows;
                MeshCount++;
            }
            for (int i = 0; i < source.childCount; i++)
            {
                Transform child = source.GetChild(i);
                // UI and effects carry game state; only meshes are cloned, never their scripts.
                if (child.GetComponent<Canvas>() != null || child.GetComponent<ParticleSystem>() != null)
                    continue;
                var node = new GameObject(child.name);
                node.transform.SetParent(destination, false);
                CopyVisualTree(child, node.transform, false);
                node.SetActive(child.gameObject.activeSelf);
            }
        }

        public static void Stop(ManualLogSource log)
        {
            bool existed = Ready || LocalCarSuppress.Active || RemoteCameraRig.Active;
            // Scene unload can destroy the root before Stop runs. Restore independently.
            RemoteCameraRig.Stop();
            CarVisuals.Clear();
            SeatRig.DetachFromProxy(log);
            LocalCarSuppress.Restore();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
            _root = null;
            _body = null;
            if (existed) log.LogInfo("proxy: removed; local car, player and camera restored");
            Buffer.Clear();
            _havePosition = false;
            _wasUnderrun = false;
            Status = "waiting";
        }

        private static Vector3 Position(CarStateMessage s) => new Vector3(s.PositionX, s.PositionY, s.PositionZ);
        private static Quaternion Rotation(CarStateMessage s) =>
            new Quaternion(s.RotationX, s.RotationY, s.RotationZ, s.RotationW).normalized;

        public static string PositionText => !Ready ? "-" :
            Target.position.x.ToString("0.00", CultureInfo.InvariantCulture) + "," +
            Target.position.y.ToString("0.00", CultureInfo.InvariantCulture) + "," +
            Target.position.z.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
