using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    /// <summary>
    /// Read-only discovery; call on the Unity main thread. No simulation, instantiation or
    /// native setters. Results are snapshots: native references can expire after a scene unload.
    /// </summary>
    public static class NativeNpcRegistry
    {
        public sealed class Entry
        {
            public Character Character;
            public string Id;
            public string ModelKey;
            public bool IsCrowd;
        }

        private const int MaxScenes = 64, MaxNodes = 65536, MaxCharacters = 8192;
        private const int MaxManagers = 128, MaxPrefabs = 4096, MaxAllocations = 16384;
        private const int MaxVisualNodes = 256, MaxMaterials = 64, MaxIdleAnimations = 64;
        private static readonly string AllocationScope = Guid.NewGuid().ToString("N");
        private static readonly Dictionary<int, Allocation> Allocations = new Dictionary<int, Allocation>();
        private static ulong _nextAllocation;
        private static string _catalogScenes;
        private static readonly List<Character> CatalogCharacters=new List<Character>();
        private static readonly List<AIManager> CatalogManagers=new List<AIManager>();
        private sealed class CachedVisual
        {
            public MeshAnimator Animator;
            public AnimSet Set;
            public string ActorName;
            public Visual Value;
        }
        private static readonly Dictionary<int,CachedVisual> VisualCache=new();

        private sealed class Allocation
        {
            public Character Character;
            public string Id;
        }

        private sealed class Visual
        {
            public GameObject Template;
            public string Signature;
            public string Key;
            // Unity asset instance IDs are ONLY a same-process ambiguity check, never wire IDs.
            public string AssetIdentity;
            public int CatalogRoot;
        }

        private sealed class Snapshot
        {
            public readonly List<Character> Characters = new List<Character>();
            public readonly HashSet<int> Crowd = new HashSet<int>();
            public readonly Dictionary<string, Visual> Visuals = new Dictionary<string, Visual>(StringComparer.Ordinal);
            public readonly HashSet<string> Ambiguous = new HashSet<string>(StringComparer.Ordinal);
        }

        private sealed class Diagnostics
        {
            private readonly ManualLogSource _log;
            private int _count;
            public Diagnostics(ManualLogSource log) { _log = log; }
            public void Warn(string message)
            {
                if (_count++ < 16) _log?.LogWarning("NPC registry: " + message);
            }
            public void Finish()
            {
                if (_count > 16) _log?.LogWarning("NPC registry: " + (_count - 16) + " further diagnostics suppressed");
            }
        }

        /// <summary>
        /// Loaded named (definedName nonempty, not crowd) Characters and loaded AIManager
        /// poolNPCs, including inactive objects. Named Id is the unmodified GetGuid(), only
        /// when unique across the collected Characters. Missing/duplicate GUIDs are omitted.
        /// Crowd Id is a process-scoped allocation, stable while the native Character lives;
        /// pool reuse retains that allocation. Guests must use the host's ID, not rescan-match it.
        /// ModelKey is empty if missing/ambiguous; the Entry is retained for identity diagnostics.
        /// A failed discovery throws; it must never be published as an empty city snapshot.
        /// </summary>
        public static IReadOnlyList<Entry> Scan(ManualLogSource log)
        {
            RequireMainThread();
            var diagnostics = new Diagnostics(log);
            try
            {
                var snapshot = Collect(diagnostics);
                var guids = new Dictionary<int, string>();
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var character in snapshot.Characters)
                {
                    string guid = character.GetGuid();
                    guids[character.GetInstanceID()] = guid;
                    if (!string.IsNullOrWhiteSpace(guid))
                        counts[guid] = counts.TryGetValue(guid, out int count) ? count + 1 : 1;
                }
                foreach (var pair in counts)
                    if (pair.Value > 1) diagnostics.Warn("duplicate GetGuid: " + pair.Key + " (" + pair.Value + ")");

                // Prune only destroyed objects, not inactive/unobserved living pool members.
                var expired = new List<int>();
                foreach (var pair in Allocations)
                    if (pair.Value.Character == null) expired.Add(pair.Key);
                foreach (int id in expired) Allocations.Remove(id);

                var entries = new List<Entry>();
                var emitted = new HashSet<string>(StringComparer.Ordinal);
                foreach (var character in snapshot.Characters)
                {
                    int instance = character.GetInstanceID();
                    bool crowd = snapshot.Crowd.Contains(instance) || character.isCrowdNPC;
                    if (!crowd && string.IsNullOrWhiteSpace(character.definedName)) continue;
                    string id;
                    if (crowd)
                    {
                        if (!Allocations.TryGetValue(instance, out var allocation))
                        {
                            if (Allocations.Count >= MaxAllocations)
                            { diagnostics.Warn("crowd allocation limit reached; character omitted"); continue; }
                            allocation = new Allocation { Character = character,
                                Id = "crowd:" + AllocationScope + ":" + (++_nextAllocation).ToString(CultureInfo.InvariantCulture) };
                            Allocations.Add(instance, allocation);
                        }
                        id = allocation.Id;
                    }
                    else
                    {
                        id = guids[instance];
                        if (string.IsNullOrWhiteSpace(id))
                        { diagnostics.Warn("missing named GetGuid: " + character.definedName); continue; }
                        if (counts[id] != 1) continue;
                    }
                    if (!emitted.Add(id)) throw new InvalidOperationException("duplicate emitted identity: " + id);
                    string key = string.Empty;
                    var visual = DescribeCharacter(character);
                    if (visual != null && !snapshot.Ambiguous.Contains(visual.Key) &&
                        snapshot.Visuals.TryGetValue(visual.Key, out var template) && SameVisual(template, visual))
                        key = visual.Key;
                    else diagnostics.Warn("missing/ambiguous ModelKey for " + id);
                    entries.Add(new Entry { Character = character, Id = id, ModelKey = key, IsCrowd = crowd });
                }
                entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
                return entries.AsReadOnly();
            }
            catch (Exception ex)
            {
                diagnostics.Warn("scan failed closed: " + ex.GetType().Name + ": " + ex.Message);
                throw new InvalidOperationException("NPC discovery failed; refusing an empty world snapshot", ex);
            }
            finally { diagnostics.Finish(); }
        }

        /// <summary>
        /// Fresh ordinal map from deterministic ModelKey to native MeshAnimator.gameObject
        /// or a static Character root (npcstatic:v1). Static roots are read-only templates;
        /// callers must copy only meshes, never instantiate the Character hierarchy.
        /// Sources: loaded managers' civilianPrefabs/robotPrefabs (explicit asset references)
        /// then loaded Characters. Catalog assets are preferred; ambiguous keys are absent.
        /// No Resources search or inactive filtering. Call again after loading changes; caller
        /// must validate every incoming active ModelKey before changing guest presentation.
        /// </summary>
        public static IReadOnlyDictionary<string, GameObject> Templates(ManualLogSource log)
        {
            RequireMainThread();
            var diagnostics = new Diagnostics(log);
            var result = new SortedDictionary<string, GameObject>(StringComparer.Ordinal);
            try
            {
                var snapshot = Collect(diagnostics);
                foreach (var pair in snapshot.Visuals)
                    if (!snapshot.Ambiguous.Contains(pair.Key) && pair.Value.Template != null)
                        result.Add(pair.Key, pair.Value.Template);
            }
            catch (Exception ex)
            {
                result.Clear();
                diagnostics.Warn("templates failed closed: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally { diagnostics.Finish(); }
            return new ReadOnlyDictionary<string, GameObject>(result);
        }

        private static void RequireMainThread()
        {
            if (!UnityEngine.Object.CurrentThreadIsMainThread())
                throw new InvalidOperationException("NativeNpcRegistry must run on the Unity main thread");
        }

        private static Snapshot Collect(Diagnostics diagnostics)
        {
            var result = new Snapshot();
            var managers = new List<AIManager>();
            var seenCharacters = new HashSet<int>();
            var seenManagers = new HashSet<int>();
            var seenScenes = new HashSet<int>();
            int prefabs = 0, poolSlots = 0;
            int scenes = SceneManager.sceneCount;
            if (scenes > MaxScenes) throw new InvalidOperationException("loaded scene limit exceeded");
            var global = Global.Instance;
            var sceneKey=new StringBuilder();
            for(int i=0;i<scenes;i++) sceneKey.Append(SceneManager.GetSceneAt(i).handle).Append(':');
            if(global!=null) sceneKey.Append(global.gameObject.scene.handle);
            string key=sceneKey.ToString();
            if(_catalogScenes!=key)
            {
                // Streamed city scenes change while pooled NPC assets remain alive.
                // Do not rebuild every immutable model on each district load.
                var staleVisuals=new List<int>();
                foreach(var pair in VisualCache)
                    if(pair.Value.Animator==null || pair.Value.Value.Template==null) staleVisuals.Add(pair.Key);
                foreach(int id in staleVisuals) VisualCache.Remove(id);
                for (int i = 0; i < scenes; i++) VisitScene(SceneManager.GetSceneAt(i));
                if (global != null) VisitScene(global.gameObject.scene);
                CatalogCharacters.Clear(); CatalogCharacters.AddRange(result.Characters);
                CatalogManagers.Clear(); CatalogManagers.AddRange(managers);
                _catalogScenes=key;
            }
            else
            {
                foreach(var character in CatalogCharacters)
                    if(character!=null && Loaded(character.gameObject)) AddCharacter(character);
                foreach(var manager in CatalogManagers)
                    if(manager!=null && Loaded(manager.gameObject)) AddManager(manager);
            }
            // Runtime actors are registered here; pools are read fresh below. Geometry-only
            // city objects need not be traversed every five seconds to find newly spawned NPCs.
            if(global!=null && global.aiManagers!=null)
                foreach(var manager in global.aiManagers) if(manager!=null) AddManager(manager);
            if(global!=null && global.savedObjectDictionary!=null)
                foreach(var pair in global.savedObjectDictionary)
                {
                    var linked=pair.Value==null ? null : pair.Value.linkedObject;
                    var character=linked==null ? null : linked.TryCast<Character>();
                    if(character!=null && Loaded(character.gameObject)) AddCharacter(character);
                }

            foreach (var manager in managers)
            {
                AddPrefabs(manager.civilianPrefabs);
                AddPrefabs(manager.robotPrefabs);
                var pool = manager.poolNPCs;
                if (pool == null) { diagnostics.Warn("AIManager.poolNPCs missing"); continue; }
                if (pool.Length > MaxCharacters - poolSlots) throw new InvalidOperationException("NPC pool work limit exceeded");
                poolSlots += pool.Length;
                for (int i = 0; i < pool.Length; i++)
                {
                    var character = pool[i];
                    if (character == null || !Loaded(character.gameObject)) continue;
                    result.Crowd.Add(character.GetInstanceID());
                    AddCharacter(character);
                }
            }
            foreach (var character in result.Characters)
            {
                var visual = DescribeCharacter(character);
                if (visual == null) diagnostics.Warn("missing visual signature: " + character.name);
                else Register(visual);
            }
            return result;

            void AddCharacter(Character character)
            {
                if (!seenCharacters.Add(character.GetInstanceID())) return;
                if (result.Characters.Count >= MaxCharacters) throw new InvalidOperationException("Character limit exceeded");
                result.Characters.Add(character);
            }
            void AddManager(AIManager manager)
            {
                if(!seenManagers.Add(manager.GetInstanceID())) return;
                if(managers.Count>=MaxManagers) throw new InvalidOperationException("AIManager limit exceeded");
                managers.Add(manager);
            }
            void VisitScene(Scene scene)
            {
                if (!scene.IsValid() || !scene.isLoaded || !seenScenes.Add(scene.handle)) return;
                if (seenScenes.Count > MaxScenes) throw new InvalidOperationException("scene limit exceeded");
                var roots = scene.GetRootGameObjects();
                if (roots.Length > MaxNodes) throw new InvalidOperationException("scene root limit exceeded");
                foreach (var root in roots)
                {
                    if(root == null) continue;
                    // Let native Unity traverse the city once per type. Visiting every road,
                    // light and voxel through managed/native getters stalls the network tick.
                    foreach(var character in root.GetComponentsInChildren<Character>(true))
                        if(character != null && Loaded(character.gameObject)) AddCharacter(character);
                    foreach(var manager in root.GetComponentsInChildren<AIManager>(true))
                    {
                        if(manager != null) AddManager(manager);
                    }
                }
            }
            void AddPrefabs(Il2CppReferenceArray<GameObject> assets)
            {
                if (assets == null) { diagnostics.Warn("AIManager prefab array missing"); return; }
                if (assets.Length > MaxPrefabs - prefabs) throw new InvalidOperationException("prefab limit exceeded");
                prefabs += assets.Length;
                for (int i = 0; i < assets.Length; i++)
                {
                    var root = assets[i];
                    if (root == null) { diagnostics.Warn("null prefab slot " + i); continue; }
                    MeshAnimator animator = null;
                    foreach (var node in VisualNodes(root.transform))
                    {
                        var components = node.GetComponents<MeshAnimator>();
                        if (components.Length > 1 || (components.Length == 1 && animator != null))
                            throw new InvalidOperationException("multiple MeshAnimators in prefab " + root.name);
                        if (components.Length == 1) animator = components[0];
                    }
                    Visual visual;
                    if (animator != null) visual = Describe(animator,Normalize(root.name));
                    else
                    {
                        Character character = null;
                        foreach (var node in VisualNodes(root.transform))
                        {
                            var components = node.GetComponents<Character>();
                            if (components.Length > 1 || (components.Length == 1 && character != null))
                                throw new InvalidOperationException("multiple Characters in static prefab " + root.name);
                            if (components.Length == 1) character = components[0];
                        }
                        visual = DescribeCharacter(character);
                    }
                    if (visual == null) { diagnostics.Warn("missing prefab signature: " + root.name); continue; }
                    visual.CatalogRoot = root.GetInstanceID();
                    Register(visual);
                }
            }
            void Register(Visual visual)
            {
                if (result.Ambiguous.Contains(visual.Key)) return;
                if (!result.Visuals.TryGetValue(visual.Key, out var existing))
                { result.Visuals.Add(visual.Key, visual); return; }
                // Distinct catalog roots sharing a signature are conservatively ambiguous,
                // even if their sampled assets match (unsampled geometry could differ).
                if (!SameVisual(existing, visual))
                {
                    result.Ambiguous.Add(visual.Key);
                    result.Visuals.Remove(visual.Key);
                    diagnostics.Warn("ambiguous/duplicate visual key excluded: " + visual.Key);
                }
                else if (existing.CatalogRoot != 0 && existing.CatalogRoot == visual.CatalogRoot)
                    diagnostics.Warn("duplicate prefab reference (same native asset): " + visual.Key);
            }
        }

        private static bool Loaded(GameObject go)
        {
            if (go == null) return false;
            var scene = go.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        private static bool SameVisual(Visual a, Visual b) =>
            a.Signature == b.Signature && a.AssetIdentity == b.AssetIdentity;

        private static Visual DescribeCharacter(Character character)
        {
            if (character == null) return null;
            // A malformed animated actor must not silently fall back to a static model.
            if (character.anim != null) return Describe(character.anim,Normalize(character.gameObject.name));
            var description = StaticNpcVisual.Describe(character);
            return description == null ? null : new Visual { Template = description.Template,
                Signature = description.Signature, Key = description.Key,
                AssetIdentity = description.AssetIdentity };
        }

        /// <summary>
        /// Exact v1 signature: length-prefixed UTF-8 strings (decimal UTF-16 length + ':' +
        /// value + ';'), in this order: 'npcvisual-v1', normalized animator GO name, idle count,
        /// every idle asset name in array order (empty for null), walkForward asset name,
        /// then each transform in depth-first sibling order: relative path, renderer count,
        /// and each renderer's shared-material count and asset names in slot order.
        /// Relative path is '/' at root, then '/siblingIndex:normalizedName' per child.
        /// Only trailing '(Clone)' tokens and surrounding whitespace are stripped from GO
        /// names; asset names are exact. Key = 'npcvisual:v1:' + uppercase SHA256 hex of
        /// UTF-8(signature). No native pointer, scene, active state or animated mesh frame
        /// enters the key. Asset IDs validate same-process equality only. Runtime clone/prefab
        /// equality of these asset fields must still be checked in the actual game.
        /// </summary>
        private static Visual Describe(MeshAnimator animator,string actorName)
        {
            if(animator==null || animator.gameObject==null || animator.animSet==null) return null;
            int id=animator.GetInstanceID();
            var set=animator.animSet;
            if(VisualCache.TryGetValue(id,out var cached) && cached.Animator!=null &&
                cached.Animator.Pointer==animator.Pointer && cached.Set!=null && cached.Set.Pointer==set.Pointer &&
                cached.ActorName==actorName && cached.Value.Template!=null)
                return cached.Value;
            var visual=DescribeUncached(animator,actorName);
            if(visual!=null)
            {
                if(VisualCache.Count>=MaxCharacters+MaxPrefabs) VisualCache.Clear();
                VisualCache[id]=new CachedVisual{Animator=animator,Set=set,ActorName=actorName,Value=visual};
            }
            return visual;
        }

        private static Visual DescribeUncached(MeshAnimator animator,string actorName)
        {
            if (animator == null || animator.gameObject == null || animator.animSet == null) return null;
            string name = Normalize(animator.gameObject.name);
            if (name.Length == 0) return null;
            var signature = new StringBuilder();
            var identity = new StringBuilder();
            Token(signature, "npcvisual-v2"); Token(signature, actorName); Token(signature, name);
            var set = animator.animSet;
            var idle = set.idleAnimations;
            int idleCount = idle == null ? 0 : idle.Length;
            if (idleCount > MaxIdleAnimations) throw new InvalidOperationException("idle animation limit exceeded");
            Token(signature, idleCount.ToString(CultureInfo.InvariantCulture));
            int animations = 0, renderers = 0, materials = 0;
            for (int i = 0; i < idleCount; i++) Asset(idle[i], true);
            Asset(set.walkForward, true);
            foreach (var node in VisualNodes(animator.transform))
            {
                Token(signature, RelativePath(node, animator.transform));
                var meshFilter=node.GetComponent<MeshFilter>();
                var mesh=meshFilter==null ? null : meshFilter.sharedMesh;
                Token(signature,mesh==null ? "" : mesh.name);
                Token(identity,mesh==null ? "0" : mesh.GetInstanceID().ToString(CultureInfo.InvariantCulture));
                if(mesh!=null)
                {
                    Token(signature,mesh.vertexCount.ToString(CultureInfo.InvariantCulture));
                    Token(signature,mesh.subMeshCount.ToString(CultureInfo.InvariantCulture));
                    if(mesh.subMeshCount>64) throw new InvalidOperationException("NPC mesh submesh limit exceeded");
                    for(int part=0;part<mesh.subMeshCount;part++) Token(signature,mesh.GetIndexCount(part).ToString(CultureInfo.InvariantCulture));
                }
                var components = node.GetComponents<Renderer>();
                if (components.Length > MaxMaterials) throw new InvalidOperationException("renderer limit exceeded");
                Token(signature, components.Length.ToString(CultureInfo.InvariantCulture));
                renderers += components.Length;
                for (int i = 0; i < components.Length; i++)
                {
                    var shared = components[i].sharedMaterials;
                    int count = shared == null ? 0 : shared.Length;
                    if (count > MaxMaterials) throw new InvalidOperationException("material slot limit exceeded");
                    Token(signature, count.ToString(CultureInfo.InvariantCulture));
                    for (int j = 0; j < count; j++) Asset(shared[j], false);
                }
            }
            if (animations == 0 || renderers == 0 || materials == 0) return null;
            string canonical = signature.ToString();
            return new Visual { Template = animator.gameObject, Signature = canonical,
                AssetIdentity = identity.ToString(),
                Key = "npcvisual:v2:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))) };

            void Asset(UnityEngine.Object asset, bool animation)
            {
                string assetName = asset == null ? string.Empty : asset.name;
                if (asset != null && string.IsNullOrWhiteSpace(assetName))
                    throw new InvalidOperationException("unnamed animation/material asset");
                Token(signature, assetName);
                Token(identity, asset == null ? "0" : asset.GetInstanceID().ToString(CultureInfo.InvariantCulture));
                if (asset != null) { if (animation) animations++; else materials++; }
            }
        }

        private static IEnumerable<Transform> VisualNodes(Transform root)
        {
            var pending = new Stack<Transform>();
            pending.Push(root);
            int visited = 0;
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node == null) throw new InvalidOperationException("visual destroyed during discovery");
                if (++visited > MaxVisualNodes) throw new InvalidOperationException("visual hierarchy limit exceeded");
                yield return node;
                int children = node.childCount;
                if (children > MaxVisualNodes - visited - pending.Count)
                    throw new InvalidOperationException("visual hierarchy work limit exceeded");
                for (int i = children - 1; i >= 0; i--) pending.Push(node.GetChild(i));
            }
        }

        private static string RelativePath(Transform node, Transform root)
        {
            var segments = new Stack<string>();
            while (node != root)
            {
                if (node == null || segments.Count >= MaxVisualNodes)
                    throw new InvalidOperationException("visual parent chain invalid");
                segments.Push(node.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + ":" + Normalize(node.name));
                node = node.parent;
            }
            return segments.Count == 0 ? "/" : "/" + string.Join("/", segments);
        }

        private static string Normalize(string value)
        {
            string name = (value ?? string.Empty).Trim();
            while (name.EndsWith("(Clone)", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 7).TrimEnd();
            return name;
        }

        private static void Token(StringBuilder builder, string value)
        {
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
        }
    }
}
