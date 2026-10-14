using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>A walking owner's travel must never relabel a parked vehicle's region.</summary>
    public sealed class ParkedVehicleRegion
    {
        public OwnedCarMessage? Last { get; private set; }
        public void Observe(OwnedCarMessage vehicle)
        {
            if(vehicle==null) throw new ArgumentNullException(nameof(vehicle));
            Last=vehicle;
        }
        public bool IsAway(int playerScene, bool onFoot) => onFoot && Last!=null && Last.Parked && Last.Scene!=playerScene;
        public bool TryCaptureAway(int playerScene, bool onFoot, uint sequence, out OwnedCarMessage? vehicle)
        {
            vehicle=null;
            if(!IsAway(playerScene,onFoot)) return false;
            var previous=Last!;var p=previous.Pose;
            // Stationary persisted pose with a fresh transport sequence. No prediction or
            // native simulation in an unloaded region, and no stale velocity extrapolation.
            var pose=new CarStateMessage(p.PositionX,p.PositionY,p.PositionZ,
                p.RotationX,p.RotationY,p.RotationZ,p.RotationW,0,0,0,0,0,0,
                p.SteerAngle,p.DamageLevel,sequence);
            vehicle=new OwnedCarMessage(previous.Vehicle,previous.Scene,true,pose);
            return true;
        }
        public void Reset()=>Last=null;
    }
}
