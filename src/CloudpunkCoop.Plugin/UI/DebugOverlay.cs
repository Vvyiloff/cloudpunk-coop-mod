using System.Text;
using CloudpunkCoop.Game;
using UnityEngine;

namespace CloudpunkCoop.UI
{
    /// <summary>
    /// IMGUI debug panel, drawn from <c>Bootstrapper.OnGUI</c>.
    ///
    /// IMGUI is the cheapest possible UI in a shipped IL2CPP build: no prefabs, no canvas,
    /// no assets. It is a debugging surface for iterations 2-8 and is not part of the MVP.
    /// </summary>
    public static class DebugOverlay
    {
        private const int PanelWidth = 460;
        private const int RowHeight = 18;

        private static readonly StringBuilder Text = new StringBuilder(1024);
        private static bool _inputBroken;

        public static bool Visible { get; private set; } = false;

        /// <summary>Which input API actually works on this build; logged once.</summary>
        public static string ToggleSource { get; private set; } = "none";

        public static void Toggle() => Visible = !Visible;

        /// <summary>
        /// Best-effort legacy key check shared with the seat controls. Returns false if the
        /// build rejects <c>UnityEngine.Input</c>, rather than throwing every frame.
        /// </summary>
        public static bool Pressed(UnityEngine.KeyCode key)
        {
            if (_inputBroken)
            {
                return false;
            }

            try
            {
                return UnityEngine.Input.GetKeyDown(key);
            }
            catch (System.Exception)
            {
                _inputBroken = true;
                return false;
            }
        }

        /// <summary>
        /// F1 detection. Legacy <c>UnityEngine.Input</c> is used because it needs no extra
        /// references; it throws outright on builds that enable the new Input System
        /// exclusively, so the failure is caught and reported once rather than crashing.
        /// </summary>
        public static bool TogglePressed()
        {
            if (_inputBroken)
            {
                return false;
            }

            try
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F1))
                {
                    ToggleSource = "UnityEngine.Input";
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                _inputBroken = true;
                ToggleSource = "unavailable (" + ex.GetType().Name + ")";
                return false;
            }
        }

        public static void Draw()
        {
            if (!Visible)
            {
                return;
            }

            Text.Length = 0;
            Text.Append("Cloudpunk Coop  debug   [F1] toggle   input=").Append(ToggleSource).Append('\n');
            Text.Append("fps ").Append(Fps().ToString("0"))
                .Append("   refs=").Append(GameRefs.Resolved ? "ok" : "pending")
                .Append("   actions=").Append(GameInputMap.All.Count.ToString())
                .Append("   moved=").Append(GameInputMap.MovedCount().ToString())
                .Append("   seat=").Append(SeatRig.Occupant)
                .Append('\n');

            if (GameRefs.Global != null)
            {
                Text.Append("car ").Append(SafeSpeed())
                    .Append("   inCar=").Append(Global.IsInVehicle ? "yes" : "no").Append('\n');
            }

            Text.Append("net ").Append(Net.CoopSession.Role.ToString().ToLowerInvariant())
                .Append("   ").Append(Net.CoopSession.Status)
                .Append("   sent=").Append(Net.CoopSession.SnapshotsSent.ToString())
                .Append(" recv=").Append(Net.CoopSession.SnapshotsReceived.ToString());

            if (Net.CoopSession.HaveRemoteCar)
            {
                Text.Append("   remoteRate=").Append(Net.CoopSession.RemoteSnapshotRate.ToString("0.0"))
                    .Append("/s   step=").Append(Net.CoopSession.RemoteStepMetres.ToString("0.00"))
                    .Append("m");
            }

            Text.Append('\n');

            if (Net.CoopSession.Role == Net.CoopRole.Guest)
            {
                Text.Append("proxy ").Append(CarProxy.Ready ? "on" : "waiting")
                    .Append("  ").Append(CarProxy.Status).Append("  local hidden=")
                    .Append(LocalCarSuppress.Active).Append('\n');
                Text.Append("pose ").Append(CarProxy.PositionText).Append("  meshes=")
                    .Append(CarProxy.MeshCount).Append("  camera=")
                    .Append(RemoteCameraRig.LiveCameraName).Append('\n');
                Text.Append("effects ").Append(CarVisuals.Emitting).Append('/')
                    .Append(CarVisuals.Count).Append("  applied=").Append(CarVisuals.Applied).Append('\n');
            }

            Text.Append("------------------------------------------\n");

            int index = 0;
            foreach (GameInputMap.Entry entry in GameInputMap.All)
            {
                string marker = entry.Touched ? "*" : " ";
                Text.Append(marker).Append(' ').Append(entry.Name.PadRight(20));
                // A crude bar: 20 cells, centre-zero for one-axis inputs.
                Text.Append('[').Append(Bar(entry.Value)).Append("] ");
                Text.Append(entry.Value.ToString("0.00").PadLeft(6));
                Text.Append("  ").Append(Clip(entry.Bindings, 22)).Append('\n');
                index++;
            }

            Text.Append("\n[F3] driver  [F4] passenger  [F5] leave  [F6] autopilot  [F7] enter car\n");

            GUI.Box(new Rect(8f, 8f, PanelWidth, 90f + RowHeight + index * RowHeight), Text.ToString());
        }

        private static float Fps()
        {
            float delta = Time.deltaTime;
            return delta > 0.0001f ? 1f / delta : 0f;
        }

        private static string SafeSpeed()
        {
            try
            {
                if (GameRefs.PlayerCar == null)
                {
                    return "<no vehicle>";
                }
                unsafe
                {
                    // currentSpeed is km/h: measured against per-sample position deltas, 100
                    // reads as 100 km/h, i.e. ~28 m/s. The label said m/s and made the car look
                    // ten times faster than it was.
                    return GameRefs.PlayerCar.currentSpeed.ToString("0.0") + " km/h";
                }
            }
            catch (System.Exception)
            {
                return "?";
            }
        }

        private static string Bar(float value)
        {
            const int width = 20;
            int centre = width / 2;
            int offset = (int)Mathf.Clamp(value * (width / 2f), -(width / 2f), width / 2f);
            int from = offset >= 0 ? centre : centre + offset;
            int to = offset >= 0 ? centre + offset + 1 : centre + 1;

            var builder = new StringBuilder(width);
            for (int i = 0; i < width; i++)
            {
                builder.Append(i >= from && i < to ? '#' : '.');
            }
            return builder.ToString();
        }

        private static string Clip(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }
    }
}
