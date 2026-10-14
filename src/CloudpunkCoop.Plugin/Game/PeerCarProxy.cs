using System;
using System.Globalization;
using BepInEx.Logging;
using CloudpunkCoop.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    /// <summary>One inert peer vehicle, independent of the native local vehicle on either role.</summary>
    public static class PeerCarProxy
    {
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Peer Car");
        private static readonly CarSnapshotBuffer Buffer = new CarSnapshotBuffer();
        private static GameObject _root;
        private static Rigidbody _body;
        private static readonly System.Collections.Generic.List<Collider> CollisionShapes = new();
        public static int ColliderCount => CollisionShapes.Count;
        public static int OverlapCorrections { get; private set; }
        public static float LastPenetration { get; private set; }
        private static Vector3 _desiredPosition;
        private static Quaternion _desiredRotation;
        private static bool _hasDesired,_snapPhysics;
        private static bool _awaitJoinClearance;
        private static int _scene, _rootScene;
        private static bool _parked, _haveState, _havePeer, _errorLogged;
        private static byte _peerId;
        private static float _lastSnapshotAt, _retryAt;

        public static Transform Target => _root == null ? null : _root.transform;
        public static bool Ready => _root != null && _root.activeInHierarchy;
        public static int MeshCount { get; private set; }
        public static string Status { get; private set; } = "waiting";
        public static string PositionText => !Ready ? "-" :
            Target.position.x.ToString("0.00", CultureInfo.InvariantCulture) + "," +
            Target.position.y.ToString("0.00", CultureInfo.InvariantCulture) + "," +
            Target.position.z.ToString("0.00", CultureInfo.InvariantCulture);

        public static bool Receive(CarStateMessage state, int scene, bool parked, float now)
        {
            // Validate sequence, time and pose before changing presentation metadata.
            long teleports=Buffer.Teleports;
            if (!Buffer.Add(state, now)) return false;
            if(Buffer.Teleports!=teleports) _snapPhysics=true;
            if (_haveState && (_scene != scene || _parked != parked))
            {
                // Never blend across a scene or between driving and a parked sender pose.
                Buffer.Clear();
                Buffer.Add(state, now);
                _snapPhysics=true;
            }
            _scene = scene;
            _parked = parked;
            _haveState = true;
            _lastSnapshotAt = now;
            return true;
        }

        public static void Tick(float now, bool connected, byte peerId)
        {
            if (!connected)
            {
                Clear();
                Status = "disconnected";
                return;
            }
            if (_havePeer && _peerId != peerId)
            {
                Clear();
                Status = "waiting for peer pose";
                return;
            }
            _peerId = peerId;
            _havePeer = true;
            if (!float.IsFinite(now) || (_haveState && now - _lastSnapshotAt > 2f))
            {
                Clear();
                Status = "stale";
                return;
            }

            try
            {
                if (!GameRefs.Resolved || GameRefs.Global == null || GameRefs.PlayerCar == null)
                {
                    Clear();
                    Status = "waiting for world";
                    return;
                }
                if (GameRefs.Global.isLoadingSaveGame || GameRefs.Global.IsInMenu())
                {
                    Clear();
                    Status = "menu/loading";
                    return;
                }
                if (!_haveState || Buffer.Count == 0)
                {
                    Status = "waiting for peer pose";
                    return;
                }
                int localScene = SceneManager.GetActiveScene().buildIndex;
                if (_scene != localScene)
                {
                    RemoveVisuals();
                    Status = "peer in another scene";
                    return;
                }
                if (_root != null && _rootScene != localScene) RemoveVisuals();
                if (!Buffer.TrySample(now, out CarStateMessage from, out CarStateMessage to,
                    out float blend, out bool underrun)) return;

                Vector3 position = Vector3.Lerp(Position(from), Position(to), blend);
                Quaternion rotation = Quaternion.Slerp(Rotation(from), Rotation(to), blend);
                if (_root == null)
                {
                    if (now < _retryAt) return;
                    _retryAt = now + 2f;
                    Create(peerId, localScene, position, rotation);
                }
                _desiredPosition=position; _desiredRotation=rotation; _hasDesired=true;
                try { CarVisuals.Apply(from.SequenceNumber); }
                catch (Exception ex) { CarVisuals.Fail(ex, Log); }
                Status = (_parked ? "parked: " : "driving: ") +
                    (underrun ? "latest (buffer underrun)" : "interpolating (100 ms)");
            }
            catch (Exception ex)
            {
                RemoveVisuals();
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.LogWarning("peer car: " + ex);
                }
                Status = "failed: " + ex.GetType().Name + " " + ex.Message.Replace(';', ',').Replace('\n', ' ');
            }
        }

        public static void PhysicsTick()
        {
            var car=GameRefs.PlayerCar;
            LastPenetration=0;
            if(!Ready || !_hasDesired || _body==null || car==null || car.rBody==null || !IndependentCoop.Playing) return;
            if(_snapPhysics)
            {
                _body.position=_desiredPosition; _body.rotation=_desiredRotation; _snapPhysics=false;
            }
            else {_body.MovePosition(_desiredPosition);_body.MoveRotation(_desiredRotation);}
            if(IndependentCoop.OwnParked) {_awaitJoinClearance=false;_body.detectCollisions=true;}
            if(_awaitJoinClearance && IndependentCoop.LocalPlayer==2 &&
                (car.rBody.position-_desiredPosition).sqrMagnitude<0.25f) SeparateInitialSpawn(car);
            var localRoot=IndependentCoop.OwnParked && car.parkObject!=null ? car.parkObject.gameObject : car.gameObject;
            foreach(var local in localRoot.GetComponentsInChildren<Collider>())
            {
                if(local.isTrigger || !local.enabled || !local.gameObject.activeInHierarchy ||
                    !IndependentCoop.OwnParked && (local.attachedRigidbody==null || local.attachedRigidbody.Pointer!=car.rBody.Pointer))continue;
                foreach(var remote in CollisionShapes)
                {
                    if(remote==null || !remote.enabled || !remote.gameObject.activeInHierarchy) continue;
                    Vector3 relative=_root.transform.InverseTransformPoint(remote.transform.position);
                    Vector3 p=_desiredPosition+_desiredRotation*Vector3.Scale(relative,_root.transform.lossyScale);
                    Quaternion q=_desiredRotation*Quaternion.Inverse(_root.transform.rotation)*remote.transform.rotation;
                    if(Physics.ComputePenetration(local,local.transform.position,local.transform.rotation,remote,p,q,out _,out float depth)) LastPenetration=Math.Max(LastPenetration,depth);
                }
            }
            if(_awaitJoinClearance && LastPenetration<0.005f) {_awaitJoinClearance=false;_body.detectCollisions=true;}
        }

        private static void SeparateInitialSpawn(PlayerCar car)
        {
            // Identical saves spawn both cars at one point. Choose a clear neighbouring
            // flight position before enabling contacts; otherwise PhysX launches one car.
            var box=car.vehicleCollider==null ? null : car.vehicleCollider.TryCast<BoxCollider>();
            if(box==null || !box.enabled) return;
            var bounds=box.bounds;Vector3 origin=car.rBody.position;
            foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
            {
                float spacing=2*(Math.Abs(direction.x)*bounds.extents.x+Math.Abs(direction.z)*bounds.extents.z)+0.3f;
                Vector3 shift=direction*spacing, candidate=origin+shift;
                if(car.parkingVolume!=null && car.parkingVolume.volumeCollider!=null &&
                    !car.parkingVolume.volumeCollider.bounds.Intersects(new Bounds(bounds.center+shift,bounds.size))) continue;
                bool clear=true;
                foreach(var hit in Physics.OverlapBox(bounds.center+shift,bounds.extents,Quaternion.identity,~0,QueryTriggerInteraction.Ignore))
                {
                    if(hit==null || hit.attachedRigidbody!=null && hit.attachedRigidbody.Pointer==car.rBody.Pointer || hit.transform.IsChildOf(_root.transform)) continue;
                    clear=false;break;
                }
                if(!clear) continue;
                car.rBody.position=candidate;OverlapCorrections++;return;
            }
        }

        private static void Create(byte peerId, int scene, Vector3 position, Quaternion rotation)
        {
            Transform source = GameRefs.PlayerCar.transform;
            _root = new GameObject("CloudpunkCoop_PeerCar_" + peerId);
            _root.SetActive(false);
            _rootScene = scene;
            _root.transform.SetPositionAndRotation(position, rotation);
            _root.transform.localScale = source.lossyScale;
            _root.layer = source.gameObject.layer;
            MeshCount = 0;
            CopyVisualTree(source, _root.transform, true);
            if (MeshCount == 0) throw new InvalidOperationException("car has no mesh renderers");
            _body = _root.AddComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.detectCollisions = false;
            _awaitJoinClearance=true;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _body.interpolation=RigidbodyInterpolation.Interpolate;
            _snapPhysics=true;
            if(CollisionShapes.Count==0) throw new InvalidOperationException("Peer car has no supported solid collision shape");
            try { CarVisuals.Create(source, _root.transform, Log); }
            catch (Exception ex) { CarVisuals.Fail(ex, Log); }
            _root.SetActive(true);
            _errorLogged = false;
            Log.LogInfo("peer car: created peer " + peerId + " with " + MeshCount + " meshes");
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
            foreach(var original in source.GetComponents<Collider>())
            {
                // Unity removes inactive native driving colliders from the physics
                // world while the owner walks; attachedRigidbody then returns null.
                // Their authored shape remains a valid replica template.
                if(original.isTrigger)continue;
                if(original.attachedRigidbody!=null && GameRefs.PlayerCar.rBody!=null &&
                    original.attachedRigidbody.Pointer!=GameRefs.PlayerCar.rBody.Pointer)continue;
                Collider shape=null;
                var box=original.TryCast<BoxCollider>();
                var capsule=original.TryCast<CapsuleCollider>();
                var sphere=original.TryCast<SphereCollider>();
                var mesh=original.TryCast<MeshCollider>();
                if(box!=null) { var c=destination.gameObject.AddComponent<BoxCollider>(); c.center=box.center; c.size=box.size; shape=c; }
                else if(capsule!=null) { var c=destination.gameObject.AddComponent<CapsuleCollider>(); c.center=capsule.center; c.radius=capsule.radius; c.height=capsule.height; c.direction=capsule.direction; shape=c; }
                else if(sphere!=null) { var c=destination.gameObject.AddComponent<SphereCollider>(); c.center=sphere.center; c.radius=sphere.radius; shape=c; }
                else if(mesh!=null && mesh.sharedMesh!=null)
                {
                    if(mesh.convex){var c=destination.gameObject.AddComponent<MeshCollider>();c.sharedMesh=mesh.sharedMesh;c.convex=true;shape=c;}
                    else {var bounds=mesh.sharedMesh.bounds;var c=destination.gameObject.AddComponent<BoxCollider>();c.center=bounds.center;c.size=bounds.size;shape=c;}
                }
                if(shape==null) continue;
                shape.sharedMaterial=original.sharedMaterial;
                // vehicleCollider is the explicit native hull even when the local
                // driving controller is parked/inactive. Do not enable cosmetic shapes.
                shape.enabled=original.enabled || GameRefs.PlayerCar.vehicleCollider!=null &&
                    original.Pointer==GameRefs.PlayerCar.vehicleCollider.Pointer;
                // Native player-car layers can exclude another player-car layer.
                if(Physics.GetIgnoreLayerCollision(destination.gameObject.layer,original.gameObject.layer))
                    destination.gameObject.layer=0;
                CollisionShapes.Add(shape);
            }
            MeshFilter filter = source.GetComponent<MeshFilter>();
            MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            if (filter != null && filter.sharedMesh != null && renderer != null)
            {
                destination.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer copy = destination.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                // Native loading/occlusion may temporarily disable the template renderer.
                copy.enabled = true;
                copy.shadowCastingMode = renderer.shadowCastingMode;
                copy.receiveShadows = renderer.receiveShadows;
                MeshCount++;
            }
            for (int i = 0; i < source.childCount; i++)
            {
                Transform child = source.GetChild(i);
                if (child.GetComponent<Canvas>() != null || child.GetComponent<ParticleSystem>() != null)
                    continue;
                GameObject node = new GameObject(child.name);
                node.transform.SetParent(destination, false);
                CopyVisualTree(child, node.transform, false);
                node.SetActive(child.gameObject.activeSelf);
            }
        }

        private static void RemoveVisuals()
        {
            CarVisuals.Clear();
            if (_root != null)
            {
                _root.SetActive(false);
                UnityEngine.Object.Destroy(_root);
            }
            _root = null;
            _body=null;
            CollisionShapes.Clear();
            _hasDesired=false; _snapPhysics=true; LastPenetration=0;
            MeshCount = 0;
        }

        public static void Clear()
        {
            RemoveVisuals();
            Buffer.Clear();
            _haveState = false;
            _havePeer = false;
            _parked = false;
            _scene = _rootScene = 0;
            _lastSnapshotAt = _retryAt = 0f;
            _errorLogged = false;
            Status = "waiting";
        }

        private static Vector3 Position(CarStateMessage state) =>
            new Vector3(state.PositionX, state.PositionY, state.PositionZ);
        private static Quaternion Rotation(CarStateMessage state) =>
            new Quaternion(state.RotationX, state.RotationY, state.RotationZ, state.RotationW).normalized;
    }
}
