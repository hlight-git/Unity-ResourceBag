using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// Runtime ResourceBag — manages stackable resource amounts with composable rules, side
    /// effects, and a public Tick hook. Constructed from a <see cref="BagBlueprint"/>;
    /// rules attach at construction. Pure C# (no MonoBehaviour).
    /// </summary>
    public partial class ResourceBag : IDisposable
    {
        /// <summary>Maximum recursion depth for side-effect re-entry. Exceeded = log + drop.</summary>
        public const int MaxSideEffectDepth = 5;

        private readonly string _id;
        private readonly BagBlueprint _blueprint;
        private readonly IBagInjector _injector;
        private readonly IBagClock _clock;
        private readonly bool _ownsClock;

        // Pre-sized to blueprint resource count → skip dictionary re-bucketing during boot.
        private readonly Dictionary<ResourceDefinition, int> _amounts;

        // Tentative writes of the open transaction, and the events they produced. Both are
        // empty outside a transaction, and both are reused rather than reallocated.
        private readonly Dictionary<ResourceDefinition, int> _pending =
            new Dictionary<ResourceDefinition, int>();
        private readonly List<ResourceChange> _pendingEvents = new List<ResourceChange>(8);

        // Open-transaction depth. A rule that calls back into a public entry point nests; only
        // the outermost scope commits or discards, which is what makes the whole multi-spend
        // all-or-nothing rather than each item independently.
        private int _tx;
        // Seeded from the blueprint's entries, then mutated by SetMaxAmount. One source, so
        // GetMaxAmount is a single lookup with no fallback chain to reason about.
        private readonly Dictionary<ResourceDefinition, int> _caps;

        private readonly List<AttachedRule> _rules = new List<AttachedRule>();

        private readonly List<ResourceSideEffect> _sideEffectBuffer = new List<ResourceSideEffect>(8);

        private bool _disposed;

        // Current pipeline depth. Public entry points enter at this value rather than 0,
        // so a Changed handler that calls back into the bag is bounded by the same limit
        // as a rule side effect.
        private int _depth;

        /// <summary>Stable identifier for this bag instance (used as persistence key root + log prefix).</summary>
        public string Id => _id;

        /// <summary>
        /// The authored scope this bag was built from. Exposed because the persistence lookup
        /// lives on the blueprint — it is a function of the authored resource list, not of a
        /// bag's runtime state. <see cref="ResourceBag{TKey}"/> narrows this to the typed
        /// blueprint and adds the enum-keyed API.
        /// </summary>
        public BagBlueprint Blueprint => _blueprint;

        /// <summary>
        /// Injector (optional) for rules that need project-side deps. A rule's
        /// <see cref="ResourceRule.Attach"/> pushes into the instance it just built.
        /// </summary>
        public IBagInjector Injector => _injector;

        /// <summary>Time source for time-dependent rules. Never null.</summary>
        public IBagClock Clock => _clock;

        /// <summary>Fires for every accepted amount change (including Add, Spend, restore).</summary>
        public event Action<ResourceChange> Changed;

        /// <summary>
        /// Construct a bag from a blueprint. Rules attach in this order: per-resource rules
        /// (in <c>def.rules</c> order, owner = def), then <c>blueprint.extraRules</c>
        /// (owner = null).
        /// </summary>
        public ResourceBag(string id, BagBlueprint blueprint, BagSnapshot snapshot = null,
                        IBagClock clock = null, IBagInjector injector = null)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            _id = id;
            _blueprint = blueprint;
            _injector = injector;

            // A bag always has a clock, so no rule needs a "no clock" branch. Injecting
            // one (server time) leaves ownership — and its persistence — with the project.
            _ownsClock = clock == null;
            if (_ownsClock)
            {
                // Seed from the snapshot at construction. Loading later would let the
                // clock anchor against empty state first and then move Now backwards,
                // breaking the IBagClock contract.
                _clock = new BagClock(snapshot?.Clock ?? default);
            }
            else
            {
                _clock = clock;
            }

            var resources = blueprint.Entries;
            _amounts = new Dictionary<ResourceDefinition, int>(resources?.Count ?? 0);
            _caps = new Dictionary<ResourceDefinition, int>(resources?.Count ?? 0);

            if (resources != null && resources.Count > 0)
            {
                for (int i = 0; i < resources.Count; i++)
                {
                    var entry = resources[i];
                    var def = entry.Resource;
                    if (def == null) continue;

                    // Duplicate *Id* across two different defs is the blueprint's business and
                    // is reported there. This one is about seeding order, which is ours.
                    if (_amounts.ContainsKey(def))
                    {
                        Debug.LogWarning(
                            $"[ResourceBag:{_id}] Duplicate ResourceDefinition '{def.Id}' in blueprint.Entries — " +
                            "the later entry's initialAmount wins; rule attach is idempotent.");
                    }

                    // Pre-seed every in-scope def, so no code path has to cope with a
                    // missing key. initialAmount deliberately bypasses cap clamping.
                    _amounts[def] = entry.InitialAmount > 0 ? entry.InitialAmount : 0;
                    _caps[def] = entry.MaxAmount > 0 ? entry.MaxAmount : 0;

                    // Attach per-resource rules.
                    var rules = def.Rules;
                    if (rules == null) continue;
                    for (int j = 0; j < rules.Length; j++)
                    {
                        var rule = rules[j];
                        if (rule == null) continue;
                        AttachRule(rule, def);
                    }
                }
            }

            // Report authoring problems when a scope is first used, not whenever a save
            // happens to touch the Id map. Cached, so this costs nothing after the first bag.
            blueprint.EnsureLookupsBuilt();

            var extras = blueprint.ExtraRules;
            if (extras != null)
            {
                for (int i = 0; i < extras.Length; i++)
                {
                    var rule = extras[i];
                    if (rule != null) AttachRule(rule, null);
                }
            }

            if (snapshot != null) ApplySnapshot(snapshot);
        }

        // Restore is silent by design: no subscriber can exist during construction, and
        // a boot-time burst of restore events is noise the UI would only have to filter.
        private void ApplySnapshot(BagSnapshot snapshot)
        {
            if (snapshot.Version > BagSnapshot.CurrentVersion)
            {
                Debug.LogWarning(
                    $"[ResourceBag:{_id}] Snapshot version {snapshot.Version} is newer than supported " +
                    $"{BagSnapshot.CurrentVersion}; loading anyway — unknown fields are ignored.");
            }

            var amounts = snapshot.Amounts;
            if (amounts != null)
            {
                for (int i = 0; i < amounts.Count; i++)
                {
                    var entry = amounts[i];
                    if (string.IsNullOrEmpty(entry.id)) continue;
                    if (!_blueprint.TryGetById(entry.id, out var def)) continue;

                    // Trust boundary: this is player-side data. Clamp before it can
                    // poison rule maths (a negative balance defeats every HasAtLeast).
                    var value = entry.amount;
                    if (value < 0) value = 0;
                    var cap = GetMaxAmount(def);
                    if (cap > 0 && value > cap) value = cap;

                    _amounts[def] = value;
                }
            }

            // Driven by the rules, not by the file: a row nobody claims is ignored, and a rule
            // with no row is never called at all, so it keeps what its constructor set.
            var ruleState = snapshot.RuleState;
            if (ruleState != null)
            {
                for (int i = 0; i < _rules.Count; i++)
                {
                    var rule = _rules[i];
                    if (string.IsNullOrEmpty(rule.StateKey)) continue;
                    var key = StateKeyOf(rule);
                    for (int j = 0; j < ruleState.Count; j++)
                    {
                        if (ruleState[j].key != key) continue;
                        rule.LoadState(ruleState[j].value);
                        break;
                    }
                }
            }
        }

        // Owner scopes the key, so the same rule SO on two defs persists separately. Rules
        // supply only the suffix and never see this.
        private static string StateKeyOf(AttachedRule rule)
            => string.Concat(rule.Owner != null ? rule.Owner.Id : "_", ":", rule.StateKey);

        /// <summary>
        /// Current amount of <paramref name="resource"/> (0 if untracked). Inside an open
        /// transaction this reports the **tentative** amount, so a rule deciding mid-transaction
        /// sees what earlier steps of that same transaction already spent or credited.
        /// </summary>
        public int GetAmount(ResourceDefinition resource)
        {
            if (resource == null) return 0;
            if (_tx > 0 && _pending.TryGetValue(resource, out var tentative)) return tentative;
            return _amounts.TryGetValue(resource, out var v) ? v : 0;
        }

        // The single write funnel. Inside a transaction nothing touches _amounts: writes land in
        // the tentative layer and are folded in at commit, or dropped entirely on failure. That
        // is what makes a failed multi-spend leave no trace — no debit-then-restore, and no
        // events for changes that did not survive.
        private void SetAmount(ResourceDefinition resource, int value)
        {
            if (_tx > 0) _pending[resource] = value;
            else _amounts[resource] = value;
        }

        /// <summary>True if amount of <paramref name="resource"/> ≥ <paramref name="amount"/>.</summary>
        public bool HasAtLeast(ResourceDefinition resource, int amount) => GetAmount(resource) >= amount;

        /// <summary>Effective max amount for <paramref name="resource"/>: per-bag override else def default. 0 = unlimited.</summary>
        public int GetMaxAmount(ResourceDefinition resource)
        {
            if (resource == null) return 0;
            return _caps.TryGetValue(resource, out var cap) ? cap : 0;
        }

        /// <summary>
        /// Change this bag's cap for <paramref name="resource"/>. 0 = unlimited. Does NOT
        /// auto-clamp the current amount — lowering below what is held leaves it held.
        /// </summary>
        /// <remarks>
        /// The authored cap comes from the blueprint entry; this is the runtime plug-point for
        /// remote config or a progression unlock.
        /// </remarks>
        public void SetMaxAmount(ResourceDefinition resource, int maxAmount)
        {
            if (resource == null) return;
            _caps[resource] = maxAmount < 0 ? 0 : maxAmount;
        }

        /// <summary>Zero out <paramref name="resource"/>. Fires Changed with the supplied reason.</summary>
        public void ResetAmount(ResourceDefinition resource, string reason = BagReasons.Unspecified)
        {
            if (resource == null) return;
            var old = GetAmount(resource);
            if (old == 0) return;
            SetAmount(resource, 0);
            FireChanged(new ResourceChange(resource, -old, old, 0, reason));
        }

        /// <summary>
        /// Subscribe to changes of one resource. Returns the unsubscribe call — hold it and
        /// invoke it in <c>OnDestroy</c>:
        /// <code>
        /// _unbind = bag.Bind(coinDef, amount =&gt; label.text = amount.ToString());
        /// </code>
        /// </summary>
        /// <remarks>
        /// Fires only when the amount actually moved: a clamped credit reports a zero delta to
        /// <see cref="Changed"/> (for analytics) but is not a change to bind against. Nothing is
        /// pushed at subscribe time — read <see cref="GetAmount"/> for the current value.
        /// <para>
        /// Because events are buffered until a transaction commits, a handler here never sees a
        /// value that was rolled back, and never runs at all for a failed multi-spend.
        /// </para>
        /// </remarks>
        public Action Bind(ResourceDefinition resource, Action<int> onChanged)
        {
            if (resource == null || onChanged == null) return static () => { };

            void Handler(ResourceChange change)
            {
                if (change.Resource == resource && change.Delta != 0) onChanged(change.NewAmount);
            }

            Changed += Handler;
            return () => Changed -= Handler;
        }

        /// <summary>
        /// Attach a rule with an explicit <paramref name="owner"/> def (or null for
        /// extraRules-style attach). Idempotent per (rule, owner) pair. Calls
        /// <see cref="ResourceRule.Attach"/> then <see cref="AttachedRule.OnAttach"/>.
        /// No-op after <see cref="Dispose"/>.
        /// </summary>
        public void AttachRule(ResourceRule rule, ResourceDefinition owner = null)
        {
            if (_disposed || rule == null) return;
            for (int i = 0; i < _rules.Count; i++)
            {
                var existing = _rules[i];
                if (existing.Source == rule && existing.Owner == owner) return;
            }
            var attached = rule.Attach(this, owner);
            if (attached == null) return;
            _rules.Add(attached);
            attached.OnAttach();
        }

        /// <summary>
        /// Detach every <see cref="AttachedRule"/> whose <see cref="AttachedRule.Source"/>
        /// is <paramref name="rule"/> — across all owners. No-op after <see cref="Dispose"/>.
        /// </summary>
        public void DetachRule(ResourceRule rule)
        {
            if (_disposed || rule == null) return;
            for (int i = _rules.Count - 1; i >= 0; i--)
            {
                if (_rules[i].Source != rule) continue;
                _rules[i].OnDetach();
                _rules.RemoveAt(i);
            }
        }

        /// <summary>
        /// Instance rule đã attach cho <paramref name="owner"/>, hoặc false khi không có. Cách duy
        /// nhất đọc được trạng thái sống của một rule — mốc bắn kế của một rule hồi theo thời gian là
        /// thứ UI phải vẽ, và nó chỉ tồn tại trong instance chứ không trong SO.
        /// </summary>
        public bool TryGetAttached<T>(ResourceDefinition owner, out T rule) where T : AttachedRule
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                if (_rules[i].Owner != owner || _rules[i] is not T typed) continue;
                rule = typed;
                return true;
            }

            rule = null;
            return false;
        }

        /// <summary>
        /// Drive rule ticks. Project owns cadence — call from Update / FixedUpdate / a
        /// task driver. <see cref="Clock"/> is read once and shared by every rule.
        /// </summary>
        public void Tick()
        {
            var now = _clock.Now;
            for (int i = 0; i < _rules.Count; i++)
            {
                _rules[i].OnTick(now);
            }
        }

        /// <summary>
        /// Capture amounts, rule state, and (when this bag owns its clock) clock state.
        /// Lists are cleared then refilled, so reusing one snapshot across saves does
        /// not allocate.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="snapshot"/> is null. This is the opposite of the constructor's
        /// nullable <c>snapshot</c> parameter: there, null means "fresh bag, nothing to
        /// read." Here there is nothing to write into, and a project whose save field
        /// starts null would otherwise lose every save silently — failing loudly on the
        /// first flush beats losing data forever.
        /// </exception>
        public void SaveTo(BagSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            snapshot.Version = BagSnapshot.CurrentVersion;

            (snapshot.Amounts ??= new List<BagSnapshot.AmountEntry>()).Clear();
            foreach (var kv in _amounts)
            {
                if (kv.Key == null) continue;
                snapshot.Amounts.Add(new BagSnapshot.AmountEntry
                {
                    id = kv.Key.Id,
                    amount = kv.Value,
                });
            }

            (snapshot.RuleState ??= new List<BagSnapshot.RuleStateEntry>()).Clear();
            for (int i = 0; i < _rules.Count; i++)
            {
                var rule = _rules[i];
                if (string.IsNullOrEmpty(rule.StateKey)) continue;
                // Empty counts as nothing to persist, same as null. Writing the row anyway
                // would come back through LoadState as an unparsable value and get reported
                // as a corrupt save — one we wrote ourselves.
                var value = rule.SaveState();
                if (string.IsNullOrEmpty(value)) continue;

                var key = StateKeyOf(rule);

                // Two rows under one key: loading matches on the first, so BOTH rules end up
                // restoring the first one's state — the second silently adopts a sibling's
                // countdown. Same class of failure as the blueprint's duplicate-Id warning,
                // and just as invisible without saying so.
                for (int j = 0; j < snapshot.RuleState.Count; j++)
                {
                    if (snapshot.RuleState[j].key != key) continue;
                    Debug.LogWarning(
                        $"[ResourceBag:{_id}] Rule state key '{key}' written twice — give one of " +
                        "the two rules a different StateKey, or on load both will restore the " +
                        "first one's state.");
                    break;
                }

                snapshot.RuleState.Add(new BagSnapshot.RuleStateEntry { key = key, value = value });
            }

            // An injected clock belongs to whoever injected it, including its persistence.
            if (_ownsClock && _clock is BagClock owned) snapshot.Clock = owned.State;
        }

        /// <summary>Dispose: detach all rules in reverse attach order, clear state, null events.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = _rules.Count - 1; i >= 0; i--)
            {
                _rules[i].OnDetach();
            }
            _rules.Clear();
            _amounts.Clear();
            _pending.Clear();
            _pendingEvents.Clear();
            _tx = 0;
            _caps.Clear();
            Changed = null;
            if (_ownsClock && _clock is IDisposable disposableClock) disposableClock.Dispose();
        }

        private void FireChanged(ResourceChange change)
        {
            // Inside a transaction the change has not survived yet, so it is buffered rather
            // than announced. Subscribers must never see a value that a later step undoes.
            if (_tx > 0) { _pendingEvents.Add(change); return; }
            Changed?.Invoke(change);
        }

        // ------------------------------- Transactions -------------------------------
        //
        // Every public mutation runs inside one. Rules still execute for real — substitution,
        // a free pass, a rejection, side effects — but their writes land in the tentative layer
        // and reads see that layer, so "can all of this be spent together?" is answered by
        // running it, not by guessing from balances, and a failure leaves nothing behind.

        private void BeginTransaction() => _tx++;

        /// <summary>
        /// Closes the innermost scope. Only the outermost one settles: a nested failure (a rule
        /// calling back into a public entry point) reports false to its own caller without
        /// killing the outer transaction, exactly as before — but if the outer one fails, its
        /// writes go too.
        /// </summary>
        private void EndTransaction(bool commit)
        {
            if (--_tx > 0) return;

            if (!commit)
            {
                _pending.Clear();
                _pendingEvents.Clear();
                return;
            }

            foreach (var kv in _pending) _amounts[kv.Key] = kv.Value;
            _pending.Clear();

            if (_pendingEvents.Count == 0) return;

            // Announce after the state is final, and from a copy: a handler is allowed to call
            // back into the bag, which opens a new transaction and would otherwise clear the
            // list being iterated. Pooled, so this costs nothing in steady state.
            using (ListPool<ResourceChange>.Get(out var settled))
            {
                settled.AddRange(_pendingEvents);
                _pendingEvents.Clear();

                // Dispatch at an elevated depth. A handler is allowed to call back into the
                // bag, and that re-entry has to stay bounded by MaxSideEffectDepth exactly as a
                // rule's side effect is — while events fired mid-pipeline that came for free,
                // announcing them after the commit would otherwise recurse until the stack dies.
                _depth++;
                try
                {
                    for (int i = 0; i < settled.Count; i++) Changed?.Invoke(settled[i]);
                }
                finally
                {
                    _depth--;
                }
            }
        }
    }
}
