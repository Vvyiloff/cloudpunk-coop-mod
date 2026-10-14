using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>
    /// One-shot inspection of the original current car. Invoke on Unity's main thread.
    /// Reads game state only; overwrites the caller's output file without creating directories.
    /// Does not sample input, emit particles, simulate physics or change visuals.
    /// </summary>
    public static class VisualProbe
    {
        private const int MaxSystems = 64, MaxTrails = 64, MaxReferences = 32;
        private const int MaxSamples = 8, MaxChars = 131072, MaxLine = 1024;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ReadInt(IntPtr system);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ReadBool(IntPtr system);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float ReadFloat(IntPtr system);

        public static void Capture(ManualLogSource log, string outputPath)
        {
            var report = new Report();
            report.Line("VisualProbe UTC=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            report.Line("Limits: systems=64 trails=64 references/group=32 samples/system=8 chars=131072.");
            report.Line("Single instant; sample order is native, not a representative distribution.");
            try
            {
                // Resolve live state rather than a cached car that can survive a vehicle switch.
                Global global = Global.Instance;
                PlayerCar car = global == null ? null : global.playerCar;
                if (car == null) report.Line("Original current car: unavailable (Global.Instance.playerCar).");
                else CaptureCar(report, car);
            }
            catch (Exception ex) { report.Line("capture error=" + Error(ex)); }

            try
            {
                if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("outputPath is empty");
                File.WriteAllText(outputPath, report.Text, new UTF8Encoding(false));
                log?.LogInfo("VisualProbe: captured original car diagnostics to " + outputPath);
            }
            catch (Exception ex) { log?.LogWarning("VisualProbe: output failed: " + Error(ex)); }
        }

        private static void CaptureCar(Report r, PlayerCar car)
        {
            Transform root = car.transform;
            r.Read("car.path", () => Path(root));
            r.Read("car.id", () => car.id);
            r.Read("car.active", () => car.gameObject.activeInHierarchy);
            r.Read("car.GasInput", () => car.GasInput);
            r.Read("car.SteerInput", () => car.SteerInput);
            r.Read("car.CurrentSpeed", () => car.CurrentSpeed);
            r.Read("car.controlsFrozen", () => car.controlsFrozen);
            r.Read("car.currentRoutine", () => car.currentRoutine != null);
            r.Read("car.layerMinY", () => car.layerMinY);
            r.Read("car.layerMaxY", () => car.layerMaxY);
            r.Read("car.currentFloorLevel", () => car.currentFloorLevel);
            r.Read("car.isRising", () => car.isRising);
            r.Read("car.speedBoostTimer", () => car.speedBoostTimer);
            r.Read("car.cockpit", () => car.isInVehicleCockpit);
            r.Read("car.position", () => V(root.position));
            r.Read("car.localScale", () => V(root.localScale));
            r.Read("car.rigidbody.velocity", () => car.rBody == null ? "<null>" : V(car.rBody.velocity));
            r.Read("car.rigidbody.isKinematic", () => car.rBody == null ? "<null>" : car.rBody.isKinematic.ToString());
            r.Read("car.particlesParent", () => ObjectPath(car.particlesParent));
            r.Read("car.hoverSmoke", () => ObjectPath(car.hoverSmoke));
            r.Read("CarVisuals.Status", () => CarVisuals.Status);
            r.Read("CarVisuals.Count", () => CarVisuals.Count);
            r.Read("CarVisuals.Emitting", () => CarVisuals.Emitting);
            r.Read("CarVisuals.Applied", () => CarVisuals.Applied);
            r.Read("CarVisuals.MissingTemplates", () => CarVisuals.MissingTemplates);
            r.Read("CarVisuals.HostSample", () => CarVisuals.HostSample);

            r.Section("native visual references", () =>
            {
                var thrusters = car.thrusterParticles;
                r.Line("thrusterParticles.count=" + (thrusters == null ? 0 : thrusters.Count));
                if (thrusters != null)
                    for (int i = 0; i < Math.Min(thrusters.Count, MaxReferences); i++)
                    {
                        int index = i;
                        r.Read("thrusterParticles[" + i + "]", () => ComponentPath(thrusters[index]));
                    }
                ParticleRefs(r, "thrusterSmoke", car.thrusterSmoke);
                ParticleRefs(r, "speedBoosterParticles", car.speedBoosterParticles);
                ParticleRefs(r, "exteriorParticles", car.exteriorParticles);
                var slots = car.exhaustSlots;
                r.Line("exhaustSlots.count=" + (slots == null ? 0 : slots.Length));
                if (slots != null)
                    for (int i = 0; i < Math.Min(slots.Length, MaxReferences); i++)
                    {
                        int index = i;
                        r.Read("exhaustSlots[" + i + "]", () => ComponentPath(slots[index]));
                    }
            });
            r.Section("thrusterValues (suspension state)", () =>
            {
                var values = car.thrusterValues;
                r.Line("thrusterValues.count=" + (values == null ? 0 : values.Length));
                if (values == null) return;
                for (int i = 0; i < Math.Min(values.Length, MaxReferences); i++)
                {
                    var tv = values[i];
                    r.Section("thruster[" + i + "]", () =>
                    {
                        if (tv == null) { r.Line("<null>"); return; }
                        r.Read("spot", () => ComponentPath(tv.spot));
                        r.Read("springForce", () => tv.springForce);
                        r.Read("damperForce", () => tv.damperForce);
                        r.Read("restLength", () => tv.restLength);
                        r.Read("previousLength", () => tv.previousLength);
                        r.Read("currentLength", () => tv.currentLength);
                        r.Read("springVelocity", () => tv.springVelocity);
                    });
                }
            });

            r.Section("original ParticleSystems (including inactive)", () =>
            {
                var systems = root.GetComponentsInChildren<ParticleSystem>(true);
                r.Line("ParticleSystem.count=" + systems.Length);
                var samples = new Il2CppStructArray<ParticleSystem.Particle>(MaxSamples);
                for (int i = 0; i < Math.Min(systems.Length, MaxSystems); i++)
                {
                    ParticleSystem ps = systems[i];
                    r.Section("ParticleSystem[" + i + "]", () => CaptureParticles(r, ps, samples));
                }
            });
            r.Section("original TrailRenderers (including inactive)", () =>
            {
                var trails = root.GetComponentsInChildren<TrailRenderer>(true);
                r.Line("TrailRenderer.count=" + trails.Length);
                for (int i = 0; i < Math.Min(trails.Length, MaxTrails); i++)
                {
                    TrailRenderer trail = trails[i];
                    r.Section("TrailRenderer[" + i + "]", () =>
                    {
                        r.Read("path", () => ComponentPath(trail));
                        r.Read("active", () => trail.gameObject.activeInHierarchy);
                        r.Read("enabled", () => trail.enabled);
                        r.Read("emitting", () => trail.emitting);
                        r.Read("positionCount", () => trail.positionCount);
                        r.Read("time", () => trail.time);
                        r.Read("startWidth", () => trail.startWidth);
                        r.Read("endWidth", () => trail.endWidth);
                        r.Read("widthMultiplier", () => trail.widthMultiplier);
                        r.Read("minVertexDistance", () => trail.minVertexDistance);
                        r.Read("autodestruct", () => trail.autodestruct);
                        r.Read("material", () => trail.sharedMaterial == null ? "<null>" : trail.sharedMaterial.name);
                    });
                }
            });
        }

        private static void CaptureParticles(Report r, ParticleSystem ps,
            Il2CppStructArray<ParticleSystem.Particle> samples)
        {
            r.Read("name", () => ps.name);
            r.Read("path", () => ComponentPath(ps));
            r.Read("active", () => ps.gameObject.activeInHierarchy);
            r.Read("isPlaying", () => ps.isPlaying);
            r.Read("isEmitting", () => ps.isEmitting);
            r.Read("emission.enabled", () => ps.emission.enabled);
            r.Read("particleCount", () => ps.particleCount);
            r.Read("position", () => V(ps.transform.position));
            r.Read("localPosition", () => V(ps.transform.localPosition));
            r.Read("localScale", () => V(ps.transform.localScale));
            r.Read("lossyScale", () => V(ps.transform.lossyScale));
            r.Section("native scalars", () =>
            {
                NativeParticleAccess.Read(ps, out float rate, out float speed, out float size, out float lifetime);
                r.Line("rate=" + F(rate) + " speed=" + F(speed) + " size=" + F(size) + " lifetime=" + F(lifetime));
            });
            // The injected legacy simulationSpace/MainModule getter is unsuitable here.
            // These read-only icall names exist in the installed UnityPlayer.dll. Resolve
            // per read so a missing binding is reported locally rather than poisoning a type initializer.
            IntPtr ptr = IL2CPP.Il2CppObjectBaseToPtrNotNull(ps);
            r.Read("native.simulationSpace (0=Local 1=World 2=Custom)", () =>
                IL2CPP.ResolveICall<ReadInt>("UnityEngine.ParticleSystem/MainModule::GetSimulationSpace")(ptr));
            r.Read("native.useRigidbodyForVelocity", () =>
                IL2CPP.ResolveICall<ReadBool>("UnityEngine.ParticleSystem/MainModule::GetUseRigidbodyForVelocity")(ptr) != 0);
            r.Read("native.inheritVelocity.enabled", () =>
                IL2CPP.ResolveICall<ReadBool>("UnityEngine.ParticleSystem/InheritVelocityModule::GetEnabled")(ptr) != 0);
            r.Read("native.inheritVelocity.mode (0=Initial 1=Current)", () =>
                IL2CPP.ResolveICall<ReadInt>("UnityEngine.ParticleSystem/InheritVelocityModule::GetMode")(ptr));
            r.Read("native.inheritVelocity.curveMultiplier", () =>
                IL2CPP.ResolveICall<ReadFloat>("UnityEngine.ParticleSystem/InheritVelocityModule::GetCurveMultiplier")(ptr));
            r.Section("renderer", () =>
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null) { r.Line("<null>"); return; }
                r.Read("renderer.enabled", () => renderer.enabled);
                r.Read("renderer.renderMode", () => renderer.renderMode);
                r.Read("renderer.velocityScale", () => renderer.velocityScale);
                r.Read("renderer.lengthScale", () => renderer.lengthScale);
                r.Read("renderer.cameraVelocityScale", () => renderer.cameraVelocityScale);
                r.Read("renderer.material", () => renderer.sharedMaterial == null ? "<null>" : renderer.sharedMaterial.name);
            });
            r.Section("GetParticles", () =>
            {
                int count = ps.GetParticles(samples, MaxSamples, 0);
                r.Line("sample.count=" + count);
                for (int i = 0; i < Math.Min(Math.Max(count, 0), MaxSamples); i++)
                {
                    // Installed interop exposes the struct fields; its property getters
                    // are stripped. Color is birth RGBA, not evaluated color-over-lifetime.
                    ParticleSystem.Particle p = samples[i];
                    Color32 c = p.m_StartColor;
                    r.Line("sample[" + i + "] position=" + V(p.m_Position) + " velocity=" + V(p.m_Velocity) +
                        " animatedVelocity=" + V(p.m_AnimatedVelocity) + " initialVelocity=" + V(p.m_InitialVelocity) +
                        " birthRGBA=" + c.r + "," + c.g + "," + c.b + "," + c.a +
                        " remainingLifetime=" + F(p.m_Lifetime) + " startLifetime=" + F(p.m_StartLifetime));
                }
            });
        }

        private static void ParticleRefs(Report r, string label, Il2CppReferenceArray<ParticleSystem> systems)
        {
            r.Line(label + ".count=" + (systems == null ? 0 : systems.Length));
            if (systems == null) return;
            for (int i = 0; i < Math.Min(systems.Length, MaxReferences); i++)
            {
                int index = i;
                r.Read(label + "[" + i + "]", () => ComponentPath(systems[index]));
            }
        }

        private static string ObjectPath(GameObject obj) => obj == null ? "<null>" : Path(obj.transform);
        private static string ComponentPath(Component obj) => obj == null ? "<null>" : Path(obj.transform);
        private static string Path(Transform node)
        {
            var path = new StringBuilder();
            int depth = 0;
            while (node != null && depth++ < 32 && path.Length < 768)
            {
                string name = node.name ?? "<unnamed>";
                if (name.Length > 64) name = name.Substring(0, 64);
                path.Insert(0, "/" + name + "[" + node.GetSiblingIndex() + "]");
                node = node.parent;
            }
            if (node != null) path.Insert(0, "<ancestor limit>");
            return path.ToString();
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string V(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
        private static string Error(Exception ex) => ex.GetType().Name + ": " + ex.Message;

        private sealed class Report
        {
            private readonly StringBuilder _text = new StringBuilder(8192);
            private bool _truncated;
            public string Text => _text.ToString();

            public void Line(string line)
            {
                if (_truncated) return;
                if (line.Length > MaxLine) line = line.Substring(0, MaxLine) + " <line truncated>";
                line = line.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
                if (_text.Length + line.Length + 1 > MaxChars - 64)
                {
                    _text.Append("<report truncated at character limit>\n");
                    _truncated = true;
                    return;
                }
                _text.Append(line).Append('\n');
            }

            public void Read(string label, Func<object> read)
            {
                if (_truncated) return;
                try { Line(label + "=" + Convert.ToString(read(), CultureInfo.InvariantCulture)); }
                catch (Exception ex) { Line(label + "=<unavailable: " + Error(ex) + ">"); }
            }

            public void Section(string label, Action read)
            {
                if (_truncated) return;
                Line("-- " + label + " --");
                try { read(); }
                catch (Exception ex) { Line("section error=" + Error(ex)); }
            }
        }
    }
}
