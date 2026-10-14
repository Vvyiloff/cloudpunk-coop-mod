using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    /// <summary>Each process owns its loaded region; campaign authority is session-wide.</summary>
    public static class RegionReplication
    {
        private static readonly RegionPresenceTracker Peer = new();
        private static uint _sequence;
        private static float _nextSend;
        private static int _sentScene = -2;
        private static bool _sentReady;
        public static int LocalScene => SceneManager.GetActiveScene().buildIndex;
        public static int PeerScene => Peer.PeerScene;
        public static bool LocalReady => GameRefs.Resolved && Global.Instance != null &&
            !Global.Instance.IsInMenu() && !Global.Instance.isLoadingSaveGame && !SceneLoader.IsLoading() &&
            IndependentCoop.Playing && (GameRefs.PlayerCar == null || !GameRefs.PlayerCar.IsInLift()) &&
            (SceneLoader.Instance == null || !SceneLoader.Instance.IsActive()) && LocalScene >= 0;
        public static bool PeerFresh => CoopSession.PeerOnline && Peer.Fresh(Time.realtimeSinceStartup);
        public static bool PeerReady => CoopSession.PeerOnline && Peer.Ready(Time.realtimeSinceStartup);
        public static bool SameRegion => CoopSession.PeerOnline && Peer.SameRegion(LocalScene, LocalReady, Time.realtimeSinceStartup);
        public static string Status => !PeerFresh ? "Partner region: loading / unavailable" :
            "Region " + LocalScene + " | partner " + PeerScene + (SameRegion ? " - together" : " - separate regions");
        public static void Reset()
        {
            Peer.Reset(); _sequence = 0; _nextSend = 0; _sentScene = -2; _sentReady = false;
        }
        public static void Receive(RegionPresenceMessage message) => Peer.Receive(message, Time.realtimeSinceStartup);
        public static void Tick(float now)
        {
            if (!CoopSession.PeerOnline) { Peer.Reset(); _nextSend = 0; _sentScene = -2; return; }
            int scene = LocalScene; bool ready = LocalReady;
            if (now < _nextSend && scene == _sentScene && ready == _sentReady) return;
            _nextSend = now + 0.5f;
            if (CoopSession.SendMessage(new RegionPresenceMessage(_sequence++, scene, ready)))
            { _sentScene = scene; _sentReady = ready; }
        }
    }
}
