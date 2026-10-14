using System;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    public static class ParkingAuthority
    {
        private static readonly ParkingReservations Reservations=new();
        private static ParkVolume _pending;
        private static uint _sequence,_request,_revision;
        private static float _requestedAt;
        private static bool _remote;
        private sealed class Grant
        {
            public ParkingSlotMessage Message;
            public string Slot;
        }
        private static readonly System.Collections.Generic.Dictionary<byte,Grant> Grants=new();
        private static readonly System.Collections.Generic.HashSet<string> RejectedSpawnSlots=new(StringComparer.Ordinal);
        private static ParkingSlotMessage _pendingMessage,_activeGrant;
        private static bool _haveActiveGrant;
        private static float _startedAt;
        private static uint _spawnRequest;
        private static float _spawnAt,_spawnRetryAt;
        private static bool _spawnWaiting;
        private static ParkingSlotMessage _spawnMessage;
        public static bool JoinPlacementPending
        {
            get
            {
                if(CoopSession.Role==CoopRole.Guest)return _spawnWaiting;
                return Grants.TryGetValue(2,out var grant) && grant.Message.Kind==ParkingSlotMessage.SpawnGranted &&
                    (!IndependentCoop.PeerCarFresh || Vector3.Distance(IndependentCoop.PeerPosition,
                        new Vector3(grant.Message.X,grant.Message.Y+0.75f,grant.Message.Z))>0.5f);
            }
        }
        public static void PrepareJoinedParkedCar()
        {
            if(CoopSession.Role!=CoopRole.Guest || !CoopSession.PeerOnline || !RegionReplication.SameRegion || !IndependentCoop.Playing ||
                !IndependentCoop.LocalOnFoot || !IndependentCoop.OwnParked || !IndependentCoop.PeerParked)return;
            float now=Time.realtimeSinceStartup;
            if(_spawnWaiting || now<_spawnRetryAt || Vector3.Distance(IndependentCoop.OwnPosition(),IndependentCoop.PeerPosition)>2.5f)return;
            _spawnWaiting=true;_spawnAt=now;_spawnRequest=++_sequence;
            var p=IndependentCoop.OwnPosition();
            _spawnMessage=new ParkingSlotMessage(_spawnRequest,IndependentCoop.State.Revision,2,ParkingSlotMessage.SpawnRequest,
                RegionReplication.LocalScene,p.x,p.y,p.z);
            CoopSession.SendMessage(_spawnMessage);
            Status="Preparing a free parking bay for your car";
        }
        private static void HandleSpawn(ParkingSlotMessage request,byte owner)
        {
            int scene=RegionReplication.LocalScene;
            bool valid=owner==2 && request.Vehicle==2 && request.Revision==IndependentCoop.State.Revision && request.Scene==scene &&
                RegionReplication.SameRegion && IndependentCoop.OwnParked && IndependentCoop.PeerParked &&
                RemoteFootAvatar.Fresh && RemoteFootAvatar.OnFoot && RemoteFootAvatar.Scene==scene &&
                IndependentCoop.State.Player2Vehicle==0 && IndependentCoop.State.Driver2==0 &&
                Vector3.Distance(IndependentCoop.OwnPosition(),IndependentCoop.PeerPosition)<2.5f &&
                Vector3.Distance(new Vector3(request.X,request.Y,request.Z),IndependentCoop.PeerPosition)<1;
            ParkVolume chosen=null;float best=float.PositiveInfinity;
            if(valid)foreach(var volume in Volumes())
            {
                if(volume==null || volume.gameObject.scene.buildIndex!=scene || volume.characterSpawnPoint==null ||
                    volume.volumeCollider==null || !volume.volumeCollider.enabled ||
                    volume.parkingCar!=null && volume.parkingCar.activeInHierarchy)continue;
                var p=volume.transform.position;
                if(RejectedSpawnSlots.Contains(Key(scene,p)))continue;
                float distance=Vector3.Distance(IndependentCoop.OwnPosition(),p+Vector3.up*0.75f);
                if(distance<4 || distance>=best || Reservations.IsReservedByOther(Key(scene,p),2,Time.realtimeSinceStartup))continue;
                chosen=volume;best=distance;
            }
            bool accepted=chosen!=null && Reservations.TryReserve(Key(scene,chosen.transform.position),2,Time.realtimeSinceStartup,true);
            Vector3 target=accepted ? chosen.transform.position : Vector3.zero;
            var reply=new ParkingSlotMessage(request.Request,request.Revision,2,accepted ? ParkingSlotMessage.SpawnGranted : ParkingSlotMessage.SpawnDenied,
                scene,target.x,target.y,target.z,accepted ? "Free parked-car spawn granted" : "No free parked-car spawn available");
            if(accepted)Grants[2]=new Grant{Message=reply,Slot=Key(scene,target)};
            CoopSession.SendMessage(reply);
        }
        private static void ReceiveSpawn(ParkingSlotMessage reply)
        {
            if(CoopSession.Role!=CoopRole.Guest || !_spawnWaiting || reply.Vehicle!=2 || reply.Request!=_spawnRequest)return;
            _spawnWaiting=false;_spawnRetryAt=Time.realtimeSinceStartup+1;
            var car=GameRefs.PlayerCar;
            if(reply.Kind!=ParkingSlotMessage.SpawnGranted || reply.Revision!=IndependentCoop.State.Revision ||
                reply.Scene!=RegionReplication.LocalScene || !RegionReplication.SameRegion || !IndependentCoop.LocalOnFoot ||
                car?.parkObject==null || !car.parkObject.activeInHierarchy)
            {if(reply.Kind==ParkingSlotMessage.SpawnGranted)Cancel(reply);Status=reply.Reason;return;}
            var target=new Vector3(reply.X,reply.Y,reply.Z);
            ParkVolume chosen=null;
            foreach(var volume in Volumes())
                if(volume!=null && Vector3.Distance(volume.transform.position,target)<0.1f && volume.characterSpawnPoint!=null &&
                    volume.volumeCollider!=null && volume.volumeCollider.enabled &&
                    (volume.parkingCar==null || !volume.parkingCar.activeInHierarchy)){chosen=volume;break;}
            if(chosen==null){Cancel(reply,true);Status="Native parked-car spawn unavailable; retrying another bay";return;}
            car.parkObject.transform.SetPositionAndRotation(target+Vector3.up*0.75f,chosen.transform.rotation);
            if(GameRefs.Player!=null && GameRefs.Player.IsActive())
            {
                var p=chosen.characterSpawnPoint.transform.position;
                GameRefs.Player.transform.position=p;
                if(GameRefs.Player.rBody!=null){GameRefs.Player.rBody.position=p;GameRefs.Player.rBody.velocity=Vector3.zero;}
            }
            _activeGrant=reply;_haveActiveGrant=true;_startedAt=Time.realtimeSinceStartup;
            Status="Your car is parked in a separate free bay";
        }
        private static ParkVolume[] _volumes=Array.Empty<ParkVolume>();
        private static int _volumeScene=-1;
        private static float _scanAt,_observeAt;
        private static ParkVolume[] Volumes()
        {
            int scene=SceneManager.GetActiveScene().buildIndex;
            if(scene!=_volumeScene || Time.realtimeSinceStartup>=_scanAt)
            {
                _volumeScene=scene; _scanAt=Time.realtimeSinceStartup+5;
                var found=new System.Collections.Generic.List<ParkVolume>();
                foreach(var v in UnityEngine.Object.FindObjectsOfType<ParkVolume>()) if(v!=null) found.Add(v);
                _volumes=found.ToArray();
            }
            return _volumes;
        }
        public static string Status {get;private set;}="idle";
        public static int Denied {get;private set;}
        public static int Granted {get;private set;}
        public static bool PromptHidden {get;private set;}
        public static bool OccupiedForLocalCar(ParkVolume volume)
        {
            if(volume==null || !IndependentCoop.Enabled || !CoopSession.PeerOnline) return false;
            int scene=SceneManager.GetActiveScene().buildIndex;
            Vector3 p=volume.transform.position;
            bool occupied=IndependentCoop.PeerParked && IndependentCoop.PeerScene==scene &&
                Vector3.Distance(IndependentCoop.PeerPosition,p+Vector3.up*0.75f)<2.5f;
            if(CoopSession.Role==CoopRole.Host) occupied|=Reservations.IsReservedByOther(Key(scene,p),IndependentCoop.LocalPlayer,Time.realtimeSinceStartup);
            return occupied;
        }
        public static void SetPromptHidden(bool hidden)=>PromptHidden=hidden;
        private static string Key(int scene,Vector3 p)=>FormattableString.Invariant($"{scene}:{Math.Round(p.x*10)}:{Math.Round(p.y*10)}:{Math.Round(p.z*10)}");

        public static void Request(bool remoteDriver)
        {
            var car=GameRefs.PlayerCar;
            if(_pending!=null || !IndependentCoop.Playing || car==null || car.IsInRoutine()) return;
            if(car.parkingVolume==null || Vector3.Distance(car.transform.position,car.parkingVolume.transform.position)>30)
            { Complete(false,"Move into a parking zone first",remoteDriver); return; }
            _pending=car.parkingVolume; _remote=remoteDriver;
            _request=++_sequence; _revision=IndependentCoop.State.Revision; _requestedAt=Time.realtimeSinceStartup;
            var p=_pending.transform.position;
            var request=new ParkingSlotMessage(_request,_revision,IndependentCoop.LocalPlayer,0,SceneManager.GetActiveScene().buildIndex,p.x,p.y,p.z);
            _pendingMessage=request;
            Status="Waiting for parking permission"; IndependentCoop.SetParkingStatus(Status);
            if(CoopSession.Role==CoopRole.Host) HandleRequest(request,1);
            else CoopSession.SendMessage(request);
        }

        public static void HandleRequest(ParkingSlotMessage message,byte owner)
        {
            if(CoopSession.Role!=CoopRole.Host || !CoopSession.PeerOnline) return;
            if(message.Kind==ParkingSlotMessage.SpawnRequest){HandleSpawn(message,owner);return;}
            Observe();
            int scene=SceneManager.GetActiveScene().buildIndex;
            Vector3 p=new(message.X,message.Y,message.Z);
            ParkVolume volume=null;
            foreach(var candidate in Volumes())
                if(candidate!=null && candidate.gameObject.activeInHierarchy && Vector3.Distance(candidate.transform.position,p)<0.1f) {volume=candidate;break;}
            Vector3 vehicle=owner==1 ? IndependentCoop.OwnPosition() : IndependentCoop.PeerPosition;
            bool valid=IndependentCoop.Playing && message.Vehicle==owner && message.Revision==IndependentCoop.State.Revision &&
                scene==message.Scene && volume!=null && Vector3.Distance(vehicle,p)<30 &&
                IndependentCoop.State.DriverOf(owner)!=0 && (owner==1 || IndependentCoop.PeerCarFresh);
            bool ambient=volume!=null && volume.parkingCar!=null && volume.parkingCar.activeInHierarchy;
            Vector3 canonical=volume==null ? p : volume.transform.position;
            string slot=Key(scene,canonical);
            // Existing/restored parked cars can predate a lease. Native ParkRoutine's
            // destination is volume.position + (0,0.75,0) (verified RVA 0x7A7DB0).
            bool otherParked=owner==1 ? IndependentCoop.PeerParked && IndependentCoop.PeerScene==scene : IndependentCoop.OwnParked;
            Vector3 otherPosition=owner==1 ? IndependentCoop.PeerPosition : IndependentCoop.OwnPosition();
            bool occupiedHull=otherParked && Vector3.Distance(otherPosition,canonical+Vector3.up*0.75f)<2.5f;
            bool accepted=valid && !ambient && !occupiedHull && Reservations.TryReserve(slot,owner,Time.realtimeSinceStartup,true);
            var reply=new ParkingSlotMessage(message.Request,message.Revision,message.Vehicle,accepted?(byte)1:(byte)2,message.Scene,canonical.x,canonical.y,canonical.z,
                accepted ? "Parking permission granted" : valid ? "Parking space occupied" : "Parking request expired or invalid");
            if(accepted) Grants[owner]=new Grant{Message=reply,Slot=slot};
            if(owner==1) Receive(reply);
            else CoopSession.SendMessage(reply);
        }

        public static void Receive(ParkingSlotMessage reply)
        {
            if(reply.Kind==ParkingSlotMessage.SpawnGranted || reply.Kind==ParkingSlotMessage.SpawnDenied){ReceiveSpawn(reply);return;}
            if(_pending==null || reply.Vehicle!=IndependentCoop.LocalPlayer || reply.Request!=_request || reply.Revision!=_revision) return;
            bool accepted=reply.Kind==1 && Time.realtimeSinceStartup-_requestedAt<=3f && IndependentCoop.State.Revision==_revision && IndependentCoop.Playing &&
                GameRefs.PlayerCar.parkingVolume!=null && GameRefs.PlayerCar.parkingVolume.Pointer==_pending.Pointer;
            _pending=null;
            if(reply.Kind==1) {_activeGrant=reply;_haveActiveGrant=true;_startedAt=Time.realtimeSinceStartup;}
            Complete(accepted,accepted ? reply.Reason : reply.Reason=="Parking permission granted" ? "Parking request expired" : reply.Reason,_remote);
        }

        private static void Complete(bool accepted,string reason,bool remote)
        {
            if(accepted) accepted=IndependentCoop.BeginGrantedParking();
            if(!accepted && _haveActiveGrant) {Cancel(_activeGrant);_haveActiveGrant=false;}
            if(accepted) Granted++; else Denied++;
            Status=accepted ? "Parking" : reason;
            IndependentCoop.SetParkingStatus(Status);
            if(remote) CoopSession.SendMessage(new VehicleParkMessage(IndependentCoop.State.Revision,IndependentCoop.LocalPlayer,accepted?(byte)1:(byte)2));
        }

        private static string ParkedSlot(bool parked,Vector3 position)
        {
            if(!parked) return null;
            foreach(var volume in Volumes())
                if(volume!=null && Vector3.Distance(volume.transform.position+Vector3.up*0.75f,position)<0.5f)
                    return Key(SceneManager.GetActiveScene().buildIndex,volume.transform.position);
            return null;
        }
        private static void Observe()
        {
            ObserveVehicle(1,IndependentCoop.OwnParked,IndependentCoop.OwnPosition(),IndependentCoop.OwnScene);
            if(IndependentCoop.PeerCarFresh)
                ObserveVehicle(2,IndependentCoop.PeerParked,IndependentCoop.PeerPosition,IndependentCoop.PeerScene);
        }
        private static void ObserveVehicle(byte vehicle,bool parked,Vector3 p,int scene)
        {
            if(Grants.TryGetValue(vehicle,out var grant))
            {
                if(scene!=grant.Message.Scene)
                {Reservations.Release(vehicle);Grants.Remove(vehicle);return;}
                if(parked) Reservations.Observe(vehicle,grant.Slot,Time.realtimeSinceStartup);
                return; // An in-flight grant stays reserved until explicit cancellation.
            }
            if(scene!=SceneManager.GetActiveScene().buildIndex) {Reservations.Release(vehicle);return;}
            string slot=ParkedSlot(parked,p);
            if(parked && slot==null) return; // Failed matching is not evidence of departure.
            Reservations.Observe(vehicle,slot,Time.realtimeSinceStartup);
        }
        private static void Cancel(ParkingSlotMessage grant,bool spawnUnavailable=false)
        {
            var message=new ParkingSlotMessage(grant.Request,grant.Revision,grant.Vehicle,3,grant.Scene,grant.X,grant.Y,grant.Z,
                spawnUnavailable ? "Native spawn occupied" : "");
            if(CoopSession.Role==CoopRole.Host) HandleCancel(message,1);else CoopSession.SendMessage(message);
        }
        public static void HandleCancel(ParkingSlotMessage message,byte owner)
        {
            if(CoopSession.Role!=CoopRole.Host || message.Vehicle!=owner || !Grants.TryGetValue(owner,out var grant) ||
                grant.Message.Request!=message.Request || grant.Message.Revision!=message.Revision) return;
            if(grant.Message.Kind==ParkingSlotMessage.SpawnGranted && message.Reason=="Native spawn occupied")
                RejectedSpawnSlots.Add(grant.Slot);
            Reservations.Release(owner);Grants.Remove(owner);
        }
        public static void Tick()
        {
            using var timing=CoopPerformance.Measure(CoopPerformance.Stage.Parking);
            if(!IndependentCoop.Enabled || !CoopSession.PeerOnline) {Reset();return;}
            // Cancel the exact original lease even if loading/region change stops
            // the overlap preparation tick. A late grant must not lock boarding.
            if(_spawnWaiting && (Time.realtimeSinceStartup-_spawnAt>3 ||
                _spawnMessage.Scene!=RegionReplication.LocalScene || !RegionReplication.SameRegion))
            {
                Cancel(_spawnMessage);_spawnWaiting=false;_spawnRetryAt=Time.realtimeSinceStartup+1;
                Status="Parked-car placement expired; retrying when ready";
            }
            if(!IndependentCoop.Playing) return;
            if(CoopSession.Role==CoopRole.Host && Time.realtimeSinceStartup>=_observeAt) {_observeAt=Time.realtimeSinceStartup+0.5f;Observe();}
            if(_pending!=null && Time.realtimeSinceStartup-_requestedAt>3f) {Cancel(_pendingMessage);_pending=null;Complete(false,"Parking permission timed out",_remote);}
            if(_haveActiveGrant)
            {
                if(!IndependentCoop.OwnParked && !IndependentCoop.ParkingInProgress && Time.realtimeSinceStartup-_startedAt>1f)
                {Cancel(_activeGrant);_haveActiveGrant=false;}
            }
        }
        public static void Reset() {_pending=null;_haveActiveGrant=false;_spawnWaiting=false;_spawnMessage=default;_spawnRequest=0;_spawnAt=_spawnRetryAt=0;Grants.Clear();RejectedSpawnSlots.Clear();Reservations.Clear();_volumes=Array.Empty<ParkVolume>();_volumeScene=-1;PromptHidden=false;Status="idle";}
    }
}
