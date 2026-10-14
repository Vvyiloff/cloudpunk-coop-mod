using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using CloudpunkCoop.Protocol;
using UnityEngine;

// The generated legacy scalar accessors call missing MainModule wrappers in this build.
// NativeParticleAccess resolves the retained engine icalls instead.

namespace CloudpunkCoop.Game
{
    /// <summary>Native particle templates, driven only by the authoritative host's state.</summary>
    public static class CarVisuals
    {
        private static readonly ManualLogSource Log=BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Effects");
        private static readonly Dictionary<string, ParticleSystem> Copies = new Dictionary<string, ParticleSystem>();
        private static readonly Dictionary<string, Vector3> ScaleFactors = new Dictionary<string, Vector3>();
        private static readonly Dictionary<string,GameObject> ContactRoots=new(StringComparer.Ordinal);
        private static Transform _proxy;
        public static long DynamicGroupsCreated {get;private set;}
        public static int DynamicGroupCount=>ContactRoots.Count;
        public static int CapturedContactParticles {get;private set;}
        private static readonly List<CarVisualMessage> Pending = new List<CarVisualMessage>(64);
        private static readonly Dictionary<string, TrailRenderer> Trails = new Dictionary<string, TrailRenderer>();
        private static readonly List<CarTrailMessage> PendingTrails = new List<CarTrailMessage>(64);
        public static int TrailCount => Trails.Count;
        private static bool _haveSequence;
        private static uint _lastSequence;
        public static int Count => Copies.Count;
        public static int Emitting { get; private set; }
        public static long Applied { get; private set; }
        public static long MissingTemplates { get; private set; }
        public static string HostSample { get; private set; } = "-";
        public static string Status { get; private set; } = "waiting";
        private static bool _failureLogged;

        public static void Fail(Exception ex, ManualLogSource log)
        {
            Status = "failed: " + ex.GetType().Name;
            if (_failureLogged) return;
            _failureLogged = true;
            log.LogWarning("proxy effects unavailable: " + ex + "; sample=" + HostSample);
        }

        public static CarVisualMessage Capture(uint sequence)
        {
            Transform root = GameRefs.PlayerCar.transform;
            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            int count = Math.Min(particles.Length, 64);
            CapturedContactParticles=0;
            var states = new ParticleVisualState[count];
            for (int i = 0; i < count; i++)
            {
                ParticleSystem ps = particles[i];
                var emission = ps.emission;
                NativeParticleAccess.Read(ps, out float rate, out float speed, out float size, out float lifetime);
                Vector3 scale = ps.transform.localScale;
                HostSample = FormattableString.Invariant($"{ps.name}: rate={rate}, speed={speed}, size={size}, lifetime={lifetime}, scale={scale.x},{scale.y},{scale.z}");
                Vector3 position=root.InverseTransformPoint(ps.transform.position);
                Quaternion rotation=Quaternion.Inverse(root.rotation)*ps.transform.rotation;
                string path=EffectPath(root,ps.transform);
                if(path.StartsWith("@contact/",StringComparison.Ordinal))CapturedContactParticles++;
                states[i] = new ParticleVisualState(path,
                    ps.gameObject.activeInHierarchy, ps.isPlaying && emission.enabled,
                    rate, speed, size, lifetime,
                    scale.x, scale.y, scale.z, 1f, 1f, 1f, 1f,
                    position.x,position.y,position.z,rotation.x,rotation.y,rotation.z,rotation.w);
            }
            return new CarVisualMessage(sequence, states);
        }

        public static CarTrailMessage CaptureTrails(uint sequence)
        {
            Transform root = GameRefs.PlayerCar.transform;
            var trails = root.GetComponentsInChildren<TrailRenderer>(true);
            var states = new TrailVisualState[Math.Min(trails.Length, 64)];
            for (int i = 0; i < states.Length; i++)
            {
                TrailRenderer t = trails[i];
                states[i] = new TrailVisualState(Path(root, t.transform), t.gameObject.activeInHierarchy && t.enabled,
                    t.emitting, t.time, t.startWidth, t.endWidth);
            }
            return new CarTrailMessage(sequence, states);
        }

