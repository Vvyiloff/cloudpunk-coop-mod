using System;
using BepInEx.Logging;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>Personal native clock and absolute guest effects. No UnityEvent replay.</summary>
    internal static class CoopPersonalEffects
    {
        private static readonly ManualLogSource Log=BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Effects");
        private static PersonalClockTracker _hostClock;
        private static long _boundSession,_seenUse;
        private static double _localElapsed;
        private static double _lastTickStart;
        private static long _eligibleTicks;
        private static uint _sequence;
        private static float _sendAt;
        private static bool _haveUseBaseline,_clockFailed;
        public static long AudioPlays {get;private set;}
        public static string LastError {get;private set;}="";
        public static bool HostClockFresh=>!_clockFailed && _hostClock!=null && _hostClock.Fresh(Time.realtimeSinceStartup);
        public static double HostElapsed=>_hostClock?.AcceptedElapsed ?? 0;
        public static long HostTicks=>_hostClock?.AcceptedTicks ?? 0;
        internal static double LocalElapsed=>_localElapsed;

        public static void Reset()
        {
            _hostClock=null;_boundSession=0;_localElapsed=0;_sequence=0;_sendAt=0;
            _lastTickStart=0;_eligibleTicks=0;
            _haveUseBaseline=false;_seenUse=0;_clockFailed=false;LastError="";
        }
        private static void Bind()
        {
            long session=CampaignReplication.SessionId;
            if(session<=0 || _boundSession==session)return;
            Reset();_boundSession=session;
            if(CoopSession.Role==CoopRole.Host)_hostClock=new PersonalClockTracker(session);
        }
        public static void BeginSnapshot(long session)
        {
            if(CoopSession.Role!=CoopRole.Guest || session==_boundSession)return;
            if(session<=0)throw new InvalidOperationException("Invalid personal effect session");
            Reset();_boundSession=session;
        }
        public static void RecordNativeTick(float scaledDelta,bool eligible)
        {
            if(CoopSession.Role!=CoopRole.Guest || !CoopSession.PeerOnline)return;
            Bind();
            if(_boundSession<=0 || !eligible || !float.IsFinite(scaledDelta) || scaledDelta<0 || scaledDelta>4)return;
            _lastTickStart=_localElapsed;_localElapsed+=scaledDelta;_eligibleTicks++;
        }
        public static void Tick(float now)
        {
            if(!CoopSession.PeerOnline)return;
            Bind();
            if(CoopSession.Role==CoopRole.Host)CoopInventory.FlushPersonalEffects(now);
            if(CoopSession.Role!=CoopRole.Guest || _boundSession<=0 || now<_sendAt)return;
            SendClock(now);
        }
        internal static bool SendClock(float now)
        {
            Bind();
            if(CoopSession.Role!=CoopRole.Guest || !CoopSession.PeerOnline || _boundSession<=0)return false;
            _sendAt=now+0.2f;
            return CoopSession.SendMessage(new PersonalClockMessage(_sequence++,_boundSession,_localElapsed,_eligibleTicks,_lastTickStart));
        }
        public static void Receive(PersonalClockMessage message)
        {
            if(CoopSession.Role!=CoopRole.Host || !CoopSession.PeerOnline || _clockFailed)return;
            Bind();
            if(_hostClock==null)return;
            bool baseline=_hostClock.HasSample;long previousTicks=_hostClock.AcceptedTicks;
            if(!_hostClock.Receive(message,Time.realtimeSinceStartup,out double delta))return;
            bool hadTicks=baseline && message.EligibleTicks>previousTicks;
            try {CoopInventory.AdvancePersonalClock(delta,hadTicks ? Math.Min(delta,message.ElapsedScaledSeconds-message.LastTickStart):0,hadTicks);}
            catch(Exception ex){_clockFailed=true;LastError="Personal clock failed: "+ex.GetType().Name;Log.LogWarning(LastError);}
        }
        private static PersonalConsumableEffects Projected(CoopInventoryState state)
        {
            if(state.Effects==null || !double.IsFinite(state.EffectsClock) || state.EffectsClock<0)
                throw new InvalidOperationException("Invalid personal effect projection");
            // The acknowledged cumulative clock and this client's clock share one session.
            double lag=Math.Max(0,_localElapsed-state.EffectsClock);
            if(lag>PersonalConsumableEffects.MaxScaledDeltaTime)throw new InvalidOperationException("Personal effect snapshot too old");
            bool hadTicks=_eligibleTicks>state.EffectsTicks;
            return state.Effects.AdvanceTicks((float)lag,hadTicks ? (float)Math.Min(lag,_localElapsed-_lastTickStart):0,hadTicks);
        }
        public static void Prepare(CoopInventoryState state,CampaignFieldTransaction assignments)
        {
            if(CoopSession.Role!=CoopRole.Guest)return;
            var effect=Projected(state);var g=Global.Instance;
            assignments.Add(v=>g.foodCooldown=v,g.foodCooldown,effect.Food);
            assignments.Add(v=>g.drinkCooldown=v,g.drinkCooldown,effect.Drink);
            assignments.Add(v=>g.drugCooldown=v,g.drugCooldown,effect.Drug);
            assignments.Add(v=>g.speedGainTimeLeft=v,g.speedGainTimeLeft,effect.SpeedLeft);
            assignments.Add(v=>g.alcoholTimeLeft=v,g.alcoholTimeLeft,effect.AlcoholLeft);
            assignments.Add(v=>g.alcoholTimePassed=v,g.alcoholTimePassed,effect.AlcoholPassed);
        }
        public static void Present(CoopInventoryState state)
        {
            if(CoopSession.Role!=CoopRole.Guest)return;
            long previous=_seenUse;
            bool play=_haveUseBaseline && state.UseSerial>previous;
            _haveUseBaseline=true;_seenUse=Math.Max(_seenUse,state.UseSerial);
            try
            {
                var g=Global.Instance;var ui=g.inventoryUI;
                if(ui!=null)
                {
                    if(ui.effectSpeedGain!=null)ui.effectSpeedGain.SetActive(g.speedGainTimeLeft>0);
                    if(ui.effectVisual!=null)ui.effectVisual.SetActive(g.alcoholTimeLeft>0 || g.pheromonesTimeLeft>0);
                }
                if(!play)return;
                foreach(var use in state.Uses)
                {
                    if(use.Serial<=previous)continue;
                // Mark serial observed before fallible presentation: retry snapshots never
                // replay a consume event or its sound. Read only the authored audio argument.
                foreach(var item in g.inventory)
                {
                    if(item==null || item.name!=use.Item || item.useEvent?.m_PersistentCalls==null)continue;
                    var calls=item.useEvent.m_PersistentCalls;
                    for(int i=0;i<calls.Count;i++)
                    {
                        var call=calls.GetListener(i);
                        if(call==null || call.m_MethodName!="PlayOneShot")continue;
                        var source=call.target?.TryCast<AudioSource>();
                        var clip=call.m_Arguments?.m_ObjectArgument?.TryCast<AudioClip>();
                        var global=call.target?.TryCast<Global>();
                        if(clip!=null && global!=null && global.Pointer==g.Pointer){global.PlayOneShot(clip);AudioPlays++;}
                        else if(source!=null && clip!=null){source.PlayOneShot(clip);AudioPlays++;}
                    }
                    break;
                }
                }
            }
            catch(Exception ex){LastError="Personal effect presentation: "+ex.GetType().Name;Log.LogWarning(LastError);}
        }
    }
}
