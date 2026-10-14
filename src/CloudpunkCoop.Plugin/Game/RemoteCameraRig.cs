using System;
using Cinemachine;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>One guest virtual camera, using the game's existing brain and output camera.</summary>
    public static class RemoteCameraRig
    {
        private static GameObject _object;
        private static CinemachineVirtualCamera _camera;
        private static Transform _oldReference;
        private static Bearroll.GDOC _gdoc;
        private static Vector3 _lastPosition;
        private static bool _havePosition;
        public static bool Active => _camera != null && _camera.isActiveAndEnabled;
        public static string LiveCameraName
        {
            get
            {
                var brain = GameRefs.Global == null ? null : GameRefs.Global.cameraBrain;
                return brain == null || brain.ActiveVirtualCamera == null ? "-" : brain.ActiveVirtualCamera.Name;
            }
        }

        public static void Begin(Transform target)
        {
            if (GameRefs.Global.cameraBrain == null)
                throw new InvalidOperationException("existing Cinemachine brain is not ready");
            _object = new GameObject("CloudpunkCoop_RemoteCamera");
            _object.SetActive(false);
            _camera = _object.AddComponent(Il2CppType.Of<CinemachineVirtualCamera>())
                .Cast<CinemachineVirtualCamera>();
            _camera.Follow = target;
            _camera.LookAt = target;
            _camera.Priority = 10000;
            LensSettings lens = _camera.m_Lens;
            Camera output = GameRefs.Global.mainCamera;
            if (output != null)
            {
                lens.FieldOfView = output.fieldOfView;
                lens.NearClipPlane = output.nearClipPlane;
                lens.FarClipPlane = output.farClipPlane;
            }
            _camera.m_Lens = lens;
            Transform owner = _camera.GetComponentOwner();
            var body = owner.gameObject.AddComponent(Il2CppType.Of<CinemachineTransposer>())
                .Cast<CinemachineTransposer>();
            body.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetWithWorldUp;
            body.m_FollowOffset = new Vector3(0f, 4f, -11f);
            body.m_XDamping = body.m_YDamping = body.m_ZDamping = 0f;
            var aim = owner.gameObject.AddComponent(Il2CppType.Of<CinemachineComposer>())
                .Cast<CinemachineComposer>();
            aim.m_TrackedObjectOffset = new Vector3(0f, 1f, 0f);
            aim.m_HorizontalDamping = aim.m_VerticalDamping = 0f;
            aim.m_ScreenX = 0.5f;
            aim.m_ScreenY = 0.5f;
            _camera.InvalidateComponentPipeline();
            _camera.PreviousStateIsValid = false;
            _object.SetActive(true);

            _gdoc = GameRefs.Global.gdoc;
            if (_gdoc != null)
            {
                _oldReference = _gdoc.referenceObject;
                _gdoc.referenceObject = target;
            }
        }

        public static void UpdatePose(Vector3 position, Quaternion rotation)
        {
            if (_camera == null) return;
            if (!_havePosition || (position - _lastPosition).sqrMagnitude > 100f * 100f)
                _camera.PreviousStateIsValid = false;
            _lastPosition = position;
            _havePosition = true;
        }

        public static void Stop()
        {
            if (_gdoc != null) _gdoc.referenceObject = _oldReference;
            if (_object != null)
            {
                _object.SetActive(false);
                UnityEngine.Object.Destroy(_object);
            }
            _object = null;
            _camera = null;
            _gdoc = null;
            _oldReference = null;
            _havePosition = false;
        }

        public static string Diagnostics
        {
            get
            {
                Camera output = GameRefs.Global == null ? null : GameRefs.Global.mainCamera;
                if (output == null) return "no output";
                Vector3 p = output.transform.position;
                return FormattableString.Invariant($"camPos={p.x:0.0},{p.y:0.0},{p.z:0.0};camMask={output.cullingMask};camNear={output.nearClipPlane:0.000};camFar={output.farClipPlane:0.0};camFov={output.fieldOfView:0.0};camForward={output.transform.forward.x:0.00},{output.transform.forward.y:0.00},{output.transform.forward.z:0.00};gdocRef={(_gdoc == null || _gdoc.referenceObject == null ? "-" : _gdoc.referenceObject.name)}");
            }
        }
    }
}
