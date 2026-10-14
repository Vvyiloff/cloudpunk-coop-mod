using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using InControl;
using UnityEngine;

namespace CloudpunkCoop.UI
{
    // Mask native consumer scopes, rather than patching trivial getters which
    // IL2CPP inlines. Restore even when the native consumer throws. No global pause.
    internal static class CoopMenuInput
    {
        private static bool _installed;
        private static readonly List<PropertyInfo> Axes=new();
        internal sealed class Saved
        {
            internal readonly List<(OneAxisInputControl Control,InputControlState Current,InputControlState Previous)> One=new();
            internal readonly List<(TwoAxisInputControl Control,Vector2 Current,Vector2 Previous,bool State,bool Last)> Two=new();
        }
        internal static void Install()
        {
            if(_installed)return;
            foreach(var property in typeof(PlayerActions).GetProperties(BindingFlags.Public|BindingFlags.Instance))
                if(property.GetIndexParameters().Length==0 &&
                    (typeof(OneAxisInputControl).IsAssignableFrom(property.PropertyType) ||
                     typeof(TwoAxisInputControl).IsAssignableFrom(property.PropertyType))) Axes.Add(property);
            var harmony=new Harmony("dev.andre.cloudpunk-coop.menu-input");
            foreach(var type in new[]{typeof(Player),typeof(PlayerCar),typeof(Global)})
                harmony.Patch(AccessTools.Method(type,"Update"),
                    prefix:new HarmonyMethod(typeof(CoopMenuInput),nameof(Prefix)),
                    finalizer:new HarmonyMethod(typeof(CoopMenuInput),nameof(Finalizer)));
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.EventSystems.EventSystem),"Update"),
                prefix:new HarmonyMethod(typeof(CoopMenuInput),nameof(EventPrefix)));
            _installed=true;
        }
        private static bool EventPrefix()=>!CoopConnectionMenu.CapturesInput;
        private static void Prefix(out Saved __state)
        {
            __state=null;
            if(!CoopConnectionMenu.CapturesInput || GameInput.Instance==null || GameInput.Instance.playerActions==null)return;
            __state=new Saved();
            try
            {
                var actions=GameInput.Instance.playerActions;
                foreach(var property in Axes)
                {
                    var value=property.GetValue(actions);
                    if(value is OneAxisInputControl one)
                    {
                        __state.One.Add((one,one.thisState,one.lastState));
                        one.thisState=default;one.lastState=default;
                    }
                    else if(value is TwoAxisInputControl two)
                    {
                        __state.Two.Add((two,two.thisValue,two.lastValue,two.thisState,two.lastState));
                        two.thisValue=two.lastValue=Vector2.zero;two.thisState=two.lastState=false;
                    }
                }
            }
            catch { Restore(__state);__state=null;throw; }
        }
        private static Exception Finalizer(Exception __exception,Saved __state)
        {Restore(__state);return __exception;}
        private static void Restore(Saved saved)
        {
            if(saved==null)return;
            for(int i=saved.One.Count-1;i>=0;i--)
            {var s=saved.One[i];s.Control.thisState=s.Current;s.Control.lastState=s.Previous;}
            for(int i=saved.Two.Count-1;i>=0;i--)
            {var s=saved.Two[i];s.Control.thisValue=s.Current;s.Control.lastValue=s.Previous;s.Control.thisState=s.State;s.Control.lastState=s.Last;}
        }
    }
}
