using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    /// <summary>Explicit developer-only native fixture commands. Never active in normal play.</summary>
    internal static class CampaignTestDriver
    {
        private static int _lastId;
        private static float _nextPoll;
        private static CampaignActionMessage _replayRequest;
        private static readonly JsonSerializerOptions Json=new(){IncludeFields=true};
        private sealed class Command
        {
            public int Id {get;set;}
            public string Action {get;set;}="";
            public string Target {get;set;}="";
            public string Argument {get;set;}="";
            public byte Kind {get;set;}
            public int Amount {get;set;}=1;
            public float X {get;set;}
            public float Y {get;set;}
            public float Z {get;set;}
            public bool Duplicate {get;set;}
        }

        public static void Tick(float now)
        {
            if(!Bootstrapper.DiagnosticsEnabled || !string.Equals(Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST"),"true",StringComparison.OrdinalIgnoreCase) ||
                !GameRefs.Resolved || Global.Instance==null || now<_nextPoll) return;
            _nextPoll=now+0.1f;
            string directory=Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST_DIR");
            if(string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) return;
            string role=CoopSession.Role.ToString().ToLowerInvariant();
            string path=Path.Combine(directory,"campaign-command-"+role+".json");
            Command cmd;
            try
            {
                // The harness atomically replaces this file. Existence and length
                // checks must share the IO guard with the read: a replacement
                // between File.Exists and FileInfo.Length is a normal retry.
                if(!File.Exists(path) || new FileInfo(path).Length>16384) return;
                cmd=JsonSerializer.Deserialize<Command>(File.ReadAllBytes(path),Json);
            }
            catch(IOException){return;}
            catch(JsonException){return;} // Atomic writer may not have committed a complete command yet.
            if(cmd==null || cmd.Id<=_lastId) return;
            _lastId=cmd.Id;
            string error="";uint request=0;
            var candidates=new List<object>();
            try
            {
                if(cmd.Action!="state" && cmd.Action!="loading" && cmd.Action!="menu-close" &&
                    !(cmd.Action=="prepare-consumables" && RegionReplication.LocalReady) && !CampaignReplication.Ready)
                    throw new InvalidOperationException("Native world is still loading");
                if(cmd.Action=="candidates")
                {
                    if(CoopSession.Role!=CoopRole.Host) throw new InvalidOperationException("Host candidates only");
                    foreach(var pair in Global.Instance.savedObjectDictionary)
                    {
                        var chr=pair.Value?.linkedObject?.TryCast<Character>();
                        if(chr==null || chr.isCrowdNPC || !chr.gameObject.activeInHierarchy || !chr.canTalkTo) continue;
                        var d=chr.FindDialog(false);
                        if(d==null || d.completed || d.repeatable || !d.HasPlayableLine()) continue;
                        int money=0;var quests=new List<string>();var items=new List<string>();
                        foreach(var line in d.lines)
                        {
                            if(line==null || line.played) continue;
                            money+=Math.Max(0,line.moneyReward);
                            if(!string.IsNullOrEmpty(line.questActivate)) quests.Add(line.questActivate);
                            if(!string.IsNullOrEmpty(line.itemGive)) items.Add(line.itemGive);
                        }
                        var p=chr.transform.position;
                        candidates.Add(new {Guid=pair.Key,Name=chr.definedName,Dialog=d.id,Money=money,Quests=quests,Items=items,X=p.x,Y=p.y,Z=p.z});
                    }
                }
                else if(cmd.Action=="parking-bays" || cmd.Action=="prepare-park")
                {
                    ParkVolume chosen=null;
                    foreach(var volume in UnityEngine.Object.FindObjectsOfType<ParkVolume>())
                    {
                        if(volume==null || volume.gameObject.scene.buildIndex!=SceneManager.GetActiveScene().buildIndex ||
                            volume.characterSpawnPoint==null || volume.volumeCollider==null || !volume.volumeCollider.enabled ||
                            volume.parkingCar!=null && volume.parkingCar.activeInHierarchy || ParkingAuthority.OccupiedForLocalCar(volume))continue;
                        var p=volume.transform.position;
                        if(cmd.Action=="parking-bays")candidates.Add(new {X=p.x,Y=p.y,Z=p.z});
                        else if(Vector3.Distance(p,new Vector3(cmd.X,cmd.Y,cmd.Z))<0.05f)chosen=volume;
                    }
                    if(cmd.Action=="prepare-park")
                    {
                        var car=GameRefs.PlayerCar;
                        if(chosen==null || car==null || car.IsInRoutine() || GameRefs.Player.IsActive() ||
                            IndependentCoop.LocalVehicle!=IndependentCoop.LocalPlayer || IndependentCoop.State.DriverOf(IndependentCoop.LocalPlayer)!=IndependentCoop.LocalPlayer)
                            throw new InvalidOperationException("Parking preparation requires an available authored bay and your own idle car");
                        // Preparation only. Native trigger detection and the normal E/RPC
                        // reservation path must perform parking and actor exit themselves.
                        var p=chosen.volumeCollider.bounds.center;
                        car.transform.SetPositionAndRotation(p,chosen.transform.rotation);
                        if(car.rBody!=null){car.rBody.position=p;car.rBody.rotation=chosen.transform.rotation;car.rBody.velocity=car.rBody.angularVelocity=Vector3.zero;}
                    }
                }
                else if(cmd.Action=="near-peer-car")
                {
                    if(GameRefs.Player==null || !GameRefs.Player.IsActive() || IndependentCoop.LocalVehicle!=0 ||
                        !IndependentCoop.PeerCarFresh || !IndependentCoop.PeerParked)throw new InvalidOperationException("Approach fixture requires walking and a fresh parked peer car");
                    // Use the authored pedestrian spawn beside the bay. An arbitrary
                    // offset from its vehicle pose can land outside the walkway.
                    CharacterSpawnPoint spawn=null;
                    foreach(var volume in UnityEngine.Object.FindObjectsOfType<ParkVolume>())
                        if(volume!=null && volume.gameObject.scene.buildIndex==IndependentCoop.PeerScene &&
                            volume.characterSpawnPoint!=null &&
                            Vector3.Distance(volume.transform.position+Vector3.up*0.75f,IndependentCoop.PeerPosition)<0.5f)
                        {spawn=volume.characterSpawnPoint;break;}
                    if(spawn==null || Vector3.Distance(spawn.transform.position,IndependentCoop.PeerPosition)>8f)
                        throw new InvalidOperationException("No authored pedestrian approach within boarding distance");
                    var p=spawn.transform.position;
                    GameRefs.Player.transform.position=p;
                    if(GameRefs.Player.rBody!=null){GameRefs.Player.rBody.position=p;GameRefs.Player.rBody.velocity=Vector3.zero;}
                }
                else if(cmd.Action=="near")
                {
                    if(CoopSession.Role!=CoopRole.Guest || !GameRefs.Player.IsActive() || IndependentCoop.LocalVehicle!=0)
                        throw new InvalidOperationException("Native guest must park and be on foot first");
                    if(!float.IsFinite(cmd.X) || !float.IsFinite(cmd.Y) || !float.IsFinite(cmd.Z) ||
                        Math.Abs(cmd.X)>100000 || Math.Abs(cmd.Y)>100000 || Math.Abs(cmd.Z)>100000)
                        throw new InvalidOperationException("Invalid native fixture position");
                    var chr=Global.Instance.FindSerializeObject(cmd.Target)?.TryCast<Character>();
                    if(chr==null || chr.isCrowdNPC) throw new InvalidOperationException("Fixture character unavailable");
                    // Preparation only. Interaction still traverses the real socket, host
                    // distance/occupancy checks and Character.OnInteract native coroutine.
                    GameRefs.Player.transform.position=new Vector3(cmd.X,cmd.Y,cmd.Z);
                }
                else if(cmd.Action=="request")
                    request=CampaignReplication.Request(cmd.Kind,cmd.Target,cmd.Argument,cmd.Amount,cmd.Duplicate);
                else if(cmd.Action=="request-burst")
                {
                    CampaignReplication.Request(cmd.Kind,cmd.Target,cmd.Argument,cmd.Amount,cmd.Duplicate);
                    request=CampaignReplication.Request(cmd.Kind,cmd.Target,cmd.Argument,cmd.Amount,cmd.Duplicate);
                }
                else if(cmd.Action=="remember-request")
                {
                    _replayRequest=CampaignReplication.TestLastRequest ?? throw new InvalidOperationException("No request to remember");
                    request=_replayRequest.RequestId;
                }
                else if(cmd.Action=="replay-request")
                {
                    if(_replayRequest==null || !CoopSession.SendMessage(_replayRequest))
                        throw new InvalidOperationException("Remembered request could not be sent");
                    request=_replayRequest.RequestId;
                }
                else if(cmd.Action=="drop-transport")CoopSession.DropTransportForTest();
                else if(cmd.Action=="continue")
                    request=CampaignReplication.RequestContinue(cmd.Duplicate);
                else if(cmd.Action=="capture")
                    NativeCampaignProbe.Capture(BepInEx.Logging.Logger.CreateLogSource("Campaign Test"),Path.Combine(directory,"campaign-native-"+role+"-"+cmd.Id+".jsonl"));
                else if(cmd.Action=="catalog")
                    NativeCampaignProbe.CaptureDefinitions(Path.Combine(directory,"campaign-definitions-host.jsonl"));
                else if(cmd.Action=="save")
                {
                    if(CoopSession.Role!=CoopRole.Host)throw new InvalidOperationException("Only the host may exercise native save commit");
                    Global.Instance.Save(0); // Timestamp return is deliberately not success evidence.
                }
                else if(cmd.Action=="native-use")
                {
                    if(CoopSession.Role!=CoopRole.Guest || Global.Instance.inventoryUI==null)
                        throw new InvalidOperationException("Native use requires the test guest inventory UI");
                    Item chosen=null;
                    foreach(var item in Global.Instance.inventory)if(item!=null && item.name==cmd.Target){chosen=item;break;}
                    if(chosen==null || !chosen.isConsumable || chosen.amount<1)throw new InvalidOperationException("Native use item unavailable");
                    var ui=Global.Instance.inventoryUI;var previous=ui.selectedItemForUse;
                    uint before=CampaignReplication.TestRequestId;
                    ui.selectedItemForUse=chosen;
                    try{ui.UseItem();}finally{ui.selectedItemForUse=previous;}
                    if(CampaignReplication.TestRequestId!=before)request=CampaignReplication.TestRequestId;
                }
                else if(cmd.Action=="menu-open" || cmd.Action=="menu-close")
                {
                    if(cmd.Action=="menu-open")Global.Instance.StartCoroutine(Global.Instance.DisplayMenu(true));
                    else Global.Instance.CloseMenu();
                }
                else if(cmd.Action=="prepare-consumables")
                {
                    if(CoopSession.Role!=CoopRole.Host || CoopSession.PeerOnline || cmd.Target!="EnergyDrink")
                        throw new InvalidOperationException("Consumable preparation requires a solo test host and audited item");
                    foreach(var item in Global.Instance.inventory)
                        if(item!=null && item.name==cmd.Target)
                        {if(!item.isConsumable)throw new InvalidOperationException("Native consumable metadata unavailable");item.amount=3;break;}
                }
                else if(cmd.Action=="state" || cmd.Action=="loading") { }
                else if(cmd.Action=="scene")
                {
                    if(cmd.Amount<1 || cmd.Amount>=SceneManager.sceneCountInBuildSettings)
                        throw new InvalidOperationException("Invalid native test scene");
                    CampaignReplication.ChangeSharedScene(cmd.Amount);
                }
                else if(cmd.Action=="lift")
                {
                    var car=GameRefs.PlayerCar;
                    if(car==null || GameRefs.Player.IsActive() || IndependentCoop.LocalVehicle!=IndependentCoop.LocalPlayer ||
                        IndependentCoop.State.DriverOf(IndependentCoop.LocalPlayer)!=IndependentCoop.LocalPlayer || car.IsInRoutine())
                        throw new InvalidOperationException("Native lift fixture requires driving your own idle car");
                    Lift chosen=null;
                    if(Layer.Instance?.tubes!=null) foreach(var lift in Layer.Instance.tubes)
                        if(lift!=null && lift.destination==cmd.Amount && lift.gameObject.activeInHierarchy &&
                            lift.endPoint!=null && lift.camera!=null){chosen=lift;break;}
                    if(chosen==null) throw new InvalidOperationException("No authored native lift to region "+cmd.Amount);
                    // Preparation only. Native EnterLift/LiftRoutine selects the authored
                    // destination exit and restores normal control, camera and physics.
                    car.transform.SetPositionAndRotation(chosen.transform.position,chosen.transform.rotation);
                    if(car.rBody!=null){car.rBody.position=chosen.transform.position;car.rBody.velocity=Vector3.zero;car.rBody.angularVelocity=Vector3.zero;}
                    car.EnterLift(chosen);
                }
                else throw new InvalidOperationException("Unknown native test command");
            }
            catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;}
            var player=GameRefs.Player==null ? Vector3.zero : GameRefs.Player.transform.position;
            Item targetItem=null;
            if(!string.IsNullOrEmpty(cmd.Target))foreach(var item in Global.Instance.inventory)
                if(item!=null && item.name==cmd.Target){targetItem=item;break;}
            var result=new {Id=cmd.Id,Error=error,RequestId=request,Scene=SceneManager.GetActiveScene().buildIndex,
                Item=targetItem==null ? null : new {Id=targetItem.name,Amount=targetItem.amount,Consumable=targetItem.isConsumable},
                PersonalEffects=new {ClockFresh=CoopPersonalEffects.HostClockFresh,Clock=CoopPersonalEffects.HostElapsed,
                    LocalClock=CoopPersonalEffects.LocalElapsed,Error=CoopPersonalEffects.LastError,
                    AudioPlays=CoopPersonalEffects.AudioPlays,Food=Global.Instance.foodCooldown,Drink=Global.Instance.drinkCooldown,
                    Drug=Global.Instance.drugCooldown,Speed=Global.Instance.speedGainTimeLeft,Alcohol=Global.Instance.alcoholTimeLeft},
                LastReply=CampaignReplication.TestLastReply,PeerOnline=CoopSession.PeerOnline,
                Status=CampaignReplication.Status,Revision=CampaignReplication.Revision,Session=CampaignReplication.SessionId,
                SaveIdentityReady=HostSaveIdentity.Ready,SaveIdentity=HostSaveIdentity.CampaignId,
                SaveIdentityError=HostSaveIdentity.LastError,CommittedSaves=HostSaveIdentity.CommittedSaves,
                InventoryPath=CoopInventory.SidecarPath,
                Applied=CampaignReplication.SnapshotsApplied,Completions=CampaignReplication.HostCompletions,
                Suppressed=CampaignReplication.SuppressedMutations,Money=Global.Instance.money,
                PlayerActive=GameRefs.Player!=null && GameRefs.Player.IsActive(),X=player.x,Y=player.y,Z=player.z,
                RegionReady=RegionReplication.LocalReady,
                LoaderActive=SceneLoader.Instance!=null && SceneLoader.Instance.IsActive(),
                LoaderLoading=SceneLoader.IsLoading(),
                LoaderFading=SceneLoader.Instance!=null && SceneLoader.Instance.IsFading(),
                LayerScene=Layer.Instance==null ? -1 : Layer.Instance.gameObject.scene.buildIndex,
                LayerName=Layer.Instance==null ? "" : Layer.Instance.gameObject.name,
                SceneRoutineState=CampaignReplication.TestSceneRoutineState,
                GdocScanning=Global.Instance.gdoc!=null && Global.Instance.gdoc.isScanning,
                Huxley=GameRefs.Player!=null && GameRefs.Player.huxley!=null,
                Pashta=GameRefs.Player!=null && GameRefs.Player.pashta!=null,
                PlayerBody=GameRefs.Player!=null && GameRefs.Player.rBody!=null,
                PlayerInteraction=GameRefs.Player!=null && GameRefs.Player.interaction!=null,
                LayerSpawn=Layer.Instance!=null && Layer.Instance.spawnPoint!=null,
                NativeException=Bootstrapper.NativeTestException,
                Conversation=CampaignReplication.TestConversation,Candidates=candidates};
            File.WriteAllBytes(Path.Combine(directory,"campaign-result-"+role+".json"),JsonSerializer.SerializeToUtf8Bytes(result,Json));
        }
    }
}
