using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>UnityPlayer particle bindings verified in the installed binary. No input hooks.</summary>
    internal static class NativeParticleAccess
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float ReadScalar(IntPtr system);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void WriteScalar(IntPtr system, float value);
        private const string Main = "UnityEngine.ParticleSystem/MainModule::";
        private const string Emission = "UnityEngine.ParticleSystem/EmissionModule::";
        private static readonly ReadScalar ReadRate = IL2CPP.ResolveICall<ReadScalar>(Emission + "GetRateOverTimeMultiplier");
        private static readonly ReadScalar ReadSpeed = IL2CPP.ResolveICall<ReadScalar>(Main + "GetStartSpeedMultiplier");
        private static readonly ReadScalar ReadSize = IL2CPP.ResolveICall<ReadScalar>(Main + "GetStartSizeXMultiplier");
        private static readonly ReadScalar ReadLifetime = IL2CPP.ResolveICall<ReadScalar>(Main + "GetStartLifetimeMultiplier");
        private static readonly WriteScalar WriteRate = IL2CPP.ResolveICall<WriteScalar>(Emission + "SetRateOverTimeMultiplier");
        private static readonly WriteScalar WriteSpeed = IL2CPP.ResolveICall<WriteScalar>(Main + "SetStartSpeedMultiplier");
        private static readonly WriteScalar WriteSize = IL2CPP.ResolveICall<WriteScalar>(Main + "SetStartSizeXMultiplier");
        private static readonly WriteScalar WriteLifetime = IL2CPP.ResolveICall<WriteScalar>(Main + "SetStartLifetimeMultiplier");

        public static void Read(ParticleSystem particle, out float rate, out float speed,
            out float size, out float lifetime)
        {
            IntPtr pointer = IL2CPP.Il2CppObjectBaseToPtrNotNull(particle);
            rate = ReadRate(pointer);
            speed = ReadSpeed(pointer);
            size = ReadSize(pointer);
            lifetime = ReadLifetime(pointer);
        }

        public static void Write(ParticleSystem particle, float rate, float speed, float size, float lifetime)
        {
            IntPtr pointer = IL2CPP.Il2CppObjectBaseToPtrNotNull(particle);
            WriteRate(pointer, rate);
            WriteSpeed(pointer, speed);
            WriteSize(pointer, size);
            WriteLifetime(pointer, lifetime);
        }
    }
}
