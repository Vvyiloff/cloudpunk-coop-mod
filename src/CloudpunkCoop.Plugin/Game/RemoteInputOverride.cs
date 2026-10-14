using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace CloudpunkCoop.Game
{
    /// <summary>
    /// Lets the mod supply driving input to the car on behalf of a remote player.
    ///
    /// The original plan was an InControl virtual device. That is not possible here:
    /// <c>InControl.Virtual.VirtualDevice</c> is absent from this build, because IL2CPP
    /// strips every type the game never references. Binding manipulation would hit the same
    /// wall for any helper type we would need.
    ///
    /// What survives is <c>PlayerAction : OneAxisInputControl</c>, so the value the game reads
    /// is <c>OneAxisInputControl.Value</c>. A Harmony prefix on that getter intercepts the
    /// eight driving actions and leaves everything else alone.
    ///
    /// Two ways of recognising "our" actions were tried and both failed, which is worth
    /// recording so nobody repeats them:
    ///
    ///   * Matching the instance by reference never fired. The prefix ran hundreds of
    ///     thousands of times while the car sat still.
    ///   * Matching by <c>PlayerAction.Name</c> never fired either, because the prefix is
    ///     always handed a wrapper typed as the base <c>OneAxisInputControl</c>, so the cast
    ///     to <c>PlayerAction</c> returns null every single time.
    ///
    /// The key that does work is the native object pointer: it identifies the action no matter
    /// which managed wrapper type the interop happens to hand over.
    ///
    /// Nothing about the vehicle is patched: <c>PlayerCar</c> still reads its own actions and
    /// still runs its own physics. Only the numbers arriving are different.
    /// </summary>
    public static class RemoteInputOverride
    {
        public const string HarmonyId = "dev.andre.cloudpunk-coop.input";

        private static readonly string[] ActionNames =
        {
            "DriveForward", "DriveBackward", "SteerLeft", "SteerRight",
            "Ascend", "Sink", "StrafeLeft", "StrafeRight",
        };

        private static readonly IntPtr[] ActionPointers = new IntPtr[ActionNames.Length];

        private static Harmony _harmony;

        public static bool Installed { get; private set; }

        /// <summary>When false the patch falls through and the game reads real hardware.</summary>
        public static bool Active { get; private set; }

        public static float Throttle { get; set; }
        public static float Brake { get; set; }

        /// <summary>Signed steering in [-1, 1]; positive turns right, matching InControl.</summary>
        public static float Steer { get; set; }

        public static float AscendValue { get; set; }
        public static float SinkValue { get; set; }
        public static float StrafeLeftValue { get; set; }
        public static float StrafeRightValue { get; set; }

        // ---- diagnostics, so "the car did not move" can be told apart from "the patch
        // ---- never recognised anything". Cheap and worth keeping while this is unproven.

        public static int PrefixHits;
        public static int PointerHits;
        public static int UnmatchedHits;

        /// <summary>
        /// True while the debug overlay is reading every action to draw its bars. Those reads
        /// go through this very patch, so without this flag the overlay's own traffic looks
        /// exactly like the game consuming input - and that miscount sent the investigation
        /// down the wrong path once already.
        /// </summary>
        public static bool OverlaySampling;

        /// <summary>Calls that did not come from our own overlay. This is the number that matters.</summary>
        public static int ExternalHits;

        /// <summary>Distinct native pointers seen, capped, as hex strings.</summary>
        public static readonly List<string> PointerSamples = new List<string>();

        public static bool Install(ManualLogSource log)
        {
            if (Installed)
            {
                return true;
            }

            if (!ResolveActions(log))
            {
                return false;
            }

            try
            {
                _harmony = new Harmony(HarmonyId);

                // Both members are patched: InControl exposes Value (after dead zones) and
                // RawValue (before them). Patching only Value is not enough to know which one
                // the vehicle actually consumes, and the cost of covering both is nil.
                int patched = 0;
                foreach (string member in new[] { "Value", "RawValue" })
                {
                    PropertyInfo property = typeof(InControl.OneAxisInputControl)
                        .GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
                    MethodInfo getter = property?.GetGetMethod();
                    if (getter == null)
                    {
                        log.LogWarning("OneAxisInputControl." + member + " getter not found via reflection");
                        continue;
                    }

                    _harmony.Patch(getter, new HarmonyMethod(typeof(RemoteInputOverride), nameof(ValuePrefix)));
                    patched++;
                }

                if (patched == 0)
                {
                    log.LogError("no input getters could be patched");
                    return false;
                }

                Installed = true;
                log.LogInfo("Harmony patch installed on OneAxisInputControl getters: " + patched);
                return true;
            }
            catch (Exception ex)
            {
                log.LogError("failed to install the input override patch: " + ex);
                return false;
            }
        }

        private static bool ResolveActions(ManualLogSource log)
        {
            int resolved = 0;
            for (int i = 0; i < ActionNames.Length; i++)
            {
                InControl.PlayerAction action = GameInputMap.ByName(ActionNames[i]);
                if (action == null)
                {
                    log.LogWarning("driving action not found: " + ActionNames[i]);
                    continue;
                }

                try
                {
                    ActionPointers[i] = IL2CPP.Il2CppObjectBaseToPtr(action);
                }
                catch (Exception ex)
                {
                    log.LogWarning("could not read the native pointer for " + ActionNames[i] + ": " + ex.Message);
                    continue;
                }

                resolved++;
            }

            log.LogInfo("driving actions resolved: " + resolved + " of " + ActionNames.Length);
            return resolved > 0;
        }

        /// <summary>Harmony prefix. Returning false skips the original getter.</summary>
        private static bool ValuePrefix(InControl.OneAxisInputControl __instance, ref float __result)
        {
            PrefixHits++;

            if (!OverlaySampling)
            {
                ExternalHits++;
            }

            if (!Active)
            {
                return true;
            }

            IntPtr pointer;
            try
            {
                pointer = IL2CPP.Il2CppObjectBaseToPtr(__instance);
            }
            catch (Exception)
            {
                UnmatchedHits++;
                return true;
            }

            if (PointerSamples.Count < 12)
            {
                string hex = "0x" + pointer.ToInt64().ToString("X");
                if (!PointerSamples.Contains(hex))
                {
                    PointerSamples.Add(hex);
                }
            }

            for (int i = 0; i < ActionPointers.Length; i++)
            {
                if (ActionPointers[i] != IntPtr.Zero && ActionPointers[i] == pointer)
                {
                    PointerHits++;
                    __result = ValueFor(i);
                    return false;
                }
            }

            UnmatchedHits++;
            return true;
        }

        private static float ValueFor(int slot)
        {
            switch (ActionNames[slot])
            {
                case "DriveForward": return Clamp01(Throttle);
                case "DriveBackward": return Clamp01(Brake);
                case "SteerLeft": return Steer < 0f ? Clamp01(-Steer) : 0f;
                case "SteerRight": return Steer > 0f ? Clamp01(Steer) : 0f;
                case "Ascend": return Clamp01(AscendValue);
                case "Sink": return Clamp01(SinkValue);
                case "StrafeLeft": return Clamp01(StrafeLeftValue);
                case "StrafeRight": return Clamp01(StrafeRightValue);
                default: return 0f;
            }
        }

        public static void SetActive(bool active)
        {
            Active = active;

            if (!active)
            {
                ClearValues();
            }
        }

        private static void ClearValues()
        {
            Throttle = 0f;
            Brake = 0f;
            Steer = 0f;
            AscendValue = 0f;
            SinkValue = 0f;
            StrafeLeftValue = 0f;
            StrafeRightValue = 0f;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }
            return value > 1f ? 1f : value;
        }

        public static void Unpatch()
        {
            if (_harmony != null && Installed)
            {
                try
                {
                    _harmony.UnpatchSelf();
                }
                catch (Exception ex)
                {
                    // Nothing useful to do at unload time.
                    _ = ex;
                }
            }

            Installed = false;
            Active = false;
            ClearValues();
        }
    }
}
