using BepInEx.Logging;
using CloudpunkCoop.Game;
using CloudpunkCoop.Net;
using UnityEngine;

namespace CloudpunkCoop.UI
{
    /// <summary>Session presence and world labels derive from handshake and rendering acknowledgements.</summary>
    public static class CoopPresence
    {
        private static bool _hostGuestAvatar;
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Presence");

        public static void Tick()
        {
            if(IndependentCoop.Enabled)
            {
                if(!GameRefs.Resolved) return;
                var state=IndependentCoop.State;
                byte vehicle=state.VehicleOf(IndependentCoop.LocalPlayer);
                bool together=CoopSession.PeerOnline && vehicle!=0 && vehicle==state.VehicleOf(IndependentCoop.PeerPlayer);
                if(together)
                {
                    if(vehicle!=IndependentCoop.LocalPlayer && PeerCarProxy.Ready) SeatRig.AttachToProxy(PeerCarProxy.Target,Log);
                    SeatKind seat=state.DriverOf(vehicle)==IndependentCoop.PeerPlayer ? SeatKind.Driver : SeatKind.Passenger;
                    if(SeatRig.Occupant!=seat) SeatRig.Sit(seat,Log);
                }
                else if(SeatRig.Occupant!=SeatKind.None) SeatRig.Leave(Log);
                return;
            }
            SeatKind desired = CoopSession.Seats.Driver == 2 ? SeatKind.Driver : SeatKind.Passenger;
            if (CoopSession.Role == CoopRole.Guest && CarProxy.Ready && !GuestMobility.OnFoot && SeatRig.Occupant != desired)
                SeatRig.Sit(desired, Log);
            if (CoopSession.Role != CoopRole.Host || !GameRefs.Resolved) return;
            bool seated = CoopSession.GuestSeated && !GameRefs.Global.IsInMenu() &&
                !GameRefs.Global.isLoadingSaveGame;
            if (seated && (!_hostGuestAvatar || SeatRig.Occupant != desired))
                _hostGuestAvatar = SeatRig.Sit(desired, Log);
            else if (!seated && _hostGuestAvatar)
            {
                SeatRig.Leave(Log);
                _hostGuestAvatar = false;
            }
        }

        public static void Draw()
        {
            CampaignReplication.Draw();
            if (CoopSession.Role == CoopRole.Off) return;
            if(IndependentCoop.Enabled) { DrawIndependent(); return; }
            bool guest = CoopSession.Role == CoopRole.Guest;
            string identity = guest ? "Player 2 (GUEST)" : "Player 1 (HOST)";
            string connection = CoopSession.PeerOnline ? "CONNECTED - 2 players" :
                (guest ? "NOT CONNECTED" : "WAITING FOR PLAYER 2 - 1 player");
            string seats = GuestMobility.OnFoot ? "Player 2 - on foot" : CoopSession.Seats.BothSeated ? OccupancyText() :
                CoopSession.GuestSeated ? "Player 2 - passenger (parked / waiting for driver)" :
                (CoopSession.PeerOnline ? "Player 2: joining world" : "Remote player: offline");
            string sync = guest && CarProxy.Ready
                ? "Sync " + CoopSession.RemoteSnapshotRate.ToString("0.0") + " Hz | FX " + CarVisuals.Status
                : CoopSession.Status;
            GUI.Box(new Rect(12f, 12f, 450f, 130f),
                identity + "\n" + connection + "\n" + seats + "\n" + sync + "\n" + RequestText() + "\n" + GuestMobility.Status);

            if (!GameRefs.Resolved || GameRefs.Global == null || GameRefs.Global.IsInMenu()) return;
            Camera camera = GameRefs.Global.mainCamera;
            if (camera == null) return;
            if (RemoteFootAvatar.OnFoot && RemoteFootAvatar.Target != null)
                Label(camera, RemoteFootAvatar.Target.position + Vector3.up * 2.2f,
                    (guest ? "Player 1" : "Player 2") + " - on foot");
            if (guest && GuestMobility.LocalOnFoot && GameRefs.Player != null)
                Label(camera, GameRefs.Player.transform.position + Vector3.up * 2.2f, "Player 2 - on foot");
            if (guest && CarProxy.Ready)
            {
                Label(camera, CarProxy.Target.position + Vector3.up * 3f,
                    (GuestMobility.OnFoot ? "Shared car - E to board" : OccupancyText()) +
                    (CoopSession.Seats.Requester == 0 ? "" : "\n" + RequestText()));
                return;
            }

            bool onFoot = GameRefs.Player != null && GameRefs.Player.IsActive();
            if (onFoot)
                Label(camera, GameRefs.Player.transform.position + Vector3.up * 2.2f,
                    (guest ? "Player 2" : "Player 1") + " - on foot");
            if (GameRefs.PlayerCar != null)
            {
                string text = onFoot ? "Player 1 - parked car" : "Player 1 - driver";
                if (guest) text = "Player 2 - local car (offline)";
                if (CoopSession.Seats.BothSeated) text = OccupancyText();
                if (CoopSession.Seats.Requester != 0) text += "\n" + RequestText();
                Label(camera, GameRefs.PlayerCar.transform.position + Vector3.up * 3f, text);
            }
        }

