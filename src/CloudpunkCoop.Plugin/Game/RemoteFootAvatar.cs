using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    public static class RemoteFootAvatar
    {
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Actor");
        private static readonly CarSnapshotBuffer Timeline = new CarSnapshotBuffer();
        private static FootPoseMessage _pose;
        private static GameObject _root;
        private static MeshAnimator _anim;
        private static ReplicatedAnimation _playback;
        private static readonly Dictionary<uint,FootPoseMessage> States = new();
        private static readonly Queue<uint> Sequences = new();
        public static string AnimationDiagnostics => "footAnimApplied="+(_playback?.AppliedFrames ?? 0)+";footAnimChanges="+(_playback?.FrameChanges ?? 0)+";footAnimMissing="+(_playback?.MissingClips ?? 0);
        private static bool _failed;
        private static float _receivedAt;
        public static bool HavePose { get; private set; }
        public static bool Fresh=>HavePose && Time.realtimeSinceStartup-_receivedAt<2f;
        public static bool OnFoot => HavePose && _pose.OnFoot;
        public static int Scene => _pose.Scene;
        public static Vector3 LatestPosition => new Vector3(_pose.X, _pose.Y, _pose.Z);
        public static Transform Target => _root == null ? null : _root.transform;

        public static FootPoseMessage Capture(uint sequence)
        {
            var player = GameRefs.Player;
            bool foot = player != null && player.IsActive() &&
                (IndependentCoop.Enabled || CoopSession.Role != CoopRole.Guest || GuestMobility.LocalOnFoot);
            Vector3 position = player == null ? Vector3.zero : player.transform.position;
            float yaw = player == null ? 0 : player.transform.eulerAngles.y;
            int nativeState = !foot || player.anim == null ? 0 : (int)player.anim.GetCurrentState();
            int nativeDirection = !foot || player.anim == null ? 0 : (int)player.anim.currentDirection;
            byte state = nativeState>=0 && nativeState<=5 ? (byte)nativeState : (byte)0;
            byte direction = nativeDirection>=0 && nativeDirection<=7 ? (byte)nativeDirection : (byte)0;
            return new FootPoseMessage(sequence, SceneManager.GetActiveScene().buildIndex, foot,
                position.x, position.y, position.z, yaw, state, direction,
                !foot || player.anim == null || player.anim.currentAnimation == null ? "" : player.anim.currentAnimation.name,
                !foot || player.anim == null ? 0 : Math.Clamp(player.anim.currentFrame,0,100000));
        }

        public static void Receive(FootPoseMessage pose, float now)
        {
            if (!IndependentCoop.Enabled && CoopSession.Role == CoopRole.Host && pose.OnFoot != GuestMobility.OnFoot) return;
            if(HavePose && unchecked((int)(pose.Sequence-_pose.Sequence))<=0) return;
            if(HavePose && pose.Scene!=_pose.Scene)
            {Timeline.Clear();States.Clear();Sequences.Clear();if(_root!=null)_root.SetActive(false);}
            Quaternion q = Quaternion.Euler(0, pose.Yaw, 0);
            if (!Timeline.Add(new CarStateMessage(pose.X,pose.Y,pose.Z,q.x,q.y,q.z,q.w,0,0,0,0,0,0,0,0,pose.Sequence), now)) return;
            _pose = pose; HavePose = true; _receivedAt = now;
            States[pose.Sequence]=pose; Sequences.Enqueue(pose.Sequence);
            while(Sequences.Count>64) States.Remove(Sequences.Dequeue());
        }

        public static void Tick(float now)
        {
            if (!RegionReplication.LocalReady || !RegionReplication.SameRegion || !OnFoot || Scene != SceneManager.GetActiveScene().buildIndex ||
                now - _receivedAt > 2f || !GameRefs.Resolved)
            { if (_root != null) _root.SetActive(false); return; }
            if (_failed) return;
            try
            {
                if (_root == null)
                {
                    if (GameRefs.Player == null || GameRefs.Player.anim == null) return;
                    _root = new GameObject("CloudpunkCoop_RemoteFootActor");
                    _root.SetActive(false);
                    var clone = UnityEngine.Object.Instantiate((UnityEngine.Object)GameRefs.Player.anim.gameObject,
                        _root.transform, false).Cast<GameObject>();
                    foreach (var component in clone.GetComponentsInChildren<Component>(true))
                    {
                        if (component.TryCast<Transform>() != null || component.TryCast<Renderer>() != null ||
                            component.TryCast<MeshFilter>() != null || component.TryCast<MeshAnimator>() != null) continue;
                        UnityEngine.Object.DestroyImmediate(component);
                    }
                    _anim = clone.GetComponent<MeshAnimator>();
                    if (_anim == null) _anim = clone.GetComponentInChildren<MeshAnimator>(true);
                    if (_anim == null) throw new InvalidOperationException("Native MeshAnimator missing from avatar");
                    _anim.parentTransform = _root.transform;
                    _anim.audioSource = null;
                    _anim.isPlayer = false;
                    _anim.updateWhenOffscreen = true;
                    _playback = new ReplicatedAnimation(_anim);
                    clone.SetActive(true);
                }
                if (!Timeline.TrySample(now, out var from, out var to, out float t, out _)) return;
                Vector3 p = Vector3.Lerp(new Vector3(from.PositionX, from.PositionY, from.PositionZ),
                    new Vector3(to.PositionX, to.PositionY, to.PositionZ), t);
                Quaternion q = Quaternion.Slerp(new Quaternion(from.RotationX,from.RotationY,from.RotationZ,from.RotationW),
                    new Quaternion(to.RotationX,to.RotationY,to.RotationZ,to.RotationW),t);
                _root.SetActive(true);
                var state=States.TryGetValue(from.SequenceNumber,out var sampled)?sampled:_pose;
                _playback.Apply(state.Animation,state.Direction,state.Clip,state.Frame);
                _root.transform.SetPositionAndRotation(p,q);
            }
            catch (Exception ex) { _failed = true; if (_root != null) _root.SetActive(false); Log.LogWarning("remote pedestrian: " + ex); }
        }

        public static void Clear()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null; _anim = null; _playback=null; Timeline.Clear(); States.Clear(); Sequences.Clear(); HavePose = false; _failed = false;
        }
    }
}
