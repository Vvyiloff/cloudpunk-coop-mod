using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BepInEx.Logging;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using HarmonyLib;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    // Save hashes are revision aliases, never campaign IDs. No native save bytes
    // are changed. Unknown imported revisions get a separate persistent identity.
    internal static class HostSaveIdentity
    {
        private static readonly ManualLogSource Log=BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Save Identity");
        private static readonly Dictionary<IntPtr,Load> Loads=new();
        private static CampaignSaveIdentities _registry;
        private static string _bound="";
        private static bool _installed,_newGamePending;
        private static long _loadGeneration;
        [ThreadStatic] private static Load _loadScope;
        [ThreadStatic] private static bool _saveScope;
        internal static string LastError {get;private set;}="";
        internal static string CampaignId=>_bound;
        internal static long CommittedSaves {get;private set;}
        internal static bool Ready=>!Excluded && _bound.Length!=0 && !_newGamePending && LastError.Length==0;
        private static bool Excluded=>CoopSession.Role==CoopRole.Guest || CampaignReplication.GuestSaveIsolated;
        private sealed class Load
        {
            internal Global._LoadSaveGameRoutine_d__335 Routine;
            internal string Hash="";
            internal long Generation;
            internal bool Restored,Cancelled;
        }
        private sealed class Document
        {
            public Document() { }
            public int Version {get;set;}=1;
            public CampaignSaveAlias[] Aliases {get;set;}=Array.Empty<CampaignSaveAlias>();
        }
        internal static void Install()
        {
            if(_installed)return;
            var h=new Harmony("dev.andre.cloudpunk-coop.save-identity");
            Patch(typeof(Global._LoadSaveGameRoutine_d__335),"MoveNext",nameof(LoadPrefix),nameof(LoadPostfix),nameof(LoadFinalizer));
            Patch(typeof(Global._LoadSaveGameRoutine_d__335),"System_IDisposable_Dispose",nameof(CancelPrefix));
            Patch(typeof(ES2Reader),"Create",nameof(ReaderPrefix),parameters:new[]{typeof(string)});
            Patch(typeof(Global),"UpdateStats",postfix:nameof(StatsPostfix));
            Patch(typeof(Global),"Save",nameof(SavePrefix),finalizer:nameof(SaveFinalizer));
            Patch(typeof(Il2CppSystem.IO.File),"Copy",postfix:nameof(CopyPostfix),parameters:new[]{typeof(string),typeof(string),typeof(bool)});
            Patch(typeof(Global),"StartNewGame",nameof(NewGamePrefix),finalizer:nameof(NewGameFinalizer));
            _installed=true;
            void Patch(Type type,string name,string prefix=null,string postfix=null,string finalizer=null,Type[] parameters=null)
            {
                var method=parameters==null ? AccessTools.Method(type,name) : AccessTools.Method(type,name,parameters);
                if(method==null)throw new MissingMethodException(type.FullName,name);
                const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
                HarmonyMethod Hook(string hook)=>hook==null ? null : new HarmonyMethod(typeof(HostSaveIdentity).GetMethod(hook,flags));
                h.Patch(method,prefix:Hook(prefix),postfix:Hook(postfix),finalizer:Hook(finalizer));
            }
        }
        private static void LoadPrefix(Global._LoadSaveGameRoutine_d__335 __instance,out Load __state)
        {
            __state=_loadScope;
            if(Excluded)return;
            if(!Loads.TryGetValue(__instance.Pointer,out var pending))
            {
                if(__instance.__1__state!=0)return;
                _bound="";_newGamePending=false;LastError="";
                CoopSession.HostCampaignChanging();
                if(Loads.Count>=4){Error(new InvalidOperationException("Too many concurrent save loads"));return;}
                pending=new Load {Routine=__instance,Generation=++_loadGeneration};Loads.Add(__instance.Pointer,pending);
            }
            _loadScope=pending;
        }
        private static void ReaderPrefix(string identifier)
        {
            var pending=_loadScope;
            if(Excluded || pending==null || pending.Generation!=_loadGeneration || pending.Cancelled || pending.Hash.Length!=0)return;
            try
            {
                string expected=SavePath(pending.Routine.__4__this.SaveGame);
                string actual=SavePath(identifier);
                if(!string.Equals(expected,actual,StringComparison.OrdinalIgnoreCase))return;
                pending.Hash=Hash(actual);
            }
            catch(Exception ex){pending.Cancelled=true;Error(ex);}
        }
        private static void StatsPostfix()
        {
            var pending=_loadScope;
            if(pending!=null && pending.Generation==_loadGeneration && !pending.Cancelled && pending.Hash.Length!=0 && pending.Routine._reader_5__5==null)
                pending.Restored=true;
        }
        private static void LoadPostfix(Global._LoadSaveGameRoutine_d__335 __instance,bool __result)
        {
            if(__result || !Loads.Remove(__instance.Pointer,out var pending))return;
            if(Excluded || pending.Generation!=_loadGeneration || pending.Cancelled || !pending.Restored || pending.Hash.Length==0)return;
            try
            {
                var candidate=ReadRegistry();
                string id=candidate.Resolve(pending.Hash);
                Persist(candidate);
                _registry=candidate;_bound=id;_newGamePending=false;LastError="";
                Log.LogInfo("Loaded save revision bound to a persistent co-op campaign");
            }
            catch(Exception ex){_bound="";Error(ex);}
        }
        private static Exception LoadFinalizer(Global._LoadSaveGameRoutine_d__335 __instance,Exception __exception,Load __state)
        {
            _loadScope=__state;
            if(__exception!=null && Loads.Remove(__instance.Pointer,out var pending))
            {pending.Cancelled=true;_bound="";Error(__exception);}
            return __exception;
        }
        private static void CancelPrefix(Global._LoadSaveGameRoutine_d__335 __instance)
        {
            if(Loads.Remove(__instance.Pointer,out var pending))pending.Cancelled=true;
        }
        private static void NewGamePrefix()
        {
            if(Excluded)return;
            foreach(var pending in Loads.Values)pending.Cancelled=true;
            ++_loadGeneration;
            Loads.Clear();_bound=Guid.NewGuid().ToString("N");_newGamePending=true;LastError="";
            CoopSession.HostCampaignChanging();
        }
        private static Exception NewGameFinalizer(Exception __exception)
        {if(__exception!=null && !Excluded){_bound="";Error(__exception);}return __exception;}
        private static void SavePrefix(out bool __state)
        {__state=_saveScope;_saveScope=!Excluded;}
        private static Exception SaveFinalizer(Exception __exception,bool __state)
        {_saveScope=__state;return __exception;}
        private static void CopyPostfix(string sourceFileName,string destFileName,bool overwrite)
        {
            if(!_saveScope || Excluded || !overwrite || Global.Instance==null || _bound.Length==0)return;
            try
            {
                if(!string.Equals(Path.GetFullPath(sourceFileName),SavePath("temp.sav"),StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(Path.GetFullPath(destFileName),SavePath(Global.Instance.SaveGame),StringComparison.OrdinalIgnoreCase))return;
                var candidate=ReadRegistry();
                string hash=Hash(destFileName),id=_bound;
                if(_newGamePending)
                {
                    foreach(var alias in candidate.Export())
                        if(string.Equals(alias.RevisionHash,hash,StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("New campaign save revision is indistinguishable from an existing campaign");
                    id=candidate.Resolve(hash);
                }
                else candidate.Associate(hash,id);
                Persist(candidate);_registry=candidate;_bound=id;_newGamePending=false;LastError="";
                CommittedSaves++;
                Log.LogInfo("Committed native save revision associated with the current co-op campaign");
            }
            catch(Exception ex){Error(ex);}
        }
        private static string SavePath(string filename)
        {
            if(string.IsNullOrWhiteSpace(filename))throw new InvalidDataException("Native save filename unavailable");
            string root=Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string path=Path.GetFullPath(Path.IsPathFullyQualified(filename) ? filename : Path.Combine(root,filename));
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Native save path escaped its directory");
            return path;
        }
        private static string RegistryPath
        {
            get
            {
                bool test=string.Equals(Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST"),"true",StringComparison.OrdinalIgnoreCase);
                string root=test ? Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST_DIR") : Path.Combine(BepInEx.Paths.ConfigPath,"CloudpunkCoop");
                if(string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))throw new InvalidDataException("Save identity registry directory unavailable");
                return Path.Combine(root,"campaign-save-identities.json");
            }
        }
        private static string Hash(string path)
        {
            using var stream=File.OpenRead(path);
            if(stream.Length<=0 || stream.Length>32*1024*1024)throw new InvalidDataException("Save identity input size unsupported");
            using var algorithm=SHA256.Create();
            return Convert.ToHexString(algorithm.ComputeHash(stream));
        }
        private static CampaignSaveIdentities ReadRegistry()
        {
            if(_registry!=null)return new CampaignSaveIdentities(_registry.Export());
            string path=RegistryPath;
            if(!File.Exists(path))return new CampaignSaveIdentities();
            if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Save identity registry exceeds limit");
            var document=JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if(document==null || document.Version!=1 || document.Aliases==null)throw new InvalidDataException("Invalid save identity registry");
            return new CampaignSaveIdentities(document.Aliases);
        }
        private static void Persist(CampaignSaveIdentities candidate)
        {
            string path=RegistryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+".tmp";
            File.WriteAllText(temporary,JsonSerializer.Serialize(new Document {Aliases=candidate.Export()}));
            File.Move(temporary,path,true);
        }
        private static void Error(Exception ex)
        {
            LastError="Campaign save identity unavailable: "+ex.GetType().Name;
            Log.LogWarning(LastError+": "+ex.Message);
        }
    }
}
