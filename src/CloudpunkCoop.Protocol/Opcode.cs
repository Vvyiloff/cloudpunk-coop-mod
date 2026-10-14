namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Wire opcode. The numeric values are part of the protocol and must never be reused.
    /// </summary>
    public enum Opcode : byte
    {
        None = 0,
        Hello = 1,
        Welcome = 2,
        CarState = 3,
        Input = 4,
        Seat = 5,
        Ping = 6,
        Pong = 7,
        Bye = 8,
        CarVisual = 9,
        SeatRequest = 10,
        SeatState = 11,
        CarTrail = 12,
        DriverInput = 13,
        Traffic = 14,
        MobilityRequest = 15,
        MobilityState = 16,
        FootPose = 17,
        Npc = 18,
        OwnedCar = 19,
        OccupancyRequest = 20,
        OccupancyState = 21,
        VehiclePark = 22,
        ParkingSlot = 23,
        CampaignChunk = 24,
        CampaignAction = 25,
        CampaignReply = 26,
        CampaignPresentation = 27,
        RegionPresence = 28,
        PersonalClock = 29,
    }
}
