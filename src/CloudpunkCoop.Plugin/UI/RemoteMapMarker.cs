using System;
using BepInEx.Logging;
using CloudpunkCoop.Game;
using CloudpunkCoop.Net;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.UI
{
    /// <summary>Uses the game's own radar registry for the connected remote occupant.</summary>
    public static class RemoteMapMarker
    {
        private static GameObject _root;
        private static MinimapObject _marker;
        private static QuestText _data;
        private static bool _failed;
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Map");
        public static bool Active => _root != null;

        public static void Tick()
        {
            if(IndependentCoop.Enabled && (!RegionReplication.SameRegion || !RegionReplication.LocalReady))
            {Clear();return;}
            if ((IndependentCoop.Enabled ? !CoopSession.PeerOnline || !PeerCarProxy.Ready && !RemoteFootAvatar.OnFoot : !CoopSession.GuestSeated && !RemoteFootAvatar.OnFoot) || !GameRefs.Resolved || GameRefs.Global.IsInMenu() ||
                GameRefs.Global.isLoadingSaveGame || Minimap.Instance == null)
            { Clear(); return; }
            if (_failed) return;
            try
            {
                Transform target = IndependentCoop.Enabled ? PeerCarProxy.Target : CoopSession.Role == CoopRole.Guest ? CarProxy.Target : GameRefs.PlayerCar.transform;
                if(IndependentCoop.Enabled && IndependentCoop.State.VehicleOf(IndependentCoop.PeerPlayer)==IndependentCoop.LocalPlayer)
                    target=GameRefs.PlayerCar.transform;
                if (RemoteFootAvatar.OnFoot) target = RemoteFootAvatar.Target;
                if (target == null || !target.gameObject.activeInHierarchy) {Clear();return;}
                if (_root == null)
                {
                    string name = CoopSession.Role == CoopRole.Guest ? "Player 1" : "Player 2";
                    _root = new GameObject("CloudpunkCoop_Map_" + name);
                    _root.SetActive(false);
                    _root.transform.position = target.position;
                    _marker = _root.AddComponent(Il2CppType.Of<MinimapObject>()).Cast<MinimapObject>();
                    _marker.radarMarkerImage = Minimap.Instance.defaulMarkerImage;
                    _marker.priority = MinimapObject.Priority.High;
                    _marker.restriction = MinimapObject.Restriction.None;
                    _marker.cullingFactor = _marker.cullingFactorInCar = 100f;
                    _marker.showOnlyOnSameLevel = false;
                    _marker.showDistance = false; // Native default marker has no distance text.
                    _marker.showAboveBelowIcon = true;
                    _data = new QuestText("CloudpunkCoop." + name, name, name, true, target.position,
                        SceneManager.GetActiveScene().buildIndex);
                    _data.enabled = true;
                    _marker.Setup(_data);
                    // Setup has the explicit presentation DTO. Start must not look up a
                    // fabricated quest name in the game's authored campaign dictionary.
                    _marker.questName = "";
                    _root.SetActive(true);
                    Log.LogInfo("registered native radar marker for " + name);
                }
                _root.transform.position = target.position;
                _data.markerPos = target.position;
                if (_marker.iconImage != null) _marker.iconImage.color = new Color(0.2f, 1f, 1f, 1f);
            }
            catch (Exception ex)
            { Clear(); _failed = true; Log.LogWarning("remote map marker failed: " + ex); }
        }

        public static void Clear()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null; _marker = null; _data = null;
        }
    }
}
