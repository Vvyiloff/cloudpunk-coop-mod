using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    // Exact binding names read from the installed AudioSource interop wrapper. Its
    // AudioMixerGroup return wrapper lacks an IntPtr constructor; use native pointers.
    internal static class NativeDialogueMixer
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Read(IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Write(IntPtr source,IntPtr mixer);
        private static readonly Read Get=IL2CPP.ResolveICall<Read>("UnityEngine.AudioSource::get_outputAudioMixerGroup");
        private static readonly Write Set=IL2CPP.ResolveICall<Write>("UnityEngine.AudioSource::set_outputAudioMixerGroup");
        internal static void Copy(AudioSource source,AudioSource target)=>Set(target.Pointer,Get(source.Pointer));
    }

    // GUI.DrawTextureWithTexCoords in this unstripped build casts a managed null
    // reference's address to a boxed value type. Allocate a real zeroed IL2CPP box
    // and pass its unboxed data to the same engine internal call instead.
    internal static class NativeDialoguePortrait
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DrawCall(IntPtr arguments);
        private static readonly DrawCall DrawTexture=IL2CPP.ResolveICall<DrawCall>("UnityEngine.Graphics::Internal_DrawTexture");
        private static Internal_DrawTextureArguments _arguments;
        private static unsafe Internal_DrawTextureArguments Create()
        {
            Il2CppType.Of<Internal_DrawTextureArguments>();
            IntPtr type=Il2CppClassPointerStore<Internal_DrawTextureArguments>.NativeClassPtr;
            uint alignment=0;
            int size=(int)IL2CPP.il2cpp_class_value_size(type,ref alignment);
            if(size<=0 || size>1024) throw new InvalidOperationException("Unsupported native portrait argument layout");
            var zero=new byte[size];
            fixed(byte* data=zero)
                return new Internal_DrawTextureArguments(IL2CPP.il2cpp_value_box(type,(IntPtr)data));
        }
        internal static void Draw(Rect destination,Texture texture,Rect source)
        {
            if(Event.current==null || Event.current.type!=EventType.Repaint) return;
            _arguments ??=Create();
            _arguments.texture=texture;_arguments.mat=GUI.blendMaterial;
            _arguments.screenRect=destination;_arguments.sourceRect=source;_arguments.color=GUI.color;
            _arguments.leftBorder=_arguments.rightBorder=_arguments.topBorder=_arguments.bottomBorder=0;
            _arguments.borderWidths=Vector4.zero;_arguments.cornerRadiuses=Vector4.zero;_arguments.pass=0;
            DrawTexture(IL2CPP.il2cpp_object_unbox(_arguments.Pointer));
        }
        internal static void Clear()=>_arguments=null;
    }
}
