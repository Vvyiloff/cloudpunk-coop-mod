using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using CloudpunkCoop.Game;

namespace CloudpunkCoop
{
    /// <summary>
    /// The game's InControl action map, read once and sampled every frame.
    ///
    /// The action set is <c>PlayerActions</c>, which derives from InControl's
    /// <c>PlayerActionSet</c>. The canonical list is <c>PlayerActionSet.actions</c> — an
    /// earlier attempt to enumerate the subclass's own fields returned nothing, so the base
    /// class collection is the supported path.
    ///
    /// Each action carries its bindings, whose <c>BindingSource.Name</c> is human readable
    /// ("W", "Left Stick X", "LeftTrigger"). That means the whole input map can be recovered
    /// statically, without anyone pressing a control.
    ///
    /// Sampling records per-action min/max so a later probe can tell which actions the car
    /// actually consumes.
    /// </summary>
    public static class GameInputMap
    {
        /// <summary>One action plus everything iteration 2 needs to know about it.</summary>
        public sealed class Entry
        {
            public InControl.PlayerAction Action;
            public string Name;
            public string Bindings;
            public float Value;
            public float Min;
            public float Max;
            public bool Touched => Max > Min + 0.001f;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        public static bool Ready => Entries.Count > 0;
        public static IReadOnlyList<Entry> All => Entries;

        /// <summary>Name -> action lookup, the same map iteration 4 will drive.</summary>
        public static InControl.PlayerAction ByName(string name)
        {
            foreach (Entry entry in Entries)
            {
                if (entry.Name == name)
                {
                    return entry.Action;
                }
            }
            return null;
        }

        /// <summary>
        /// Live value of one action by name, read straight from InControl.
        ///
        /// This is the effective value the game itself would see, including anything the mod
        /// injects, so it answers "is this input arriving at all" without guessing.
        /// </summary>
        public static float ValueOf(string name)
        {
            InControl.PlayerAction action = ByName(name);
            if (action == null)
            {
                return -1f;
            }

            unsafe
            {
                RemoteInputOverride.OverlaySampling = true;
                try
                {
                    return action.Value;
                }
                finally
                {
                    RemoteInputOverride.OverlaySampling = false;
                }
            }
        }

        public static void Initialize(ManualLogSource log)
        {
            if (Ready)
            {
                return;
            }

            PlayerActions actions = null;
            if (GameRefs.GameInput != null)
            {
                unsafe
                {
                    actions = GameRefs.GameInput.playerActions;
                }
            }

            if (actions == null)
            {
                log.LogWarning("cannot build the input map: GameInput.playerActions is null");
                return;
            }

            unsafe
            {
                Il2CppSystem.Collections.Generic.List<InControl.PlayerAction> all = actions.actions;
                if (all == null)
                {
                    log.LogWarning("PlayerActionSet.actions is null");
                    return;
                }

                int count = all.Count;
                for (int i = 0; i < count; i++)
                {
                    InControl.PlayerAction action = all[i];
                    if (action == null)
                    {
                        continue;
                    }

                    Entries.Add(new Entry
                    {
                        Action = action,
                        Name = action.Name,
                        Bindings = DescribeBindings(action),
                        Value = 0f,
                        Min = 0f,
                        Max = 0f,
                    });
                }
            }

            log.LogInfo("---- input map (" + Entries.Count + " actions) ----");
            foreach (Entry entry in Entries)
            {
                log.LogInfo("  " + entry.Name.PadRight(22) + " <- " + entry.Bindings);
            }
            log.LogInfo("--------------------------------------");
        }

        private static unsafe string DescribeBindings(InControl.PlayerAction action)
        {
            var builder = new StringBuilder();
            Il2CppSystem.Collections.ObjectModel.ReadOnlyCollection<InControl.BindingSource> sources =
                action.bindings;

            if (sources == null || sources.Count == 0)
            {
                return "<unbound>";
            }

            for (int i = 0; i < sources.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }
                InControl.BindingSource source = sources[i];
                builder.Append(source == null ? "?" : source.Name);
            }
            return builder.ToString();
        }

        /// <summary>Call once per frame from Unity's main thread.</summary>
        public static void Sample()
        {
            if (Entries.Count == 0)
            {
                return;
            }

            unsafe
            {
                RemoteInputOverride.OverlaySampling = true;
                try
                {
                    foreach (Entry entry in Entries)
                    {
                        float value = entry.Action.Value;
                        entry.Value = value;
                        if (value < entry.Min)
                        {
                            entry.Min = value;
                        }
                        if (value > entry.Max)
                        {
                            entry.Max = value;
                        }
                    }
                }
                finally
                {
                    RemoteInputOverride.OverlaySampling = false;
                }
            }
        }

        /// <summary>Actions that actually moved since the world loaded.</summary>
        public static int MovedCount()
        {
            int moved = 0;
            foreach (Entry entry in Entries)
            {
                if (entry.Touched)
                {
                    moved++;
                }
            }
            return moved;
        }

        public static void LogActivity(ManualLogSource log)
        {
            if (Entries.Count == 0)
            {
                return;
            }

            log.LogInfo("---- action activity (min..max since world load) ----");
            foreach (Entry entry in Entries)
            {
                if (!entry.Touched)
                {
                    continue;
                }
                log.LogInfo("  " + entry.Name.PadRight(22) +
                            " " + entry.Min.ToString("0.###") + " .. " + entry.Max.ToString("0.###"));
            }
            log.LogInfo("moved " + MovedCount() + " of " + Entries.Count + " actions");
            log.LogInfo("------------------------------------------------");
        }
    }
}