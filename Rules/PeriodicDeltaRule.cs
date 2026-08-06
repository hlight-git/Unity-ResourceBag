using System;
using UnityEngine;

namespace Hlight.ResourceBag.Rules
{
    /// <summary>
    /// Periodic signed delta on the owner resource: every <c>intervalSec</c>, apply
    /// <c>amountPerInterval</c> — positive regenerates, negative decays. Runs on
    /// <see cref="ResourceBag.Clock"/> using an absolute next-fire time, so it survives
    /// app close once the bag's snapshot is persisted.
    /// </summary>
    /// <remarks>
    /// Blocked (at cap when regenerating, at zero when decaying) resets the countdown
    /// rather than accruing a debt, so draining a full resource means waiting a whole
    /// interval instead of receiving a stored burst — <b>but only if at least one
    /// <see cref="ResourceBag.Tick"/> happens between reaching the block and the spend.</b>
    /// The reset lives inside <c>OnTick</c>, so a project that only ticks while some UI
    /// is open (a valid choice — see README's "from whichever driver you prefer") can sit
    /// blocked with a stale next-fire time; the first tick after the spend then replays
    /// the whole accumulated catch-up as an instant refill instead of pausing.
    /// </remarks>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag/Rules/Periodic Delta", fileName = "PeriodicDeltaRule", order = 10)]
    public class PeriodicDeltaRule : ResourceRule
    {
        /// <summary>Seconds between applications.</summary>
        [SerializeField] private float intervalSec = 1f;

        /// <summary>Signed amount applied each interval. Positive regenerates, negative decays.</summary>
        [SerializeField] private int amountPerInterval = 1;

        private const string DefaultStateKey = "periodic";

        /// <summary>Persistence key suffix. Change it when one def carries two of these rules.</summary>
        [SerializeField] private string stateKey = DefaultStateKey;

        /// <inheritdoc />
        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => owner != null ? new Instance(this, owner, bag) : null;

        [Serializable]
        private sealed class PeriodicState
        {
            public double nextFireAt;
        }

        private sealed class Instance : AttachedRule<PeriodicState>
        {
            // Backstop against a corrupt or cross-epoch next-fire time.
            private const int MaxCatchUpFires = 100_000;
            private const double MaxPlausibleDrift = 30 * 24 * 3600;

            private readonly PeriodicDeltaRule _cfg;

            public Instance(PeriodicDeltaRule cfg, ResourceDefinition owner, ResourceBag bag)
                : base(cfg, owner, bag)
            {
                _cfg = cfg;
                State.nextFireAt = bag.Clock.Now + cfg.intervalSec;
            }

            // Falls back to the field's own default: an empty key would make the bag treat this
            // rule as stateless, so a designer who cleared the field would silently lose the
            // countdown on every launch instead of seeing anything go wrong.
            public override string StateKey
                => string.IsNullOrEmpty(_cfg.stateKey) ? DefaultStateKey : _cfg.stateKey;

            protected override void OnStateLoaded()
            {
                // A fire time only means something on this bag's clock. A corrupt save — or an
                // IBagClock swapped for one on another epoch — restores a value millions of
                // seconds away; the catch-up clamp in OnTick keeps that from crashing, but the
                // behaviour would still be wrong, so start the interval over instead.
                var now = Bag.Clock.Now;
                if (State.nextFireAt > 0 && Math.Abs(State.nextFireAt - now) <= MaxPlausibleDrift) return;

                Debug.LogWarning(
                    $"[ResourceBag] PeriodicDeltaRule state '{StateKey}' for '{Owner?.Id}' is " +
                    $"implausible ({State.nextFireAt:F0} against now {now:F0}) — treating as a " +
                    "first run. Usually a corrupt save or an IBagClock whose epoch differs.");
                State.nextFireAt = now + _cfg.intervalSec;
            }

            public override void OnTick(double now)
            {
                if (_cfg.intervalSec <= 0f || _cfg.amountPerInterval == 0) return;

                // Blocked: reset the countdown instead of banking the interval.
                if (_cfg.amountPerInterval > 0)
                {
                    var cap = Bag.GetMaxAmount(Owner);
                    if (cap > 0 && Bag.GetAmount(Owner) >= cap)
                    {
                        State.nextFireAt = now + _cfg.intervalSec;
                        return;
                    }
                }
                else if (Bag.GetAmount(Owner) <= 0)
                {
                    State.nextFireAt = now + _cfg.intervalSec;
                    return;
                }

                if (now < State.nextFireAt) return;

                // Clamp in double space BEFORE the cast: casting an out-of-range double
                // to int is undefined in an unchecked context and can yield int.MinValue,
                // which would make every guard below useless.
                var raw = (now - State.nextFireAt) / _cfg.intervalSec + 1.0;
                if (raw < 1.0) raw = 1.0;
                if (raw > MaxCatchUpFires) raw = MaxCatchUpFires;
                var missed = (int)raw;

                // One batched change rather than a loop: eight hours at a one-second
                // interval would otherwise be 28,800 iterations in a single frame.
                var total = (long)missed * _cfg.amountPerInterval;
                if (total > int.MaxValue) total = int.MaxValue;
                if (total < -int.MaxValue) total = -int.MaxValue;

                if (total > 0)
                {
                    Bag.Add(Owner, (int)total, RuleReasons.Periodic);
                }
                else
                {
                    // Unlike Add (which clamps to cap internally), TrySpend is all-or-nothing —
                    // a catch-up total larger than the balance would spend nothing at all.
                    // Clamp here so decay still stops exactly at zero.
                    var spend = (int)Math.Min(-total, Bag.GetAmount(Owner));
                    if (spend > 0) Bag.TrySpend(Owner, spend, RuleReasons.Periodic);
                }

                State.nextFireAt += missed * (double)_cfg.intervalSec;
                if (State.nextFireAt <= now) State.nextFireAt = now + _cfg.intervalSec;
            }
        }
    }
}
