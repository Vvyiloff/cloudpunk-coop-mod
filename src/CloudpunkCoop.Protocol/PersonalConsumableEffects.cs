using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Detached immutable timers, in scaled seconds. Only the five audited recipes are supported.
    /// Quantity, ownership, story eligibility, persistence and request deduplication belong to the caller.
    /// The caller also chooses eligibility and any normalization of native negative values before import.
    /// This reducer has no clock, offline decay, native events, callbacks, audio or presentation effects.
    /// </summary>
    public sealed class PersonalConsumableEffects
    {
        // Defensive application limits, NOT native constants: a day of accumulated timer state
        // allows substantial stacking, while an hour per tick allows stalls without accepting
        // arbitrary offline intervals. Both keep float arithmetic far from numeric overflow.
        public const float MaxTimerSeconds = 86400f;
        public const float MaxScaledDeltaTime = 3600f;
        public const float ReuseCooldownThreshold = 60f;

        public float Food { get; }
        public float Drink { get; }
        public float Drug { get; }
        public float SpeedLeft { get; }
        public float AlcoholLeft { get; }
        public float AlcoholPassed { get; }

        public PersonalConsumableEffects(float food = 0, float drink = 0, float drug = 0,
            float speedLeft = 0, float alcoholLeft = 0, float alcoholPassed = 0)
        {
            Food = Validate(food, MaxTimerSeconds, nameof(food));
            Drink = Validate(drink, MaxTimerSeconds, nameof(drink));
            Drug = Validate(drug, MaxTimerSeconds, nameof(drug));
            SpeedLeft = Validate(speedLeft, MaxTimerSeconds, nameof(speedLeft));
            AlcoholLeft = Validate(alcoholLeft, MaxTimerSeconds, nameof(alcoholLeft));
            AlcoholPassed = Validate(alcoholPassed, MaxTimerSeconds, nameof(alcoholPassed));
        }

        /// <summary>
        /// Matches exact, case-sensitive item names. On rejection, result is this unchanged state.
        /// On success, result is a distinct state; no inventory is consumed by this operation.
        /// Additions exceeding the accepted state bound are rejected atomically, never saturated.
        /// </summary>
        public bool TryUse(string? itemId, out PersonalConsumableEffects result)
        {
            result = this;
            float food = Food, drink = Drink, drug = Drug, speed = SpeedLeft, alcohol = AlcoholLeft;
            switch (itemId)
            {
                case "Bubblegum":
                    if (Food > ReuseCooldownThreshold) return false;
                    food += 30f; speed += 30f;
                    break;
                case "Soda":
                    if (Drink > ReuseCooldownThreshold) return false;
                    drink += 30f; speed += 60f;
                    break;
                case "EnergyDrink":
                    if (Drink > ReuseCooldownThreshold) return false;
                    drink += 55f; speed += 180f;
                    break;
                case "Protein Shake":
                    if (Drink > ReuseCooldownThreshold) return false;
                    drink += 55f; speed += 120f;
                    break;
                case "Liquor":
                    if (Drug > ReuseCooldownThreshold) return false;
                    drug += 55f; alcohol += 120f;
                    break;
                default:
                    // Pheromones is deliberately excluded: its lifetime is unresolved.
                    return false;
            }

            if (food > MaxTimerSeconds || drink > MaxTimerSeconds || drug > MaxTimerSeconds ||
                speed > MaxTimerSeconds || alcohol > MaxTimerSeconds)
                return false;
            result = new PersonalConsumableEffects(food, drink, drug, speed, alcohol, AlcoholPassed);
            return true;
        }

        /// <summary>
        /// Applies one caller-supplied scaled tick. Invalid delta is rejected even when ineligible.
        /// Ineligible ticks preserve this state. Eligible ticks return a distinct immutable state.
        /// AlcoholPassed gains the full delta if AlcoholLeft was positive on entry, even on
        /// overshoot; it resets only on a subsequent eligible tick entering with zero left.
        /// A passed-time addition beyond the state bound throws without changing this state.
        /// </summary>
        public PersonalConsumableEffects Advance(float scaledDeltaTime, bool eligible)
        {
            Validate(scaledDeltaTime, MaxScaledDeltaTime, nameof(scaledDeltaTime));
            if (!eligible) return this;

            float passed = 0f;
            if (AlcoholLeft > 0f)
            {
                // Check before adding; never partially apply a tick or silently clamp passed time.
                if (scaledDeltaTime > MaxTimerSeconds - AlcoholPassed)
                    throw new InvalidOperationException("Alcohol passed time would exceed the accepted timer bound.");
                passed = AlcoholPassed + scaledDeltaTime;
            }
            return new PersonalConsumableEffects(
                Remaining(Food, scaledDeltaTime), Remaining(Drink, scaledDeltaTime),
                Remaining(Drug, scaledDeltaTime), Remaining(SpeedLeft, scaledDeltaTime),
                Remaining(AlcoholLeft, scaledDeltaTime), passed);
        }

        /// <summary>Advance a batch of eligible native ticks without losing the last
        /// tick's entry condition. A heartbeat with no ticks must not reset passed time.</summary>
        public PersonalConsumableEffects AdvanceTicks(float totalDelta, float lastDelta, bool hadTicks)
        {
            Validate(totalDelta, MaxTimerSeconds, nameof(totalDelta));
            Validate(lastDelta, MaxScaledDeltaTime, nameof(lastDelta));
            if(lastDelta>totalDelta || !hadTicks && totalDelta!=0)
                throw new ArgumentOutOfRangeException(nameof(lastDelta));
            if(!hadTicks)return this;
            float passed=0;
            if(AlcoholLeft>0 && totalDelta-lastDelta<AlcoholLeft)
            {
                if(totalDelta>MaxTimerSeconds-AlcoholPassed)
                    throw new InvalidOperationException("Alcohol passed time would exceed the accepted timer bound.");
                passed=AlcoholPassed+totalDelta;
            }
            return new PersonalConsumableEffects(Remaining(Food,totalDelta),Remaining(Drink,totalDelta),
                Remaining(Drug,totalDelta),Remaining(SpeedLeft,totalDelta),Remaining(AlcoholLeft,totalDelta),passed);
        }

        private static float Remaining(float value, float delta) => Math.Max(0f, value - delta);

        private static float Validate(float value, float max, string name)
        {
            if (!float.IsFinite(value) || value < 0f || value > max)
                throw new ArgumentOutOfRangeException(name, value, "Expected finite, nonnegative seconds within the documented bound.");
            return value;
        }
    }
}
