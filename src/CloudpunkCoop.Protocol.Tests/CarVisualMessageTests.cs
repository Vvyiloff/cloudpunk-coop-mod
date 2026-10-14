using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class CarVisualMessageTests
    {
        public static CarVisualMessage SampleMessage() => new CarVisualMessage(uint.MaxValue, new[]
        {
            new ParticleVisualState("Thrusters/Left", true, true,
                12.5f, 3.25f, 0.5f, 2f, 1f, 2f, 3f, 0.1f, 0.2f, 0.8f, 1f,
                -3.5f, 2.25f, 8f, -0.25f, 0.5f, -0.75f, 2f),
            new ParticleVisualState("Effects/IdleSmoke", true, false,
                0f, 0.25f, 1.5f, 5f, 0.5f, 0.75f, 1.25f, 0.4f, 0.5f, 0.6f, 0.7f,
                8192f, -8192f, 0f, 0f, -2f, 0f, 0f),
        });

        [Fact]
        public void ThrusterAndIdleSmoke_RoundTripEveryField()
        {
            var original = SampleMessage();
            var decoded = Assert.IsType<CarVisualMessage>(MessageCodec.Decode(MessageCodec.Encode(original)));
            Assert.Equal((byte)9, (byte)decoded.Opcode);
            Assert.Equal(original.SequenceNumber, decoded.SequenceNumber);
            // Struct equality checks all backing fields, including the boolean flags.
            Assert.Equal(original.Particles, decoded.Particles);
            Assert.Equal("18", ProtocolConstants.ProtocolVersion);
        }

        [Fact]
        public void EmptyParticles_HaveExactWireBytesAndSize()
        {
            var message = new CarVisualMessage(0x04030201, Array.Empty<ParticleVisualState>());
            Assert.Equal(new byte[] { 9, 1, 2, 3, 4, 0 }, MessageCodec.Encode(message));
            Assert.Equal(6, MessageCodec.GetPayloadSize(message));
            Assert.Empty(((CarVisualMessage)MessageCodec.Decode(MessageCodec.Encode(message))).Particles);
        }

        [Fact]
        public void PayloadSize_IncludesUtf8BytesAndEveryField()
        {
            var message = new CarVisualMessage(7, new[] { Particle("дым/🚀") });
            int expected = 6 + 4 + Encoding.UTF8.GetByteCount("дым/🚀") + 2 + 18 * 4;
            Assert.Equal(expected, MessageCodec.GetPayloadSize(message));
            Assert.Equal(expected, MessageCodec.Encode(message).Length);
            Assert.Equal(message.Particles, ((CarVisualMessage)MessageCodec.Decode(MessageCodec.Encode(message))).Particles);
        }

        [Fact]
        public void WireLayout_MatchesExplicitPrimitiveOrder()
        {
            var message = SampleMessage();
            var writer = new ProtocolWriter(256);
            writer.WriteByte(9);
            writer.WriteUInt32(uint.MaxValue);
            writer.WriteByte(2);
            foreach (var p in message.Particles)
            {
                writer.WriteString(p.Path);
                writer.WriteBoolean(p.Active);
                writer.WriteBoolean(p.Playing);
                foreach (float value in new[] { p.Rate, p.Speed, p.Size, p.Lifetime,
                    p.ScaleX, p.ScaleY, p.ScaleZ, p.ColorR, p.ColorG, p.ColorB, p.ColorA,
                    p.PositionX, p.PositionY, p.PositionZ, p.RotationX, p.RotationY, p.RotationZ, p.RotationW })
                    writer.WriteSingle(value);
            }
            Assert.Equal(writer.ToArray(), MessageCodec.Encode(message));
        }

        [Fact]
        public void Message_DefensivelyCopiesInputAndGetterArrays()
        {
            var particles = SampleMessage().Particles;
            var message = new CarVisualMessage(1, particles);
            byte[] before = MessageCodec.Encode(message);
            particles[0] = default;
            message.Particles[1] = default;
            Assert.Equal(before, MessageCodec.Encode(message));
            Assert.True(typeof(CarVisualMessage).IsSealed);
            Assert.False(typeof(CarVisualMessage).IsValueType);
            Assert.Throws<ArgumentNullException>(() => new CarVisualMessage(0, null!));
        }

        [Fact]
        public void CountLimit_Accepts64AndRejects65()
        {
            var particles = new ParticleVisualState[64];
            Array.Fill(particles, Particle(new string('x', 256)));
            var message = new CarVisualMessage(1, particles);
            Assert.Equal(particles, ((CarVisualMessage)MessageCodec.Decode(MessageCodec.Encode(message))).Particles);
            Assert.Equal(6 + 64 * (4 + 256 + 2 + 72), MessageCodec.GetPayloadSize(message));
            Assert.Equal(ProtocolErrorKind.InvalidParticleCount,
                Assert.Throws<ProtocolException>(() => new CarVisualMessage(1, new ParticleVisualState[65])).Kind);
            Reject(new byte[] { 9, 0, 0, 0, 0, 65 }, ProtocolErrorKind.InvalidParticleCount);
        }

        [Theory]
        [InlineData("x", 256)]
        [InlineData("я", 128)]
        public void PathLimit_UsesUtf8ByteCount(string character, int repetitions)
        {
            string path = string.Concat(System.Linq.Enumerable.Repeat(character, repetitions));
            var valid = new CarVisualMessage(1, new[] { Particle(path) });
            Assert.Equal(path, ((CarVisualMessage)MessageCodec.Decode(MessageCodec.Encode(valid))).Particles[0].Path);
            var invalid = new CarVisualMessage(1, new[] { Particle(path + character) });
            Assert.Equal(ProtocolErrorKind.InvalidStringLength,
                Assert.Throws<ProtocolException>(() => MessageCodec.Encode(invalid)).Kind);
            Assert.Throws<ProtocolException>(() => MessageCodec.GetPayloadSize(invalid));
        }

        public static IEnumerable<object[]> InvalidFloats()
        {
            for (int field = 0; field < 18; field++)
                foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    yield return new object[] { field, value };
            foreach (int field in new[] { 0, 2, 3, 7, 8, 9, 10 })
                yield return new object[] { field, -0.01f };
        }

        [Fact]
        public void ReversedParticleSpeedAndMirroredScaleAreValid()
        {
            var original = new CarVisualMessage(4, new[] { new ParticleVisualState("mirror",
                true, true, 10f, -2f, 1f, 1f, -1f, 1f, -1f, 1f, 1f, 1f, 1f) });
            var decoded = (CarVisualMessage)MessageCodec.Decode(MessageCodec.Encode(original));
            Assert.Equal(original.Particles, decoded.Particles);
        }

        [Theory]
        [MemberData(nameof(InvalidFloats))]
        public void EveryFloat_RejectsNonfiniteAndNegativeOnEncodeAndDecode(int field, float value)
        {
            var v = new float[18];
            Array.Fill(v, 1f);
            v[field] = value;
            var invalid = new CarVisualMessage(1, new[] { new ParticleVisualState("p", true, false,
                v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10],
                v[11], v[12], v[13], v[14], v[15], v[16], v[17]) });
            Assert.Equal(ProtocolErrorKind.InvalidParticleValue,
                Assert.Throws<ProtocolException>(() => MessageCodec.Encode(invalid)).Kind);
            Assert.Throws<ProtocolException>(() => MessageCodec.GetPayloadSize(invalid));
            var writer = new ProtocolWriter(16);
            writer.WriteByte(42);
            Assert.Throws<ProtocolException>(() => MessageCodec.Write(ref writer, invalid));
            Assert.Equal(new byte[] { 42 }, writer.ToArray());
            byte[] payload = MessageCodec.Encode(new CarVisualMessage(1, new[] { Particle("p") }));
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(6 + 4 + 1 + 2 + field * 4),
                BitConverter.SingleToInt32Bits(value));
            Reject(payload, ProtocolErrorKind.InvalidParticleValue);
        }

        [Fact]
        public void EveryTruncatedPrefix_AndTrailingBytesAreRejected()
        {
            byte[] payload = MessageCodec.Encode(SampleMessage());
            for (int length = 0; length < payload.Length; length++)
                Reject(payload.AsSpan(0, length).ToArray(), ProtocolErrorKind.TruncatedPayload);
            Array.Resize(ref payload, payload.Length + 1);
            Reject(payload, ProtocolErrorKind.TrailingBytes);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(257)]
        [InlineData(int.MaxValue)]
        public void MalformedPathLength_IsRejectedBeforeAllocation(int length)
        {
            byte[] payload = { 9, 0, 0, 0, 0, 1, 0, 0, 0, 0 };
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(6), length);
            Reject(payload, ProtocolErrorKind.InvalidStringLength);
        }

        [Fact]
        public void InvalidUtf8AndBooleans_AreRejected()
        {
            byte[] payload = MessageCodec.Encode(new CarVisualMessage(1, new[] { Particle("p") }));
            payload[10] = 0xFF;
            Reject(payload, ProtocolErrorKind.InvalidParticleValue);
            payload[10] = (byte)'p';
            payload[11] = 2;
            Reject(payload, ProtocolErrorKind.TruncatedPayload);
            payload[11] = 1;
            payload[12] = 2;
            Reject(payload, ProtocolErrorKind.TruncatedPayload);
        }

        [Fact]
        public void ExistingConstructor_DefaultsToOriginAndIdentityRotation()
        {
            var particle = Particle("p");
            Assert.Equal(0f, particle.PositionX);
            Assert.Equal(0f, particle.PositionY);
            Assert.Equal(0f, particle.PositionZ);
            Assert.Equal(0f, particle.RotationX);
            Assert.Equal(0f, particle.RotationY);
            Assert.Equal(0f, particle.RotationZ);
            Assert.Equal(1f, particle.RotationW);
            var decoded = (CarVisualMessage)MessageCodec.Decode(
                MessageCodec.Encode(new CarVisualMessage(1, new[] { particle })));
            Assert.Equal(particle, decoded.Particles[0]);
        }

        [Fact]
        public void SpatialWireLayout_MatchesGoldenLittleEndianBytes()
        {
            var particle = new ParticleVisualState("p", true, false,
                0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 1, -2, 3, 0.5f, -1, 2, 4);
            var message = new CarVisualMessage(0x04030201, new[] { particle });
            // Fixed bytes independent of ProtocolWriter and MessageCodec.
            byte[] golden = Convert.FromHexString("09010203040101000000700100000000000000000000000000000000000000803F0000803F0000803F000000000000000000000000000000000000803F000000C0000040400000003F000080BF0000004000008040");
            Assert.Equal(85, MessageCodec.GetPayloadSize(message));
            Assert.Equal(golden, MessageCodec.Encode(message));
            Assert.Equal(message.Particles, ((CarVisualMessage)MessageCodec.Decode(golden)).Particles);
        }

        public static IEnumerable<object[]> InvalidSpatialStates()
        {
            for (int axis = 0; axis < 3; axis++)
            {
                foreach (float value in new[] { -8192.001f, 8192.001f, float.MinValue, float.MaxValue })
                {
                    var pose = new float[] { 0, 0, 0, 0, 0, 0, 1 };
                    pose[axis] = value;
                    yield return new object[] { pose };
                }
            }
            yield return new object[] { new float[] { 0, 0, 0, 0, 0, 0, 0 } };
            // Finite components can still overflow their squared norm.
            for (int component = 3; component < 7; component++)
            {
                var pose = new float[] { 0, 0, 0, 0, 0, 0, 1 };
                pose[component] = float.MaxValue;
                yield return new object[] { pose };
            }
            yield return new object[] { new float[] { 0, 0, 0, float.Epsilon, 0, 0, 0 } };
        }

        [Theory]
        [MemberData(nameof(InvalidSpatialStates))]
        public void InvalidSpatialState_RejectsEncodeSizeWriteAndDecode(float[] pose)
        {
            var invalidParticle = new ParticleVisualState("p", true, false,
                0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0,
                pose[0], pose[1], pose[2], pose[3], pose[4], pose[5], pose[6]);
            // A later invalid particle must also leave an existing writer untouched.
            var invalid = new CarVisualMessage(1, new[] { Particle("valid"), invalidParticle });
            Assert.Equal(ProtocolErrorKind.InvalidParticleValue,
                Assert.Throws<ProtocolException>(() => MessageCodec.Encode(invalid)).Kind);
            Assert.Equal(ProtocolErrorKind.InvalidParticleValue,
                Assert.Throws<ProtocolException>(() => MessageCodec.GetPayloadSize(invalid)).Kind);
            var writer = new ProtocolWriter(16);
            writer.WriteByte(42);
            Assert.Equal(ProtocolErrorKind.InvalidParticleValue,
                Assert.Throws<ProtocolException>(() => MessageCodec.Write(ref writer, invalid)).Kind);
            Assert.Equal(new byte[] { 42 }, writer.ToArray());
            byte[] payload = MessageCodec.Encode(new CarVisualMessage(1, new[] { Particle("p") }));
            for (int field = 0; field < 7; field++)
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(13 + (11 + field) * 4),
                    BitConverter.SingleToInt32Bits(pose[field]));
            Reject(payload, ProtocolErrorKind.InvalidParticleValue);
        }

        [Fact]
        public void WrongParticleSizesAndCounts_AreRejected()
        {
            byte[] payload = MessageCodec.Encode(new CarVisualMessage(1, new[] { Particle("p") }));
            // The old eleven-float layout cannot be accepted as a v15 particle.
            Reject(payload.AsSpan(0, payload.Length - 28).ToArray(), ProtocolErrorKind.TruncatedPayload);
            Reject(payload.AsSpan(0, payload.Length - 4).ToArray(), ProtocolErrorKind.TruncatedPayload);
            byte[] extraFloat = new byte[payload.Length + 4];
            payload.CopyTo(extraFloat, 0);
            Reject(extraFloat, ProtocolErrorKind.TrailingBytes);
            payload[5] = 2;
            Reject(payload, ProtocolErrorKind.TruncatedPayload);
            payload[5] = 0;
            Reject(payload, ProtocolErrorKind.TrailingBytes);
        }

        private static ParticleVisualState Particle(string path) => new ParticleVisualState(
            path, true, false, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0);

        private static void Reject(byte[] payload, ProtocolErrorKind kind)
        {
            Assert.Equal(kind, Assert.Throws<ProtocolException>(() => MessageCodec.Decode(payload)).Kind);
            Assert.False(MessageCodec.TryDecode(payload, out var message));
            Assert.Null(message);
        }
    }
}
