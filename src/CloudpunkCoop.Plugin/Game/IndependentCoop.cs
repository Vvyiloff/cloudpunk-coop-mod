using System;
using System.Reflection;
using BepInEx.Logging;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    /// <summary>Two native owner-simulated cars, with all boarding decisions serialized by host.</summary>
    public static class IndependentCoop
    {
        private static readonly ManualLogSource Log=BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Vehicles");
        private static VehicleOccupancy _authority=new VehicleOccupancy();
        private static OwnedCarMessage _peer;
        private static readonly ParkedVehicleRegion OwnRegion=new();
        public static int OwnScene=>OwnRegion.Last?.Scene ?? SceneManager.GetActiveScene().buildIndex;
        private static bool OwnCarAway=>OwnRegion.IsAway(SceneManager.GetActiveScene().buildIndex,
            GameRefs.Player!=null && GameRefs.Player.IsActive());
        public static void RememberOwnCar(OwnedCarMessage message)=>OwnRegion.Observe(message);
        public static bool TryCaptureParkedAway(uint sequence,out OwnedCarMessage message)=>
            OwnRegion.TryCaptureAway(SceneManager.GetActiveScene().buildIndex,
                GameRefs.Player!=null && GameRefs.Player.IsActive(),sequence,out message);
        private static float _peerAt, _nextPublish, _pendingAt, _nativeTransitionUntil;
        private static bool _installed, _bypass, _pending, _remoteSeat, _initialised, _remoteNativeActive, _registered;
        private static byte _requested;
        private static uint _requestRevision;
        private static UsableCar _usable;
        private static GameObject _spawn;
        private static Vector3 _footPosition;
        private static Quaternion _footRotation;
        private static int _footScene;
        private static bool _savedFoot;
        private static bool _waitingPark,_nativePark;
        private static float _parkAt;
        private static uint _parkRevision;
        private static float _parkingGraceUntil;
        public static bool Enabled { get; set; }=true;
        public static bool Playing=>GameRefs.Resolved && GameRefs.Global!=null && !GameRefs.Global.IsInMenu() &&
            !GameRefs.Global.isLoadingSaveGame && GameRefs.Global.mainCamera!=null && GameRefs.Global.mainCamera.enabled &&
            (GameRefs.Global.mainCamera.farClipPlane>50f || GameRefs.PlayerCar!=null && GameRefs.PlayerCar.isInVehicleCockpit) && GameRefs.Global.cameraBrain!=null &&
            GameRefs.Global.cameraBrain.ActiveVirtualCamera!=null;
        public static byte LocalPlayer=>CoopSession.Role==CoopRole.Host ? (byte)1 : (byte)2;
        public static byte PeerPlayer=>LocalPlayer==1 ? (byte)2 : (byte)1;
        public static OccupancyStateMessage State { get; private set; }=new OccupancyStateMessage(0,0,0,0,0,0,0);
        public static byte LocalVehicle=>State.VehicleOf(LocalPlayer);
        public static bool LocalOnFoot=>LocalVehicle==0;
        public static bool RemoteSeat=>_remoteSeat;
        public static bool BoardingTransition=>Time.realtimeSinceStartup<_nativeTransitionUntil;
        public static bool ParkingInProgress=>_nativePark;
        public static bool PeerCarFresh=>_peer!=null && Time.realtimeSinceStartup-_peerAt<2f;
        public static bool PeerParked=>PeerCarFresh && _peer.Parked;
        public static Vector3 PeerPosition=>_peer==null ? Vector3.positiveInfinity : Position(_peer.Pose);
        public static int PeerScene=>_peer==null ? -1 : _peer.Scene;
        public static void SetParkingStatus(string text)=>Status=text;
        public static bool BeginGrantedParking()
        {
            var car=GameRefs.PlayerCar;
            if(car==null || car.parkingVolume==null || car.IsInRoutine()) return false;
            _nativePark=true;
            _parkingGraceUntil=Time.realtimeSinceStartup+1f;
            try {car.currentRoutine=car.StartCoroutine(car.ParkRoutine(car.parkingVolume));return car.currentRoutine!=null;}
            catch(Exception ex){_nativePark=false;Log.LogWarning("Granted parking failed: "+ex);return false;}
        }
        public static string Status { get; private set; }="Joining vehicle authority";

        public static void Install()
        {
            if(_installed) return;
            new Harmony("dev.andre.cloudpunk-coop.boarding").Patch(AccessTools.Method(typeof(UsableCar),"OnInteract"),
                prefix:new HarmonyMethod(typeof(IndependentCoop).GetMethod(nameof(BoardPrefix),BindingFlags.NonPublic|BindingFlags.Static)));
            _installed=true;
            new Harmony("dev.andre.cloudpunk-coop.remote-park-actor").Patch(
                AccessTools.Method(typeof(Player),"Activate",new[]{typeof(CharacterSpawnPoint),typeof(bool),typeof(bool),typeof(float)}),
                prefix:new HarmonyMethod(typeof(IndependentCoop).GetMethod(nameof(ActivationPrefix),BindingFlags.NonPublic|BindingFlags.Static)));
        }
        private static bool ActivationPrefix()=>!Enabled || !_nativePark || LocalVehicle==LocalPlayer;

        private static bool BoardPrefix(UsableCar __instance)
        {
            if(!Enabled || !CoopSession.PeerOnline || _bypass) return true;
            if(_nativePark || Time.realtimeSinceStartup<_parkingGraceUntil || GameRefs.PlayerCar!=null && GameRefs.PlayerCar.IsInRoutine()) return false;
            if(_remoteSeat) return false;
            if(!GameRefs.Resolved || GameRefs.Global.IsInMenu() || GameRefs.Global.isLoadingSaveGame) return false;
            if(ParkingAuthority.JoinPlacementPending) {Status="Preparing a free parking bay";return false;}
            _usable=__instance;
            Request(LocalPlayer);
            return false; // Native entry runs only after the host grants this vehicle.
        }

        public static void RequestToggle(bool preferPeer=false)
        {
            if(ParkingAuthority.JoinPlacementPending){Status="Preparing a free parking bay";return;}
            if(CampaignReplication.ConsumesInteract()) return;
            if(!Enabled || !CoopSession.PeerOnline || !GameRefs.Resolved || _pending) return;
            if(_remoteSeat)
            {
                if(PeerParked) Request(0);
                else if(State.DriverOf(LocalVehicle)==LocalPlayer && !_waitingPark)
                {
                    _waitingPark=true; _parkAt=Time.realtimeSinceStartup; _parkRevision=State.Revision;
                    CoopSession.SendMessage(new VehicleParkMessage(State.Revision,LocalVehicle,0));
                    Status="Waiting for the car to park";
                }
                else if(!_waitingPark) Status="Driver must park before leaving";
                return;
            }
            if(GameRefs.Player==null) return;
            if(!GameRefs.Player.IsActive())
            {
                if(LocalVehicle==LocalPlayer && State.DriverOf(LocalPlayer)==LocalPlayer && !BoardingTransition)
                    ParkingAuthority.Request(false);
                return;
            }
            if(_nativePark || Time.realtimeSinceStartup<_parkingGraceUntil) return;
            Vector3 p=GameRefs.Player.transform.position;
            if(preferPeer && PeerCarProxy.Ready && Vector3.Distance(p,PeerCarProxy.Target.position)<8f)
                Request(PeerPlayer);
            else if(!preferPeer && !OwnCarAway && Vector3.Distance(p,OwnPosition())<8f) Request(LocalPlayer);
        }

        private static void Request(byte vehicle)
        {
            if(_pending || State.VehicleOf(LocalPlayer)==vehicle && _initialised) return;
            _pending=true; _requested=vehicle; _pendingAt=Time.realtimeSinceStartup; _requestRevision=State.Revision;
            Status="Waiting for boarding authority";
            if(CoopSession.Role==CoopRole.Host) HandleRequest(new OccupancyRequestMessage(State.Revision,vehicle),1);
            else CoopSession.SendMessage(new OccupancyRequestMessage(State.Revision,vehicle));
        }

        public static void HandleRequest(OccupancyRequestMessage request,byte player)
        {
            if(CoopSession.Role!=CoopRole.Host || !CoopSession.PeerOnline || !GameRefs.Resolved) return;
            bool valid=RegionReplication.LocalReady && (player==1 || RegionReplication.PeerReady);
            if(request.Vehicle!=0)
            {
                if(request.Vehicle==2 && ParkingAuthority.JoinPlacementPending)return;
                int scene=SceneManager.GetActiveScene().buildIndex;
                Vector3 target=request.Vehicle==1 ? OwnPosition() : _peer==null ? Vector3.positiveInfinity : Position(_peer.Pose);
                Vector3 actor=player==1 ? GameRefs.Player.transform.position : RemoteFootAvatar.LatestPosition;
                // An owner already inside its native car may register that car on join.
                bool joining=player==request.Vehicle && (player==1 ? _authority.Player1Vehicle==0 && !GameRefs.Player.IsActive() :
                    _authority.Player2Vehicle==0 && PeerCarFresh && !_peer.Parked);
                valid &= joining || (player==1 ? GameRefs.Player.IsActive() : RemoteFootAvatar.Fresh && RemoteFootAvatar.OnFoot && RemoteFootAvatar.Scene==scene) &&
                    (request.Vehicle==1 || PeerCarFresh && _peer.Scene==scene) && Vector3.Distance(actor,target)<=8f;
            }
            else
            {
                byte current=player==1 ? _authority.Player1Vehicle : _authority.Player2Vehicle;
                valid &= current==0 || (current==1 ? OwnParked : PeerParked);
            }
            if(valid) _authority.SetPlayerVehicle(player,request.Vehicle,request.Revision);
            Publish(Time.realtimeSinceStartup);
        }

        private static void Publish(float now)
        {
            var before=State;
            State=new OccupancyStateMessage(_authority.Revision,_authority.Player1Vehicle,_authority.Player2Vehicle,
                _authority.Driver1,_authority.Driver2,_authority.Requester,
                _authority.Requester==0 ? 0 : Mathf.Clamp((float)(_authority.ExpiresAt-now),0,10));
            CoopSession.SendMessage(State); _nextPublish=now+1f;
            ResolvePending();
            RefreshSeatStatus(before);
        }
        public static void ReceiveState(OccupancyStateMessage state)
        {
            if(CoopSession.Role!=CoopRole.Guest || !CoopSession.PeerOnline || unchecked((int)(state.Revision-State.Revision))<0) return;
            var before=State;
            State=state; ResolvePending();RefreshSeatStatus(before);
        }
        private static void RefreshSeatStatus(OccupancyStateMessage before)
        {
            if(_pending || !_registered || LocalVehicle==0)return;
            if(before.VehicleOf(LocalPlayer)==LocalVehicle && before.DriverOf(LocalVehicle)==State.DriverOf(LocalVehicle))return;
            Status=(State.DriverOf(LocalVehicle)==LocalPlayer ? "Driver of car " : "Passenger of car ")+LocalVehicle;
        }
        private static void ResolvePending()
        {
            if(!_pending) return;
            bool accepted=State.VehicleOf(LocalPlayer)==_requested;
            if(!accepted && unchecked((int)(State.Revision-_requestRevision))<=0) return;
            _pending=false; _initialised=true;
            Status=accepted ? (_requested==0 ? "On foot" : State.DriverOf(_requested)==LocalPlayer ? "Driver of car "+_requested : "Passenger of car "+_requested) : "Boarding state changed; try again";
            if(!accepted) return;
            _registered=true;
            try { ApplyLocalSeat(); }
            catch(Exception ex) { Status="Native boarding failed"; Log.LogWarning(ex); }
        }
        private static void ApplyLocalSeat()
        {
            if(LocalVehicle==LocalPlayer)
            {
                if(_remoteSeat) { RemoteCameraRig.Stop(); SeatRig.DetachFromProxy(Log); _remoteSeat=false; }
                if(GameRefs.Player.IsActive())
                {
                    _nativeTransitionUntil=Time.realtimeSinceStartup+2f;
                    if(GameRefs.PlayerCar.gameObject.activeInHierarchy && !OwnParked)
                    {
                        Player.Deactivate(0f); GameRefs.Player.gameObject.SetActive(false);
                        GameRefs.Global.OnChangeVehicleState(0f);
                        return;
                    }
                    // The parked usable is a separate native object, not necessarily
                    // a child of the driving PlayerCar controller.
                    var parkedUsable=GameRefs.PlayerCar.parkObject==null ? null :
                        GameRefs.PlayerCar.parkObject.GetComponentInChildren<UsableCar>(true);
                    if(parkedUsable!=null) _usable=parkedUsable;
                    if(_usable==null) _usable=GameRefs.PlayerCar.GetComponentInChildren<UsableCar>(true);
                    if(_usable==null) throw new InvalidOperationException("Native car interaction is unavailable");
                    _bypass=true; try { _usable.OnInteract(); } finally { _bypass=false; }
                }
            }
            else if(LocalVehicle!=0)
            {
                if(!PeerCarProxy.Ready) throw new InvalidOperationException("Remote car is not rendered yet");
                if(!_remoteSeat)
                {
                    _footPosition=GameRefs.Player.transform.position; _footRotation=GameRefs.Player.transform.rotation;
                    _footScene=SceneManager.GetActiveScene().buildIndex; _savedFoot=true;
                }
                Player.Deactivate(0f); GameRefs.Player.gameObject.SetActive(false);
                RemoteCameraRig.Begin(PeerCarProxy.Target); _remoteSeat=true;
            }
            else if(_remoteSeat)
            {
                ParkVolume parking=null;
                foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ParkVolume>())
                    if(candidate.characterSpawnPoint!=null && PeerCarProxy.Ready &&
                        Vector3.Distance(candidate.transform.position,PeerCarProxy.Target.position)<30f)
                    { parking=candidate; break; }
                if(parking==null) throw new InvalidOperationException("Park before leaving the shared car");
                var point=parking.characterSpawnPoint.transform;
                ActivateFoot(point.position,point.rotation);
                _remoteSeat=false; _savedFoot=false;
            }
        }

        private static void ActivateFoot(Vector3 position,Quaternion rotation)
        {
            if(_spawn!=null) UnityEngine.Object.Destroy(_spawn);
            _spawn=new GameObject("CloudpunkCoop_RemoteExit"); _spawn.SetActive(false);
            _spawn.transform.SetPositionAndRotation(position,rotation);
            var spawn=_spawn.AddComponent(Il2CppType.Of<CharacterSpawnPoint>()).Cast<CharacterSpawnPoint>();
            spawn.registerAsLevelEntry=false; spawn.dependingTriggerName=""; spawn.action=new UnityEvent();
            _spawn.SetActive(true); RemoteCameraRig.Stop(); SeatRig.DetachFromProxy(Log);
            GameRefs.Player.Activate(spawn,false,false,0f);
            if(GameRefs.Global.gdoc!=null) GameRefs.Global.gdoc.referenceObject=GameRefs.Player.transform;
        }

        private static void RestoreRemoteSeat()
        {
            if(!_remoteSeat) return;
            try
            {
                RemoteCameraRig.Stop(); SeatRig.DetachFromProxy(Log);
                if(_savedFoot && GameRefs.Resolved && GameRefs.Player!=null && !GameRefs.Global.IsInMenu() &&
                    !GameRefs.Global.isLoadingSaveGame && _footScene==SceneManager.GetActiveScene().buildIndex)
                    ActivateFoot(_footPosition,_footRotation);
            }
            catch(Exception ex) { Log.LogWarning("Remote seat restore failed: "+ex); }
            finally { _remoteSeat=false; _savedFoot=false; }
        }

        public static bool ReceiveCar(OwnedCarMessage message,float now)
        {
            if(!Enabled || !CoopSession.PeerOnline || message.Vehicle!=PeerPlayer) return false;
            if(!PeerCarProxy.Receive(message.Pose,message.Scene,message.Parked,now)) return false;
            _peer=message; _peerAt=now;
            return true;
        }
        public static void PressSwap(uint revision,byte player)
        {
            if(CoopSession.Role==CoopRole.Host && _authority.PressSwap(player,revision,Time.realtimeSinceStartup))
                Publish(Time.realtimeSinceStartup);
        }

        public static void HandleParking(VehicleParkMessage message)
        {
            if(!CoopSession.PeerOnline || message.Revision!=State.Revision) return;
            if(message.Kind!=0)
            {
                if(!_waitingPark || message.Vehicle!=LocalVehicle || message.Revision!=_parkRevision) return;
                if(message.Kind==2) { _waitingPark=false; Status="Parking unavailable or occupied"; }
                return;
            }
            if(message.Vehicle!=LocalPlayer || State.DriverOf(LocalPlayer)!=PeerPlayer || !Playing) return;
            ParkingAuthority.Request(true);
        }

        public static void Tick(float now)
        {
            if(!Enabled || !GameRefs.Resolved) return;
            if(!CoopSession.PeerOnline)
            {
                bool wasRemoteSeat=_remoteSeat;
                RestoreRemoteSeat();
                if(CoopSession.Role==CoopRole.Host)
                {
                    _authority.DisconnectGuest();
                    if(wasRemoteSeat) _authority.SetPlayerVehicle(1,0,_authority.Revision);
                }
                _initialised=_pending=_registered=false;
                _peer=null;
                _waitingPark=_nativePark=false;
                if(CoopSession.Role==CoopRole.Host)
                    State=new OccupancyStateMessage(_authority.Revision,_authority.Player1Vehicle,0,_authority.Driver1,_authority.Driver2,0,0);
                return;
            }
            if(!Playing) return;
            ParkingAuthority.PrepareJoinedParkedCar();
            if(_nativePark && GameRefs.PlayerCar!=null && !GameRefs.PlayerCar.IsInRoutine())
            {_nativePark=false;_parkingGraceUntil=now+0.5f;}
            if(_waitingPark)
            {
                if(PeerParked) { _waitingPark=false; Request(0); }
                else if(now-_parkAt>10f) { _waitingPark=false; Status="Parking timed out; try again"; }
            }
            if(CoopSession.Role==CoopRole.Host)
            { if(_authority.Expire(now) || now>=_nextPublish) Publish(now); }
            if(_pending && now-_pendingAt>3f) { _pending=false; Status="Boarding timed out; try again"; }
            bool nativeFoot=GameRefs.Player.IsActive();
            bool remoteDriver=State.DriverOf(LocalPlayer)==PeerPlayer;
            if(remoteDriver && !_remoteNativeActive)
            {
                var car=GameRefs.PlayerCar;
                if(car.parkObject!=null && car.parkObject.activeInHierarchy)
                    car.Activate(false,car.parkObject.transform);
                _remoteNativeActive=true;
            }
            else if(!remoteDriver) _remoteNativeActive=false;
            if(!_pending && !_remoteSeat && now>=_nativeTransitionUntil)
            {
                if(nativeFoot && LocalVehicle==LocalPlayer) Request(0);
                else if(!_registered)
                {
                    if(nativeFoot || LocalVehicle==LocalPlayer)
                    {_registered=true;if(nativeFoot)Status="On foot";}
                    else Request(LocalPlayer);
                }
            }
            if(_remoteSeat && PeerCarProxy.Ready)
            {
                GameRefs.Player.transform.position=PeerCarProxy.Target.position;
                if(GameRefs.Global.gdoc!=null) GameRefs.Global.gdoc.referenceObject=PeerCarProxy.Target;
            }
        }

        public static Vector3 OwnPosition()
        {
            if(OwnCarAway) return Position(OwnRegion.Last.Pose);
            var car=GameRefs.PlayerCar;
            return car.parkObject!=null && car.parkObject.activeInHierarchy ? car.parkObject.transform.position : car.transform.position;
        }
        public static bool OwnParked=>OwnCarAway || GameRefs.PlayerCar!=null && GameRefs.PlayerCar.parkObject!=null && GameRefs.PlayerCar.parkObject.activeInHierarchy;
        private static Vector3 Position(CarStateMessage p)=>new Vector3(p.PositionX,p.PositionY,p.PositionZ);
        public static void Reset()
        {
            RestoreRemoteSeat();
            _authority=new VehicleOccupancy(); State=new OccupancyStateMessage(0,0,0,0,0,0,0);
            _peer=null; _pending=_initialised=_remoteSeat=_remoteNativeActive=_registered=false; _usable=null; _nextPublish=0;
            OwnRegion.Reset();
            _waitingPark=_nativePark=false;
            if(_spawn!=null) UnityEngine.Object.Destroy(_spawn); _spawn=null;
            PeerCarProxy.Clear(); Status="Joining vehicle authority";
        }
    }
}
