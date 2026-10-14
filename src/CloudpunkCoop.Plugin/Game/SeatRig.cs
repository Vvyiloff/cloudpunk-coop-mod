using System.Collections.Generic;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    public enum SeatKind
    {
        None,
        Driver,
        Passenger,
    }

    /// <summary>
    /// Seat anchors on the player's car, and the avatar clone that occupies one.
    ///
    /// The game's own characters are not reusable for this: <c>Player.Instance</c> is a
    /// singleton wired into <c>Global</c>, and <c>Character</c> drives NPC behaviour and story
    /// flags. Cloning either wholesale would fight the game for control. Instead the visual
    /// mesh subtree is cloned on its own and stripped of behaviour, so it is inert.
    ///
    /// Anchors are discovered by name where possible and by an offset otherwise; the full
    /// car hierarchy is dumped so the fallback offsets can be tuned from evidence.
    /// </summary>
    public static class SeatRig
    {
        public static bool Ready { get; private set; }
        public static SeatKind Occupant { get; private set; } = SeatKind.None;
        public static GameObject Avatar { get; private set; }
        public static Transform DriverAnchor { get; private set; }
        public static Transform PassengerAnchor { get; private set; }
        private static Transform _localDriverAnchor;
        private static Transform _localPassengerAnchor;
        private static bool _onProxy;

        public static void AttachToProxy(Transform proxy, ManualLogSource log)
        {
            if (_onProxy) return;
            _localDriverAnchor = DriverAnchor;
            _localPassengerAnchor = PassengerAnchor;
            // The exterior car's _Interior_Position is the local origin of the cabin.
            Vector3 interiorOffset = GameRefs.PlayerCar.interiorPosition == null
                ? new Vector3(0f, -0.1f, 1f)
                : GameRefs.PlayerCar.interiorPosition.localPosition;
            DriverAnchor = CreateAnchor(proxy, "CP_RemoteSeat_Driver", interiorOffset + DriverSeatLocal);
            PassengerAnchor = CreateAnchor(proxy, "CP_RemoteSeat_Passenger", interiorOffset + PassengerSeatLocal);
            _onProxy = true;
            if (!Sit(SeatKind.Passenger, log))
                throw new System.InvalidOperationException("passenger visual is not ready");
        }

        public static void DetachFromProxy(ManualLogSource log)
        {
            if (!_onProxy) return;
            Leave(log);
            DriverAnchor = _localDriverAnchor;
            PassengerAnchor = _localPassengerAnchor;
            _localDriverAnchor = null;
            _localPassengerAnchor = null;
            _onProxy = false;
        }

        /// <summary>Builds anchors and dumps the car interior so they can be verified.</summary>
        public static void Initialize(ManualLogSource log)
        {
            if (Ready || GameRefs.PlayerCar == null)
            {
                return;
            }

            GameRefs.DumpHierarchy(GameRefs.PlayerCar.transform, "player car", log, 3);

            unsafe
            {
                VehicleInterior interior = GameRefs.Global.vehicleInterior;
                if (interior != null)
                {
                    GameRefs.DumpHierarchy(interior.interiorLocal, "vehicle interior", log, 3);
                    ResolveAnchors(interior, log);
                }
            }

            Ready = true;

            log.LogInfo("---- seat rig ----");
            log.LogInfo("driverAnchor   : " + (DriverAnchor == null ? "<fallback>" : DriverAnchor.name));
            log.LogInfo("passengerAnchor: " + (PassengerAnchor == null ? "<fallback>" : PassengerAnchor.name));
            log.LogInfo("------------------");
        }

        /// <summary>
        /// Seat positions measured from the dumped interior hierarchy, in the local space of
        /// <c>_Vehicle_Interior_Local</c>:
        ///
        ///   _Steering_Wheel          (-0.37, 0.51, -0.10)   driver controls
        ///   CameraSocket Interior    (-0.37, 0.82, -0.57)   driver's head
        ///   Point Light   [driver]   (-0.42, 0.59, -0.43)
        ///   Point Light (1)[passngr]  ( 0.40, 0.59, -0.43)   passenger side, mirrored on X
        ///
        /// -Z is rearward in this interior, so a seated body sits behind the wheel. The
        /// passenger seat is the driver seat mirrored across X, corroborated by the second
        /// point light landing at the matching height and depth.
        /// </summary>
        private static readonly Vector3 DriverSeatLocal = new Vector3(-0.37f, 0.55f, -0.45f);
        private static readonly Vector3 PassengerSeatLocal = new Vector3(0.38f, 0.55f, -0.45f);

        // "steering" is deliberately absent from the driver keywords: the only match would be
        // _Steering_Wheel, which is a control rather than a seat, and parenting a body into
        // it would place the avatar inside the wheel geometry.
        private static unsafe void ResolveAnchors(VehicleInterior interior, ManualLogSource log)
        {
            Transform root = interior.interiorLocal;

            // Both searches run before either anchor is created. Creating them in between let the
            // passenger search match the freshly named CP_Seat_Driver, because "seat" is a
            // passenger keyword.
            Transform driver = FindByKeywords(root, 0, "driver", "cockpit");
            Transform passenger = FindByKeywords(root, 0, "passenger", "chair", "seat");

            if (driver != null)
            {
                DriverAnchor = driver;
                log.LogInfo("driver anchor: found existing '" + driver.name + "'");
            }
            else
            {
                DriverAnchor = CreateAnchor(root, "CP_Seat_Driver", DriverSeatLocal);
                log.LogInfo("driver anchor: created at measured interior offset");
            }

            if (passenger != null)
            {
                PassengerAnchor = passenger;
                log.LogInfo("passenger anchor: found existing '" + passenger.name + "'");
            }
            else
            {
                PassengerAnchor = CreateAnchor(root, "CP_Seat_Passenger", PassengerSeatLocal);
                log.LogInfo("passenger anchor: created at mirrored interior offset");
            }
        }

        private static Transform CreateAnchor(Transform parent, string name, Vector3 localPosition)
        {
            var host = new GameObject(name);
            Transform t = host.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.identity;
            return t;
        }

        private static unsafe Transform FindByKeywords(Transform node, int depth, params string[] keywords)
        {
            if (depth > 5)
            {
                return null;
            }

            string name = node.name;
            if (name != null)
            {
                string lower = name.ToLowerInvariant();
                foreach (string keyword in keywords)
                {
                    if (lower.Contains(keyword))
                    {
                        return node;
                    }
                }
            }

            int count = node.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform hit = FindByKeywords(node.GetChild(i), depth + 1, keywords);
                if (hit != null)
                {
                    return hit;
                }
            }
            return null;
        }

        /// <summary>
        /// Clones the player's visual mesh and parents it into a seat. Returns false if there
        /// is nothing suitable to clone.
        /// </summary>
        public static bool Sit(SeatKind seat, ManualLogSource log)
        {
            if (seat == SeatKind.None || GameRefs.Player == null)
            {
                return false;
            }

            Leave(log);

            Transform anchor = seat == SeatKind.Driver ? DriverAnchor : PassengerAnchor;
            if (anchor == null)
            {
                log.LogWarning("seat " + seat + ": no anchor available");
                return false;
            }

            GameObject visual = FindVisual(GameRefs.Player.gameObject);
            if (visual == null)
            {
                log.LogWarning("seat " + seat + ": could not find a visual mesh under the player");
                return false;
            }

            GameObject clone = UnityEngine.Object.Instantiate(visual);
            if (clone == null)
            {
                log.LogError("seat " + seat + ": Instantiate returned null");
                return false;
            }

            clone.name = "CloudpunkCoop_Avatar_" + seat;
            StripToVisualsOnly(clone, log);

            Transform t = clone.transform;
            t.SetParent(anchor, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            Avatar = clone;
            Occupant = seat;

            log.LogInfo("seat " + seat + ": avatar '" + clone.name + "' placed under '" + anchor.name +
                        "' from source visual '" + visual.name + "'");
            return true;
        }

        public static void Leave(ManualLogSource log)
        {
            if (Avatar == null)
            {
                Occupant = SeatKind.None;
                return;
            }

            string name = Avatar.name;
            UnityEngine.Object.Destroy(Avatar);
            Avatar = null;
            Occupant = SeatKind.None;
            log.LogInfo("seat cleared, removed '" + name + "'");
        }

        /// <summary>
        /// Removes everything from the clone except renderers and animation, so it cannot
        /// take input, register with the story system, or be picked up by Easy Save 2.
        /// </summary>
        private static void StripToVisualsOnly(GameObject clone, ManualLogSource log)
        {
            Il2CppArrayBase<Component> components = clone.GetComponents<Component>();
            int removed = 0;

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                string typeName = component.GetIl2CppType().Name;
                bool keep = component.TryCast<Transform>()!=null || component.TryCast<Renderer>()!=null ||
                            component.TryCast<MeshFilter>()!=null || component.TryCast<Animator>()!=null ||
                            typeName=="MeshAnimator";
                if (keep)
                {
                    continue;
                }

                UnityEngine.Object.Destroy(component);
                removed++;
            }

            log.LogInfo("avatar stripped: kept renderers/animators, removed " + removed + " components");
        }

        /// <summary>First descendant that carries a renderer, i.e. the character model.</summary>
        private static GameObject FindVisual(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root.GetComponent<Renderer>() != null && root != GameRefs.Player.gameObject)
            {
                return root;
            }

            Transform t = root.transform;
            int count = t.childCount;
            for (int i = 0; i < count; i++)
            {
                GameObject hit = FindVisual(t.GetChild(i).gameObject);
                if (hit != null)
                {
                    return hit;
                }
            }
            return null;
        }
    }
}
