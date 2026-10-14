using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>
    /// NPC-only mesh presentation. Never instantiate native actors or their scripts.
    /// Wire signature: normalized Character root name, projected mesh hierarchy paths,
    /// shared mesh names/counts/bounds/submesh topology and index counts, then shared
    /// material and shader asset names in slot order. Same-process asset IDs disambiguate
    /// collisions. This is an asset descriptor, not a cross-install geometry content hash.
    /// Mutable transforms, visibility and instance IDs never enter the wire signature.
    /// </summary>
    internal static class StaticNpcVisual
    {
        internal const string KeyPrefix = "npcstatic:v1:";
        private const int MaxNodes = 256, MaxMaterials = 64, MaxSubMeshes = 64;

        internal sealed class Description
        {
            public GameObject Template;
            public string Signature, AssetIdentity, Key;
            internal List<Node> Nodes;
        }

        internal sealed class Node
        {
            public Transform Source;
            public int Parent;
            public bool Included;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
        }

        internal static Description Describe(Character character)
        {
            if (character == null || character.anim != null) return null;
            return Describe(character.gameObject, character.uiCanvas);
        }

        private static Description Describe(GameObject root, Transform ui)
        {
            if (root == null) return null;
            var nodes = new List<Node>();
            var pending = new Stack<(Transform Transform, int Parent)>();
            pending.Push((root.transform, -1));
            int visited = 0, meshCount = 0;
            while (pending.Count != 0)
            {
                var item = pending.Pop();
                var t = item.Transform;
                if (t == null) throw new InvalidOperationException("Static NPC visual destroyed during discovery");
                if (++visited > MaxNodes) throw new InvalidOperationException("Static NPC hierarchy limit exceeded");
                // Character UI is presentation outside the mesh template; do not traverse it.
                if ((ui != null && t == ui) || t.GetComponent<Canvas>() != null) continue;
                if (t.GetComponents<MeshAnimator>().Length != 0) return null;
                var renderers = t.GetComponents<Renderer>();
                var filters = t.GetComponents<MeshFilter>();
                if (renderers.Length > 1 || filters.Length > 1) return null;
                MeshRenderer renderer = renderers.Length == 0 ? null : renderers[0].TryCast<MeshRenderer>();
                if (renderers.Length != 0 && renderer == null) return null;
                MeshFilter filter = filters.Length == 0 ? null : filters[0];
                if ((renderer == null) != (filter == null)) return null;
                if (renderer != null && (filter.sharedMesh == null || renderer.HasPropertyBlock() ||
                    renderer.additionalVertexStreams != null)) return null;
                int index = nodes.Count;
                nodes.Add(new Node { Source = t, Parent = item.Parent, Filter = filter,
                    Renderer = renderer, Included = renderer != null });
                if (renderer != null) meshCount++;
                int children = t.childCount;
                if (children > MaxNodes - visited - pending.Count)
                    throw new InvalidOperationException("Static NPC hierarchy work limit exceeded");
                for (int i = children - 1; i >= 0; i--) pending.Push((t.GetChild(i), index));
            }
            if (meshCount == 0) return null;
            // Project onto mesh-bearing branches. Runtime UI/nonvisual siblings cannot change keys.
            for (int i = nodes.Count - 1; i >= 0; i--)
                if (nodes[i].Included && nodes[i].Parent >= 0) nodes[nodes[i].Parent].Included = true;
            var signature = new StringBuilder();
            var identity = new StringBuilder();
            var paths = new string[nodes.Count];
            var siblings = new int[nodes.Count];
            Token(signature, "npcstatic-v1");
            Token(signature, Normalize(root.name));
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (!node.Included) continue;
                paths[i] = node.Parent < 0 ? "/" : paths[node.Parent] + "/" +
                    (siblings[node.Parent]++).ToString(CultureInfo.InvariantCulture) + ":" + Normalize(node.Source.name);
                Token(signature, paths[i]);
                Token(signature, node.Renderer == null ? "0" : "1");
                if (node.Renderer == null) continue;
                Mesh mesh = node.Filter.sharedMesh;
                Asset(mesh);
                Token(signature, mesh.vertexCount.ToString(CultureInfo.InvariantCulture));
                int subMeshes = mesh.subMeshCount;
                if (subMeshes < 1 || subMeshes > MaxSubMeshes)
                    throw new InvalidOperationException("Static NPC submesh limit exceeded");
                Token(signature, subMeshes.ToString(CultureInfo.InvariantCulture));
                Bounds bounds = mesh.bounds;
                Vector(signature, bounds.center); Vector(signature, bounds.size);
                for (int j = 0; j < subMeshes; j++)
                {
                    Token(signature, ((int)mesh.GetTopology(j)).ToString(CultureInfo.InvariantCulture));
                    Token(signature, mesh.GetIndexCount(j).ToString(CultureInfo.InvariantCulture));
                }
                var materials = node.Renderer.sharedMaterials;
                int count = materials == null ? 0 : materials.Length;
                if (count == 0 || count > MaxMaterials) return null;
                Token(signature, count.ToString(CultureInfo.InvariantCulture));
                for (int j = 0; j < count; j++)
                {
                    Material material = materials[j];
                    if (material == null || material.shader == null) return null;
                    Asset(material); Asset(material.shader);
                }
            }
            string canonical = signature.ToString();
            return new Description { Template = root, Nodes = nodes, Signature = canonical,
                AssetIdentity = identity.ToString(),
                Key = KeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))) };

            void Asset(UnityEngine.Object asset)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.name))
                    throw new InvalidOperationException("Static NPC unnamed/missing visual asset");
                Token(signature, asset.name);
                // Same-process clone/catalog validation; instance IDs never enter the wire key.
                Token(identity, asset.GetInstanceID().ToString(CultureInfo.InvariantCulture));
            }
        }

        internal static void Validate(GameObject template, string expectedKey)
        {
            var character = template == null ? null : template.GetComponent<Character>();
            var description = Describe(character);
            if (description == null || description.Key != expectedKey)
                throw new InvalidOperationException("Static NPC template no longer matches model: " + expectedKey);
        }

        internal static void Copy(GameObject template, Transform destination, string expectedKey)
        {
            var character = template == null ? null : template.GetComponent<Character>();
            var description = Describe(character);
            if (description == null || description.Key != expectedKey)
                throw new InvalidOperationException("Static NPC template no longer matches model: " + expectedKey);
            var copies = new Transform[description.Nodes.Count];
            for (int i = 0; i < description.Nodes.Count; i++)
            {
                Node node = description.Nodes[i];
                if (!node.Included) continue;
                var go = new GameObject("NPCMesh_" + Normalize(node.Source.name));
                go.SetActive(false);
                Transform copy = go.transform;
                copy.SetParent(node.Parent < 0 ? destination : copies[node.Parent], false);
                // Host packet supplies Character root world position/yaw/lossy scale.
                // Preserve only the template's child poses; procedural child motion is not sent.
                copy.localPosition = node.Parent < 0 ? Vector3.zero : node.Source.localPosition;
                copy.localRotation = node.Parent < 0 ? Quaternion.identity : node.Source.localRotation;
                copy.localScale = node.Parent < 0 ? Vector3.one : node.Source.localScale;
                go.layer = node.Source.gameObject.layer;
                copies[i] = copy;
                if (node.Renderer != null)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = node.Filter.sharedMesh;
                    MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = node.Renderer.sharedMaterials;
                    // Loaded fallback actors may be hidden by local NPC suppression already.
                    renderer.enabled = true;
                    renderer.shadowCastingMode = node.Renderer.shadowCastingMode;
                    renderer.receiveShadows = node.Renderer.receiveShadows;
                    renderer.lightProbeUsage = node.Renderer.lightProbeUsage;
                    renderer.reflectionProbeUsage = node.Renderer.reflectionProbeUsage;
                    renderer.sortingLayerID = node.Renderer.sortingLayerID;
                    renderer.sortingOrder = node.Renderer.sortingOrder;
                }
                go.SetActive(node.Parent < 0 || node.Source.gameObject.activeSelf);
            }
        }

        private static void Vector(StringBuilder builder, Vector3 value)
        {
            Number(value.x); Number(value.y); Number(value.z);
            void Number(float number)
            {
                if (float.IsNaN(number) || float.IsInfinity(number))
                    throw new InvalidOperationException("Static NPC non-finite mesh bounds");
                Token(builder, (number == 0f ? 0f : number).ToString("R", CultureInfo.InvariantCulture));
            }
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
