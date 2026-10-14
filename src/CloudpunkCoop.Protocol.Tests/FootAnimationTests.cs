using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class FootAnimationTests
    {
        [Fact]
        public void RoundTripPreservesWalkingClipAndFrame()
        {
            var source=new FootPoseMessage(28,5,true,1,2,3,90,2,3,"walkLeft45",27);
            var result=Assert.IsType<FootPoseMessage>(MessageCodec.Decode(MessageCodec.Encode(source)));
            Assert.Equal(source.Sequence,result.Sequence);
            Assert.Equal(source.Clip,result.Clip);
            Assert.Equal(source.Frame,result.Frame);
            Assert.Equal(source.Animation,result.Animation);
            Assert.Equal(source.Direction,result.Direction);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(100001)]
        public void RejectsInvalidFrame(int frame) => Assert.Throws<ProtocolException>(()=>
            new FootPoseMessage(1,5,true,0,0,0,0,2,0,"walk",frame));

        [Fact]
        public void RejectsTruncatedAnimationPayload()
        {
            var payload=MessageCodec.Encode(new FootPoseMessage(1,5,true,0,0,0,0,2,0,"walk",4));
            Assert.Throws<ProtocolException>(()=>MessageCodec.Decode(payload.AsSpan(0,payload.Length-1)));
        }
    }
}
