using System;
using System.Collections.Generic;
namespace CloudpunkCoop.Protocol
{
    public sealed class ParkingReservations
    {
        private sealed class Reservation { public byte Vehicle; public double Until; public bool Parked; }
        private readonly Dictionary<string,Reservation> _slots=new(StringComparer.Ordinal);
        public bool IsReservedByOther(string slot,byte vehicle,double now)=>
            _slots.TryGetValue(slot,out var r) && r.Vehicle!=vehicle && (r.Parked || now<r.Until);
        public bool TryReserve(string slot,byte vehicle,double now,bool holdUntilRelease=false)
        {
            if(vehicle<1 || vehicle>2 || string.IsNullOrEmpty(slot) || !double.IsFinite(now)) return false;
            if(_slots.TryGetValue(slot,out var r) && (r.Parked || now<r.Until)) return r.Vehicle==vehicle;
            _slots[slot]=new Reservation{Vehicle=vehicle,Until=holdUntilRelease ? double.PositiveInfinity : now+10};
            return true;
        }
        public void Observe(byte vehicle,string? parkedSlot,double now)
        {
            var remove=new List<string>();
            foreach(var pair in _slots)
                if(pair.Value.Vehicle==vehicle && pair.Value.Parked && pair.Key!=parkedSlot) remove.Add(pair.Key);
            foreach(var key in remove) _slots.Remove(key);
            if(parkedSlot==null) return;
            if(!_slots.TryGetValue(parkedSlot,out var r) || (!r.Parked && now>=r.Until))
                _slots[parkedSlot]=r=new Reservation{Vehicle=vehicle};
            if(r.Vehicle==vehicle) {r.Parked=true; r.Until=double.PositiveInfinity;}
        }
        public void Release(byte vehicle)
        {
            var remove=new List<string>();
            foreach(var pair in _slots) if(pair.Value.Vehicle==vehicle) remove.Add(pair.Key);
            foreach(var key in remove) _slots.Remove(key);
        }
        public void Clear()=>_slots.Clear();
    }
}
