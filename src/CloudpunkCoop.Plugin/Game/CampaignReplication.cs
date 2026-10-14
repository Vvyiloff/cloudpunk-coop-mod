using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using BepInEx.Logging;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    /// <summary>Host-only story execution, absolute guest state and independent presentation.</summary>
    public static class CampaignReplication
    {
        private static readonly ManualLogSource Log=BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Campaign");
        private static readonly CampaignFrameAssembler Assembler=new();
        private static readonly DialogueAuthority Dialogue=new();
        private static readonly Dictionary<uint,CampaignReplyMessage> Replies=new();
        private static readonly JsonSerializerOptions Json=new(){IncludeFields=true,MaxDepth=32};
        private static CampaignSnapshot _pending,_last;
        private static CampaignConversation _conversation=new();
        private static bool _installed,_failed,_wasOnline,_dirty=true;
        private static float _nextSnapshot,_nextPresentation,_receivedAt;
        private static float _lastSnapshotAt=float.NegativeInfinity;
        private static uint _revision,_requestId;
        private static uint _presentationSequence,_receivedPresentation;
        internal static CampaignReplyMessage TestLastReply {get;private set;}
        internal static uint TestRequestId=>_requestId;
        internal static CampaignActionMessage TestLastRequest {get;private set;}
        private static long _sessionId;
        private static IntPtr _dialogPointer;
        private static byte _dialogOwner=1;
        private static bool _startingGuestConversation;
        private static bool _executingContinue;
        private static BepInEx.Configuration.ConfigEntry<string> _campaignConfig;
        private static bool _havePresentation;
        private static bool _guestSaveIsolation;
        private static int _appliedScene=-1;
        private static Il2CppSystem.Collections.IEnumerator _sceneFixture;
        // Developer-only layout verified against Player.<ChangeSceneRoutine>d__115,
        // dump.cs:226298: native state field at 0x10 in this supported game build.
        internal static int TestSceneRoutineState=>_sceneFixture==null ? -2 :
            System.Runtime.InteropServices.Marshal.ReadInt32(_sceneFixture.Pointer,0x10);
        private static string _nearbyId="",_nearbyName="";
        private static float _nextNearby;
        public static string Status { get; private set; }="waiting for shared campaign";
        public static long SnapshotsApplied { get; private set; }
        public static long SuppressedMutations { get; private set; }
        public static long HostCompletions { get; private set; }
        public static uint Revision=>_revision;
        public static long SessionId=>_sessionId;
        public static ulong ConversationGeneration=>Dialogue.Generation;
        public static bool GuardGuest=>CoopSession.Role==CoopRole.Guest && Global.Instance!=null && !Global.Instance.isLoadingSaveGame;
        public static bool Ready=>CoopSession.PeerOnline && GameRefs.Resolved && Global.Instance!=null &&
            RegionReplication.LocalReady;
        public static bool ConversationActive=>GuardGuest && _conversation.Active && CoopSession.PeerOnline;
        internal static CampaignConversation TestConversation=>CoopSession.Role==CoopRole.Host ? CampaignSnapshot.CaptureConversation() : _conversation;

        public static void ConfigureCampaign(BepInEx.Configuration.ConfigEntry<string> setting)=>_campaignConfig=setting;
        public static void IsolateGuestSave()=>_guestSaveIsolation=true;
        public static bool GuestSaveIsolated=>_guestSaveIsolation;

        internal static void ChangeSharedScene(int scene)
        {
            if(SceneLoader.Instance==null || GameRefs.Player==null) throw new InvalidOperationException("Scene transition runner unavailable");
            // The native routine ALSO starts its nested loader on Player (RVA 66D660).
            // An outer active runner cannot fix an inactive driving actor. Driving travel
            // must use the native lift sequence; this developer fixture is walking only.
            if(!GameRefs.Player.IsActive() || !GameRefs.Player.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Walking scene fixture requires parking and exiting first; cars use native lifts");
            // Keep the outer routine alive if destination initialization disables Player.
            // Its nested loader still needs the active Player validated above.
            _sceneFixture=GameRefs.Player.ChangeSceneRoutine(scene);
            var routine=SceneLoader.Instance.StartCoroutine(_sceneFixture);
            if(routine==null) throw new InvalidOperationException("Native scene coroutine did not start");
        }

        private static void ConfigureInventory()
        {
            if(!HostSaveIdentity.Ready)throw new InvalidOperationException(HostSaveIdentity.LastError.Length==0 ? "Waiting for a completed native campaign load" : HostSaveIdentity.LastError);
            string key=HostSaveIdentity.CampaignId;
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
            bool test=string.Equals(Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST"),"true",StringComparison.OrdinalIgnoreCase);
            string root=test ? Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST_DIR") : System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"CloudpunkCoop");
            if(string.IsNullOrWhiteSpace(root) || !System.IO.Path.IsPathFullyQualified(root))throw new InvalidOperationException("Inventory profile directory unavailable");
            CoopInventory.ConfigureSidecar(System.IO.Path.Combine(root,hash+"-player2.json"));
        }
        internal static void BindInventory()=>ConfigureInventory();

        public static void Install()
        {
            if(_installed) return;
            var h=new Harmony("dev.andre.cloudpunk-coop.campaign");
            Patch(h,typeof(DialogLine),"Complete",nameof(LinePrefix),nameof(LinePostfix));
            Patch(h,typeof(Character),"OnInteract",nameof(InteractPrefix));
            Patch(h,typeof(Character),"OnFinishedDialog",nameof(MutationPrefix));
            Patch(h,typeof(DialogHUD),"FadeIn",nameof(FadeInPrefix));
            Patch(h,typeof(DialogHUD),"OnContinue",nameof(ContinuePrefix));
            Patch(h,typeof(DialogHUD),"OnSell",nameof(MutationPrefix));
            Patch(h,typeof(Global),"Autosave",nameof(AutosavePrefix));
            Patch(h,typeof(Global),"Save",nameof(SavePrefix));
            Patch(h,typeof(Global),"ActivateQuest",nameof(MutationPrefix));
            Patch(h,typeof(Global),"DeactivateQuest",nameof(MutationPrefix));
            Patch(h,typeof(Global),"ProgressQuest",nameof(MutationPrefix));
            Patch(h,typeof(Global),"StartNewGame",nameof(NewCampaignPrefix));
            Patch(h,typeof(StoryTrigger),"Check",nameof(CheckPrefix));
            foreach(string name in new[]{"Unlock","UpdateTimeout","ActivateProgress","Reset","StopTimeout","ChangeScene","TriggerJobComplete"})
                Patch(h,typeof(StoryTrigger),name,name=="Unlock" ? nameof(UnlockPrefix) : nameof(MutationPrefix));
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
            h.Patch(AccessTools.Method(typeof(Global),"Update"),
                prefix:new HarmonyMethod(typeof(CampaignReplication).GetMethod(nameof(GlobalUpdatePrefix),flags)),
                finalizer:new HarmonyMethod(typeof(CampaignReplication).GetMethod(nameof(GlobalUpdateFinalizer),flags)));
            Patch(h,typeof(IncomingCall),"FadeIn",nameof(MutationPrefix));
            Patch(h,typeof(Item),"ChangeUnits",nameof(ItemChangePrefix));
            Patch(h,typeof(Item),"OnUse",nameof(ItemUsePrefix));
            Patch(h,typeof(Item),"Equip",nameof(ItemEquipPrefix));
            Patch(h,typeof(TradeOffer),"OnClick",nameof(BuyPrefix));
            Patch(h,typeof(InventoryUI),"SellItem",nameof(SellPrefix));
            Patch(h,typeof(Global),"SpendMoney",nameof(SpendPrefix));
            Patch(h,typeof(Global),"GainMoney",nameof(MutationPrefix));
            Patch(h,typeof(Global),"IsBlocking",postfix:nameof(BlockingPostfix));
            _installed=true;
            Log.LogInfo("Campaign mutation guards installed; guest solo saves remain isolated");
        }

        private static void Patch(Harmony h,Type type,string method,string prefix=null,string postfix=null)
        {
            var target=AccessTools.Method(type,method);
            if(target==null) throw new MissingMethodException(type.Name,method);
            const BindingFlags f=BindingFlags.NonPublic|BindingFlags.Static;
            h.Patch(target,prefix:prefix==null ? null : new HarmonyMethod(typeof(CampaignReplication).GetMethod(prefix,f)),
                postfix:postfix==null ? null : new HarmonyMethod(typeof(CampaignReplication).GetMethod(postfix,f)));
        }

        public static void Reset()
        {
            CoopInventory.FlushPersonalEffects(Time.realtimeSinceStartup,true);
            Assembler.Reset();_pending=null;_last=null;_conversation=new();
            CampaignPresentation.Stop();
            CampaignSnapshot.ResetPresentation();
            _revision=0;_requestId=0;_failed=false;_dirty=true;_wasOnline=false;
            TestLastRequest=null;TestLastReply=null;
            _presentationSequence=0;_receivedPresentation=0;_havePresentation=false;
            _sessionId=DateTime.UtcNow.Ticks;Dialogue.Reset();_dialogPointer=IntPtr.Zero;
            _requestId=(uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1,int.MaxValue);
            _nextSnapshot=0;_nextPresentation=0;Replies.Clear();
            _nearbyId="";_nearbyName="";_nextNearby=0;
            _appliedScene=-1;
            _sceneFixture=null;
            CoopInventory.Reset();
            CoopPersonalEffects.Reset();
            Status="waiting for shared campaign";
            // Guest save isolation stays latched through shutdown, before native final saves.
        }

        public static void TransportReconnected()
        {
            CoopPersonalEffects.Reset();
            Assembler.Reset();_pending=null;_last=null;_conversation=new();_havePresentation=false;
            _appliedScene=-1;
            CampaignPresentation.Stop();
            _receivedPresentation=0;_receivedAt=Time.realtimeSinceStartup;_wasOnline=false;
            _dirty=true;_nextSnapshot=0;_nextPresentation=0;_nearbyId="";_nearbyName="";
            // Host session identity, request outcomes, personal ledger and conversation
            // generation survive a socket replacement. Reconnecting never replays rewards.
        }

        public static void Tick(float now)
        {
            using var timing=CoopPerformance.Measure(CoopPerformance.Stage.Campaign);
            if(!CoopSession.PeerOnline)
            {
                if(_wasOnline) {Assembler.Reset();_pending=null;_conversation=new();_last=null;_havePresentation=false;_dirty=true;CampaignPresentation.Stop();}
                _wasOnline=false;
                Status=GuardGuest ? "Campaign paused - reconnect to host" : "waiting for shared campaign";
                return;
            }
            if(!_wasOnline){_wasOnline=true;_dirty=true;_nextSnapshot=0;}
            if(!Ready || _failed) return;
            try
            {
                if(CoopSession.Role==CoopRole.Host)
                {
                    CoopInventory.InitializeGuest();
                    ObserveConversation();
                    var hud=Global.Instance.dialogUI;
                    bool talking=hud!=null && hud.IsActiveAndNotFadingOut();
                    if(now>=_nextSnapshot || _dirty && now-_lastSnapshotAt>=0.25f)
                    {
                        _lastSnapshotAt=now;_nextSnapshot=now+2f;_dirty=false;
                        var snapshot=CampaignSnapshot.Capture(++_revision,SceneManager.GetActiveScene().buildIndex);
                        snapshot.Validate();
                        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(snapshot,Json);
                        if(bytes.Length>1024*1024) throw new InvalidOperationException("Campaign snapshot exceeds 1 MiB");
                        int count=(bytes.Length+16383)/16384;
                        for(int i=0;i<count;i++)
                        {
                            int length=Math.Min(16384,bytes.Length-i*16384);
                            var chunk=new byte[length];Array.Copy(bytes,i*16384,chunk,0,length);
                            if(!CoopSession.SendMessage(new CampaignChunkMessage(_revision,snapshot.Scene,(ushort)i,(ushort)count,chunk)))
                                throw new InvalidOperationException("Campaign snapshot send failed");
                        }
                        Status="shared campaign authoritative";
                    }
                    if(now>=_nextPresentation)
                    {
                        _nextPresentation=now+(talking ? 0.2f : 1f);
                        var text=JsonSerializer.SerializeToUtf8Bytes(CampaignSnapshot.CaptureConversation());
                        CoopSession.SendMessage(new CampaignPresentationMessage(++_presentationSequence,_revision,
                            SceneManager.GetActiveScene().buildIndex,text));
                    }
                }
                else if(_pending!=null)
                {
                    // Campaign state is session-wide. A snapshot's source region is not
                    // a travel command; Apply touches only authored records loaded here.
                    _pending.Apply();_revision=_pending.Revision;_sessionId=_pending.SessionId;
                    if(!_havePresentation && RegionReplication.SameRegion)
                    {_conversation=_pending.Conversation;CampaignPresentation.Receive(_conversation);}
                    _appliedScene=SceneManager.GetActiveScene().buildIndex;
                    _last=_pending;_pending=null;SnapshotsApplied++;
                    Status="shared campaign synchronized";
                }
                else if(CoopSession.Role==CoopRole.Guest && _last!=null &&
                    _appliedScene!=SceneManager.GetActiveScene().buildIndex)
                {
                    // Project the latest absolute state into newly loaded local objects.
                    // Never rerun native rewards or move the other player on region entry.
                    _last.Apply();_appliedScene=SceneManager.GetActiveScene().buildIndex;
                }
                else if(CoopSession.Role==CoopRole.Guest && now-_receivedAt>5f)
                    Status="Waiting for host campaign state";
                if(CoopSession.Role==CoopRole.Guest)
                {
                    if(!RegionReplication.SameRegion && _conversation.Active)
                    {_conversation=new();_havePresentation=false;CampaignPresentation.Stop();}
                    if(now>=_nextNearby)
                    {
                        _nextNearby=now+0.2f;_nearbyId="";_nearbyName="";
                        if(!ConversationActive && GameRefs.Player!=null && GameRefs.Player.IsActive())
                            WorldNpcReplication.FindNearbyNamed(GameRefs.Player.transform.position,3f,out _nearbyId,out _nearbyName);
                    }
                    if(!CloudpunkCoop.UI.CoopConnectionMenu.CapturesInput && Input.GetKeyDown(KeyCode.E) && _nearbyId.Length!=0 && !ConversationActive)
                        Request(1,_nearbyId);
                }
            }
            catch(Exception ex)
            {
                _failed=true;Status="Campaign synchronization failed - restart both co-op sessions";
                Log.LogError("Campaign synchronization stopped safely: "+ex);
                if(Bootstrapper.DiagnosticsEnabled)
                    NativeCampaignProbe.Capture(Log,System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
                        "coop-campaign-"+CoopSession.Role.ToString().ToLowerInvariant()+".jsonl"));
            }
        }

        public static void Receive(CampaignChunkMessage message)
        {
            if(CoopSession.Role!=CoopRole.Guest || !CoopSession.PeerOnline) return;
            try
            {
                if(!Assembler.Add(message,out var bytes)) return;
                var snapshot=JsonSerializer.Deserialize<CampaignSnapshot>(bytes,Json);
                if(snapshot==null || snapshot.Revision!=message.Revision || snapshot.Scene!=message.Scene)
                    throw new InvalidOperationException("Campaign frame identity mismatch");
                snapshot.Validate();_pending=snapshot;_receivedAt=Time.realtimeSinceStartup;
            }
            catch(Exception ex){_failed=true;Status="Invalid campaign state - restart both co-op sessions";Log.LogError(ex);}
        }

        public static void Receive(CampaignPresentationMessage message)
        {
            if(CoopSession.Role!=CoopRole.Guest || !CoopSession.PeerOnline ||
                !RegionReplication.SameRegion ||
                message.Scene!=SceneManager.GetActiveScene().buildIndex ||
                _havePresentation && unchecked((int)(message.Sequence-_receivedPresentation))<=0) return;
            try
            {
                var text=JsonSerializer.Deserialize<CampaignConversation>(message.DataSpan);
                if(text==null || (text.Text?.Length ?? 0)>16384 || (text.Speaker?.Length ?? 0)>1024 ||
                    (text.Dialog?.Length ?? 0)>256 || (text.Trader?.Length ?? 0)>256 ||
                    (text.Portrait?.Length ?? 0)>256 || (text.Voice?.Length ?? 0)>256 || !float.IsFinite(text.VoiceTime) || text.VoiceTime<0)
                    throw new InvalidOperationException("Invalid shared conversation");
                _conversation=text;_receivedPresentation=message.Sequence;_havePresentation=true;
                CampaignPresentation.Receive(text);
            }
            catch(Exception ex){_failed=true;Status="Invalid shared conversation";Log.LogError(ex);}
        }

        public static uint Request(byte kind,string target,string argument="",int amount=1,bool duplicate=false)
        {
            if(!Ready || _failed || CoopSession.Role!=CoopRole.Guest || _last==null) return 0;
            // Order the current native clock BEFORE the use action on the same
            // reliable stream so pre-use elapsed time cannot decay the new effect.
            if(kind==3 && !CoopPersonalEffects.SendClock(Time.realtimeSinceStartup))return 0;
            if(++_requestId==0) ++_requestId;
            var message=new CampaignActionMessage(_requestId,_revision,
                SceneManager.GetActiveScene().buildIndex,kind,target ?? "",argument ?? "",amount,
                _sessionId,kind==2 ? _conversation.Generation : 0,kind==2 ? _conversation.LineIndex : -1);
            if(!CoopSession.SendMessage(message)) return 0;
            if(Bootstrapper.DiagnosticsEnabled)TestLastRequest=message;
            if(duplicate) CoopSession.SendMessage(message);
            return _requestId;
        }

        internal static uint RequestContinue(bool duplicate=false)=>!_conversation.Active ? 0 :
            Request(2,_conversation.Dialog,_conversation.Line.ToString(),1,duplicate);

        public static void Receive(CampaignReplyMessage reply)
        {
            if(CoopSession.Role!=CoopRole.Guest || !CoopSession.PeerOnline) return;
            TestLastReply=reply;
            if(!reply.Accepted) Status=reply.Reason;
        }

        public static void Handle(CampaignActionMessage request)
        {
            if(CoopSession.Role!=CoopRole.Host || !CoopSession.PeerOnline) return;
            if(request.SessionId!=_sessionId){CoopSession.SendMessage(new CampaignReplyMessage(request.RequestId,_revision,false,"Campaign session changed"));return;}
            if(Replies.TryGetValue(request.RequestId,out var prior)) {CoopSession.SendMessage(prior);return;}
            if(Replies.Count>=4096)
            {
                CoopSession.SendMessage(new CampaignReplyMessage(request.RequestId,_revision,false,"Session request limit - restart host session required"));
                return;
            }
            // Reserve before entering native callbacks, which can reenter the mod.
            Replies[request.RequestId]=new CampaignReplyMessage(request.RequestId,_revision,false,"Action pending");
            string reason="Action unavailable";bool accepted=false;
            try
            {
                if(!Ready || _failed) reason="Shared world not ready";
                else if(!RegionReplication.PeerReady || request.Scene!=RegionReplication.PeerScene ||
                    (request.Kind!=3 && request.Kind!=4) &&
                    (request.Scene!=SceneManager.GetActiveScene().buildIndex || !RemoteFootAvatar.Fresh ||
                    RemoteFootAvatar.Scene!=request.Scene)) reason="Player scene or position is stale";
                else if(request.Revision!=_revision && request.Kind!=2 && request.Kind!=3 && request.Kind!=4) reason="Campaign changed - retry interaction";
                else if(request.Kind==1) accepted=StartConversation(request,out reason);
                else if(request.Kind==2) accepted=ContinueConversation(request,out reason);
                else accepted=CoopInventory.ExecuteGuest(request.Kind,request.Target,request.Argument,request.Amount,out reason);
                if(accepted) _dirty=true;
            }
            catch(Exception ex){reason="Native action failed";Log.LogError("Campaign request: "+ex);}
            Reply(request,accepted,reason);
        }

        private static void Reply(CampaignActionMessage request,bool accepted,string reason)
        {
            while(System.Text.Encoding.UTF8.GetByteCount(reason)>256) reason=reason.Substring(0,reason.Length-1);
            var reply=new CampaignReplyMessage(request.RequestId,_revision,accepted,reason);
            Replies[request.RequestId]=reply;CoopSession.SendMessage(reply);
        }

        private static bool StartConversation(CampaignActionMessage request,out string reason)
        {
            reason="Character unavailable";
            if(!RemoteFootAvatar.OnFoot || IndependentCoop.State.VehicleOf(2)!=0) return false;
            var g=Global.Instance;
            var chr=g.FindSerializeObject(request.Target)?.TryCast<Character>();
            if(chr==null || chr.isCrowdNPC || !chr.gameObject.activeInHierarchy || !chr.canTalkTo ||
                Vector3.Distance(chr.transform.position,RemoteFootAvatar.LatestPosition)>4f) return false;
            if(g.dialogUI!=null && g.dialogUI.IsActiveAndNotFadingOut()) {reason="Conversation already in progress";return false;}
            var dlg=chr.FindDialog(false);
            if(dlg==null || !dlg.IsPlayable(false,false) || !dlg.HasPlayableLine()) return false;
            _startingGuestConversation=true;
            try {chr.OnInteract();} finally {_startingGuestConversation=false;}
            reason="Conversation started";return true;
        }

        private static bool ContinueConversation(CampaignActionMessage request,out string reason)
        {
            reason="Conversation has changed";
            var hud=Global.Instance.dialogUI;
            if(hud==null || !hud.IsActiveAndNotFadingOut() || hud.dialog==null || hud.currentLine==null ||
                hud.dialog.id!=request.Target || hud.currentLine.lineID.ToString()!=request.Argument) return false;
            ObserveConversation();
            bool ready=CampaignSnapshot.CanAdvance(hud);
            if(!Dialogue.TryContinue(2,request.Conversation,request.LineIndex,hud.currentLine.lineID,ready))
            {reason="Line not ready or already continued";return false;}
            _executingContinue=true;
            try {hud.OnContinue(true);} finally {_executingContinue=false;}
            reason="Continue";return true;
        }

        private static bool FadeInPrefix(Dialog dlg)
        {
            if(GuardGuest){SuppressedMutations++;return false;}
            Dialogue.Reset();_dialogPointer=IntPtr.Zero;_dialogOwner=_startingGuestConversation ? (byte)2 : (byte)1;
            return true;
        }

        private static bool NewCampaignPrefix()
        {
            if(GuardGuest){SuppressedMutations++;Status="Only the host can start a shared campaign";return false;}
            if(CoopSession.Role!=CoopRole.Host) return true;
            Reset();return true;
        }

        private static void ObserveConversation()
        {
            var state=CampaignSnapshot.CaptureConversation();
            if(!state.Active || state.LineIndex<0)
            {if(Dialogue.Active) Dialogue.End(Dialogue.Generation);_dialogPointer=IntPtr.Zero;return;}
            var pointer=Global.Instance.dialogUI.dialog.Pointer;
            if(!Dialogue.Active || _dialogPointer!=pointer)
            {
                if(Dialogue.Active) Dialogue.End(Dialogue.Generation);
                Dialogue.Begin(_dialogOwner,state.Dialog,state.LineIndex,state.Line,state.CanContinue);
                _dialogPointer=pointer;
            }
            else Dialogue.SetLine(Dialogue.Generation,state.LineIndex,state.Line,state.CanContinue);
        }

        private static bool MutationPrefix()
        {if(!GuardGuest && !CoopInventory.IsExecutingGuest) return true;SuppressedMutations++;return false;}
        private sealed class UpdateState
        {
            public Global Global;
            public Il2CppSystem.Collections.Generic.List<StoryTrigger> Triggers,Timers;
            public Il2CppSystem.Collections.Generic.List<Dialog> Dialogs;
            public float CallTimer;
            public float EffectDelta;
            public bool EffectTick;
        }
        private static readonly Il2CppSystem.Collections.Generic.List<StoryTrigger> EmptyTimers=new();
        private static readonly Il2CppSystem.Collections.Generic.List<Dialog> EmptyDialogs=new();
        private static bool _maskingUpdate;
        private static void GlobalUpdatePrefix(Global __instance,out UpdateState __state)
        {
            __state=null;
            if(!GuardGuest || _maskingUpdate) return;
            __state=new UpdateState {Global=__instance,Triggers=__instance.triggers,
                Timers=__instance.timeBasedTriggers,Dialogs=__instance.selfTriggeringDialogs,
                CallTimer=__instance.makeCallTimer,EffectDelta=Time.deltaTime,
                EffectTick=!__instance.IsInMenu() && (SceneLoader.Instance==null || !SceneLoader.Instance.IsActive())};
            _maskingUpdate=true;
            // Global.Update lazily resolves tutorial triggers through this registry
            // (native RVA 6312F7). Emptying it causes a native null dereference.
            // Trigger mutation bodies remain guarded, so retain the authored lookup.
            __instance.timeBasedTriggers=EmptyTimers;
            __instance.selfTriggeringDialogs=EmptyDialogs;
        }
        private static Exception GlobalUpdateFinalizer(UpdateState __state,Exception __exception)
        {
            if(__state==null) return __exception;
            try
            {
                __state.Global.triggers=__state.Triggers;__state.Global.timeBasedTriggers=__state.Timers;
                __state.Global.selfTriggeringDialogs=__state.Dialogs;__state.Global.makeCallTimer=__state.CallTimer;
            }
            finally {_maskingUpdate=false;EmptyTimers.Clear();EmptyDialogs.Clear();}
            if(__exception==null)CoopPersonalEffects.RecordNativeTick(__state.EffectDelta,__state.EffectTick);
            return __exception;
        }
        private static bool SavePrefix(ref int __result)
        {if(!_guestSaveIsolation && CoopSession.Role!=CoopRole.Guest && !CoopInventory.IsExecutingGuest) return true;SuppressedMutations++;__result=0;return false;}
        private static bool AutosavePrefix()
        {if(!_guestSaveIsolation && CoopSession.Role!=CoopRole.Guest && !CoopInventory.IsExecutingGuest) return true;SuppressedMutations++;return false;}
        private static bool SpendPrefix(ref bool __result)
        {if(!GuardGuest) return true;SuppressedMutations++;__result=false;return false;}
        private static bool CheckPrefix(ref bool __result)
        {if(!GuardGuest) return true;SuppressedMutations++;__result=false;return false;}
        private static bool UnlockPrefix(StoryTrigger __instance)
        {if(!GuardGuest){if(__instance.unlocked==null || !__instance.unlocked.value) _dirty=true;return true;}SuppressedMutations++;return false;}
        private static bool InteractPrefix(Character __instance)
        {
            if(!GuardGuest) return true;
            SuppressedMutations++;
            if(__instance!=null && !__instance.isCrowdNPC) Request(1,__instance.GetGuid());
            return false;
        }
        private static bool ContinuePrefix(DialogHUD __instance)
        {
            if(!GuardGuest)
            {
                if(CoopSession.Role!=CoopRole.Host || !CoopSession.PeerOnline || _executingContinue) return true;
                ObserveConversation();
                if(!Dialogue.Active || __instance.currentLine==null) return true;
                // A native call has already passed the game's skip/automatic timing seam.
                // Reserve it too, so a same-frame guest request cannot advance this line twice.
                Dialogue.SetLine(Dialogue.Generation,Dialogue.LineIndex,__instance.currentLine.lineID,true);
                return Dialogue.TryContinue(1,Dialogue.Generation,Dialogue.LineIndex,__instance.currentLine.lineID,true);
            }
            SuppressedMutations++;
            if(_conversation.Active) Request(2,_conversation.Dialog,_conversation.Line.ToString());
            return false;
        }
        private static bool ItemChangePrefix()
        {if(!GuardGuest) return true;SuppressedMutations++;return false;}
        private static bool ItemUsePrefix(Item __instance)
        {if(!GuardGuest) return true;Request(3,__instance.name);SuppressedMutations++;return false;}
        private static bool ItemEquipPrefix(Item __instance)
        {if(!GuardGuest) return true;Request(4,__instance.name);SuppressedMutations++;return false;}
        private static bool BuyPrefix(TradeOffer __instance)
        {
            if(!GuardGuest) return true;
            if(__instance.item!=null && __instance.merchant!=null) Request(5,__instance.item.name,__instance.merchant.GetGuid());
            SuppressedMutations++;return false;
        }
        private static bool SellPrefix(InventoryUI __instance)
        {
            if(!GuardGuest) return true;
            if(__instance.selectedItemForSale!=null && _conversation.Trader.Length!=0)
                Request(6,__instance.selectedItemForSale.name,_conversation.Trader);
            SuppressedMutations++;return false;
        }
        private static void BlockingPostfix(ref bool __result)
        {if(ConversationActive) __result=true;}
        private static bool LinePrefix(DialogLine __instance,bool repeatable,out CoopInventoryState __state)
        {
            __state=null;
            if(GuardGuest){SuppressedMutations++;return false;}
            if(!CoopSession.PeerOnline || CoopSession.Role!=CoopRole.Host) return true;
            if(!repeatable && __instance.played) return false;
            try {CoopInventory.InitializeGuest();__state=CoopInventory.CaptureLocal();return true;}
            catch(Exception ex){_failed=true;Status="Campaign reward baseline unavailable";Log.LogError(ex);return false;}
        }
        private static void LinePostfix(CoopInventoryState __state)
        {
            if(__state==null) return;
            try {CoopInventory.OnSharedReward(__state);HostCompletions++;_dirty=true;}
            catch(Exception ex){_failed=true;Status="Campaign reward reconciliation required";Log.LogError(ex);}
        }

        public static bool ConsumesInteract()
        {
            if(!Ready) return false;
            if(Global.Instance.dialogUI!=null && Global.Instance.dialogUI.IsActiveAndNotFadingOut()) return true;
            if(ConversationActive) return true;
            return GuardGuest && _nearbyId.Length!=0 && GameRefs.Player!=null && GameRefs.Player.IsActive();
        }

        public static void Draw()
        {
            if(_failed && CoopSession.Role!=CoopRole.Off) GUI.Box(new Rect(12,150,450,45),Status);
            if(!GuardGuest || !GameRefs.Resolved || Global.Instance.IsInMenu()) return;
            if(ConversationActive)
            {
                float width=Math.Min(760,Screen.width-40);float x=(Screen.width-width)/2;
                GUI.Box(new Rect(x,Screen.height-230,width,215),"Shared conversation - "+_conversation.Speaker);
                var style=new GUIStyle(GUI.skin.label){wordWrap=true,fontSize=18};
                CampaignPresentation.DrawPortrait(new Rect(x+12,Screen.height-194,110,130));
                GUI.Label(new Rect(x+138,Screen.height-200,width-156,130),_conversation.Text,style);
                if(_conversation.CanContinue && GUI.Button(new Rect(x+width-150,Screen.height-60,130,32),"Continue"))
                    Request(2,_conversation.Dialog,_conversation.Line.ToString());
            }
            else if(Ready && _nearbyId.Length!=0 && GameRefs.Player!=null && GameRefs.Player.IsActive())
            {
                GUI.Box(new Rect(Screen.width/2-180,Screen.height-90,360,55),"E: Talk to "+_nearbyName);
            }
            if(!CoopSession.PeerOnline || _failed)
                GUI.Box(new Rect(12,150,450,45),Status);
        }
    }
}
