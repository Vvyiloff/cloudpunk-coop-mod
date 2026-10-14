using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class PersonalClockTests
    {
        private static PersonalClockMessage Sample(uint sequence, double elapsed, long session = 7) =>
            new PersonalClockMessage(sequence, session, elapsed,
                double.IsFinite(elapsed) && elapsed>=0 ? (long)(elapsed*1000):0,Math.Max(0,elapsed-0.001));

        [Fact]
        public void ExactGoldenLayoutRoundTripsAndRejectsEveryTruncationAndTrailingBytes()
        {
            var message = new PersonalClockMessage(0x04030201, 0x0807060504030201, 1.5);
            byte[] bytes = MessageCodec.Encode(message);
            Assert.Equal(new byte[] {29, 1, 2, 3, 4, 1, 2, 3, 4, 5, 6, 7, 8,
                0, 0, 0, 0, 0, 0, 248, 63,
                0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}, bytes);
            Assert.Equal(37, MessageCodec.GetPayloadSize(message));
            var copy = Assert.IsType<PersonalClockMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(message.Sequence, copy.Sequence);
            Assert.Equal(message.SessionId, copy.SessionId);
            Assert.Equal(message.ElapsedScaledSeconds, copy.ElapsedScaledSeconds);
            Assert.True(MessageCodec.TryDecode(bytes, out var valid));
            Assert.IsType<PersonalClockMessage>(valid);
            for (int i = 0; i < bytes.Length; i++)
            {
                Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes.AsSpan(0, i)));
                Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0, i), out var invalid));
                Assert.Null(invalid);
            }
            Array.Resize(ref bytes, 38);
            Assert.Equal(ProtocolErrorKind.TrailingBytes,
                Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
            Assert.False(MessageCodec.TryDecode(bytes, out var trailing));
            Assert.Null(trailing);
        }

        [Theory]
        [InlineData(0, 0)] [InlineData(-1, 0)]
        [InlineData(1, -1)] [InlineData(1, 1000000001)]
        [InlineData(1, double.NaN)] [InlineData(1, double.PositiveInfinity)]
        [InlineData(1, double.NegativeInfinity)]
        public void InvalidFieldsRejectedByConstructorAndWire(long session, double elapsed)
        {
            Assert.Throws<ProtocolException>(() => new PersonalClockMessage(0, session, elapsed));
            var writer = new ProtocolWriter(37);
            writer.WriteByte(29); writer.WriteUInt32(0); writer.WriteInt64(session); writer.WriteDouble(elapsed);
            writer.WriteInt64(0);writer.WriteDouble(0);
            byte[] bytes = writer.ToArray();
            Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes));
            Assert.False(MessageCodec.TryDecode(bytes, out var invalid));
            Assert.Null(invalid);
        }

        [Theory]
        [InlineData(0)] [InlineData(1000000000)]
        public void ElapsedBoundsAccepted(double elapsed)
        {
            var copy = Assert.IsType<PersonalClockMessage>(MessageCodec.Decode(MessageCodec.Encode(Sample(0, elapsed))));
            Assert.Equal(elapsed, copy.ElapsedScaledSeconds);
        }

        [Theory]
        [InlineData(0L)] [InlineData(long.MinValue)] [InlineData(long.MaxValue)]
        [InlineData(0x3ff8000000000000L)] [InlineData(0x7ff8000000000001L)]
        public void DoublePrimitivesPreserveExactIeeeBits(long bits)
        {
            var writer = new ProtocolWriter(8);
            writer.WriteDouble(BitConverter.Int64BitsToDouble(bits));
            byte[] bytes = writer.ToArray();
            for (int i = 0; i < 8; i++) Assert.Equal((byte)(unchecked((ulong)bits) >> (8 * i)), bytes[i]);
            var reader = new ProtocolReader(bytes);
            Assert.Equal(bits, BitConverter.DoubleToInt64Bits(reader.ReadDouble()));
            Assert.True(reader.IsAtEnd);
        }

        [Fact]
        public void FirstSampleOnlyBaselinesAndDuplicatesNeverAdvanceTwice()
        {
            var tracker = new PersonalClockTracker(7);
            Assert.Equal(0, tracker.AcceptedElapsed);
            Assert.True(tracker.Receive(Sample(10, 100), 0, out var advance)); Assert.Equal(0, advance);
            Assert.False(tracker.Receive(Sample(11, 100.25), 0, out advance)); Assert.Equal(0, advance);
            Assert.True(tracker.Receive(Sample(11, 101), 1, out advance)); Assert.Equal(1, advance);
            Assert.False(tracker.Receive(Sample(11, 101), 2, out advance)); Assert.Equal(0, advance);
            Assert.False(tracker.Receive(Sample(9, 102), 2, out advance)); Assert.Equal(0, advance);
            Assert.True(tracker.Receive(Sample(12, 101), 2, out advance)); Assert.Equal(0, advance);
            Assert.Equal(101, tracker.AcceptedElapsed);
        }

        [Fact]
        public void SequenceWrapUsesStrictHalfRangeOrdering()
        {
            var tracker = new PersonalClockTracker(7);
            Assert.True(tracker.Receive(Sample(uint.MaxValue, 10), 0, out _));
            Assert.True(tracker.Receive(Sample(0, 11), 1, out var advance)); Assert.Equal(1, advance);
            Assert.False(tracker.Receive(Sample(uint.MaxValue, 12), 2, out _));
            Assert.False(tracker.Receive(Sample(0x80000000, 12), 2, out _));
            Assert.True(tracker.Receive(Sample(1, 12), 2, out advance)); Assert.Equal(1, advance);
        }

        [Fact]
        public void PauseUpdatesHeartbeatSequenceAndCapsUnusedCredit()
        {
            var tracker = new PersonalClockTracker(7);
            Assert.True(tracker.Receive(Sample(1, 10), 0, out _));
            Assert.True(tracker.Receive(Sample(2, 10), 100, out var advance)); Assert.Equal(0, advance);
            Assert.True(tracker.Fresh(102.99));
            Assert.False(tracker.Receive(Sample(2, 10), 101, out _));
            Assert.False(tracker.Receive(Sample(3, 10.75), 100, out _));
            Assert.True(tracker.Receive(Sample(3, 10.5), 100, out advance)); Assert.Equal(0.5, advance);
            Assert.False(tracker.Receive(Sample(4, 10.625), 100, out _));
        }

        [Fact]
        public void BurstAndIncrementSpamShareOneBudgetWithNoPacketSlack()
        {
            var tracker = new PersonalClockTracker(7);
            Assert.True(tracker.Receive(Sample(0, 0), 0, out _));
            Assert.True(tracker.Receive(Sample(1, 0), 1, out _));
            for (uint sequence = 2; sequence <= 5; sequence++)
            {
                Assert.True(tracker.Receive(Sample(sequence, (sequence - 1) * 0.125), 1, out var advance));
                Assert.Equal(0.125, advance);
            }
            for (uint sequence = 6; sequence < 100; sequence++)
            {
                Assert.False(tracker.Receive(Sample(sequence, 0.625), 1, out var advance));
                Assert.Equal(0, advance);
            }
            Assert.False(tracker.Receive(Sample(6, 1.125), 1.125, out _));
            Assert.True(tracker.Receive(Sample(6, 1), 1.125, out var accepted)); Assert.Equal(0.5, accepted);
        }

        [Fact]
        public void RejectedSamplesDoNotConsumeSequenceElapsedReceiptOrCredit()
        {
            var tracker = new PersonalClockTracker(7);
            Assert.True(tracker.Receive(Sample(0, 10), 0, out _));
            Assert.True(tracker.Receive(Sample(1, 10), 1, out _));
            Assert.False(tracker.Receive(Sample(100, 100), 2, out var advance)); Assert.Equal(0, advance);
            Assert.False(tracker.Receive(Sample(100, 9), 2, out _));
            Assert.False(tracker.Receive(Sample(100, 10, 8), 2, out _));
            Assert.False(tracker.Receive(Sample(100, 10), 0.5, out _));
            Assert.False(tracker.Receive(Sample(1, 10), 2, out _));
            Assert.Equal(10, tracker.AcceptedElapsed);
            Assert.False(tracker.Fresh(4));
            Assert.True(tracker.Receive(Sample(2, 14.5), 2, out advance)); Assert.Equal(4.5, advance);
        }

        [Fact]
        public void LongLegitimateGapsUseFullRefillBeforeCappingRemainingCredit()
        {
            var tracker = new PersonalClockTracker(7);
            Assert.True(tracker.Receive(Sample(1, 100), 0, out _));
            Assert.True(tracker.Receive(Sample(2, 4100), 1000, out var advance)); Assert.Equal(4000, advance);
            Assert.True(tracker.Receive(Sample(3, 4100), 1001, out _));
            Assert.True(tracker.Receive(Sample(4, 4500.5), 1101, out advance)); Assert.Equal(400.5, advance);
            Assert.False(tracker.Receive(Sample(5, 4500.625), 1101, out _));
        }

        [Fact]
        public void InvalidTimeAndResetValidationPreserveStateAndReconnectBaselines()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PersonalClockTracker(0));
            var tracker = new PersonalClockTracker(7);
            Assert.True(tracker.Receive(Sample(1, 100), 10, out _));
            foreach (double now in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Receive(Sample(2, 100), now, out _));
            Assert.Throws<ArgumentNullException>(() => tracker.Receive(null!, 11, out _));
            Assert.False(tracker.Receive(Sample(2, 100), 9, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Reset(-1));
            Assert.Equal(100, tracker.AcceptedElapsed); Assert.True(tracker.Fresh(10));
            Assert.True(tracker.Receive(Sample(2, 100), 11, out _));
            tracker.Reset(8);
            Assert.Equal(0, tracker.AcceptedElapsed); Assert.False(tracker.Fresh(11));
            Assert.False(tracker.Receive(Sample(3, 1000), 20, out _));
            Assert.True(tracker.Receive(Sample(0, 1000, 8), 0, out var advance)); Assert.Equal(0, advance);
            Assert.False(tracker.Receive(Sample(1, 1000.25, 8), 0, out _));
            Assert.True(tracker.Receive(Sample(1, 1001, 8), 1, out advance)); Assert.Equal(1, advance);
        }

        [Fact]
        public void FreshRequiresAcceptedHeartbeatWithinStrictThreeSeconds()
        {
            var tracker = new PersonalClockTracker(7);
            Assert.False(tracker.Fresh(0));
            Assert.True(tracker.Receive(Sample(1, 0), 10, out _));
            Assert.True(tracker.Fresh(10)); Assert.True(tracker.Fresh(12.999));
            foreach (double now in new[] {9, 13, double.NaN, double.PositiveInfinity, double.NegativeInfinity})
                Assert.False(tracker.Fresh(now));
            Assert.False(tracker.Receive(Sample(1, 0), 12, out _));
            Assert.False(tracker.Fresh(13));
        }

        [Fact]
        public void EligibleZeroDeltaTickSurvivesWireAndIsDistinctFromPause()
        {
            var tracker=new PersonalClockTracker(7);
            Assert.True(tracker.Receive(new PersonalClockMessage(0,7,10,100,9.9),0,out _));
            var tick=Assert.IsType<PersonalClockMessage>(MessageCodec.Decode(MessageCodec.Encode(new PersonalClockMessage(1,7,10,101,10))));
            Assert.True(tracker.Receive(tick,1,out var delta));Assert.Equal(0,delta);
            Assert.Equal(101,tracker.AcceptedTicks);Assert.Equal(10,tick.LastTickStart);
            Assert.True(tracker.Receive(new PersonalClockMessage(2,7,10,101,10),2,out delta));
            Assert.Equal(101,tracker.AcceptedTicks);Assert.Equal(0,delta);
        }

        [Fact]
        public void TickRegressionAndElapsedWithoutTicksAreRejectedAtomically()
        {
            var tracker=new PersonalClockTracker(7);
            Assert.True(tracker.Receive(new PersonalClockMessage(0,7,10,100,9.9),0,out _));
            Assert.False(tracker.Receive(new PersonalClockMessage(1,7,11,100,10.9),1,out _));
            Assert.False(tracker.Receive(new PersonalClockMessage(1,7,11,99,10.9),1,out _));
            Assert.False(tracker.Receive(new PersonalClockMessage(1,7,11,101,9.9),1,out _));
            Assert.Equal(10,tracker.AcceptedElapsed);Assert.Equal(100,tracker.AcceptedTicks);
            Assert.True(tracker.Receive(new PersonalClockMessage(1,7,11,101,10),1,out var delta));
            Assert.Equal(1,delta);Assert.Equal(101,tracker.AcceptedTicks);
        }

        [Theory]
        [InlineData(-1,0)] [InlineData(1,-1)] [InlineData(1,2)]
        [InlineData(1,double.NaN)] [InlineData(1,double.PositiveInfinity)]
        public void InvalidTickMetadataFailsBeforeTracker(long ticks,double start)
        {
            Assert.Throws<ProtocolException>(()=>new PersonalClockMessage(0,7,1,ticks,start));
        }
    }
}
