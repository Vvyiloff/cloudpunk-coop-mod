using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    // Absolute data only. Applying a snapshot must never invoke quest/reward UnityEvents.
    internal sealed class CampaignSnapshot
    {
        public uint Revision { get; set; }
        public long SessionId { get; set; }
        public int Scene { get; set; }
        public List<CampaignValue> Values { get; set; } = new();
        public List<CampaignDialog> Dialogs { get; set; } = new();
        public List<CampaignQuest> Quests { get; set; } = new();
        public List<CampaignCharacter> Characters { get; set; } = new();
        public CoopInventoryState Inventory { get; set; }
        public CampaignConversation Conversation { get; set; } = new();

        public static CampaignSnapshot Capture(uint revision, int scene)
        {
            var g=Global.Instance;
            var snapshot=new CampaignSnapshot { Revision=revision, Scene=scene,
                SessionId=CampaignReplication.SessionId,Inventory=CoopInventory.GuestState };
            var triggerIds=new HashSet<string>(StringComparer.Ordinal);
            if(g.triggers!=null) foreach(var trigger in g.triggers)
                if(trigger!=null) triggerIds.Add(trigger.GetGuid());
            if(g.savedObjectDictionary!=null)
                foreach(var pair in g.savedObjectDictionary)
                {
                    var saved=pair.Value;
                    if(saved==null || saved.values==null) continue;
                    // Primitive story fields only; never copy unrelated NPC/personal actor saves.
                    if(!triggerIds.Contains(pair.Key) &&
                        (saved.linkedObject==null || saved.linkedObject.TryCast<StoryTrigger>()==null)) continue;
                    foreach(var value in saved.values)
                    {
                        if(value.Value==null) continue;
                        var row=new CampaignValue { Object=pair.Key, Id=value.Key };
                        var b=value.Value.TryCast<SavedBool>();
                        var i=value.Value.TryCast<SavedInt>();
                        var f=value.Value.TryCast<SavedFloat>();
                        var s=value.Value.TryCast<SavedString>();
                        if(b!=null) { row.Kind=1; row.Integer=b.value ? 1 : 0; }
                        else if(i!=null) { row.Kind=2; row.Integer=i.value; }
                        else if(f!=null) { row.Kind=3; row.Number=f.value; }
                        else if(s!=null) { row.Kind=4; row.Text=s.value ?? ""; }
                        else continue; // Transform/object state is never replayed through callbacks.
                        snapshot.Values.Add(row);
                    }
                }
            if(g.savedObjectDictionary!=null)
                foreach(var pair in g.savedObjectDictionary)
                {
                    var c=pair.Value?.linkedObject?.TryCast<Character>();
                    if(c!=null && !c.isCrowdNPC)
                        snapshot.Characters.Add(new CampaignCharacter { Id=pair.Key, CanTalk=c.canTalkTo,
                            Name=c.definedName ?? "" });
                }
            if(g.Dialogs!=null)
                foreach(var d in g.Dialogs)
                {
                    if(d==null) continue;
                    var row=new CampaignDialog { Id=d.id, Completed=d.completed };
                    if(d.lines!=null) foreach(var line in d.lines)
                        row.Lines.Add(line!=null && line.played);
                    snapshot.Dialogs.Add(row);
                }
            if(g.questTexts!=null)
                foreach(var pair in g.questTexts)
                {
                    var q=pair.Value;
                    if(q==null) continue;
                    snapshot.Quests.Add(new CampaignQuest { Id=pair.Key, Enabled=q.enabled,
                        Current=q.progressCurrent, Max=q.progressMax, Marker=(int)q.markerType,
                        Scene=q.markerInScene, X=q.markerPos.x,Y=q.markerPos.y,Z=q.markerPos.z });
                }
            snapshot.Conversation=CaptureConversation();
            return snapshot;
        }

        public static CampaignConversation CaptureConversation()
        {
            var hud=Global.Instance.dialogUI;
            if(hud!=null && hud.dialog!=null && hud.IsActiveAndNotFadingOut())
            {
                var line=hud.currentLine;
                int index=-1;
                if(line!=null && hud.dialog.lines!=null)
                    for(int n=0;n<hud.dialog.lines.Count;n++)
                        if(hud.dialog.lines[n]!=null && hud.dialog.lines[n].Pointer==line.Pointer){index=n;break;}
                string prefix="Voiceover/InCar/";
                if(line!=null)
                {
                    int profile=line.character==null ? 0 : (int)line.character.forceVoiceProfile;
                    if(line.overCom || profile==2) prefix="Voiceover/Com/";
                    else if(profile==1) prefix="Voiceover/CORA/";
                    else if(profile==4 || profile==0 && GameRefs.Player.IsActive()) prefix="Voiceover/Outdoor/";
                }
                return new CampaignConversation { Active=true, Dialog=hud.dialog.id,
                    Line=line?.lineID ?? -1,LineIndex=index,Generation=CampaignReplication.ConversationGeneration,
                    Speaker=Global.IsInVehicleCockpit && hud.nameObjInterior!=null ? hud.nameObjInterior.text : hud.nameObj==null ? "" : hud.nameObj.text,
                    Text=Global.IsInVehicleCockpit && hud.textFieldInterior!=null ? hud.textFieldInterior.text : hud.textField==null ? "" : hud.textField.text,
                    Trader=hud.character==null ? "" : hud.character.GetGuid(),
                    Portrait=line?.character==null ? "" : line.character.name,
                    Voice=line==null || string.IsNullOrEmpty(line.voiceFileID) ? "" : prefix+hud.dialog.id+"#"+line.voiceFileID,
                    VoicePlaying=hud.voiceoverSource!=null && hud.voiceoverSource.isPlaying,
                    VoiceTime=hud.voiceoverSource==null ? 0 : hud.voiceoverSource.time,
                    ShowOffers=line!=null && line.showItemOffers,
                    CanContinue=CanAdvance(hud) };
            }
            return new CampaignConversation();
        }

        internal static bool CanAdvance(DialogHUD hud)=>hud!=null && hud.currentLine!=null && !hud.fadingOut &&
            (hud.doneButton!=null && hud.doneButton.activeInHierarchy || !hud.blockSkipping &&
                (hud.currentLine.played || hud.dialog!=null && hud.dialog.repeatable &&
                    hud.voiceoverSource!=null && !hud.voiceoverSource.isPlaying));

        public void Validate()
        {
            if(SessionId<=0) throw new InvalidOperationException("Missing campaign identity");
            if(Values==null || Dialogs==null || Quests==null || Characters==null || Conversation==null || Inventory==null ||
                Values.Count>40000 || Dialogs.Count>10000 || Quests.Count>10000 || Characters.Count>4096)
                throw new InvalidOperationException("Invalid campaign snapshot collections");
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var v in Values)
            {
                if(v==null || string.IsNullOrEmpty(v.Object) || v.Object.Length>256 || v.Kind<1 || v.Kind>4 ||
                    !float.IsFinite(v.Number) || (v.Text?.Length ?? 0)>4096 ||
                    !keys.Add(v.Object+"\0"+v.Id)) throw new InvalidOperationException("Invalid campaign value");
            }
            keys.Clear();
            foreach(var c in Characters)
                if(c==null || string.IsNullOrEmpty(c.Id) || c.Id.Length>256 || c.Name.Length>256 || !keys.Add(c.Id))
                    throw new InvalidOperationException("Invalid story character");
            keys.Clear();
            foreach(var d in Dialogs)
                if(d==null || string.IsNullOrEmpty(d.Id) || d.Id.Length>256 || d.Lines==null ||
                    d.Lines.Count>4096 || !keys.Add(d.Id)) throw new InvalidOperationException("Invalid campaign dialog");
            keys.Clear();
            foreach(var q in Quests)
                if(q==null || string.IsNullOrEmpty(q.Id) || q.Id.Length>256 || q.Marker<0 || q.Marker>4 ||
                    !float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !keys.Add(q.Id))
                    throw new InvalidOperationException("Invalid campaign quest");
            if((Conversation.Text?.Length ?? 0)>16384 || (Conversation.Speaker?.Length ?? 0)>1024 ||
                (Conversation.Dialog?.Length ?? 0)>256 || (Conversation.Trader?.Length ?? 0)>256 ||
                (Conversation.Portrait?.Length ?? 0)>256 || (Conversation.Voice?.Length ?? 0)>256 ||
                !float.IsFinite(Conversation.VoiceTime) || Conversation.VoiceTime<0)
                throw new InvalidOperationException("Invalid conversation presentation");
            if(Inventory.Items==null || Inventory.Items.Count>2048 || Inventory.Money<0)
                throw new InvalidOperationException("Invalid personal inventory");
            keys.Clear();
            foreach(var item in Inventory.Items)
                if(item==null || string.IsNullOrEmpty(item.Id) || item.Id.Length>256 || item.Amount< -1 ||
                    item.Available< -1 || !keys.Add(item.Id)) throw new InvalidOperationException("Invalid inventory item");
        }

        private static readonly DeferredCampaignRefresh MoneyRefresh=new("money HUD");
        private static readonly DeferredCampaignRefresh MarkerRefresh=new("quest markers");
        internal static bool PresentationPending=>MoneyRefresh.Pending || MarkerRefresh.Pending || CoopInventory.EquipmentPresentationPending;
        internal static string LastPresentationError=>MoneyRefresh.LastError.Length!=0 ? MoneyRefresh.LastError :
            MarkerRefresh.LastError.Length!=0 ? MarkerRefresh.LastError : CoopInventory.LastEquipmentPresentationError;
        internal static void ResetPresentation(){MoneyRefresh.Reset();MarkerRefresh.Reset();}

        public void Apply()
        {
            Validate();
            CoopPersonalEffects.BeginSnapshot(SessionId);
            var g=Global.Instance;
            // Preflight every loaded value type and authored line count before any mutation.
            var assignments=new CampaignFieldTransaction();
            bool equipmentChanged=CoopInventory.PrepareLocal(Inventory,assignments);
            foreach(var v in Values)
            {
                if(g.savedObjectDictionary==null || !g.savedObjectDictionary.TryGetValue(v.Object,out var obj) ||
                    obj==null || obj.values==null || !obj.values.TryGetValue(v.Id,out var native) || native==null) continue;
                if(v.Kind==1) {var b=native.TryCast<SavedBool>(); if(b==null) throw new InvalidOperationException("Saved bool schema mismatch"); assignments.Add(value=>b.value=value,b.value,v.Integer!=0);}
                else if(v.Kind==2) {var i=native.TryCast<SavedInt>(); if(i==null) throw new InvalidOperationException("Saved int schema mismatch"); assignments.Add(value=>i.value=value,i.value,v.Integer);}
                else if(v.Kind==3) {var f=native.TryCast<SavedFloat>(); if(f==null) throw new InvalidOperationException("Saved float schema mismatch"); assignments.Add(value=>f.value=value,f.value,v.Number);}
                else {var s=native.TryCast<SavedString>(); if(s==null) throw new InvalidOperationException("Saved string schema mismatch"); assignments.Add(value=>s.value=value,s.value,v.Text ?? "");}
            }
            var dialogs=new Dictionary<string,Dialog>(StringComparer.Ordinal);
            if(g.Dialogs!=null) foreach(var d in g.Dialogs) if(d!=null) dialogs[d.id]=d;
            foreach(var row in Dialogs)
            {
                if(!dialogs.TryGetValue(row.Id,out var d)) continue;
                if(d.lines==null || d.lines.Count!=row.Lines.Count) throw new InvalidOperationException("Dialog schema mismatch: "+row.Id);
                assignments.Add(value=>d.completed=value,d.completed,row.Completed);
                assignments.Add(value=>d.isPlaying=value,d.isPlaying,false);
                for(int n=0;n<row.Lines.Count;n++)
                {
                    var line=d.lines[n];
                    if(line!=null) assignments.Add(value=>line.played=value,line.played,row.Lines[n]);
                }
            }
            foreach(var row in Quests)
            {
                if(g.questTexts==null || !g.questTexts.TryGetValue(row.Id,out var q) || q==null) continue;
                assignments.Add(value=>q.enabled=value,q.enabled,row.Enabled);
                assignments.Add(value=>q.progressCurrent=value,q.progressCurrent,row.Current);
                assignments.Add(value=>q.progressMax=value,q.progressMax,row.Max);
                assignments.Add(value=>q.markerType=value,q.markerType,(QuestText.Marker)row.Marker);
                assignments.Add(value=>q.markerInScene=value,q.markerInScene,row.Scene);
                assignments.Add(value=>q.markerPos=value,q.markerPos,new Vector3(row.X,row.Y,row.Z));
            }
            foreach(var row in Characters)
            {
                var c=g.FindSerializeObject(row.Id)?.TryCast<Character>();
                if(c!=null) assignments.Add(value=>c.canTalkTo=value,c.canTalkTo,row.CanTalk);
            }
            // Only direct authoritative setters participate in this transaction. Every old
            // value is read during preflight, including dialog playback and personal stock.
            assignments.Commit();
            CoopPersonalEffects.Present(Inventory);
            CoopInventory.RefreshLocalPresentation(equipmentChanged);
            MoneyRefresh.Run(true,()=>
            {
                if(g.textMoney==null) throw new InvalidOperationException("Money HUD unavailable");
                g.textMoney.text=g.money.ToString();
            });
            MarkerRefresh.Run(true,()=>g.UpdateQuestMarkers());
        }
    }

    // One main-thread transaction, prepared entirely before the first setter is called.
    // Individual fields (rather than object-sized actions) allow rollback after a partial
    // dialog/quest/item write. Attempt the failing setter's rollback as well.
    internal sealed class CampaignFieldTransaction
    {
        private const int MaxFields=131072;
        private readonly List<(Action Apply,Action Restore)> _fields=new();
        internal void Add<T>(Action<T> setter,T before,T after)
        {
            if(_fields.Count>=MaxFields) throw new InvalidOperationException("Campaign assignment limit exceeded");
            _fields.Add((()=>setter(after),()=>setter(before)));
        }

        internal void Commit()
        {
            int current=0;
            try
            {
                for(;current<_fields.Count;current++) _fields[current].Apply();
            }
            catch(Exception writeError)
            {
                List<Exception> errors=null;
                // Do not let one failed restore prevent restoration of other fields.
                for(int n=current;n>=0;n--)
                    try { _fields[n].Restore(); }
                    catch(Exception restoreError)
                    {
                        errors ??= new List<Exception> { writeError };
                        errors.Add(restoreError);
                    }
                if(errors!=null) throw new AggregateException("Campaign authority rollback incomplete",errors);
                throw;
            }
        }
    }

    // A single pending bit per presentation channel, no queued snapshots or background
    // retries. The next successfully committed absolute snapshot retries current authority.
    internal sealed class DeferredCampaignRefresh
    {
        private static BepInEx.Logging.ManualLogSource _log;
        private readonly string _channel;
        private int _warnings;
        internal bool Pending { get; private set; }
        internal string LastError { get; private set; }="";
        internal DeferredCampaignRefresh(string channel) { _channel=channel; }
        internal void Reset() { Pending=false;LastError="";_warnings=0; }
        internal void Run(bool requested,Action refresh)
        {
            if(!requested && !Pending) return;
            Pending=true;
            try { refresh(); Reset(); }
            catch(Exception ex)
            {
                // Do not retain exception graphs or native objects, or flood a failing HUD.
                LastError=_channel+" refresh deferred: "+ex.GetType().Name;
                if(LastError.Length>256) LastError=LastError.Substring(0,256);
                if(_warnings>=3) return;
                _warnings++;
                try
                {
                    _log ??= BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Projection");
                    _log.LogWarning(LastError+"; retry at next valid snapshot"+
                        (_warnings==3 ? " (further warnings suppressed until recovery)" : ""));
                }
                catch(Exception) { /* LastError remains observable even if logging fails. */ }
            }
        }
    }

    internal sealed class CampaignValue
    {
        public string Object { get; set; }="";
        public int Id { get; set; }
        public byte Kind { get; set; }
        public int Integer { get; set; }
        public float Number { get; set; }
        public string Text { get; set; }="";
    }
    internal sealed class CampaignDialog
    {
        public string Id { get; set; }="";
        public bool Completed { get; set; }
        public List<bool> Lines { get; set; }=new();
    }
    internal sealed class CampaignQuest
    {
        public string Id { get; set; }="";
        public bool Enabled { get; set; }
        public int Current { get; set; }
        public int Max { get; set; }
        public int Marker { get; set; }
        public int Scene { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }
    internal sealed class CampaignConversation
    {
        public bool Active { get; set; }
        public string Dialog { get; set; }="";
        public int Line { get; set; }=-1;
        public int LineIndex { get; set; }=-1;
        public ulong Generation { get; set; }
        public string Speaker { get; set; }="";
        public string Text { get; set; }="";
        public string Trader { get; set; }="";
        public bool CanContinue { get; set; }
        public string Portrait { get; set; }="";
        public string Voice { get; set; }="";
        public float VoiceTime { get; set; }
        public bool VoicePlaying { get; set; }
        public bool ShowOffers { get; set; }
    }
    internal sealed class CampaignCharacter
    {
        public string Id { get; set; }="";
        public bool CanTalk { get; set; }
        public string Name { get; set; }="";
    }
}