        public static void Create(Transform source, Transform proxy, ManualLogSource log)
        {
            ClearContacts();_proxy=proxy;
            Copies.Clear();
            ScaleFactors.Clear();
            var particles = source.GetComponentsInChildren<ParticleSystem>(true);
            Trails.Clear();
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem ps = particles[i];
                if(ContactAncestor(source,ps.transform)!=null)continue;
                bool nested = false;
                for (Transform p = ps.transform.parent; p != null && p != source; p = p.parent)
                    if (p.GetComponent<ParticleSystem>() != null) { nested = true; break; }
                if (nested) continue;

                // The proxy is inactive while we instantiate. No cloned MonoBehaviour can
                // run Awake/OnEnable; remove everything except particle visuals before activation.
                GameObject clone = UnityEngine.Object.Instantiate((UnityEngine.Object)ps.gameObject,
                    proxy, false).Cast<GameObject>();
                clone.name = "CP_Effect_" + ps.name;
                clone.transform.localPosition = source.InverseTransformPoint(ps.transform.position);
                clone.transform.localRotation = Quaternion.Inverse(source.rotation) * ps.transform.rotation;
                Vector3 scale = ps.transform.lossyScale;
                Vector3 parentScale = proxy.lossyScale;
                clone.transform.localScale = new Vector3(scale.x / parentScale.x,
                    scale.y / parentScale.y, scale.z / parentScale.z);
                var components = clone.GetComponentsInChildren<Component>(true);
                for (int c = components.Length - 1; c >= 0; c--)
                {
                    Component component = components[c];
                    if (component.TryCast<Transform>() != null || component.TryCast<ParticleSystem>() != null ||
                        component.TryCast<ParticleSystemRenderer>() != null ||
                        component.TryCast<TrailRenderer>() != null) continue;
                    var behaviour = component.TryCast<Behaviour>();
                    if (behaviour != null) behaviour.enabled = false;
                    UnityEngine.Object.DestroyImmediate(component);
                }
                var originalSystems = ps.GetComponentsInChildren<ParticleSystem>(true);
                var cloneSystems = clone.GetComponentsInChildren<ParticleSystem>(true);
                var originalTrails = ps.GetComponentsInChildren<TrailRenderer>(true);
                var cloneTrails = clone.GetComponentsInChildren<TrailRenderer>(true);
                for (int t = 0; t < Math.Min(originalTrails.Length, cloneTrails.Length); t++)
                {
                    cloneTrails[t].Clear();
                    cloneTrails[t].autodestruct = false;
                    Trails[Path(source, originalTrails[t].transform)] = cloneTrails[t];
                }
                for (int n = 0; n < Math.Min(originalSystems.Length, cloneSystems.Length); n++)
                {
                    string path = Path(source, originalSystems[n].transform);
                    ParticleSystem copy = cloneSystems[n];
                    // Fixed full transforms are sampled every render frame; local particles
                    // must travel with the car, while world-space smoke trails retain their setting.
                    copy.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    Copies[path] = copy;
                    Vector3 originalScale = originalSystems[n].transform.localScale;
                    Vector3 cloneScale = copy.transform.localScale;
                    ScaleFactors[path] = new Vector3(
                        originalScale.x == 0f ? 1f : cloneScale.x / originalScale.x,
                        originalScale.y == 0f ? 1f : cloneScale.y / originalScale.y,
                        originalScale.z == 0f ? 1f : cloneScale.z / originalScale.z);
                    log.LogInfo("proxy effect: " + path);
                }
                clone.SetActive(true);
            }
        }

