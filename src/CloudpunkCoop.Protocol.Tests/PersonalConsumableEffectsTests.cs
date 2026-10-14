using System;
using System.Collections.Generic;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public sealed class PersonalConsumableEffectsTests
    {
        [Theory]
        [InlineData("Bubblegum", 30, 0, 0, 30, 0)]
        [InlineData("Soda", 0, 30, 0, 60, 0)]
        [InlineData("EnergyDrink", 0, 55, 0, 180, 0)]
        [InlineData("Protein Shake", 0, 55, 0, 120, 0)]
        [InlineData("Liquor", 0, 0, 55, 0, 120)]
        public void ExactRecipesAddOnlyTheirAuditedEffects(string id, float food, float drink,
            float drug, float speed, float alcohol)
        {
            var before = new PersonalConsumableEffects();
            Assert.True(before.TryUse(id, out var after));
            Assert.NotSame(before, after);
            AssertState(after, food, drink, drug, speed, alcohol, 0);
            AssertState(before, 0, 0, 0, 0, 0, 0);
        }

        [Theory]
        [InlineData("Bubblegum")]
        [InlineData("Soda")]
        [InlineData("EnergyDrink")]
        [InlineData("Protein Shake")]
        [InlineData("Liquor")]
        public void NativeCooldownThresholdIsInclusiveSixty(string id)
        {
            Assert.True(new PersonalConsumableEffects(60, 60, 60).TryUse(id, out _));
            var blocked = new PersonalConsumableEffects(60.001f, 60.001f, 60.001f);
            Assert.False(blocked.TryUse(id, out var unchanged));
            Assert.Same(blocked, unchanged);
        }

        [Theory]
        [InlineData("EnergyDrink", 360, 0)]
        [InlineData("Protein Shake", 240, 0)]
        [InlineData("Liquor", 0, 240)]
        public void FiftyFiveCanBeAddedTwiceButNotThreeTimes(string id, float speed, float alcohol)
        {
            var before = new PersonalConsumableEffects();
            Assert.True(before.TryUse(id, out var first));
            Assert.True(first.TryUse(id, out var second));
            Assert.False(second.TryUse(id, out var rejected));
            Assert.Same(second, rejected);
            AssertState(second, 0, id == "Liquor" ? 0 : 110, id == "Liquor" ? 110 : 0,
                speed, alcohol, 0);
            Assert.Equal(id == "Liquor" ? 55 : 0, first.Drug);
            Assert.Equal(id == "Liquor" ? 0 : 55, first.Drink);
            AssertState(before, 0, 0, 0, 0, 0, 0);
        }

        [Fact]
        public void FoodDrinkAndDrugGatesAreIndependentAndSpeedStacks()
        {
            var before = new PersonalConsumableEffects(61, 0, 61, 10, 20, 7);
            Assert.True(before.TryUse("Soda", out var soda));
            Assert.True(soda.TryUse("EnergyDrink", out var energy));
            AssertState(energy, 61, 85, 61, 250, 20, 7);
            Assert.False(energy.TryUse("Protein Shake", out _));
            Assert.False(energy.TryUse("Bubblegum", out _));
            Assert.False(energy.TryUse("Liquor", out _));
            Assert.True(new PersonalConsumableEffects(0, 61, 61).TryUse("Bubblegum", out _));
            Assert.True(new PersonalConsumableEffects(61, 61, 0).TryUse("Liquor", out _));
            AssertState(before, 61, 0, 61, 10, 20, 7);
        }

        [Fact]
        public void AlcoholUseAddsWithoutResettingPassedTime()
        {
            var before = new PersonalConsumableEffects(alcoholLeft: 25, alcoholPassed: 37);
            Assert.True(before.TryUse("Liquor", out var after));
            AssertState(after, 0, 0, 55, 0, 145, 37);
            AssertState(before, 0, 0, 0, 0, 25, 37);
        }

        [Fact]
        public void EligibleScaledTimeSubtractsAndClampsWithoutMutatingInput()
        {
            var before = new PersonalConsumableEffects(1, 2, 3, 4, 5, 6);
            var partial = before.Advance(0.5f, true);
            Assert.NotSame(before, partial);
            AssertState(partial, 0.5f, 1.5f, 2.5f, 3.5f, 4.5f, 6.5f);
            var expired = partial.Advance(10, true);
            AssertState(expired, 0, 0, 0, 0, 0, 16.5f);
            AssertState(before, 1, 2, 3, 4, 5, 6);
            AssertState(partial, 0.5f, 1.5f, 2.5f, 3.5f, 4.5f, 6.5f);
        }

        [Fact]
        public void IneligibleTicksAndZeroScaledTimePausePositiveTimers()
        {
            var before = new PersonalConsumableEffects(10, 20, 30, 40, 50, 60);
            Assert.Same(before, before.Advance(3600, false));
            var paused = before.Advance(0, true);
            Assert.NotSame(before, paused);
            AssertState(paused, 10, 20, 30, 40, 50, 60);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(10)]
        public void AlcoholPassedResetsOnNextEligibleTickAfterExactOrOvershootExpiration(float delta)
        {
            var before = new PersonalConsumableEffects(alcoholLeft: 2, alcoholPassed: 3);
            var expired = before.Advance(delta, true);
            AssertState(expired, 0, 0, 0, 0, 0, 3 + delta);
            Assert.Same(expired, expired.Advance(10, false));
            var next = expired.Advance(0, true);
            Assert.NotSame(expired, next);
            AssertState(next, 0, 0, 0, 0, 0, 0);
            Assert.Equal(3 + delta, expired.AlcoholPassed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Pheromones")]
        [InlineData("bubblegum")]
        [InlineData("Energy Drink")]
        [InlineData("ProteinShake")]
        [InlineData("Soda ")]
        [InlineData("unknown")]
        public void UnknownOrInexactNamesAreRejected(string? id)
        {
            var before = new PersonalConsumableEffects();
            Assert.False(before.TryUse(id, out var after));
            Assert.Same(before, after);
        }

        public static IEnumerable<object[]> InvalidValues()
        {
            foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity,
                float.NegativeInfinity, float.MaxValue, PersonalConsumableEffects.MaxTimerSeconds + 1 })
                for (int field = 0; field < 6; field++)
                    yield return new object[] { field, value };
        }

        [Theory]
        [MemberData(nameof(InvalidValues))]
        public void ConstructorRejectsEveryInvalidExternalTimer(int field, float value)
        {
            var values = new float[6];
            values[field] = value;
            Assert.Throws<ArgumentOutOfRangeException>(() => new PersonalConsumableEffects(
                values[0], values[1], values[2], values[3], values[4], values[5]));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(float.MaxValue)]
        [InlineData(3600.01f)]
        public void InvalidDeltaIsRejectedEvenWhenIneligible(float delta)
        {
            var before = new PersonalConsumableEffects(1, 2, 3, 4, 5, 6);
            Assert.Throws<ArgumentOutOfRangeException>(() => before.Advance(delta, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => before.Advance(delta, false));
            AssertState(before, 1, 2, 3, 4, 5, 6);
        }

        [Theory]
        [InlineData("Bubblegum", 30)]
        [InlineData("Soda", 60)]
        [InlineData("EnergyDrink", 180)]
        [InlineData("Protein Shake", 120)]
        public void SpeedAdditionAtBoundSucceedsButBeyondBoundIsAtomicRejection(string id, float addition)
        {
            float max = PersonalConsumableEffects.MaxTimerSeconds;
            Assert.True(new PersonalConsumableEffects(speedLeft: max - addition).TryUse(id, out var exact));
            Assert.Equal(max, exact.SpeedLeft);
            var before = new PersonalConsumableEffects(speedLeft: max - addition + 1);
            Assert.False(before.TryUse(id, out var rejected));
            Assert.Same(before, rejected);
            Assert.Equal(0, rejected.Food + rejected.Drink + rejected.Drug);
        }

        [Fact]
        public void AlcoholAdditionAndPassedAdvanceRespectBoundWithoutPartialChanges()
        {
            float max = PersonalConsumableEffects.MaxTimerSeconds;
            Assert.True(new PersonalConsumableEffects(alcoholLeft: max - 120).TryUse("Liquor", out var exact));
            Assert.Equal(max, exact.AlcoholLeft);
            var full = new PersonalConsumableEffects(alcoholLeft: max, alcoholPassed: max);
            Assert.False(full.TryUse("Liquor", out var rejected));
            Assert.Same(full, rejected);
            Assert.Throws<InvalidOperationException>(() => full.Advance(1, true));
            AssertState(full, 0, 0, 0, 0, max, max);
            Assert.Same(full, full.Advance(1, false));
            Assert.Equal(max, new PersonalConsumableEffects(alcoholLeft: 1, alcoholPassed: max - 1)
                .Advance(1, true).AlcoholPassed);
        }

        [Fact]
        public void MaximumStateAndDeltaAreAcceptedAndZeroAlcoholEntryResetsPassed()
        {
            float max = PersonalConsumableEffects.MaxTimerSeconds;
            var before = new PersonalConsumableEffects(max, max, max, max, 0, max);
            var after = before.Advance(PersonalConsumableEffects.MaxScaledDeltaTime, true);
            AssertState(after, max - 3600, max - 3600, max - 3600, max - 3600, 0, 0);
            Assert.Equal(max, before.AlcoholPassed);
        }

        private static void AssertState(PersonalConsumableEffects state, float food, float drink,
            float drug, float speed, float alcohol, float passed)
        {
            Assert.Equal(food, state.Food);
            Assert.Equal(drink, state.Drink);
            Assert.Equal(drug, state.Drug);
            Assert.Equal(speed, state.SpeedLeft);
            Assert.Equal(alcohol, state.AlcoholLeft);
            Assert.Equal(passed, state.AlcoholPassed);
        }
    }
}
