using System;

namespace HoloTable.Domain.Mtg
{
    public enum TapTransition
    {
        None = 0,
        Tapped = 1,
        Untapped = 2,
    }

    /// <summary>Immutable tap state of a permanent plus the yaw captured when it entered play.</summary>
    public sealed record TapState(bool IsTapped, float ReferenceYawDegrees);

    /// <summary>
    /// Turns the noisy yaw of a tracked card into clean tap/untap events.
    /// Hysteresis (tap at ≥65°, untap at ≤25°) prevents flicker from tracking jitter,
    /// and folding the angle makes a 180° upside-down card count as untapped.
    /// </summary>
    public static class TapRules
    {
        public const float DefaultTapThreshold = 65f;
        public const float DefaultUntapThreshold = 25f;

        /// <summary>Signed smallest difference current-reference, in (-180, 180].</summary>
        public static float SignedDelta(float referenceYaw, float currentYaw)
        {
            float delta = (currentYaw - referenceYaw) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta <= -180f) delta += 360f;
            return delta;
        }

        /// <summary>Angle away from the untapped orientation, folded into [0, 90].</summary>
        public static float TapAngle(float referenceYaw, float currentYaw)
        {
            float abs = Math.Abs(SignedDelta(referenceYaw, currentYaw));
            return abs > 90f ? 180f - abs : abs;
        }

        public static (TapState State, TapTransition Transition) Evaluate(
            TapState state,
            float currentYaw,
            float tapThreshold = DefaultTapThreshold,
            float untapThreshold = DefaultUntapThreshold)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (untapThreshold >= tapThreshold)
            {
                throw new ArgumentException("Untap threshold must be lower than tap threshold (hysteresis).");
            }

            float angle = TapAngle(state.ReferenceYawDegrees, currentYaw);

            if (!state.IsTapped && angle >= tapThreshold)
            {
                return (state with { IsTapped = true }, TapTransition.Tapped);
            }

            if (state.IsTapped && angle <= untapThreshold)
            {
                return (state with { IsTapped = false }, TapTransition.Untapped);
            }

            return (state, TapTransition.None);
        }
    }
}
