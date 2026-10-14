using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class PersonalEffectBatchTests
    {
        [Fact]
        public void ExpirationBeforeLastTickResetsAlcoholPassed()
        {
            var initial=new PersonalConsumableEffects(alcoholLeft:0.05f,alcoholPassed:10);
            var native=initial.Advance(0.1f,true).Advance(0.1f,true);
            var batched=initial.AdvanceTicks(0.2f,0.1f,true);
            Assert.Equal(native.AlcoholLeft,batched.AlcoholLeft);
            Assert.Equal(0,batched.AlcoholPassed);
        }

        [Theory]
        [InlineData(0.15f,0.2f,0.1f,10.2f)]
        [InlineData(0.1f,0.2f,0.1f,0f)]
        [InlineData(1f,0.2f,0.1f,10.2f)]
        [InlineData(0f,0f,0f,0f)]
        public void LastTickEntryMatchesNativeBoundary(float left,float total,float last,float passed)
        {
            var actual=new PersonalConsumableEffects(alcoholLeft:left,alcoholPassed:10).AdvanceTicks(total,last,true);
            Assert.Equal(passed,actual.AlcoholPassed);
        }

        [Fact]
        public void ZeroTimeEligibleTickDiffersFromPausedHeartbeat()
        {
            var initial=new PersonalConsumableEffects(alcoholPassed:10);
            Assert.Same(initial,initial.AdvanceTicks(0,0,false));
            Assert.Equal(0,initial.AdvanceTicks(0,0,true).AlcoholPassed);
        }

        [Fact]
        public void InvalidBatchCannotMutateState()
        {
            var initial=new PersonalConsumableEffects(10,20,30,40,50,60);
            Assert.Throws<ArgumentOutOfRangeException>(()=>initial.AdvanceTicks(1,2,true));
            Assert.Throws<ArgumentOutOfRangeException>(()=>initial.AdvanceTicks(1,0,false));
            Assert.Throws<ArgumentOutOfRangeException>(()=>initial.AdvanceTicks(float.NaN,0,true));
            Assert.Equal(50,initial.AlcoholLeft);Assert.Equal(60,initial.AlcoholPassed);
        }
    }
}
