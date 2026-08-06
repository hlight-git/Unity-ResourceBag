using System;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Deterministic IBagClock for rule tests. Enforces the monotonic contract so a
    /// test that tries to move time backwards fails loudly instead of silently
    /// exercising behaviour no real clock can produce.
    /// </summary>
    internal sealed class FakeBagClock : IBagClock
    {
        private double _now;

        // A plausible Unix timestamp, so rules see values in the real domain.
        public FakeBagClock(double start = 1_700_000_000) => _now = start;

        public double Now => _now;

        public void Advance(double seconds)
        {
            if (seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds), "IBagClock must never go backwards");
            _now += seconds;
        }

        public void SetNow(double seconds)
        {
            if (seconds < _now)
                throw new ArgumentOutOfRangeException(nameof(seconds), "IBagClock must never go backwards");
            _now = seconds;
        }
    }
}
