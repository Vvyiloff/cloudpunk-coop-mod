using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class NpcFrameTests
    {
        private static NpcVisualState N(string id) => new NpcVisualState(id, "civilianA", true, true,
            1,2,3,90,1,1,1,2,0,"walk",5);
        private static NpcMessage Chunk(byte index, string id, uint sequence = 1, uint epoch = 1) =>
            new NpcMessage(epoch, sequence, 6, index, 2, new[] { N(id) });

        [Fact]
        public void CompleteOutOfOrderChunksCommitOnceAndRejectDuplicates()
        {
            var a = new NpcFrameAssembler();
            Assert.False(a.Add(Chunk(1,"b"),out var none)); Assert.Null(none);
            Assert.False(a.Add(Chunk(1,"b"),out _));
            Assert.True(a.Add(Chunk(0,"a"),out var frame)); Assert.Equal(2,frame!.Length);
            Assert.Equal("a",frame[0].Id); Assert.Equal("b",frame[1].Id);
            Assert.False(a.Add(Chunk(0,"a"),out _));
        }

        [Fact]
        public void StaleOrMixedFramesCannotCombineIntoAWorld()
        {
            var a = new NpcFrameAssembler();
            a.Add(Chunk(0,"a",1),out _);
            Assert.False(a.Add(Chunk(1,"b",2),out _));
            Assert.False(a.Add(Chunk(0,"a",1),out _));
            Assert.True(a.Add(Chunk(0,"c",2),out var frame)); Assert.Equal("c",frame![0].Id);
            Assert.False(a.Add(Chunk(0,"x",3,0),out _));
            Assert.False(a.Add(Chunk(0,"same",3),out _));
            Assert.False(a.Add(Chunk(1,"same",3),out _));
        }

        [Fact]
        public void InvalidFrameDoesNotResetSequenceWatermark()
        {
            var a = new NpcFrameAssembler();
            a.Add(Chunk(0,"a",10),out _);
            var inconsistent = new NpcMessage(1,10,7,1,2,new[] { N("b") });
            Assert.False(a.Add(inconsistent,out _));
            Assert.False(a.Add(Chunk(0,"old",9),out _));
            Assert.False(a.Add(Chunk(1,"old2",9),out _));
            Assert.False(a.Add(Chunk(0,"next",11),out _));
            Assert.True(a.Add(Chunk(1,"next2",11),out _));
            a.Add(Chunk(0,"duplicate",12),out _);
            Assert.False(a.Add(Chunk(1,"duplicate",12),out _));
            Assert.False(a.Add(Chunk(0,"rollback",11),out _));
        }

        [Fact]
        public void DefaultNpcCannotBeEncodedAsAValidFrame()
        {
            Assert.Throws<ProtocolException>(()=>new NpcMessage(1,1,6,0,1,new NpcVisualState[1]));
        }

        [Fact]
        public void NpcWireLayoutAndLimitsAreCheckedBeforeApplication()
        {
            var original = Chunk(0,"a"); var bytes = MessageCodec.Encode(original);
            Assert.Equal(bytes.Length,MessageCodec.GetPayloadSize(original));
            var copy = Assert.IsType<NpcMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(original.Npcs,copy.Npcs);
            for(int i=0;i<bytes.Length;i++) Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0,i),out _));
            Assert.Throws<ProtocolException>(()=>new NpcMessage(1,1,6,0,0,Array.Empty<NpcVisualState>()));
            Assert.Throws<ProtocolException>(()=>new NpcVisualState("x","y",true,true,float.NaN,0,0,0,1,1,1,0,0,"",0));
            Assert.Throws<ProtocolException>(()=>new NpcVisualState("x","y",true,true,0,0,0,0,1,1,1,6,0,"",0));
        }
    }
}
