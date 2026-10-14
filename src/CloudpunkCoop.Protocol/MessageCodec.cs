using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Turns messages into payload bytes and back. The layout is fixed and explicit:
    /// the first byte is the <see cref="Opcode"/>, followed by the fields of that message
    /// written in declaration order with <see cref="ProtocolWriter"/>.
    ///
    /// A payload must contain exactly one message and no trailing bytes. Stricter layouts
    /// catch mismatched builds immediately instead of silently misreading fields.
    /// </summary>
    public static class MessageCodec
    {
        /// <summary>
        /// Exact payload size in bytes for a message, excluding the frame length prefix.
        /// Lets a caller size a writer buffer up front.
        /// </summary>
        public static int GetPayloadSize(IMessage message)
        {
            switch (message)
            {
                case RegionPresenceMessage:
                    return 10;
                case PersonalClockMessage:
                    return 37;
                case null:
                    throw new ArgumentNullException(nameof(message));
                case HelloMessage hello:
                    return 1 + StringSize(hello.ProtocolVersion) + StringSize(hello.DisplayName);
                case CampaignChunkMessage chunk:
                    return 1 + 4 + 4 + 2 + 2 + 4 + chunk.DataSpan.Length;
                case CampaignActionMessage action:
                    return 1 + 4 + 4 + 4 + 1 + StringSize(action.Target) + StringSize(action.Argument) + 4 + 8 + 8 + 4;
                case CampaignReplyMessage reply:
                    return 1 + 4 + 4 + 1 + StringSize(reply.Reason);
                case CampaignPresentationMessage presentation:
                    return 1 + 4 + 4 + 4 + 4 + presentation.DataSpan.Length;
                case WelcomeMessage welcome:
                    return 1 + 1 + StringSize(welcome.ProtocolVersion);
                case CarStateMessage _:
                    // 3 position + 4 rotation + 3 linear velocity + 3 angular velocity
                    // + steer + damage = 15 floats, plus the sequence number.
                    return 1 + (15 * 4) + 4;
                case OwnedCarMessage _:
                    return 1+1+4+1+65;
                case OccupancyRequestMessage _:
                    return 6;
                case VehicleParkMessage _:
                    return 7;
                case OccupancyStateMessage _:
                    return 14;
                case CarVisualMessage visual:
                {
                    int size = 1 + 4 + 1;
                    foreach (var particle in visual.ParticleSpan)
                    {
                        particle.Validate();
                        size += StringSize(particle.Path) + 2 + (18 * 4);
                    }
                    return size;
                }
                case InputMessage _:
                    return 1 + (5 * 4) + 4;
                case SeatRequestMessage _:
                    return 5;
                case DriverInputMessage _:
                    return 25;
                case MobilityRequestMessage _:
                    return 6;
                case MobilityStateMessage state:
                    return 1+4+2+StringSize(state.Reason)+4+16;
                case ParkingSlotMessage parking:
                    return 1+4+4+1+1+4+12+StringSize(parking.Reason);
                case FootPoseMessage foot:
                    return 1+4+4+1+16+2+StringSize(foot.Clip)+4;
                case NpcMessage npcs:
                {
                    int size = 1+4+4+4+1+1+1;
                    foreach (var n in npcs.NpcSpan)
                        size += StringSize(n.Id)+StringSize(n.Model)+StringSize(n.Clip)+2+7*4+2+4;
                    return size;
                }
                case TrafficMessage traffic:
                    return 1 + 4 + 4 + StringSize(traffic.PoolFingerprint) + 2 + traffic.VehicleSpan.Length * 31;
                case SeatStateMessage _:
                    return 12;
                case CarTrailMessage trail:
                {
                    int size = 6;
                    foreach (var t in trail.TrailSpan) size += StringSize(t.Path) + 14;
                    return size;
                }
                case SeatMessage _:
                    return 1 + 1 + 1;
                case PingMessage _:
                    return 1 + 4 + 8;
                case PongMessage _:
                    return 1 + 4 + 8;
                case ByeMessage _:
                    return 1 + 1;
                default:
                    throw new ArgumentException(
                        $"Unknown message implementation '{message.GetType().FullName}'.", nameof(message));
            }
        }

        /// <summary>Encodes a message into a freshly allocated payload array.</summary>
        public static byte[] Encode(IMessage message)
        {
            var writer = new ProtocolWriter(GetPayloadSize(message));
            Write(ref writer, message);
            return writer.ToArray();
        }

        /// <summary>
        /// Appends a message to an existing writer. Used by the transport to frame and encode
        /// in one step without an intermediate array.
        /// </summary>
        public static void Write(ref ProtocolWriter writer, IMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            switch (message)
            {
                case RegionPresenceMessage region:
                    writer.WriteByte((byte)Opcode.RegionPresence);
                    writer.WriteUInt32(region.Sequence); writer.WriteInt32(region.Scene); writer.WriteBoolean(region.Ready);
                    break;
                case PersonalClockMessage clock:
                    writer.WriteByte((byte)Opcode.PersonalClock);
                    writer.WriteUInt32(clock.Sequence);
                    writer.WriteInt64(clock.SessionId);
                    writer.WriteDouble(clock.ElapsedScaledSeconds);
                    writer.WriteInt64(clock.EligibleTicks);writer.WriteDouble(clock.LastTickStart);
                    break;
                case HelloMessage hello:
                    writer.WriteByte((byte)Opcode.Hello);
                    writer.WriteString(hello.ProtocolVersion);
                    writer.WriteString(hello.DisplayName);
                    break;

                case CampaignChunkMessage chunk:
                    writer.WriteByte((byte)Opcode.CampaignChunk);
                    writer.WriteUInt32(chunk.Revision); writer.WriteInt32(chunk.Scene);
                    writer.WriteUInt16(chunk.Index); writer.WriteUInt16(chunk.Count);
                    writer.WriteBlob(chunk.DataSpan, CampaignChunkMessage.MaxChunkDataLength);
                    break;
                case CampaignActionMessage action:
                    writer.WriteByte((byte)Opcode.CampaignAction);
                    writer.WriteUInt32(action.RequestId); writer.WriteUInt32(action.Revision);
                    writer.WriteInt32(action.Scene); writer.WriteByte(action.Kind);
                    writer.WriteString(action.Target); writer.WriteString(action.Argument); writer.WriteInt32(action.Amount);
                    writer.WriteInt64(action.SessionId); writer.WriteInt64(unchecked((long)action.Conversation));
                    writer.WriteInt32(action.LineIndex);
                    break;
                case CampaignReplyMessage reply:
                    writer.WriteByte((byte)Opcode.CampaignReply);
                    writer.WriteUInt32(reply.RequestId); writer.WriteUInt32(reply.Revision);
                    writer.WriteBoolean(reply.Accepted); writer.WriteString(reply.Reason);
                    break;
                case CampaignPresentationMessage presentation:
                    writer.WriteByte((byte)Opcode.CampaignPresentation);
                    writer.WriteUInt32(presentation.Sequence); writer.WriteUInt32(presentation.Revision);
                    writer.WriteInt32(presentation.Scene);
                    writer.WriteBlob(presentation.DataSpan, CampaignPresentationMessage.MaxDataLength);
                    break;

                case WelcomeMessage welcome:
                    writer.WriteByte((byte)Opcode.Welcome);
                    writer.WriteByte((byte)welcome.Role);
                    writer.WriteString(welcome.ProtocolVersion);
                    break;

                case CarStateMessage car:
                    writer.WriteByte((byte)Opcode.CarState);
                    writer.WriteSingle(car.PositionX);
                    writer.WriteSingle(car.PositionY);
                    writer.WriteSingle(car.PositionZ);
                    writer.WriteSingle(car.RotationX);
                    writer.WriteSingle(car.RotationY);
                    writer.WriteSingle(car.RotationZ);
                    writer.WriteSingle(car.RotationW);
                    writer.WriteSingle(car.LinearVelocityX);
                    writer.WriteSingle(car.LinearVelocityY);
                    writer.WriteSingle(car.LinearVelocityZ);
                    writer.WriteSingle(car.AngularVelocityX);
                    writer.WriteSingle(car.AngularVelocityY);
                    writer.WriteSingle(car.AngularVelocityZ);
                    writer.WriteSingle(car.SteerAngle);
                    writer.WriteSingle(car.DamageLevel);
                    writer.WriteUInt32(car.SequenceNumber);
                    break;
                case OwnedCarMessage owned:
                    writer.WriteByte((byte)Opcode.OwnedCar); writer.WriteByte(owned.Vehicle);
                    writer.WriteInt32(owned.Scene); writer.WriteBoolean(owned.Parked);
                    Write(ref writer, owned.Pose);
                    break;
                case OccupancyRequestMessage boarding:
                    writer.WriteByte((byte)Opcode.OccupancyRequest); writer.WriteUInt32(boarding.Revision);
                    writer.WriteByte(boarding.Vehicle); break;
                case VehicleParkMessage parking:
                    writer.WriteByte((byte)Opcode.VehiclePark); writer.WriteUInt32(parking.Revision);
                    writer.WriteByte(parking.Vehicle); writer.WriteByte(parking.Kind); break;
                case OccupancyStateMessage occupancy:
                    writer.WriteByte((byte)Opcode.OccupancyState); writer.WriteUInt32(occupancy.Revision);
                    writer.WriteByte(occupancy.Player1Vehicle); writer.WriteByte(occupancy.Player2Vehicle);
                    writer.WriteByte(occupancy.Driver1); writer.WriteByte(occupancy.Driver2);
                    writer.WriteByte(occupancy.Requester); writer.WriteSingle(occupancy.SecondsLeft); break;

                case CarVisualMessage visual:
                    // Validate the entire message before appending anything to the writer.
                    GetPayloadSize(visual);
                    writer.WriteByte((byte)Opcode.CarVisual);
                    writer.WriteUInt32(visual.SequenceNumber);
                    writer.WriteByte((byte)visual.ParticleSpan.Length);
                    foreach (var particle in visual.ParticleSpan)
                    {
                        writer.WriteString(particle.Path);
                        writer.WriteBoolean(particle.Active);
                        writer.WriteBoolean(particle.Playing);
                        writer.WriteSingle(particle.Rate);
                        writer.WriteSingle(particle.Speed);
                        writer.WriteSingle(particle.Size);
                        writer.WriteSingle(particle.Lifetime);
                        writer.WriteSingle(particle.ScaleX);
                        writer.WriteSingle(particle.ScaleY);
                        writer.WriteSingle(particle.ScaleZ);
                        writer.WriteSingle(particle.ColorR);
                        writer.WriteSingle(particle.ColorG);
                        writer.WriteSingle(particle.ColorB);
                        writer.WriteSingle(particle.ColorA);
                        writer.WriteSingle(particle.PositionX);
                        writer.WriteSingle(particle.PositionY);
                        writer.WriteSingle(particle.PositionZ);
                        writer.WriteSingle(particle.RotationX);
                        writer.WriteSingle(particle.RotationY);
                        writer.WriteSingle(particle.RotationZ);
                        writer.WriteSingle(particle.RotationW);
                    }
                    break;

                case InputMessage input:
                    writer.WriteByte((byte)Opcode.Input);
                    writer.WriteSingle(input.Throttle);
                    writer.WriteSingle(input.Brake);
                    writer.WriteSingle(input.Steer);
                    writer.WriteSingle(input.Ascend);
                    writer.WriteSingle(input.Boost);
                    writer.WriteUInt32(input.SequenceNumber);
                    break;

                case SeatMessage seat:
                    writer.WriteByte((byte)Opcode.Seat);
                    writer.WriteByte(seat.SeatId);
                    writer.WriteBoolean(seat.IsDriving);
                    break;

                case SeatRequestMessage request:
                    writer.WriteByte((byte)Opcode.SeatRequest);
                    writer.WriteUInt32(request.Revision);
                    break;

                case DriverInputMessage input:
                    writer.WriteByte((byte)Opcode.DriverInput);
                    writer.WriteUInt32(input.Revision); writer.WriteUInt32(input.Sequence);
                    writer.WriteSingle(input.Drive); writer.WriteSingle(input.Steer);
                    writer.WriteSingle(input.Altitude); writer.WriteSingle(input.Strafe);
                    break;

                case TrafficMessage traffic:
                    writer.WriteByte((byte)Opcode.Traffic); writer.WriteUInt32(traffic.SequenceNumber);
                    writer.WriteInt32(traffic.Scene); writer.WriteString(traffic.PoolFingerprint);
                    writer.WriteUInt16((ushort)traffic.VehicleSpan.Length);
                    foreach (var v in traffic.VehicleSpan)
                    {
                        writer.WriteUInt16(v.Id); writer.WriteBoolean(v.Active);
                        writer.WriteSingle(v.X); writer.WriteSingle(v.Y); writer.WriteSingle(v.Z);
                        writer.WriteSingle(v.Qx); writer.WriteSingle(v.Qy); writer.WriteSingle(v.Qz); writer.WriteSingle(v.Qw);
                    }
                    break;

                case MobilityRequestMessage request:
                    writer.WriteByte((byte)Opcode.MobilityRequest); writer.WriteUInt32(request.Revision);
                    writer.WriteBoolean(request.OnFoot);
                    break;
                case MobilityStateMessage state:
                    writer.WriteByte((byte)Opcode.MobilityState); writer.WriteUInt32(state.Revision);
                    writer.WriteBoolean(state.OnFoot); writer.WriteBoolean(state.Accepted); writer.WriteString(state.Reason);
                    writer.WriteInt32(state.Scene); writer.WriteSingle(state.X); writer.WriteSingle(state.Y);
                    writer.WriteSingle(state.Z); writer.WriteSingle(state.Yaw);
                    break;
                case ParkingSlotMessage parking:
                    writer.WriteByte((byte)Opcode.ParkingSlot); writer.WriteUInt32(parking.Request); writer.WriteUInt32(parking.Revision);
                    writer.WriteByte(parking.Vehicle); writer.WriteByte(parking.Kind); writer.WriteInt32(parking.Scene);
                    writer.WriteSingle(parking.X); writer.WriteSingle(parking.Y); writer.WriteSingle(parking.Z); writer.WriteString(parking.Reason);
                    break;
                case FootPoseMessage pose:
                    writer.WriteByte((byte)Opcode.FootPose); writer.WriteUInt32(pose.Sequence); writer.WriteInt32(pose.Scene);
                    writer.WriteBoolean(pose.OnFoot); writer.WriteSingle(pose.X); writer.WriteSingle(pose.Y);
                    writer.WriteSingle(pose.Z); writer.WriteSingle(pose.Yaw); writer.WriteByte(pose.Animation); writer.WriteByte(pose.Direction);
                    writer.WriteString(pose.Clip); writer.WriteInt32(pose.Frame);
                    break;

                case NpcMessage npcs:
                    writer.WriteByte((byte)Opcode.Npc); writer.WriteUInt32(npcs.Epoch); writer.WriteUInt32(npcs.Sequence);
                    writer.WriteInt32(npcs.Scene); writer.WriteByte(npcs.Chunk); writer.WriteByte(npcs.Chunks);
                    writer.WriteByte((byte)npcs.NpcSpan.Length);
                    foreach (var n in npcs.NpcSpan)
                    {
                        writer.WriteString(n.Id); writer.WriteString(n.Model); writer.WriteBoolean(n.Crowd); writer.WriteBoolean(n.Active);
                        writer.WriteSingle(n.X); writer.WriteSingle(n.Y); writer.WriteSingle(n.Z); writer.WriteSingle(n.Yaw);
                        writer.WriteSingle(n.ScaleX); writer.WriteSingle(n.ScaleY); writer.WriteSingle(n.ScaleZ);
                        writer.WriteByte(n.Animation); writer.WriteByte(n.Direction); writer.WriteString(n.Clip); writer.WriteInt32(n.Frame);
                    }
                    break;

                case CarTrailMessage trails:
                    writer.WriteByte((byte)Opcode.CarTrail);
                    writer.WriteUInt32(trails.SequenceNumber);
                    writer.WriteByte((byte)trails.TrailSpan.Length);
                    foreach (var t in trails.TrailSpan)
                    {
                        writer.WriteString(t.Path); writer.WriteBoolean(t.Active); writer.WriteBoolean(t.Emitting);
                        writer.WriteSingle(t.Time); writer.WriteSingle(t.StartWidth); writer.WriteSingle(t.EndWidth);
                    }
                    break;

                case SeatStateMessage state:
                    writer.WriteByte((byte)Opcode.SeatState);
                    writer.WriteUInt32(state.Revision);
                    writer.WriteByte(state.Driver);
                    writer.WriteByte(state.Requester);
                    writer.WriteBoolean(state.BothSeated);
                    writer.WriteSingle(state.SecondsLeft);
                    break;

                case PingMessage ping:
                    writer.WriteByte((byte)Opcode.Ping);
                    writer.WriteUInt32(ping.SequenceNumber);
                    writer.WriteInt64(ping.SentAtMilliseconds);
                    break;

                case PongMessage pong:
                    writer.WriteByte((byte)Opcode.Pong);
                    writer.WriteUInt32(pong.SequenceNumber);
                    writer.WriteInt64(pong.SentAtMilliseconds);
                    break;

                case ByeMessage bye:
                    writer.WriteByte((byte)Opcode.Bye);
                    writer.WriteByte((byte)bye.Reason);
                    break;

                default:
                    throw new ArgumentException(
                        $"Unknown message implementation '{message.GetType().FullName}'.", nameof(message));
            }
        }

        /// <summary>
        /// Decodes a payload, throwing <see cref="ProtocolException"/> if it is malformed.
        /// </summary>
        public static IMessage Decode(ReadOnlySpan<byte> payload)
        {
            if (payload.Length == 0)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.TruncatedPayload, "Payload is empty, expected at least one opcode byte.");
            }

            var reader = new ProtocolReader(payload);
            IMessage message = ReadMessage(ref reader);
            if (!reader.IsAtEnd)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.TrailingBytes,
                    $"Message decoded but {reader.Remaining} trailing bytes were left in the payload.");
            }

            return message;
        }

        /// <summary>
        /// Decodes a payload without throwing on malformed input. Returns false and sets
        /// <paramref name="message"/> to null for anything the codec cannot read.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<byte> payload, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IMessage? message)
        {
            message = null;

            try
            {
                var reader = new ProtocolReader(payload);
                message = ReadMessage(ref reader);

                if (!reader.IsAtEnd)
                {
                    // Honour the documented contract: a false return always yields null,
                    // so a caller cannot accidentally use a half-validated message.
                    message = null;
                    return false;
                }

                return true;
            }
            catch (ProtocolException)
            {
                message = null;
                return false;
            }
        }

        private static IMessage ReadMessage(ref ProtocolReader reader)
        {
            var rawOpcode = reader.ReadByte();

            switch ((Opcode)rawOpcode)
            {
                case Opcode.RegionPresence:
                    return new RegionPresenceMessage(reader.ReadUInt32(), reader.ReadInt32(), reader.ReadBoolean());
                case Opcode.PersonalClock:
                    return new PersonalClockMessage(reader.ReadUInt32(), reader.ReadInt64(), reader.ReadDouble(),reader.ReadInt64(),reader.ReadDouble());
                case Opcode.Hello:
                    return new HelloMessage(reader.ReadString(), reader.ReadString());

                case Opcode.CampaignChunk:
                {
                    uint revision = reader.ReadUInt32(); int scene = reader.ReadInt32();
                    ushort index = reader.ReadUInt16(), count = reader.ReadUInt16();
                    CampaignChunkMessage.ValidateHeader(index, count);
                    var data = reader.ReadBlob(CampaignChunkMessage.MaxChunkDataLength);
                    return new CampaignChunkMessage(revision, scene, index, count, data.ToArray());
                }
                case Opcode.CampaignAction:
                    return new CampaignActionMessage(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadInt32(),
                        reader.ReadByte(), reader.ReadString(256, true), reader.ReadString(256, true), reader.ReadInt32(),
                        reader.ReadInt64(), unchecked((ulong)reader.ReadInt64()), reader.ReadInt32());
                case Opcode.CampaignReply:
                    return new CampaignReplyMessage(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadBoolean(),
                        reader.ReadString(256, true));
                case Opcode.CampaignPresentation:
                {
                    uint sequence = reader.ReadUInt32(), revision = reader.ReadUInt32();
                    int scene = reader.ReadInt32();
                    var data = reader.ReadBlob(CampaignPresentationMessage.MaxDataLength);
                    return new CampaignPresentationMessage(sequence, revision, scene, data.ToArray());
                }

                case Opcode.Welcome:
                {
                    var role = (PeerRole)reader.ReadByte();
                    if (role != PeerRole.Host && role != PeerRole.Guest)
                    {
                        throw new ProtocolException(
                            ProtocolErrorKind.TruncatedPayload, $"Unknown peer role {(byte)role}.");
                    }

                    return new WelcomeMessage(role, reader.ReadString());
                }

                case Opcode.CarState:
                    return new CarStateMessage(
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadUInt32());
                case Opcode.OwnedCar:
                {
                    byte vehicle=reader.ReadByte(); int scene=reader.ReadInt32(); bool parked=reader.ReadBoolean();
                    if(reader.ReadByte()!=(byte)Opcode.CarState)
                        throw new ProtocolException(ProtocolErrorKind.InvalidSeatState,"Owned vehicle requires a car pose.");
                    var pose=new CarStateMessage(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),
                        reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),
                        reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),
                        reader.ReadSingle(),reader.ReadSingle(),reader.ReadUInt32());
                    return new OwnedCarMessage(vehicle,scene,parked,pose);
                }
                case Opcode.OccupancyRequest:
                    return new OccupancyRequestMessage(reader.ReadUInt32(),reader.ReadByte());
                case Opcode.VehiclePark:
                    return new VehicleParkMessage(reader.ReadUInt32(),reader.ReadByte(),reader.ReadByte());
                case Opcode.OccupancyState:
                    return new OccupancyStateMessage(reader.ReadUInt32(),reader.ReadByte(),reader.ReadByte(),
                        reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),reader.ReadSingle());

                case Opcode.CarVisual:
                {
                    uint sequence = reader.ReadUInt32();
                    int count = reader.ReadByte();
                    if (count > CarVisualMessage.MaxParticleCount)
                        throw new ProtocolException(ProtocolErrorKind.InvalidParticleCount,
                            "CarVisual allows at most 64 particles.");
                    var particles = new ParticleVisualState[count];
                    for (int i = 0; i < count; i++)
                    {
                        var particle = new ParticleVisualState(
                            reader.ReadString(ParticleVisualState.MaxPathByteLength, strictUtf8: true),
                            reader.ReadBoolean(), reader.ReadBoolean(),
                            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        particle.Validate();
                        particles[i] = particle;
                    }
                    return new CarVisualMessage(sequence, particles);
                }

                case Opcode.Input:
                    return new InputMessage(
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadUInt32());

                case Opcode.Seat:
                    return new SeatMessage(reader.ReadByte(), reader.ReadBoolean());

                case Opcode.SeatRequest:
                    return new SeatRequestMessage(reader.ReadUInt32());

                case Opcode.DriverInput:
                    return new DriverInputMessage(reader.ReadUInt32(), reader.ReadUInt32(),
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

                case Opcode.MobilityRequest:
                    return new MobilityRequestMessage(reader.ReadUInt32(), reader.ReadBoolean());
                case Opcode.MobilityState:
                    return new MobilityStateMessage(reader.ReadUInt32(), reader.ReadBoolean(), reader.ReadBoolean(),
                        reader.ReadString(), reader.ReadInt32(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                case Opcode.ParkingSlot:
                    return new ParkingSlotMessage(reader.ReadUInt32(),reader.ReadUInt32(),reader.ReadByte(),reader.ReadByte(),reader.ReadInt32(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadString());
                case Opcode.FootPose:
                    return new FootPoseMessage(reader.ReadUInt32(), reader.ReadInt32(), reader.ReadBoolean(),
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadByte(), reader.ReadByte(), reader.ReadString(), reader.ReadInt32());

                case Opcode.Npc:
                {
                    uint epoch = reader.ReadUInt32(), sequence = reader.ReadUInt32(); int scene = reader.ReadInt32();
                    byte chunk = reader.ReadByte(), chunks = reader.ReadByte(), count = reader.ReadByte();
                    if (count > NpcMessage.MaxPerChunk)
                        throw new ProtocolException(ProtocolErrorKind.InvalidNpcState, "NPC chunk count exceeds limit.");
                    var npcs = new NpcVisualState[count];
                    for (int i = 0; i < count; i++)
                        npcs[i] = new NpcVisualState(reader.ReadString(256, true), reader.ReadString(256, true),
                            reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                            reader.ReadByte(), reader.ReadByte(), reader.ReadString(256, true), reader.ReadInt32());
                    return new NpcMessage(epoch, sequence, scene, chunk, chunks, npcs);
                }

                case Opcode.Traffic:
                {
                    uint sequence = reader.ReadUInt32(); int scene = reader.ReadInt32();
                    string fingerprint = reader.ReadString(64, true);
                    int count = reader.ReadUInt16();
                    if (count > TrafficMessage.MaxVehicles)
                        throw new ProtocolException(ProtocolErrorKind.InvalidTrafficState, "Too many traffic vehicles.");
                    var vehicles = new TrafficVehicleState[count];
                    for (int i = 0; i < count; i++)
                        vehicles[i] = new TrafficVehicleState(reader.ReadUInt16(), reader.ReadBoolean(),
                            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    return new TrafficMessage(sequence, scene, fingerprint, vehicles);
                }

                case Opcode.SeatState:
                    return new SeatStateMessage(reader.ReadUInt32(), reader.ReadByte(),
                        reader.ReadByte(), reader.ReadBoolean(), reader.ReadSingle());

                case Opcode.CarTrail:
                {
                    uint sequence = reader.ReadUInt32();
                    int count = reader.ReadByte();
                    if (count > 64) throw new ProtocolException(ProtocolErrorKind.InvalidParticleCount, "Too many trails.");
                    var trails = new TrailVisualState[count];
                    for (int i = 0; i < count; i++)
                        trails[i] = new TrailVisualState(reader.ReadString(256, true), reader.ReadBoolean(),
                            reader.ReadBoolean(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    return new CarTrailMessage(sequence, trails);
                }

                case Opcode.Ping:
                    return new PingMessage(reader.ReadUInt32(), reader.ReadInt64());

                case Opcode.Pong:
                    return new PongMessage(reader.ReadUInt32(), reader.ReadInt64());

                case Opcode.Bye:
                    return new ByeMessage((ByeReason)reader.ReadByte());

                default:
                    throw new ProtocolException(
                        ProtocolErrorKind.UnknownOpcode, $"Unknown opcode 0x{rawOpcode:X2}.");
            }
        }

        private static int StringSize(string value) =>
            value == null ? 4 : 4 + System.Text.Encoding.UTF8.GetByteCount(value);
    }
}
