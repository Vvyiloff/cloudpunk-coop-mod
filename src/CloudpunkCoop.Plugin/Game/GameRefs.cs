using BepInEx.Logging;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>
    /// Cached handles to the game's singletons.
    ///
    /// Every one of these is a MonoBehaviour with a static <c>Instance</c> that the game
    /// assigns during its own startup, so they are null until the world exists. Resolve()
    /// is called repeatedly by <see cref="Bootstrapper"/> until the world exists.
    ///
    /// Names are unqualified on purpose: the game's own code lives in the global namespace
    /// (see docs/01-architecture.md), so <c>Global</c>, <c>Player</c> and <c>PlayerCar</c>
    /// resolve without a using directive.
    ///
    /// Two IL2CPP interop rules apply throughout: generated properties are declared
    /// <c>unsafe</c>, so reads sit inside <c>unsafe</c> blocks; and BepInEx's interpolated
    /// log handler supports neither alignment nor format specifiers, so log lines are built
    /// with plain concatenation and <c>PadRight</c>.
    /// </summary>
    public static class GameRefs
    {
        public static Global Global { get; private set; }
        public static GameInput GameInput { get; private set; }
        public static Player Player { get; private set; }
        public static PlayerCar PlayerCar { get; private set; }

        public static bool Resolved { get; private set; }

        public static void Refresh()
        {
            if(!Resolved) return;
            var current=global::Global.Instance;
            if(current==null) return;
            Global=current;
            Player=global::Player.Instance;
            PlayerCar=current.playerCar;
            GameInput=global::GameInput.Instance;
        }

        /// <summary>Wrapper around <c>Global.IsInVehicle</c>.</summary>
        public static bool InVehicle
        {
            get
            {
                if (Global == null)
                {
                    return false;
                }
                unsafe
                {
                    return Global.IsInVehicle;
                }
            }
        }

        /// <summary>
        /// Put the player into the driver seat of the current <see cref="PlayerCar"/> so that
        /// <c>PlayerCar</c> physics and input handling activate. Done by feeding the game's own
        /// <c>UsableCar.OnInteract</c> path through <c>Global.ChangePlayerCar</c>, so the car is
        /// never in a state the game itself did not author.
        /// </summary>
        public static bool EnterVehicle(ManualLogSource log)
        {
            try
            {
                if (Global == null || PlayerCar == null)
                {
                    log.LogWarning("EnterVehicle: Global or PlayerCar is null");
                    return false;
                }

                if (InVehicle)
                {
                    log.LogInfo("EnterVehicle: already in a vehicle");
                    return true;
                }

                UsableCar uc = null;
                GameObject carObject = PlayerCar.gameObject;
                uc = carObject.GetComponent<UsableCar>();
                if (uc == null)
                {
                    uc = carObject.GetComponentInChildren<UsableCar>();
                }

                if (uc == null)
                {
                    log.LogWarning("EnterVehicle: no UsableCar component found on PlayerCar");
                    return false;
                }

                int carID = uc.carID;
                unsafe
                {
                    bool result = Global.ChangePlayerCar(carID, uc);
                    log.LogInfo("EnterVehicle: ChangePlayerCar(" + carID + ") => " + result);
                    return result;
                }
            }
            catch (System.Exception ex)
            {
                log.LogError("EnterVehicle failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// One resolution pass. Returns false while the game world is still loading.
        /// Never throws: a half-initialised world is the normal case, not an error.
        /// </summary>
        public static bool Resolve(ManualLogSource log)
        {
            if (Resolved)
            {
                return true;
            }

            Global = Global.Instance;
            if (Global == null)
            {
                return false;
            }

            GameInput = GameInput.Instance;
            Player = Player.Instance;
            unsafe
            {
                PlayerCar = Global.playerCar;
            }

            Resolved = true;

            log.LogInfo("---- Cloudpunk Coop: game references resolved ----");
            log.LogInfo("Global      : " + Describe(Global));
            log.LogInfo("GameInput   : " + Describe(GameInput));
            log.LogInfo("Player      : " + Describe(Player));
            log.LogInfo("PlayerCar   : " + Describe(PlayerCar));

            LogSceneContext(log);
            LogCarTuning(log);

            log.LogInfo("---------------------------------------------");
            return true;
        }

        public static string Describe(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return "<null>";
            }

            unsafe
            {
                return obj.GetType().Name + " instance='" + obj.name + "'";
            }
        }

        private static void LogSceneContext(ManualLogSource log)
        {
            unsafe
            {
                log.LogInfo("mainCamera  : " + Describe(Global.mainCamera));
                log.LogInfo("uiCamera    : " + Describe(Global.uiCamera));
                log.LogInfo("cameraBrain : " + (Global.cameraBrain == null ? "null" : "present"));
                log.LogInfo("sectors     : " + (Global.sectors == null ? "null" : "present"));
                log.LogInfo("playerCars  : " + (Global.playerCars == null
                    ? "null"
                    : Global.playerCars.Length.ToString()));
            }
        }

        private static void LogCarTuning(ManualLogSource log)
        {
            if (PlayerCar == null)
            {
                log.LogWarning("PlayerCar is null - not currently in a vehicle");
                return;
            }

            unsafe
            {
                // Only the parameters iteration 4 needs to rebuild the hover model.
                log.LogInfo("car tuning  : stabilizer=" + PlayerCar.stabilizerConstant +
                            " damper=" + PlayerCar.stabilizerDamperConstant +
                            " accel=" + PlayerCar.acceleration +
                            " motor=" + PlayerCar.motorTorque +
                            " steer=" + PlayerCar.steerTorque +
                            " maxSpeed=" + PlayerCar.maximumSpeed +
                            " stableHeight=" + PlayerCar.stableHeight);
                log.LogInfo("car thrusters: " + PlayerCar.thrusterSpots.Length);
            }
        }

        /// <summary>
        /// Prints a transform subtree with local positions. Used to locate the seat anchors,
        /// since the car interior's anchor names are not known ahead of time.
        /// </summary>
        public static void DumpHierarchy(Transform root, string label, ManualLogSource log, int maxDepth = 4)
        {
            if (root == null)
            {
                log.LogInfo(label + ": <null>");
                return;
            }

            log.LogInfo("---- hierarchy: " + label + " ----");
            unsafe
            {
                Walk(root, 0, maxDepth, log);
            }
            log.LogInfo("---------------------------");
        }

        private static unsafe void Walk(Transform node, int depth, int maxDepth, ManualLogSource log)
        {
            if (depth > maxDepth)
            {
                return;
            }

            Vector3 local = node.localPosition;
            log.LogInfo(new string(' ', depth * 2) + node.name +
                        "  [" + local.x.ToString("0.00") + ", " +
                        local.y.ToString("0.00") + ", " + local.z.ToString("0.00") + "]");

            int count = node.childCount;
            for (int i = 0; i < count; i++)
            {
                Walk(node.GetChild(i), depth + 1, maxDepth, log);
            }
        }
    }
}
