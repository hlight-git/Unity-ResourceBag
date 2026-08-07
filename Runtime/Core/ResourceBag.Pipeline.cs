using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Hlight.ResourceBag
{
    public partial class ResourceBag
    {
        /// <summary>Credit <paramref name="amount"/> of <paramref name="resource"/> through the rule pipeline.</summary>
        public void Add(ResourceDefinition resource, int amount, string reason = BagReasons.Unspecified)
        {
            if (resource == null || amount <= 0) return;

            BeginTransaction();
            var reached = false;
            try
            {
                var intent = new ResourceIntent(resource, amount, reason);
                ChangeInternal(ref intent, _depth, out _);
                reached = true;                    // a credit has no failure mode of its own
            }
            finally
            {
                // finally, not a straight call: a rule that throws would otherwise leave the
                // transaction open forever, and from then on every write would sit in the
                // tentative layer unwritten while every event stayed buffered — a bag that
                // looks alive and silently stops recording anything.
                EndTransaction(reached);
            }
        }

        /// <summary>
        /// Attempt to debit <paramref name="amount"/> of <paramref name="resource"/> through
        /// the rule pipeline. True when accepted — including a skipped primary (free
        /// pass) and a successful substitution.
        /// </summary>
        public bool TrySpend(ResourceDefinition resource, int amount, string reason = BagReasons.Unspecified)
        {
            if (resource == null || amount <= 0) return false;

            BeginTransaction();
            var success = false;
            try
            {
                var intent = new ResourceIntent(resource, -amount, reason);
                ChangeInternal(ref intent, _depth, out success);
            }
            finally
            {
                // A failed debit takes its rules' side effects down with it — before the
                // tentative layer those landed anyway whenever the failure was a short balance
                // rather than an outright Reject, so a spend that did not happen could still pay
                // out. A throwing rule counts as a failure for the same reason.
                EndTransaction(success);
            }

            return success;
        }

        /// <summary>
        /// All-or-nothing multi-debit: every entry runs against a tentative state, and the whole
        /// set is written only if all of them clear. A failure writes nothing and announces
        /// nothing — no debit-then-restore, no events for amounts that never settled.
        /// </summary>
        /// <remarks>
        /// The entries genuinely run rather than being pre-checked against balances, because
        /// balances do not decide the outcome: a rule may substitute another resource, wave the
        /// cost through, or reject a spend the balance could afford. Running them on a tentative
        /// layer is what makes "can all of this be spent together?" answerable — including when
        /// two entries compete for the same substitute, where the answer depends on order.
        /// </remarks>
        public bool TrySpendAll(IReadOnlyList<(ResourceDefinition resource, int amount)> items,
                                string reason = BagReasons.Unspecified)
        {
            if (items == null || items.Count == 0) return true;

            BeginTransaction();
            var all = false;
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var (resource, amount) = items[i];
                    if (resource == null || amount <= 0) continue;
                    var intent = new ResourceIntent(resource, -amount, reason);
                    ChangeInternal(ref intent, _depth, out var success);
                    if (!success) return false;    // finally discards the tentative writes
                }

                all = true;
                return true;
            }
            finally
            {
                EndTransaction(all);
            }
        }

        private void ChangeInternal(ref ResourceIntent intent, int depth, out bool success)
        {
            success = false;

            if (depth > MaxSideEffectDepth)
            {
                Debug.LogError(
                    $"[ResourceBag:{_id}] Side-effect depth exceeded ({MaxSideEffectDepth}); " +
                    $"dropping intent for '{intent.Resource?.Id}'.");
                return;
            }

            _depth = depth + 1;
            try
            {
                var bufferStart = _sideEffectBuffer.Count;
                var isDebit = intent.Delta < 0;

                // Read Count each iteration: a rule may detach itself or a sibling.
                for (int i = 0; i < _rules.Count; i++)
                {
                    if (isDebit)
                    {
                        // Reject is sticky. Rules are asked to respect it, but etiquette
                        // is not enforcement — a rule writes straight into ref intent.
                        var wasRejected = intent.Outcome == SpendOutcome.Reject;
                        _rules[i].OnBeforeSpend(ref intent, _sideEffectBuffer);
                        if (wasRejected) intent.Outcome = SpendOutcome.Reject;
                    }
                    else
                    {
                        _rules[i].OnBeforeAdd(ref intent, _sideEffectBuffer);
                    }
                }

                success = ApplyPrimary(ref intent, isDebit);

                var count = _sideEffectBuffer.Count - bufferStart;
                if (count <= 0) return;

                // Reject aborts the whole transaction, so nothing accumulated may land.
                if (isDebit && intent.Outcome == SpendOutcome.Reject)
                {
                    _sideEffectBuffer.RemoveRange(bufferStart, count);
                    return;
                }

                // Copy our slice out and clear it from the shared buffer so nested
                // re-entry sees a clean window. Zero-alloc in steady state.
                using (ListPool<ResourceSideEffect>.Get(out var slice))
                {
                    for (int i = 0; i < count; i++) slice.Add(_sideEffectBuffer[bufferStart + i]);
                    _sideEffectBuffer.RemoveRange(bufferStart, count);

                    for (int i = 0; i < count; i++)
                    {
                        var effect = slice[i];
                        if (effect.Resource == null || effect.Delta == 0) continue;
                        var nested = new ResourceIntent(effect.Resource, effect.Delta, effect.Reason);
                        ChangeInternal(ref nested, depth + 1, out _);
                    }
                }
            }
            finally
            {
                _depth = depth;
            }
        }

        private bool ApplyPrimary(ref ResourceIntent intent, bool isDebit)
        {
            if (!isDebit)
            {
                if (!intent.SkipPrimary && intent.Resource != null && intent.Delta > 0)
                    ApplyDirect(intent.Resource, intent.Delta, intent.Reason);
                return true;
            }

            switch (intent.Outcome)
            {
                case SpendOutcome.Reject:
                    return false;

                case SpendOutcome.Substitute:
                    if (intent.SubstituteWith == null || intent.SubstituteAmount <= 0) return false;
                    if (GetAmount(intent.SubstituteWith) < intent.SubstituteAmount) return false;
                    ApplyDirect(intent.SubstituteWith, -intent.SubstituteAmount, intent.Reason);
                    return true;

                default:
                    // A skipped debit is the free pass: success, no deduction.
                    if (intent.SkipPrimary) return true;
                    if (intent.Resource == null || intent.Delta >= 0) return false;
                    var needed = -intent.Delta;
                    if (GetAmount(intent.Resource) < needed) return false;
                    ApplyDirect(intent.Resource, intent.Delta, intent.Reason);
                    return true;
            }
        }

        // Signed apply, clamped at the cap above and at zero below.
        private void ApplyDirect(ResourceDefinition resource, int delta, string reason)
        {
            var current = GetAmount(resource);

            if (delta > 0)
            {
                var cap = GetMaxAmount(resource);
                var effectiveCap = cap <= 0 ? int.MaxValue : cap;
                var target = current + delta;
                if (target < 0) target = int.MaxValue;              // overflow guard
                var clamped = target > effectiveCap ? effectiveCap : target;
                var accepted = clamped - current;
                if (accepted <= 0)
                {
                    FireChanged(new ResourceChange(resource, 0, current, current, BagReasons.Overflow));
                    return;
                }

                SetAmount(resource, clamped);
                FireChanged(new ResourceChange(resource, accepted, current, clamped, reason));
                return;
            }

            var next = current + delta;
            if (next < 0) next = 0;
            var actual = current - next;
            if (actual <= 0) return;
            SetAmount(resource, next);
            FireChanged(new ResourceChange(resource, -actual, current, next, reason));
        }
    }
}
