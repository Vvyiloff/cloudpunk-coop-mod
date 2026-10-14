using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CloudpunkCoop.Game
{
    internal static class CoopPerformance
    {
        public enum Stage { Network, Snapshot, NpcScan, NpcCapture, Traffic, Presentation, Collision, Parking, Campaign }
        private sealed class Counter {public long Count;public double Sum,Max,Last;}
        private static readonly Counter[] Counters=Create();
        private static readonly float[] Frames=new float[300];
        private static int _frameIndex,_frameCount;
        private static Counter[] Create(){var c=new Counter[Enum.GetValues<Stage>().Length];for(int i=0;i<c.Length;i++)c[i]=new Counter();return c;}
        public readonly struct Scope : IDisposable
        {
            private readonly Stage _stage;
            private readonly long _start;
            public Scope(Stage stage){_stage=stage;_start=Stopwatch.GetTimestamp();}
            public void Dispose()
            {
                double ms=(Stopwatch.GetTimestamp()-_start)*1000.0/Stopwatch.Frequency;
                var c=Counters[(int)_stage];c.Count++;c.Sum+=ms;c.Max=Math.Max(c.Max,ms);c.Last=ms;
            }
        }
        public static Scope Measure(Stage stage)=>new(stage);
        public static void Frame(float seconds)
        {
            if(!float.IsFinite(seconds) || seconds<=0)return;
            Frames[_frameIndex]=seconds*1000;_frameIndex=(_frameIndex+1)%Frames.Length;_frameCount=Math.Min(_frameCount+1,Frames.Length);
        }
        public static string Diagnostics()
        {
            var b=new StringBuilder();
            if(_frameCount>0)
            {
                var sorted=new float[_frameCount];Array.Copy(Frames,sorted,_frameCount);Array.Sort(sorted);
                double total=0;foreach(float f in sorted)total+=f;
                b.Append(FormattableString.Invariant($"frameAvgMs={total/_frameCount:0.00};frameP95Ms={sorted[(int)((_frameCount-1)*0.95)]:0.00};frameMaxMs={sorted[_frameCount-1]:0.00};"));
            }
            for(int i=0;i<Counters.Length;i++)
            {
                var c=Counters[i];
                b.Append("perf").Append(((Stage)i).ToString()).Append('=')
                    .Append((c.Count==0 ? 0 : c.Sum/c.Count).ToString("0.00",CultureInfo.InvariantCulture)).Append('/')
                    .Append(c.Max.ToString("0.00",CultureInfo.InvariantCulture)).Append('/')
                    .Append(c.Last.ToString("0.00",CultureInfo.InvariantCulture)).Append('/').Append(c.Count).Append(';');
            }
            return b.ToString();
        }
    }
}