        private static void DrawIndependent()
        {
            // Main-menu connection status is already presented by Host / Join.
            // Vehicle controls have no meaning before the native world is active.
            if(!GameRefs.Resolved || GameRefs.Global==null || GameRefs.Global.IsInMenu())return;
            byte self=IndependentCoop.LocalPlayer,peer=IndependentCoop.PeerPlayer;
            var state=IndependentCoop.State;
            string identity="Player "+self+(self==1 ? " (HOST)" : " (GUEST)");
            string seats=state.VehicleOf(self)==0 ? "On foot" : "Car "+state.VehicleOf(self)+" - "+(state.DriverOf(state.VehicleOf(self))==self ? "driver" : "passenger");
            string request=state.Requester==0 ? "F8: request seat exchange" : "Player "+state.Requester+" - Хочу смениться (F8, "+state.SecondsLeft.ToString("0")+"s)";
            GUI.Box(new Rect(12,12,450,145),identity+"\n"+(CoopSession.PeerOnline ? "CONNECTED - 2 players" : "Remote player: offline")+
                "\n"+seats+"\n"+IndependentCoop.Status+"\n"+RegionReplication.Status+"\n"+request+"\nE: board / park | Shift+E: partner car");
            if(!GameRefs.Resolved || GameRefs.Global.IsInMenu() || GameRefs.Global.mainCamera==null) return;
            var camera=GameRefs.Global.mainCamera;
            if(IndependentCoop.OwnScene==RegionReplication.LocalScene)
                Label(camera,IndependentCoop.OwnPosition()+Vector3.up*3f,CarLabel(self));
            if(PeerCarProxy.Ready) Label(camera,PeerCarProxy.Target.position+Vector3.up*3f,CarLabel(peer));
            if(GameRefs.Player!=null && GameRefs.Player.IsActive()) Label(camera,GameRefs.Player.transform.position+Vector3.up*2.2f,"Player "+self+" - on foot");
            if(RegionReplication.SameRegion && RemoteFootAvatar.OnFoot && RemoteFootAvatar.Target!=null &&
                RemoteFootAvatar.Target.gameObject.activeInHierarchy)
                Label(camera,RemoteFootAvatar.Target.position+Vector3.up*2.2f,"Player "+peer+" - on foot");

            string CarLabel(byte car)
            {
                string label="Car "+car;
                byte driver=state.DriverOf(car);
                if(driver==0) return label+" - parked";
                label+="\nPlayer "+driver+" - driver";
                byte passenger=driver==1 ? (byte)2 : (byte)1;
                if(state.VehicleOf(passenger)==car) label+="\nPlayer "+passenger+" - passenger";
                return label;
            }
        }

        private static void Label(Camera camera, Vector3 position, string text)
        {
            Vector3 screen = camera.WorldToScreenPoint(position);
            if (screen.z <= 0f) return;
            float height = 24f + (text.Split('\n').Length - 1) * 18f;
            GUI.Box(new Rect(screen.x - 125f, Screen.height - screen.y - height,
                250f, height), text);
        }

        private static string OccupancyText() => CoopSession.Seats.Driver == 1
            ? "Player 1 - driver\nPlayer 2 - passenger" : "Player 1 - passenger\nPlayer 2 - driver";
        private static string RequestText() => CoopSession.Seats.Requester == 0
            ? "F8: request seat exchange" : "Player " + CoopSession.Seats.Requester +
                " - Хочу смениться (F8, " + CoopSession.Seats.SecondsLeft.ToString("0") + "s)";
    }
}
