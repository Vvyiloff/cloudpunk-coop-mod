using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    /// <summary>Ordered host NPC presentation. Campaign transactions remain a separate authority layer.</summary>
    public static class WorldNpcReplication
    {
        private sealed class Suppressed
        {
            public Character Character;
            public Renderer[] Renderers;
            public bool[] Visible;
            public Behaviour[] Updaters;
            public bool[] Enabled;
            public Collider[] Colliders;
            public bool[] Collision;
            public GameObject Ui;
            public bool UiActive;
        }
        private sealed class Replica
        {
            public GameObject Root;
            public MeshAnimator Animator;
            public ReplicatedAnimation Playback;
            public string Model;
            public readonly CarSnapshotBuffer Timeline = new CarSnapshotBuffer();
            public readonly Dictionary<uint,NpcVisualState> States = new Dictionary<uint,NpcVisualState>();
            public readonly Queue<uint> Sequences = new Queue<uint>();
            public NpcVisualState Latest;
            public float SeenAt;
            public bool Visible;
        }
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop NPCs");
        private static readonly NpcFrameAssembler Assembler = new NpcFrameAssembler();
        private static readonly Dictionary<string,Replica> Replicas = new Dictionary<string,Replica>(StringComparer.Ordinal);
        private static readonly List<Suppressed> Original = new List<Suppressed>();
        private static readonly List<MEC.CoroutineHandle> Paused = new List<MEC.CoroutineHandle>();
        private static readonly Dictionary<string,NpcVisualState> SourceStates=new Dictionary<string,NpcVisualState>(StringComparer.Ordinal);
        private static IReadOnlyList<NativeNpcRegistry.Entry> _source;
        private static IReadOnlyDictionary<string,GameObject> _templates;
        private static NpcVisualState[] _pending;
        private static uint _pendingSequence, _pendingEpoch, _epoch = 1, _renderEpoch;
        private static int _renderScene = -1;
        private static int _receiveScene = -1;
        private static int _pendingScene, _sourceScene = -1;
        private static int _sourceHandle=-1;
        private static float _scanAt, _receivedAt;
        private static bool _active, _failed, _logged;
        private static int _prewarmedHandle=-1;
        /// <summary>Build immutable model descriptions behind the loading fade, before
        /// the first live replica frame can stall gameplay after regional reunion.</summary>
        public static void PrewarmLoadedRegion()
        {
            if(CoopSession.Role==CoopRole.Off || !CoopSession.PeerOnline || !GameRefs.Resolved ||
                Global.Instance==null || Global.Instance.isLoadingSaveGame || Global.Instance.IsInMenu() ||
                SceneLoader.Instance==null || !SceneLoader.Instance.IsActive() || SceneLoader.IsLoading() ||
                Global.Instance.gdoc!=null && Global.Instance.gdoc.isScanning || !RegionReplication.PeerFresh ||
                RegionReplication.PeerScene!=SceneManager.GetActiveScene().buildIndex) return;
            int handle=SceneManager.GetActiveScene().handle;
            if(handle==_prewarmedHandle) return;
            _prewarmedHandle=handle;
            using var timing=CoopPerformance.Measure(CoopPerformance.Stage.NpcScan);
            var templates=NativeNpcRegistry.Templates(Log);
            if(Bootstrapper.DiagnosticsEnabled) Log.LogInfo("Loading-screen NPC descriptor warmup: "+templates.Count+" models");
        }
        public static string Status { get; private set; } = "waiting";
        public static int Count { get; private set; }
        public static int ActiveCount { get; private set; }

        public static bool FindNearbyNamed(Vector3 position,float radius,out string id,out string name)
        {
            id="";name="";
            if(!_active || _renderScene!=SceneManager.GetActiveScene().buildIndex) return false;
            float nearest=radius;
            foreach(var pair in Replicas)
            {
                var r=pair.Value;
                if(!r.Visible || r.Latest.Crowd || r.Root==null || Time.realtimeSinceStartup-r.SeenAt>2f) continue;
                float distance=Vector3.Distance(position,r.Root.transform.position);
                if(distance>=nearest) continue;
                var native=Global.Instance.FindSerializeObject(pair.Key)?.TryCast<Character>();
                if(native==null || !native.canTalkTo || native.FindDialog(false)==null) continue;
                nearest=distance;id=pair.Key;name=native.definedName;
            }
            return id.Length!=0;
        }

        public static void CaptureAndSend(uint sequence)
        {
            using var timing=CoopPerformance.Measure(CoopPerformance.Stage.NpcCapture);
            if (!RegionReplication.LocalReady)
            {
                _source=null; _sourceScene=-1; _scanAt=0;
                SourceStates.Clear();
                return;
            }
            if (!GameRefs.Resolved || GameRefs.Global.IsInMenu() || GameRefs.Global.isLoadingSaveGame) return;
            if (_source == null && Time.realtimeSinceStartup < _scanAt) return;
            try
            {
                int scene = SceneManager.GetActiveScene().buildIndex;
                int handle=SceneManager.GetActiveScene().handle;
                if (_source == null || scene != _sourceScene || handle!=_sourceHandle || Time.realtimeSinceStartup >= _scanAt)
                {
                    if (scene != _sourceScene || handle!=_sourceHandle) { _epoch++; SourceStates.Clear(); }
                    _sourceHandle=handle;
                    _sourceScene = scene; _scanAt = Time.realtimeSinceStartup + 5f;
                    using var scanTiming=CoopPerformance.Measure(CoopPerformance.Stage.NpcScan);
                    _source = NativeNpcRegistry.Scan(Log);
                }
                var states = new List<NpcVisualState>();
                ActiveCount = 0;
                foreach (var entry in _source)
                {
                    var character = entry.Character;
                    if (character == null) continue;
                    bool active = character.gameObject.activeInHierarchy;
                    if (string.IsNullOrEmpty(entry.ModelKey))
                    {
                        if (active) throw new InvalidOperationException("Active NPC model is missing: " + entry.Id);
                        continue;
                    }
                    if(!active && SourceStates.TryGetValue(entry.Id,out var previous) && previous.Model==entry.ModelKey)
                    {
                        var dormant=new NpcVisualState(entry.Id,entry.ModelKey,entry.IsCrowd,false,
                            previous.X,previous.Y,previous.Z,previous.Yaw,previous.ScaleX,previous.ScaleY,previous.ScaleZ,0,0,"",0);
                        states.Add(dormant); SourceStates[entry.Id]=dormant; continue;
                    }
                    var t = character.transform; var p = t.position; var scale = t.lossyScale; var anim = character.anim;
                    int nativeState=!active || anim==null ? 0 : (int)anim.GetCurrentState();
                    int nativeDirection=!active || anim==null ? 0 : (int)anim.currentDirection;
                    var captured=new NpcVisualState(entry.Id, entry.ModelKey, entry.IsCrowd, active,
                        p.x,p.y,p.z,t.eulerAngles.y,scale.x,scale.y,scale.z,
                        nativeState>=0 && nativeState<=5 ? (byte)nativeState : (byte)0,
                        nativeDirection>=0 && nativeDirection<=7 ? (byte)nativeDirection : (byte)0,
                        anim == null || anim.currentAnimation == null ? "" : anim.currentAnimation.name,
                        anim == null ? 0 : Math.Clamp(anim.currentFrame,0,100000));
                    states.Add(captured); SourceStates[entry.Id]=captured;
                    if (active) ActiveCount++;
                }
                if (states.Count > NpcMessage.MaxChunks * NpcMessage.MaxPerChunk)
                    throw new InvalidOperationException("World NPC count exceeds bounded frame capacity");
                Count = states.Count;
                int chunks = Math.Max(1,(states.Count + NpcMessage.MaxPerChunk-1)/NpcMessage.MaxPerChunk);
                for (int c=0;c<chunks;c++)
                {
                    int size = Math.Min(NpcMessage.MaxPerChunk,states.Count-c*NpcMessage.MaxPerChunk);
                    var block = new NpcVisualState[size];
                    states.CopyTo(c*NpcMessage.MaxPerChunk,block,0,size);
                    CoopSession.SendMessage(new NpcMessage(_epoch,sequence,scene,(byte)c,(byte)chunks,block));
                }
                Status = "authoritative";
            }
            catch(Exception ex) { Fail(ex); }
        }

        public static void Receive(NpcMessage message, float now)
        {
            // Reject before assembly: a foreign frame must not consume epoch/sequence state.
            if (!RegionReplication.LocalReady || !RegionReplication.SameRegion ||
                message.Scene != SceneManager.GetActiveScene().buildIndex) return;
            if (_receiveScene != -1 && _receiveScene != message.Scene) Stop();
            _receiveScene = message.Scene;
            if (!Assembler.Add(message,out var frame)) return;
            _pending = frame; _pendingScene = message.Scene; _pendingEpoch = message.Epoch;
            _pendingSequence = message.Sequence; _receivedAt = now;
        }

        private static void Begin()
        {
            // Validate models before this method. No native OnDisable/ReturnToPool is invoked:
            // existing objects remain in their original pools and only presentation is hidden.
            var global = Global.Instance;
            if (global.aiManagers != null)
                foreach (var manager in global.aiManagers)
                    if (manager != null && MEC.Timing.PauseCoroutines(manager.updateRoutine) > 0)
                        Paused.Add(manager.updateRoutine);
            foreach(var entry in NativeNpcRegistry.Scan(Log))
            {
                Character c = entry.Character;
                if (c == null) continue;
                var renderers = c.GetComponentsInChildren<Renderer>(true);
                var colliders = c.GetComponentsInChildren<Collider>(true);
                var saved = new Suppressed { Character = c, Renderers = new Renderer[renderers.Length],
                    Visible = new bool[renderers.Length], Colliders = new Collider[colliders.Length],
                    Collision = new bool[colliders.Length], Ui = c.uiCanvas == null ? null : c.uiCanvas.gameObject };
                for(int i=0;i<renderers.Length;i++) { saved.Renderers[i]=renderers[i]; saved.Visible[i]=renderers[i].enabled; }
                for(int i=0;i<colliders.Length;i++) { saved.Colliders[i]=colliders[i]; saved.Collision[i]=colliders[i].enabled; }
                var updaters = new List<Behaviour>();
                foreach(var component in c.GetComponentsInChildren<Behaviour>(true))
                    if(component.TryCast<CharacterUpdater>() != null || component.TryCast<MeshAnimatorUpdater>() != null)
                        updaters.Add(component);
                saved.Updaters=updaters.ToArray(); saved.Enabled=new bool[updaters.Count];
                for(int i=0;i<updaters.Count;i++) saved.Enabled[i]=updaters[i].enabled;
                saved.UiActive = saved.Ui != null && saved.Ui.activeSelf;
                Original.Add(saved);
                foreach(var updater in saved.Updaters) updater.enabled=false;
            }
            _active = true;
            Log.LogInfo("NPC presentation: " + Original.Count + " local actors hidden; " + Paused.Count + " AI routines paused");
        }

        public static void Tick(float now)
        {
            if (CoopSession.Role != CoopRole.Guest) return;
            if (!RegionReplication.LocalReady || !RegionReplication.SameRegion)
            { Stop(); return; }
            int scene = SceneManager.GetActiveScene().buildIndex;
            if ((_active && _renderScene != scene) || (_receiveScene != -1 && _receiveScene != scene))
            { Stop(); return; }
            if (!CoopSession.PeerOnline || (now-_receivedAt>2f && _active)) { Stop(); return; }
            if (!GameRefs.Resolved || GameRefs.Global.IsInMenu() || GameRefs.Global.isLoadingSaveGame)
            { if(_active) Stop(); return; }
            if (_failed) return;
            try
            {
                if (_pending != null)
                {
                    if (_pendingScene != SceneManager.GetActiveScene().buildIndex)
                    {
                        Stop();
                        Status="waiting for host scene";
                        return;
                    }
                    if(_active && (_pendingEpoch != _renderEpoch || _pendingScene != _renderScene))
                        RestorePresentation();
                    if (_templates == null) _templates=NativeNpcRegistry.Templates(Log);
                    var checkedStaticModels = new HashSet<string>(StringComparer.Ordinal);
                    foreach(var n in _pending)
                    {
                        if(!n.Active) continue;
                        if(!_templates.TryGetValue(n.Model,out var template) || template==null)
                            throw new InvalidOperationException("Guest NPC model unavailable: " + n.Model);
                        if(n.Model.StartsWith(StaticNpcVisual.KeyPrefix,StringComparison.Ordinal))
                        {
                            if(!string.IsNullOrEmpty(n.Clip) || n.Frame!=0 || n.Animation!=0 || n.Direction!=0)
                                throw new InvalidOperationException("Static NPC received animated state: " + n.Id);
                            if(checkedStaticModels.Add(n.Model)) StaticNpcVisual.Validate(template,n.Model);
                        }
                    }
                    if (_pending.Length==0 && !_active) { _pending=null; return; }
                    if(!_active) { Begin(); _renderEpoch=_pendingEpoch; _renderScene=_pendingScene; }
                    var present = new HashSet<string>(StringComparer.Ordinal);
                    ActiveCount=0; Count=_pending.Length;
                    foreach(var n in _pending)
                    {
                        present.Add(n.Id);
                        if(!Replicas.TryGetValue(n.Id,out var r))
                        {
                            r=new Replica { Model=n.Model }; Replicas.Add(n.Id,r);
                        }
                        if(r.Model!=n.Model) throw new InvalidOperationException("NPC model changed without new identity");
                        r.Latest=n; r.SeenAt=now; r.Visible=n.Active;
                        Quaternion q=Quaternion.Euler(0,n.Yaw,0);
                        r.Timeline.Add(new CarStateMessage(n.X,n.Y,n.Z,q.x,q.y,q.z,q.w,0,0,0,0,0,0,0,0,_pendingSequence),_receivedAt);
                        r.States[_pendingSequence]=n; r.Sequences.Enqueue(_pendingSequence);
                        while(r.Sequences.Count>64) r.States.Remove(r.Sequences.Dequeue());
                        if(n.Active) ActiveCount++;
                    }
                    foreach(var pair in Replicas)
                        if(!present.Contains(pair.Key))
                        { pair.Value.Visible=false; if(pair.Value.Root!=null) pair.Value.Root.SetActive(false); }
                    _pending=null;
                }
                if(!_active) return;
                foreach(var saved in Original)
                {
                    if(saved.Character==null) continue;
                    foreach(var renderer in saved.Renderers) if(renderer!=null) renderer.enabled=false;
                    foreach(var collider in saved.Colliders) if(collider!=null) collider.enabled=false;
                    if(saved.Ui!=null) saved.Ui.SetActive(false);
                }
                var expired=new List<string>();
                foreach(var pair in Replicas)
                {
                    Replica r=pair.Value;
                    if(now-r.SeenAt>5f) { if(r.Root!=null) UnityEngine.Object.Destroy(r.Root); expired.Add(pair.Key); continue; }
                    if(!r.Visible) { if(r.Root!=null) r.Root.SetActive(false); continue; }
                    if(r.Root==null) Create(r,pair.Key);
                    if(!r.Timeline.TrySample(now,out var from,out var to,out float blend,out _)) continue;
                    Vector3 p=Vector3.Lerp(new Vector3(from.PositionX,from.PositionY,from.PositionZ),
                        new Vector3(to.PositionX,to.PositionY,to.PositionZ),blend);
                    Quaternion q=Quaternion.Slerp(new Quaternion(from.RotationX,from.RotationY,from.RotationZ,from.RotationW),
                        new Quaternion(to.RotationX,to.RotationY,to.RotationZ,to.RotationW),blend);
                    NpcVisualState state=r.States.TryGetValue(from.SequenceNumber,out var s)?s:r.Latest;
                    r.Root.SetActive(true); r.Root.transform.localScale=new Vector3(state.ScaleX,state.ScaleY,state.ScaleZ);
                    ApplyAnimation(r,state);
                    r.Root.transform.SetPositionAndRotation(p,q); // Native root motion cannot change authority.
                }
                foreach(string id in expired) Replicas.Remove(id);
                Status="replicating";
            }
            catch(Exception ex) { Stop(); _failed=true; Fail(ex); }
        }

        private static void Create(Replica r,string id)
        {
            r.Root=new GameObject("CloudpunkCoop_NPC_"+id); r.Root.SetActive(false);
            if(r.Model.StartsWith(StaticNpcVisual.KeyPrefix,StringComparison.Ordinal))
            {
                StaticNpcVisual.Copy(_templates[r.Model],r.Root.transform,r.Model);
                return;
            }
            var clone=UnityEngine.Object.Instantiate((UnityEngine.Object)_templates[r.Model],r.Root.transform,false).Cast<GameObject>();
            foreach(var component in clone.GetComponentsInChildren<Component>(true))
            {
                if(component.TryCast<Transform>()!=null || component.TryCast<Renderer>()!=null ||
                    component.TryCast<MeshFilter>()!=null || component.TryCast<MeshAnimator>()!=null) continue;
                UnityEngine.Object.DestroyImmediate(component);
            }
            r.Animator=clone.GetComponent<MeshAnimator>();
            if(r.Animator==null) r.Animator=clone.GetComponentInChildren<MeshAnimator>(true);
            if(r.Animator==null) throw new InvalidOperationException("NPC template has no native MeshAnimator");
            r.Animator.parentTransform=r.Root.transform; r.Animator.audioSource=null;
            r.Animator.OnAnimationFinished=null;
            r.Animator.isPlayer=false; r.Animator.updateWhenOffscreen=true;
            r.Playback=new ReplicatedAnimation(r.Animator);
            clone.SetActive(true);
        }

        private static void ApplyAnimation(Replica r,NpcVisualState state)
        {
            if(r.Model.StartsWith(StaticNpcVisual.KeyPrefix,StringComparison.Ordinal)) return;
            r.Playback.Apply(state.Animation,state.Direction,state.Clip,state.Frame);
        }

        public static string AnimationDiagnostics
        {
            get
            {
                int applied=0,changes=0,missing=0;
                foreach(var r in Replicas.Values)
                    if(r.Playback!=null) { applied+=r.Playback.AppliedFrames; changes+=r.Playback.FrameChanges; missing+=r.Playback.MissingClips; }
                return "npcAnimApplied="+applied+";npcAnimChanges="+changes+";npcAnimMissing="+missing;
            }
        }

        private static void Fail(Exception ex)
        {
            Status="failed: "+ex.Message;
            if(!_logged) { _logged=true; Log.LogWarning("world NPC replication: "+ex); }
        }

        public static void DrawLabels()
        {
            if(!TrafficReplication.ShowLabels || !GameRefs.Resolved || !CoopSession.PeerOnline) return;
            var camera=GameRefs.Global.mainCamera;
            if(camera==null) return;
            if(CoopSession.Role==CoopRole.Host && _source!=null)
                foreach(var entry in _source)
                    if(entry.Character!=null && entry.Character.gameObject.activeInHierarchy)
                        Draw(entry.Id,entry.Character.transform.position,entry.Character.anim==null ? 0 : entry.Character.anim.currentFrame);
            if(CoopSession.Role==CoopRole.Guest)
                foreach(var pair in Replicas)
                    if(pair.Value.Root!=null && pair.Value.Visible)
                        Draw(pair.Key,pair.Value.Root.transform.position,pair.Value.Latest.Frame);

            void Draw(string id,Vector3 position,int frame)
            {
                Vector3 p=camera.WorldToScreenPoint(position+Vector3.up*2f);
                if(p.z<=0 || p.z>60 || p.x<0 || p.x>=Screen.width || p.y<0 || p.y>=Screen.height) return;
                string label=id.StartsWith("crowd:",StringComparison.Ordinal) ? "Crowd "+id.Substring(id.LastIndexOf(':')+1) : "NPC "+id.Substring(0,Math.Min(8,id.Length));
                GUI.Box(new Rect(p.x-65,Screen.height-p.y,130,22),label+" f"+frame);
            }
        }

        public static void Stop()
        {
            RestorePresentation();
            _failed=_logged=false; _pending=null; _source=null;
            _pendingSequence=_pendingEpoch=0; _pendingScene=-1; _receivedAt=0;
            _receiveScene=-1;
            _sourceScene=-1; _scanAt=0;
            _sourceHandle=-1;
            SourceStates.Clear();
            Assembler.Clear(); Count=ActiveCount=0; Status="waiting";
        }

        private static void RestorePresentation()
        {
            foreach(var pair in Replicas)
                RestoreNative(() =>
                {
                    if(pair.Value.Root==null) return;
                    pair.Value.Root.SetActive(false);
                    UnityEngine.Object.Destroy(pair.Value.Root);
                });
            Replicas.Clear();
            foreach(var saved in Original)
            {
                for(int i=0;i<saved.Renderers.Length;i++)
                {
                    int index=i;
                    RestoreNative(() => { if(saved.Renderers[index]!=null) saved.Renderers[index].enabled=saved.Visible[index]; });
                }
                for(int i=0;i<saved.Colliders.Length;i++)
                {
                    int index=i;
                    RestoreNative(() => { if(saved.Colliders[index]!=null) saved.Colliders[index].enabled=saved.Collision[index]; });
                }
                for(int i=0;i<saved.Updaters.Length;i++)
                {
                    int index=i;
                    RestoreNative(() => { if(saved.Updaters[index]!=null) saved.Updaters[index].enabled=saved.Enabled[index]; });
                }
                RestoreNative(() => { if(saved.Ui!=null) saved.Ui.SetActive(saved.UiActive); });
            }
            Original.Clear();
            foreach(var handle in Paused)
                try { MEC.Timing.ResumeCoroutines(handle); }
                catch(Exception ex) { Log.LogWarning("NPC AI restore: " + ex.Message); }
            Paused.Clear(); _active=false; _templates=null; _renderScene=-1; _renderEpoch=0;
        }

        private static void RestoreNative(Action restore)
        {
            try { restore(); }
            catch(Exception ex) { Log.LogWarning("NPC presentation restore: " + ex.Message); }
        }
    }
}
