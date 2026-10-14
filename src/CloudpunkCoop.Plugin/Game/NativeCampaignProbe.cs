using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BepInEx.Logging;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

namespace CloudpunkCoop.Game
{
    /// <summary>Read-only canonical campaign inventory before applying network authority.</summary>
    public static class NativeCampaignProbe
    {
        /// <summary>Developer-only authored metadata evidence. Reads fields/listeners;
        /// never calls IsPlayable, Check, FindDialog, Complete or UnityEvent.Invoke.</summary>
        internal static void CaptureDefinitions(string path)
        {
            if(!Bootstrapper.DiagnosticsEnabled || Net.CoopSession.Role!=Net.CoopRole.Host || !RegionReplication.LocalReady)
                throw new InvalidOperationException("A ready developer test host is required for authored catalog capture");
            var g=Global.Instance;var rows=new List<string>();
            string Id(SerializeObject obj)=>obj==null ? "" : obj.GetGuid();
            object Event(UnityEventBase evt)
            {
                if(evt==null)return new {Present=false};
                var calls=new List<object>();
                var persistent=evt.m_PersistentCalls;
                if(persistent!=null)for(int i=0;i<persistent.Count;i++)
                {
                    var call=persistent.GetListener(i);
                    if(call==null){calls.Add(new {Missing=true});continue;}
                    var target=call.target;var args=call.m_Arguments;
                    calls.Add(new {Index=i,Method=call.m_MethodName,Mode=(int)call.m_Mode,State=(int)call.m_CallState,
                        TargetGuid=Id(target?.TryCast<SerializeObject>()),TargetName=target==null ? "" : target.name,
                        TargetKind=target==null ? "Missing" : target.TryCast<StoryTrigger>()!=null ? "StoryTrigger" :
                            target.TryCast<Character>()!=null ? "Character" : target.TryCast<Global>()!=null ? "Global" : "Unknown",
                        Int=args?.m_IntArgument,Float=args?.m_FloatArgument,String=args?.m_StringArgument,Bool=args?.m_BoolArgument,
                        ObjectName=args?.m_ObjectArgument==null ? "" : args.m_ObjectArgument.name,
                        ObjectType=args?.m_ObjectArgumentAssemblyTypeName});
                }
                return new {Present=true,Persistent=calls,RuntimeListeners=evt.m_Calls?.m_RuntimeCalls?.Count ?? 0};
            }
            if(g.savedObjectDictionary!=null)foreach(var pair in g.savedObjectDictionary)
            {
                var linked=pair.Value?.linkedObject;
                var chr=linked?.TryCast<Character>();
                if(chr!=null && !chr.isCrowdNPC)
                {
                    var p=chr.transform.position;
                    rows.Add(JsonSerializer.Serialize(new {Kind="Actor",Guid=pair.Key,Name=chr.definedName,
                        Scene=chr.gameObject.scene.buildIndex,Active=chr.gameObject.activeInHierarchy,chr.isStationary,
                        chr.canTalkTo,chr.disableCondition,chr.priceFactor,chr.buysItems,X=p.x,Y=p.y,Z=p.z,
                        Before=Event(chr.beforeDialogAction),After=Event(chr.afterDialogAction)}));
                }
                var trigger=linked?.TryCast<StoryTrigger>();
                if(trigger==null)continue;
                var inputs=new List<string>();
                if(trigger.inputs!=null)foreach(var input in trigger.inputs)inputs.Add(Id(input));
                rows.Add(JsonSerializer.Serialize(new {Kind="Trigger",Guid=pair.Key,Name=trigger.name,
                    Scene=trigger.gameObject.scene.buildIndex,trigger.inputsOR,Inputs=inputs,
                    RequiredItem=trigger.requiredItem==null ? "" : trigger.requiredItem.name,
                    trigger.requireItemAmount,trigger.alwaysCheckItemAmount,trigger.progressToUnlock,trigger.timeBased,
                    trigger.activateQuestText,trigger.deactivateQuestText,trigger.progressQuestText,
                    Item=trigger.giveOrTakeItem==null ? "" : trigger.giveOrTakeItem.name,trigger.itemAmount,Action=Event(trigger.action)}));
            }
            if(g.Dialogs!=null)foreach(var d in g.Dialogs)
            {
                if(d==null)continue;
                var lines=new List<object>();
                if(d.lines!=null)for(int n=0;n<d.lines.Count;n++)
                {
                    var line=d.lines[n];if(line==null){lines.Add(new {Index=n,Missing=true});continue;}
                    lines.Add(new {Index=n,line.lineID,line.triggerName,line.invalidateDialog,
                        line.questActivate,line.questDeactivate,line.itemGive,line.itemAmount,line.moneyReward,
                        line.moneyReason,line.callDelay,line.showItemOffers,line.overCom,line.voiceFileID});
                }
                rows.Add(JsonSerializer.Serialize(new {Kind="Dialog",Id=d.id,
                    Actor=d.activatedByCharacter==null ? "" : d.activatedByCharacter.name,
                    d.dependencyName,d.checkNotUnlocked,d.selfTrigger,d.repeatable,d.inCar,d.notInCar,d.inApartment,
                    d.isRemote,d.noBlock,d.lockCharacter,Lines=lines}));
            }
            rows.Sort(StringComparer.Ordinal);
            string body=string.Join("\n",rows);
            string header=JsonSerializer.Serialize(new {Kind="Header",Version=1,Utc=DateTime.UtcNow,
                Scene=RegionReplication.LocalScene,Records=rows.Count,
                Digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))),
                Certified=false,Note="Observed loaded host definitions only. No event execution or complete regional certificate."});
            File.WriteAllText(path,header+"\n"+body,new UTF8Encoding(false));
        }

        public static void Capture(ManualLogSource log, string path)
        {
            try
            {
                var g = Global.Instance;
                if (g == null || g.isLoadingSaveGame || g.IsInMenu()) return;
                var rows = new List<string>();
                int missingObjects = 0;
                var objects = g.savedObjectDictionary;
                if (objects != null)
                    foreach (var pair in objects)
                    {
                        var obj = pair.Value;
                        if (obj == null || obj.linkedObject == null) { missingObjects++; continue; }
                        var character=obj.linkedObject.TryCast<Character>();
                        if(character!=null && !character.isCrowdNPC)
                        {
                            var p=character.transform.position;
                            rows.Add(JsonSerializer.Serialize(new {kind="character",id=pair.Key,
                                character.definedName,character.canTalkTo,active=character.gameObject.activeInHierarchy,
                                scene=character.gameObject.scene.buildIndex,x=p.x,y=p.y,z=p.z}));
                        }
                        var trigger = obj.linkedObject.TryCast<StoryTrigger>();
                        if (trigger == null) continue;
                        rows.Add(JsonSerializer.Serialize(new { kind = "trigger", id = pair.Key,
                            unlockedId = trigger.unlocked == null ? -1 : trigger.unlocked.Id(),
                            unlocked = trigger.unlocked != null && trigger.unlocked.value,
                            progressId = trigger.progress == null ? -1 : trigger.progress.Id(),
                            progress = trigger.progress == null ? 0 : trigger.progress.value }));
                    }
                if (g.questTexts != null)
                    foreach (var pair in g.questTexts)
                    {
                        var q = pair.Value;
                        rows.Add(JsonSerializer.Serialize(new { kind = "quest", id = pair.Key, q.enabled,
                            q.progressCurrent, q.progressMax, marker = (int)q.markerType,
                            x = q.markerPos.x, y = q.markerPos.y, z = q.markerPos.z, scene = q.markerInScene }));
                    }
                if (g.Dialogs != null)
                    for (int i = 0; i < g.Dialogs.Count; i++)
                    {
                        var d = g.Dialogs[i];
                        if (d == null) continue;
                        rows.Add(JsonSerializer.Serialize(new { kind = "dialog", id = d.id, d.completed, d.isPlaying,
                            actor=d.activatedByCharacter==null ? "" : d.activatedByCharacter.name,
                            d.dependencyName,d.selfTrigger,d.repeatable,d.inCar,d.notInCar }));
                        if (d.lines == null) continue;
                        for (int n = 0; n < d.lines.Count; n++)
                        {
                            var line = d.lines[n];
                            if (line != null) rows.Add(JsonSerializer.Serialize(new { kind = "line", dialog = d.id,
                                index = n, id = line.lineID, line.played,line.moneyReward,line.itemGive,
                                line.itemAmount,line.questActivate,line.questDeactivate,line.triggerName,line.voiceFileID }));
                        }
                    }
                if (g.inventory != null)
                    for (int i = 0; i < g.inventory.Length; i++)
                    {
                        var item = g.inventory[i];
                        if (item != null) rows.Add(JsonSerializer.Serialize(new { kind = "item", key = item.name,
                            item.amount, item.totalAmountAvailable, item.isEquipped, slot = (int)item.equipSlot }));
                    }
                rows.Sort(StringComparer.Ordinal);
                string canonical = string.Join("\n", rows);
                string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
                string header = JsonSerializer.Serialize(new { utc = DateTime.UtcNow, scene = SceneManager.GetActiveScene().buildIndex,
                    money = g.money, digest, records = rows.Count, missingObjects,
                    note = "Read-only native state. Does not demonstrate presentation or authority synchronization." });
                File.WriteAllText(path, header + "\n" + canonical, new UTF8Encoding(false));
                log.LogInfo("campaign probe: " + rows.Count + " records; money=" + g.money + "; digest=" + digest);
            }
            catch (Exception ex) { log.LogWarning("campaign probe failed: " + ex); }
        }
    }
}
