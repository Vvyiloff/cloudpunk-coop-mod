using System;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>Guest-only audio/portrait rendering. Never starts native dialog routines.</summary>
    internal static class CampaignPresentation
    {
        private static GameObject _root;
        private static AudioSource _audio;
        private static string _key="";
        private static bool _started;
        private static Sprite _portrait;
        private static string _portraitKey="";
        public static long ClipsPlayed {get;private set;}
        public static long MissingClips {get;private set;}
        public static long PortraitFrames {get;private set;}
        public static string LastError {get;private set;}="";
        private static readonly BepInEx.Logging.ManualLogSource Log=BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Dialogue");
        private static int _warnings;

        public static void Receive(CampaignConversation conversation)
        {
            try { ReceiveCore(conversation); }
            catch(Exception ex){Report("audio/portrait preparation",ex);}
        }
        private static void ReceiveCore(CampaignConversation conversation)
        {
            if(!CampaignReplication.GuardGuest) return;
            if(!conversation.Active){Stop();return;}
            conversation.Portrait ??="";conversation.Voice ??="";
            if(conversation.Portrait!=_portraitKey)
            {
                _portraitKey=conversation.Portrait;_portrait=null;
                if(Global.Instance.characters!=null && Global.Instance.characters.TryGetValue(_portraitKey,out var definition) && definition!=null)
                    _portrait=definition.portrait;
            }
            if(conversation.Voice.Length==0){if(_audio!=null)_audio.Stop();return;}
            if(!ValidVoice(conversation.Voice)) throw new InvalidOperationException("Unexpected native voice resource path");
            if(_root==null)
            {
                _root=new GameObject("CloudpunkCoop_ConversationAudio");
                _audio=_root.AddComponent<AudioSource>();_audio.playOnAwake=false;_audio.loop=false;_audio.spatialBlend=0;
            }
            var native=Global.Instance.dialogUI?.voiceoverSource;
            if(native!=null){NativeDialogueMixer.Copy(native,_audio);_audio.volume=native.volume;_audio.pitch=native.pitch;}
            string key=conversation.Generation+":"+conversation.LineIndex+":"+conversation.Voice;
            if(_key!=key)
            {
                _key=key;_started=false;_audio.Stop();_audio.clip=Resources.Load<AudioClip>(conversation.Voice);
                if(_audio.clip==null){MissingClips++;return;}
                if(conversation.VoicePlaying){Seek(conversation.VoiceTime);_audio.Play();_started=true;ClipsPlayed++;}
            }
            if(_audio.clip==null) return;
            if(!conversation.VoicePlaying){if(_audio.isPlaying)_audio.Pause();return;}
            if(!_audio.isPlaying){Seek(conversation.VoiceTime);if(_started)_audio.UnPause();else{_audio.Play();_started=true;ClipsPlayed++;}}
            else if(Math.Abs(_audio.time-conversation.VoiceTime)>0.3f) Seek(conversation.VoiceTime);
        }
        private static bool ValidVoice(string path)=>path.Length<=256 && path.IndexOf("..",StringComparison.Ordinal)<0 &&
            (path.StartsWith("Voiceover/InCar/",StringComparison.Ordinal) || path.StartsWith("Voiceover/Outdoor/",StringComparison.Ordinal) ||
             path.StartsWith("Voiceover/Com/",StringComparison.Ordinal) || path.StartsWith("Voiceover/CORA/",StringComparison.Ordinal));
        private static void Seek(float time)
        {if(_audio.clip!=null && _audio.clip.length>0.02f)_audio.time=Mathf.Clamp(time,0,_audio.clip.length-0.02f);}
        public static void DrawPortrait(Rect destination)
        {
            try { DrawPortraitCore(destination); }
            catch(Exception ex){Report("portrait drawing",ex);}
        }
        private static void DrawPortraitCore(Rect destination)
        {
            if(_portrait==null || _portrait.texture==null) return;
            var texture=_portrait.texture;var r=_portrait.rect;
            NativeDialoguePortrait.Draw(destination,texture,new Rect(r.x/texture.width,r.y/texture.height,r.width/texture.width,r.height/texture.height));
            if(Event.current!=null && Event.current.type==EventType.Repaint) PortraitFrames++;
        }
        private static void Report(string channel,Exception ex)
        {
            LastError=channel+": "+ex.GetType().Name;
            if(_warnings++<3) Log.LogWarning("Dialogue presentation deferred, campaign authority remains active: "+LastError+" "+ex.Message);
        }
        public static void Stop()
        {
            try {if(_audio!=null)_audio.Stop();if(_root!=null)UnityEngine.Object.Destroy(_root);}
            catch(Exception ex){Report("presentation cleanup",ex);}
            _root=null;_audio=null;_key="";_portraitKey="";_portrait=null;
            NativeDialoguePortrait.Clear();LastError="";_warnings=0;
        }
    }
}
