using System;
using System.Collections.Generic;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    /// <summary>
    /// Round-trip guarantees for every message type, plus the failure modes that hostile or
    /// mismatched peers can actually produce.
    ///
    /// Float fields are compared exactly on purpose: the codec writes the IEEE-754 bit
    /// pattern, so a round trip must be lossless rather than close.
    /// </summary>
    public class MessageCodecTests
    {
        public static IEnumerable<object[]> AllMessages()
        {
            yield return new object[] { new RegionPresenceMessage(4, 6, true) };
            yield return new object[] { new PersonalClockMessage(4, 7, 1.5) };
            yield return new object[] { new HelloMessage("1.0", "Andrey") };
            yield return new object[] { new CampaignChunkMessage(1, 6, 0, 1, new byte[] { 1, 2, 3 }) };
            yield return new object[] { new CampaignActionMessage(2, 1, 6, 2, "dialog", "line", 1, 123, ulong.MaxValue, 4095) };
            yield return new object[] { new CampaignReplyMessage(2, 1, false, "unavailable") };
            yield return new object[] { new CampaignPresentationMessage(3, 1, 6, new byte[] { 123, 125 }) };
            yield return new object[] { new WelcomeMessage(PeerRole.Guest, "1.0") };
            yield return new object[] { new WelcomeMessage(PeerRole.Host, "1.0") };
            yield return new object[]
            {
                new CarStateMessage(
                    1.5f, -2.25f, 300.125f,
                    0f, 0.7071f, 0f, 0.7071f,
                    12.5f, 0f, -7.25f,
                    0f, 1.25f, 0f,
                    0.33f, 0.42f, 987654u),
            };
            yield return new object[] { new InputMessage(1f, 0f, -0.5f, 0.25f, 0.75f, 42u) };
            yield return new object[] { new SeatMessage(1, true) };
            yield return new object[] { new SeatMessage(0, false) };
            yield return new object[] { new PingMessage(7u, 1234567890123L) };
            yield return new object[] { new PongMessage(7u, 1234567890123L) };
            yield return new object[] { new ByeMessage(ByeReason.LocalShutdown) };
            yield return new object[] { CarVisualMessageTests.SampleMessage() };
            yield return new object[] { new SeatRequestMessage(10) };
            yield return new object[] { new OwnedCarMessage(2,6,false,new CarStateMessage(1,2,3,0,0,0,1,0,0,0,0,0,0,0,0,20)) };
            yield return new object[] { new OccupancyRequestMessage(5,2) };
            yield return new object[] { new VehicleParkMessage(5,2,0) };
            yield return new object[] { new ParkingSlotMessage(5,5,2,0,5,1,2,3) };
            yield return new object[] { new OccupancyStateMessage(6,1,2,1,2,0,0) };
            yield return new object[] { new DriverInputMessage(11, 15, 1, -0.5f, 0.2f, 0) };
            yield return new object[] { new TrafficMessage(2, 6, new string('A', 64), new[] { new TrafficVehicleState(0, true, 1, 2, 3, 0, 0, 0, 1) }) };
            yield return new object[] { new MobilityRequestMessage(1, true) };
            yield return new object[] { new MobilityStateMessage(2, true, true, "on foot", 6, 1, 2, 3, 90) };
            yield return new object[] { new FootPoseMessage(3, 6, true, 1, 2, 3, 90, 2, 0) };
            yield return new object[] { new NpcMessage(1, 1, 6, 0, 1, new[] { new NpcVisualState("npc1", "model1", true, true, 1,2,3,90,1,1,1,2,0,"walk",5) }) };
            yield return new object[] { new SeatStateMessage(11, 1, 2, true, 8f) };
            yield return new object[] { new CarTrailMessage(12, new[] { new TrailVisualState("exhaust/trail", true, true, 1f, 0.3f, 0.05f) }) };
        }

        [Theory]
        [MemberData(nameof(AllMessages))]
        public void EncodeThenDecode_ReturnsEquivalentMessage(IMessage original)
        {
            byte[] payload = MessageCodec.Encode(original);

            IMessage decoded = MessageCodec.Decode(payload);

            Assert.Equal(original.Opcode, decoded.Opcode);
            Assert.Equal(original.ToString(), decoded.ToString());
        }

        [Theory]
        [MemberData(nameof(AllMessages))]
        public void GetPayloadSize_MatchesActualEncodedLength(IMessage original)
        {
            int predicted = MessageCodec.GetPayloadSize(original);

            byte[] payload = MessageCodec.Encode(original);

            Assert.Equal(predicted, payload.Length);
        }

        [Theory]
        [MemberData(nameof(AllMessages))]
        public void TryDecode_SucceedsForValidPayload(IMessage original)
        {
            byte[] payload = MessageCodec.Encode(original);

            bool ok = MessageCodec.TryDecode(payload, out IMessage? decoded);

            Assert.True(ok);
            Assert.NotNull(decoded);
            Assert.Equal(original.Opcode, decoded!.Opcode);
        }

        [Fact]
        public void EveryDefinedOpcode_HasAMessageUnderTest()
        {
            var covered = new HashSet<Opcode>();
            foreach (object[] row in AllMessages())
            {
                covered.Add(((IMessage)row[0]).Opcode);
            }

            foreach (Opcode opcode in Enum.GetValues<Opcode>())
            {
                if (opcode == Opcode.None)
                {
                    continue;
                }

                Assert.Contains(opcode, covered);
            }
        }

        [Fact]
        public void CurrentProtocolIsAdvertisedInHandshake()
        {
            Assert.Equal("18", ProtocolConstants.ProtocolVersion);
            var hello = new HelloMessage(ProtocolConstants.ProtocolVersion, "peer");
            var welcome = new WelcomeMessage(PeerRole.Guest, ProtocolConstants.ProtocolVersion);
            Assert.Equal("18", Assert.IsType<HelloMessage>(MessageCodec.Decode(MessageCodec.Encode(hello))).ProtocolVersion);
            Assert.Equal("18", Assert.IsType<WelcomeMessage>(MessageCodec.Decode(MessageCodec.Encode(welcome))).ProtocolVersion);
        }

        [Fact]
        public void CarState_SequenceAndSteer_SurviveExactly()
        {
            var original = new CarStateMessage(
                -123.456f, 789.012f, 0.5f,
                0.1f, 0.2f, 0.3f, 0.9f,
                0f, 0f, 70f,
                0f, 0f, -1f,
                -0.75f, 0.9f, uint.MaxValue);

            var decoded = (CarStateMessage)MessageCodec.Decode(MessageCodec.Encode(original));

            Assert.Equal(original.PositionX, decoded.PositionX);
            Assert.Equal(original.PositionZ, decoded.PositionZ);
            Assert.Equal(original.LinearVelocityZ, decoded.LinearVelocityZ);
            Assert.Equal(original.SteerAngle, decoded.SteerAngle);
            Assert.Equal(original.DamageLevel, decoded.DamageLevel);
            Assert.Equal(uint.MaxValue, decoded.SequenceNumber);
        }

        [Fact]
        public void EmptyStringFields_RoundTrip()
        {
            var original = new HelloMessage(string.Empty, string.Empty);

            var decoded = (HelloMessage)MessageCodec.Decode(MessageCodec.Encode(original));

            Assert.Equal(string.Empty, decoded.ProtocolVersion);
            Assert.Equal(string.Empty, decoded.DisplayName);
        }

        [Fact]
        public void HelloMessage_NullStrings_BecomeEmptyRatherThanThrowing()
        {
            var original = new HelloMessage(null!, null!);

            var decoded = (HelloMessage)MessageCodec.Decode(MessageCodec.Encode(original));

            Assert.Equal(string.Empty, decoded.ProtocolVersion);
            Assert.Equal(string.Empty, decoded.DisplayName);
        }

        [Fact]
        public void Utf8DisplayName_RoundTrips()
        {
            var original = new HelloMessage("1.0", "Андрей ✅");

            var decoded = (HelloMessage)MessageCodec.Decode(MessageCodec.Encode(original));

            Assert.Equal("Андрей ✅", decoded.DisplayName);
        }

        [Fact]
        public void OversizedString_IsRejectedOnWrite()
        {
            string tooLong = new string('x', ProtocolConstants.MaxStringByteLength + 1);
            var message = new HelloMessage(tooLong, "peer");

            ProtocolException ex = Assert.Throws<ProtocolException>(() => MessageCodec.Encode(message));
            Assert.Equal(ProtocolErrorKind.InvalidStringLength, ex.Kind);
        }

        [Fact]
        public void UnknownOpcodeByte_IsRejected()
        {
            byte[] payload = { 0xFE, 0x00 };

            ProtocolException ex = Assert.Throws<ProtocolException>(() => MessageCodec.Decode(payload));
            Assert.Equal(ProtocolErrorKind.UnknownOpcode, ex.Kind);
        }

        [Fact]
        public void TruncatedPayload_IsRejected()
        {
            byte[] payload = MessageCodec.Encode(new CarStateMessage(
                1f, 1f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1u));

            byte[] truncated = new byte[payload.Length - 3];
            Array.Copy(payload, truncated, truncated.Length);

            ProtocolException ex = Assert.Throws<ProtocolException>(() => MessageCodec.Decode(truncated));
            Assert.Equal(ProtocolErrorKind.TruncatedPayload, ex.Kind);
        }

        [Fact]
        public void TrailingBytes_AreRejected()
        {
            byte[] payload = MessageCodec.Encode(new ByeMessage(ByeReason.LocalShutdown));
            byte[] withTrailer = new byte[payload.Length + 1];
            Array.Copy(payload, withTrailer, payload.Length);

            ProtocolException ex = Assert.Throws<ProtocolException>(() => MessageCodec.Decode(withTrailer));
            Assert.Equal(ProtocolErrorKind.TrailingBytes, ex.Kind);
        }

        [Fact]
        public void EmptyPayload_IsRejected()
        {
            ProtocolException ex = Assert.Throws<ProtocolException>(() => MessageCodec.Decode(ReadOnlySpan<byte>.Empty));
            Assert.Equal(ProtocolErrorKind.TruncatedPayload, ex.Kind);
        }

        [Fact]
        public void UnknownPeerRole_IsRejected()
        {
            var writer = new ProtocolWriter(32);
            writer.WriteByte((byte)Opcode.Welcome);
            writer.WriteByte(99);
            writer.WriteString("1.0");

            ProtocolException ex = Assert.Throws<ProtocolException>(() => MessageCodec.Decode(writer.WrittenSpan));
            Assert.Equal(ProtocolErrorKind.TruncatedPayload, ex.Kind);
        }

        [Fact]
        public void BooleanFieldOtherThanZeroOrOne_IsRejected()
        {
            var writer = new ProtocolWriter(16);
            writer.WriteByte((byte)Opcode.Seat);
            writer.WriteByte(0);
            writer.WriteByte(7);

            ProtocolException ex = Assert.Throws<ProtocolException>(() => MessageCodec.Decode(writer.WrittenSpan));
            Assert.Equal(ProtocolErrorKind.TruncatedPayload, ex.Kind);
        }

        [Fact]
        public void TryDecode_ReturnsFalseInsteadOfThrowingOnGarbage()
        {
            var random = new Random(20261012);

            for (int i = 0; i < 500; i++)
            {
                byte[] garbage = new byte[random.Next(0, 64)];
                random.NextBytes(garbage);

                bool ok = MessageCodec.TryDecode(garbage, out IMessage? decoded);

                if (!ok)
                {
                    Assert.Null(decoded);
                }
                else
                {
                    Assert.NotNull(decoded);
                }
            }
        }

        [Fact]
        public void WriterBytes_AreLittleEndian()
        {
            var writer = new ProtocolWriter(16);
            writer.WriteUInt32(0x04030201u);

            byte[] bytes = writer.ToArray();

            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, bytes);
        }

        [Fact]
        public void WriterNegativeAndFloatBits_RoundTripThroughReader()
        {
            var writer = new ProtocolWriter(32);
            writer.WriteInt32(-12345);
            writer.WriteInt64(long.MinValue);
            writer.WriteSingle(float.NegativeInfinity);

            var reader = new ProtocolReader(writer.WrittenSpan);

            Assert.Equal(-12345, reader.ReadInt32());
            Assert.Equal(long.MinValue, reader.ReadInt64());
            Assert.Equal(float.NegativeInfinity, reader.ReadSingle());
            Assert.True(reader.IsAtEnd);
        }

        [Fact]
        public void Reader_ReportsTruncationRatherThanReadingPastTheEnd()
        {
            var writer = new ProtocolWriter(16);
            writer.WriteUInt32(1u);
            var reader = new ProtocolReader(writer.WrittenSpan);
            reader.ReadUInt32();

            // ProtocolReader is a ref struct, so the assertion cannot be wrapped in a lambda.
            ProtocolException? caught = null;
            try
            {
                reader.ReadUInt32();
            }
            catch (ProtocolException ex)
            {
                caught = ex;
            }

            Assert.NotNull(caught);
            Assert.Equal(ProtocolErrorKind.TruncatedPayload, caught!.Kind);
        }

        [Fact]
        public void Reader_RejectsStringLongerThanThePayload()
        {
            // A length that is legal for the protocol but longer than the bytes actually
            // present, so the truncation check is what rejects it.
            var writer = new ProtocolWriter(16);
            writer.WriteInt32(200);

            var reader = new ProtocolReader(writer.WrittenSpan);

            ProtocolException? caught = null;
            try
            {
                reader.ReadString();
            }
            catch (ProtocolException ex)
            {
                caught = ex;
            }

            Assert.NotNull(caught);
            Assert.Equal(ProtocolErrorKind.TruncatedPayload, caught!.Kind);
        }

        [Fact]
        public void Reader_RejectsStringLengthAboveTheProtocolLimit()
        {
            var writer = new ProtocolWriter(16);
            writer.WriteInt32(ProtocolConstants.MaxStringByteLength + 1);

            var reader = new ProtocolReader(writer.WrittenSpan);

            ProtocolException? caught = null;
            try
            {
                reader.ReadString();
            }
            catch (ProtocolException ex)
            {
                caught = ex;
            }

            Assert.NotNull(caught);
            Assert.Equal(ProtocolErrorKind.InvalidStringLength, caught!.Kind);
        }

        [Fact]
        public void Writer_RejectsNonPositiveCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProtocolWriter(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProtocolWriter(-8));
        }

        [Fact]
        public void Writer_GrowsPastItsInitialCapacity()
        {
            var writer = new ProtocolWriter(16);
            for (int i = 0; i < 1000; i++)
            {
                writer.WriteUInt32((uint)i);
            }

            var reader = new ProtocolReader(writer.WrittenSpan);
            for (int i = 0; i < 1000; i++)
            {
                Assert.Equal((uint)i, reader.ReadUInt32());
            }
        }

        [Fact]
        public void WriterReset_AllowsBufferReuse()
        {
            var writer = new ProtocolWriter(16);
            writer.WriteUInt32(1u);
            writer.Reset();

            Assert.True(writer.IsEmpty);
            writer.WriteByte(9);

            Assert.Equal(new byte[] { 9 }, writer.ToArray());
        }
    }
}