        public static void Receive(CarVisualMessage message)
        {
            if (_haveSequence && unchecked((int)(message.SequenceNumber - _lastSequence)) <= 0) return;
            _haveSequence = true;
            _lastSequence = message.SequenceNumber;
            if (Pending.Count == 64) Pending.RemoveAt(0);
            Pending.Add(message);
        }

        public static void Apply(uint renderedSequence)
        {
            while (PendingTrails.Count > 0 && unchecked((int)(renderedSequence - PendingTrails[0].SequenceNumber)) >= 0)
            {
                CarTrailMessage message = PendingTrails[0]; PendingTrails.RemoveAt(0);
                foreach (TrailVisualState t in message.Trails)
                    if (Trails.TryGetValue(t.Path, out var copy) && copy != null)
                    {
                        copy.enabled = t.Active; copy.emitting = t.Active && t.Emitting;
                        copy.time = t.Time; copy.startWidth = t.StartWidth; copy.endWidth = t.EndWidth;
                    }
            }
            CarVisualMessage chosen = null;
            while (Pending.Count > 0 && unchecked((int)(renderedSequence - Pending[0].SequenceNumber)) >= 0)
            {
                chosen = Pending[0];
                Pending.RemoveAt(0);
            }
            if (chosen == null) return;
            Emitting = 0;
            int missing=0;
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach (ParticleVisualState state in chosen.Particles)
            {
                if(ContactGroup(state.Path,out string group))
                {
                    seen.Add(group);
                    if(!ContactRoots.TryGetValue(group,out var contact) || contact==null)
                    {RemoveContact(group);CreateContact(group);}
                }
                if (!Copies.TryGetValue(state.Path, out ParticleSystem copy) || copy == null)
                {
                    MissingTemplates++;
                    missing++;
                    if(Bootstrapper.DiagnosticsEnabled && MissingTemplates<=3)
                        Log.LogWarning("Missing peer effect path="+state.Path+"; known="+string.Join(" | ",Copies.Keys));
                    continue;
                }
                copy.gameObject.SetActive(state.Active);
                if(_proxy!=null)
                {
                    copy.transform.position=_proxy.TransformPoint(new Vector3(state.PositionX,state.PositionY,state.PositionZ));
                    copy.transform.rotation=_proxy.rotation*new Quaternion(state.RotationX,state.RotationY,state.RotationZ,state.RotationW).normalized;
                }
                NativeParticleAccess.Write(copy, state.Rate, state.Speed, state.Size, state.Lifetime);
                // Keep the original native colour gradients. The legacy startColor wrapper
                // throws in this build; the cloned template already supplies blue exhaust.
                Vector3 factor = ScaleFactors[state.Path];
                copy.transform.localScale = new Vector3(state.ScaleX * factor.x,
                    state.ScaleY * factor.y, state.ScaleZ * factor.z);
                var emission = copy.emission;
                emission.enabled = state.Playing;
                if (state.Active && state.Playing)
                {
                    if (!copy.isPlaying) copy.Play(false);
                    Emitting++;
                }
                else if (copy.isPlaying) copy.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            foreach(string group in new List<string>(ContactRoots.Keys))if(!seen.Contains(group))RemoveContact(group);
            Applied++;
            Status = missing==0 ? "replicating" : "partial - missing "+missing+" effect templates";
        }

        public static void Clear()
        {
            ClearContacts();_proxy=null;
            Copies.Clear();
            ScaleFactors.Clear();
            Pending.Clear();
            Trails.Clear();
            PendingTrails.Clear();
            _haveSequence = false;
            Emitting = 0;
            Status = "waiting";
            _failureLogged = false;
        }

        public static void ReceiveTrails(CarTrailMessage message)
        {
            if (PendingTrails.Count > 0 && unchecked((int)(message.SequenceNumber -
                PendingTrails[PendingTrails.Count - 1].SequenceNumber)) <= 0) return;
            if (PendingTrails.Count == 64) PendingTrails.RemoveAt(0);
            PendingTrails.Add(message);
        }

        private static string Path(Transform root, Transform node)

        {
            string path = node.name;
            for (Transform parent = node.parent; parent != null && parent != root; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
        private static string ComponentPath(Transform root,Transform node)=>root.Pointer==node.Pointer ? "" : Path(root,node);
        private static Transform ContactAncestor(Transform root,Transform node)
        {
            var prefab=GameRefs.PlayerCar?.contactSparks;
            if(prefab==null)return null;
            string name=prefab.name+"(Clone)";
            for(var current=node;current!=null && current.Pointer!=root.Pointer;current=current.parent)
                if(string.Equals(current.name,name,StringComparison.Ordinal))return current;
            return null;
        }
        private static string EffectPath(Transform root,Transform node)
        {
            var contact=ContactAncestor(root,node);
            return contact==null ? Path(root,node) : "@contact/"+contact.GetInstanceID().ToString(CultureInfo.InvariantCulture)+"|"+ComponentPath(contact,node);
        }
        private static bool ContactGroup(string path,out string group)
        {
            group="";
            if(path==null || !path.StartsWith("@contact/",StringComparison.Ordinal))return false;
            int delimiter=path.IndexOf('|');
            if(delimiter<=9)return false;
            string text=path.Substring(9,delimiter-9);
            if(!int.TryParse(text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int id) || id==0 ||
                text!=id.ToString(CultureInfo.InvariantCulture))return false;
            group=path.Substring(0,delimiter);return true;
        }
        private static void CreateContact(string group)
        {
            // Peer text selects only a logical incarnation of this native prefab;
            // it never selects an arbitrary asset or a component/script to execute.
            var prefab=GameRefs.PlayerCar?.contactSparks;
            if(prefab==null || _proxy==null || ContactRoots.Count>=32)return;
            var container=new GameObject("CP_Contact_"+group.Substring(9));
            container.SetActive(false);container.transform.SetParent(_proxy,false);
            ContactRoots.Add(group,container);
            try
            {
                var clone=UnityEngine.Object.Instantiate((UnityEngine.Object)prefab,container.transform,false).Cast<GameObject>();
                var components=clone.GetComponentsInChildren<Component>(true);
                for(int i=components.Length-1;i>=0;i--)
                {
                    var component=components[i];
                    if(component.TryCast<Transform>()!=null || component.TryCast<ParticleSystem>()!=null ||
                        component.TryCast<ParticleSystemRenderer>()!=null || component.TryCast<TrailRenderer>()!=null)continue;
                    var behaviour=component.TryCast<Behaviour>();if(behaviour!=null)behaviour.enabled=false;
                    UnityEngine.Object.DestroyImmediate(component);
                }
                var originals=prefab.GetComponentsInChildren<ParticleSystem>(true);
                var copies=clone.GetComponentsInChildren<ParticleSystem>(true);
                if(originals.Length!=copies.Length || originals.Length==0)throw new InvalidOperationException("Contact particle prefab layout differs");
                for(int i=0;i<copies.Length;i++)
                {
                    string key=group+"|"+ComponentPath(prefab.transform,originals[i].transform);
                    copies[i].Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    Copies[key]=copies[i];ScaleFactors[key]=Vector3.one;
                }
                clone.SetActive(true);container.SetActive(true);DynamicGroupsCreated++;
            }
            catch{RemoveContact(group);throw;}
        }
        private static void RemoveContact(string group)
        {
            if(ContactRoots.Remove(group,out var root) && root!=null){root.SetActive(false);UnityEngine.Object.Destroy(root);}
            foreach(string key in new List<string>(Copies.Keys))
                if(key.StartsWith(group+"|",StringComparison.Ordinal)){Copies.Remove(key);ScaleFactors.Remove(key);}
        }
        private static void ClearContacts()
        {foreach(string group in new List<string>(ContactRoots.Keys))RemoveContact(group);}
    }
}
