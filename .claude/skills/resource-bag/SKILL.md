---
name: resource-bag
description: "Use the Hlight.ResourceBag Unity package (Packages/com.hlight.resource-bag) to model stackable, count-only resources — currencies (coin, gem), hearts/lives, boosters, consumables, event tickets. Composable behavior via ScriptableObject rules (PeriodicDelta — regen/decay, OverflowConvert, BundleResolve, Substitute). Topics: defining resources, enum-keyed access via ResourceDefinition<TKey>, BagBlueprint authoring, writing custom ResourceRule, cross-bag rules via IBagServiceLocator, persistence via BagSnapshot (constructor load + SaveTo), server-time clocks via IBagClock, tick driver pattern, side-effect depth, TrySpendAll atomicity. Trigger when user mentions ResourceBag, BagBlueprint, ResourceDefinition, ResourceRule, PeriodicDeltaRule, currencies, hearts, boosters, stackable inventory — even if they don't name the package."
version: 1.0.0
argument-hint: "[task — e.g. 'add currency', 'enable regen', 'persist bag', 'custom rule', 'cross-bag buff', 'server time']"
---

# Hlight.ResourceBag — Usage Skill

Pure-C# runtime for stackable resources with composable ScriptableObject rules. Lives at `Packages/com.hlight.resource-bag`. Namespaces: `Hlight.ResourceBag` (Core), `Hlight.ResourceBag.Rules`.

## When to use this package

- **Currencies** (coin, gem, gold) — uncapped balances
- **Hearts / lives / energy** — capped, regenerating
- **Boosters / consumables** (hammer, hint, undo) — capped or uncapped count
- **Event tickets / tokens** — short-lived, may unlock buffs
- **Reward chests / bundles** — pack that fans out into components

## When NOT to use it

- **Instanced items with per-instance state** — equipment with durability/level/locked-flag is a different problem (inventory of unique objects, not a counter). This package only tracks `int amount` per `ResourceDefinition`.
- **Per-stack metadata** (e.g. timestamp on each unit) — not supported.
- **Order matters** (queues, FIFO drops) — use your own data structure.

---

## Mental model

```
ResourceDefinition<TKey> (abstract SO)   BagBlueprint<TKey> (abstract SO)
  ├── Key → Id = Key.ToString()         ├── Resources[] : Entry
  ├── DisplayName                       │     ├── resource : ResourceDefinition<TKey>
  ├── Icon                              │     ├── initialAmount (0 = no seed)
  │                                     │     └── maxAmount (0 = unlimited)
  └── Rules[] (ResourceRule SO refs)    └── ExtraRules[]  ← cross-cutting rules
   (subclass both, one line per family)
        │
        │ Attach(bag, owner)        ┌────────────────────────────────────────┐
        ▼                           │  ResourceBag (runtime, pure C#)            │
ResourceRule (abstract SO, factory)    │  ├── ctor(id, blueprint, snapshot?,     │
        │                           │  │       clock?, locator?) — loads     │
        │ produces                  │  │       snapshot inline, no Changed   │
        ▼                           │  ├── Add / TrySpend / TrySpendAll      │
AttachedRule (per-bag instance)     │  ├── GetAmount / HasAtLeast / Reset    │
  ├── Source (rule SO)              │  ├── GetMaxAmount / SetMaxAmount       │
  ├── Owner (def or null)           │  ├── Tick() — project-driven, reads    │
  ├── StateKey / Save+LoadState     │  │       Clock once per call           │
  ├── OnAttach / OnDetach           │  ├── Changed event                     │
  ├── OnTick(double now)            │  ├── SaveTo(snapshot)                  │
  ├── OnBeforeAdd(ref intent, fx)   │  ├── Clock : IBagClock (owned/injected)│
  └── OnBeforeSpend(ref intent, fx) │  ResourceBag<TKey> adds: Add(key, …),  │
                                    │  Resolve(key), Blueprint (typed)       │
                                    └────────────────────────────────────────┘
```

**Key insight — implicit owner pattern:** A rule SO placed in `DefA.Rules` and `DefB.Rules` produces TWO independent `AttachedRule` instances when a bag is constructed. Each instance carries its own state (timer, elapsed counter, etc.) as ordinary C# fields. **No `object` casts, no per-bag state dictionary.** State lives in the `AttachedRule` private nested class.

**Lifecycle:** Bag is ready immediately after `new ResourceBag(id, blueprint)`. No `Open()`. Rules attach during construction; a `BagSnapshot` passed to the constructor is applied inline during construction too — so before any subscriber could exist, meaning it fires **no** `Changed` events. `Dispose()` detaches all rules in reverse order, clears amounts, and disposes an owned clock (an injected one is left untouched).

---

## Core task 1 — Define resources (enum-keyed, recommended)

This is the **default pattern** for new projects. One enum + one one-line `ResourceDefinition<TKey>` subclass per domain → no SO refs at call site.

```csharp
// 1. One enum per key family — this is a bag's id namespace
public enum CurrencyId { Coin, Gem, Heart, Energy }

// 2. One-line def subclass — Id auto-derived from Key.ToString()
public sealed class CurrencyDef : ResourceDefinition<CurrencyId> { }

// 3. One-line blueprint subclass — the scope, and what carries [CreateAssetMenu]
[CreateAssetMenu(menuName = "MyGame/Currency Blueprint")]
public sealed class CurrencyBlueprint : BagBlueprint<CurrencyId> { }
```

**Every def belongs to a family.** A blueprint entry's resource slot is typed
`ResourceDefinition<TKey>`, so the Inspector refuses a def from another family — and there is
no "hand-rolled `Id`" path any more. One bag = one family; a resource shared by two bags means
both bags share one enum, with each blueprint taking the subset it needs.

Then in Editor:
- Create one `CurrencyDef` resource per enum value (e.g. `CoinDef.asset`, `GemDef.asset`, etc.)
- Set the `Key` field in Inspector to the matching enum value
- Set `DisplayName`, `Icon`, `Rules[]`. The cap is per-scope policy — set it on the
  blueprint entry's `maxAmount`, not on the def

Call sites use the enum:

```csharp
bag.Add(CurrencyId.Coin, 100, "quest_reward");
bag.TrySpend(CurrencyId.Gem, 50, "buy_hint");
int hearts = bag.GetAmount(CurrencyId.Heart);
```

These are instance methods on `ResourceBag<TKey>`; they resolve through `Blueprint.ResolveByKey(key)`, whose map is built once per blueprint and cached. `reason` is optional (defaults to `BagReasons.Unspecified` = `"_unspecified"`), so `bag.Add(CurrencyId.Coin, 10)` is legal and still traceable.

**What the compiler now catches, and what it still cannot:**

| Mistake | When it surfaces |
|---|---|
| Key from another family — `bag.Add(BoosterId.Hammer, 1)` | compile error |
| Blueprint of another family passed to the ctor | compile error |
| Def of another family dragged into a blueprint | Inspector refuses it |
| `bag.Add(0, 5)` — a zero constant converts to any enum | compile error (a poisoned `int` overload catches it) |
| **Enum member whose def was never authored into the blueprint** | `KeyNotFoundException` on first call — the mapping lives in an asset, so no type can see it |

**No two families may share a member name.** `Id` is `Key.ToString()`, so `CurrencyId.Ticket` and `EventId.Ticket` would be one save key. A single blueprint holding both warns on first use.

### When to use def-refs instead

- Holding a `[SerializeField] ResourceDefinition` slot on a MonoBehaviour for a designer-set reference
- **Rule code** — `AttachedRule.Bag` is the base `ResourceBag` (a rule cannot know the family), so rules always work from def refs
- One-off cross-bag rule referencing a foreign def

```csharp
[SerializeField] private ResourceDefinition coinDef;
bag.Add(coinDef, 100, "reward");
```

Both APIs coexist freely; pick per call site.

## Core task 2 — Author a BagBlueprint

`BagBlueprint<TKey>` is abstract, so a family needs the one-line subclass from Core task 1 — that subclass carries the `[CreateAssetMenu]`. Author assets of it: one per scope, so two bags on one family are two assets of the same class. The built-in rules have menu entries under `Hlight → Resource Bag → Rules`.

| Field | Purpose |
|---|---|
| `Resources[]` | All in-scope entries. Each = `(ResourceDefinition<TKey> resource, int initialAmount, int maxAmount)`. The resource slot only accepts defs of this family. `initialAmount` bypasses the rule pipeline *and* the cap. `maxAmount = 0` means unlimited. |
| `ExtraRules[]` | Rules attached AFTER per-resource rules. Owner = null. For blueprint-only cross-cutting rules. |

> **Duplicate detection** — if `Resources[]` has the same def twice, the bag ctor logs a
> `Debug.LogWarning`. Rules attach is idempotent so behavior won't break, but the
> later entry's `initialAmount` overwrites the earlier one.

## Core task 3 — Instantiate at runtime

```csharp
public class PlayerBagOwner : MonoBehaviour
{
    [SerializeField] private CurrencyBlueprint blueprint;          // your BagBlueprint<CurrencyId> subclass
    private ResourceBag<CurrencyId> _bag;
    private readonly BagSnapshot _snapshot = new BagSnapshot();   // never null — SaveTo requires that

    private void Awake()     => _bag = new ResourceBag<CurrencyId>("player", blueprint, _snapshot);
    private void Update()    => _bag.Tick();
    private void OnDestroy() => _bag.Dispose();

    public void Flush()      => _bag.SaveTo(_snapshot);
}
```

Five lines cover the whole lifecycle: construct (with an optional snapshot to restore),
tick, dispose, flush. `_snapshot` is a `BagSnapshot` — see Core task 5 for where a loaded
one comes from. Pass `null` to the constructor instead when the bag doesn't need to
persist — but `SaveTo` has no such null-means-skip escape hatch; it throws
`ArgumentNullException` on a null snapshot, since there is nothing to write into.

`bag.Tick()` takes **no arguments** — the bag reads its own `Clock` once per call. Use
`Update` / `FixedUpdate` / a server clock / a task driver — your call; the package
ships NO MonoBehaviour ticker.

To react to amount changes:

```csharp
_bag.Changed += c => Debug.Log($"{c.Resource?.Id} {c.OldAmount}→{c.NewAmount} ({c.Reason})");
```

## Core task 4 — Add regenerating hearts

1. Create `HeartRegenRule.asset` (`PeriodicDeltaRule` SO) — set `intervalSec = 5`, `amountPerInterval = 1` (positive = regen)
2. Drop `HeartRegenRule` into `HeartDef.asset`'s `Rules[]`; set the cap on the blueprint
   entry for Heart (`maxAmount = 5`)
3. Add `HeartDef` to the blueprint
4. Call `bag.Tick()` every frame
5. Bag will Add hearts every 5 seconds until at max — never exceeds max amount
6. **Persist the snapshot so regen survives a restart.** Without this, closing and
   reopening resets the rule's next-fire time to "now + interval" and any elapsed
   offline time is lost:

```csharp
var bag = new ResourceBag<CurrencyId>("player", blueprint, loadedSnapshotOrNull);
// ... gameplay ...
bag.SaveTo(snapshotToSave);
```

The rule's own countdown round-trips through `BagSnapshot.RuleState` automatically
(its state class is persisted for it) — no extra wiring needed once the bag is persisted.

**Note:** The same `HeartRegenRule.asset` can be placed in `EnergyDef.Rules[]` too — each owner gets a separate next-fire-time instance.

To make the same resource **decay** instead, set `amountPerInterval` negative — it's the
same rule, one sign flip; the countdown pauses at zero instead of at the cap.

## Core task 5 — Persist amounts (via Hlight DataPersistence)

The package ships NO persistence layer. `BagSnapshot` is the bridge — pass it to the
constructor to load, and to `SaveTo` to capture:

```csharp
public class GameBundle : DataBundle
{
    [DataKey("PlayerBag")] [field: SerializeField]
    public DataEntry<BagSnapshot> PlayerBag { get; private set; }
}

// Boot
await bundle.LoadAllAsync();
var bag = new ResourceBag<CurrencyId>("player", blueprint, bundle.PlayerBag.CurrentValue);

// Flush
bag.SaveTo(bundle.PlayerBag.CurrentValue);
await bundle.SaveAllAsync();
```

`DataEntry<T>` requires `where T : class, new()` — `BagSnapshot` is a class with a
parameterless ctor, so it plugs in directly, no wrapper type needed. Loading fires
**no** `Changed` events (it happens inside the constructor, before any subscriber
could exist). Amounts are clamped to `[0, cap]` on load — the save payload is
player-controlled, so it never reaches a rule unclamped.

For non-Hlight projects (PlayerPrefs, files, cloud) the bridge is the same — serialize
the `BagSnapshot` POCO with whatever the project already uses.

## Core task 6 — Write a custom ResourceRule

Inherit `ResourceRule`, return an `AttachedRule` instance from `Attach`. State as plain fields on the nested instance class.

```csharp
[CreateAssetMenu(menuName = "MyGame/Rules/FirstPurchaseBonus")]
public class FirstPurchaseBonusRule : ResourceRule
{
    [SerializeField] private int _bonusAmount = 100;

    public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
        => owner != null ? new Instance(this, owner, bag) : null;

    // Place rule in CoinDef.Rules → Owner = CoinDef → buff applies to coin Adds.
    private sealed class Instance : AttachedRule
    {
        private readonly FirstPurchaseBonusRule _cfg;
        private bool _claimed;

        public Instance(FirstPurchaseBonusRule cfg, ResourceDefinition owner, ResourceBag bag)
            : base(cfg, owner, bag) { _cfg = cfg; }

        public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
        {
            if (intent.Resource != Owner || _claimed) return;
            _claimed = true;
            sideEffects.Add(new ResourceSideEffect(Owner, _cfg._bonusAmount, "first_purchase_bonus"));
        }
    }
}
```

### Pipeline outcomes per `OnBeforeAdd`/`OnBeforeSpend`

| Mutation | Effect |
|---|---|
| `intent.Delta *= 2` | Inflate/deflate the primary amount (sign preserved — same field for credit and debit) |
| `intent.SkipPrimary = true` | Skip the primary mutation; side effects still fire. On a debit, `TrySpend` reports success without deducting — the free-pass case. |
| `intent.Outcome = SpendOutcome.Reject` | `TrySpend` returns false; side effects accumulated so far ARE DISCARDED. Sticky — a later rule cannot resurrect it. |
| `intent.WithSubstitute(otherAsset, n)` | Debit `otherAsset` instead |
| `sideEffects.Add(new ResourceSideEffect(...))` | Emit a secondary change that re-enters the pipeline at `depth+1` |

## Core task 7 — Cross-bag rules (event ticket affects player coin add)

Project supplies an `IBagServiceLocator` implementation. Pull foreign deps once in `OnAttach`, then forget the locator:

```csharp
public class EventBuffRule : ResourceRule
{
    [SerializeField] private ResourceDefinition _eventTicket;
    [SerializeField] private int _multiplier = 2;

    public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
        => owner != null ? new Instance(this, owner, bag) : null;

    private sealed class Instance : AttachedRule
    {
        private readonly EventBuffRule _cfg;
        private ResourceBag _eventBag;

        public Instance(EventBuffRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag) { _cfg = cfg; }

        public override void OnAttach()
        {
            Bag.Locator?.TryProvide(out _eventBag, "event"); // key disambiguates
        }
        // NOTE the requested type is the base ResourceBag — a rule cannot know the foreign
        // bag's family. So register bags as ResourceBag, not ResourceBag<TKey>, or this
        // request misses and the rule silently does nothing.

        public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
        {
            if (intent.Resource != Owner) return;
            if (_eventBag == null || _eventBag.GetAmount(_cfg._eventTicket) < 1) return;
            intent.Delta *= _cfg._multiplier;
        }
    }
}

// Wiring
var eventBag = new ResourceBag<EventId>("event", eventBlueprint);
locator.Register<ResourceBag>(eventBag, "event");     // base type — rules ask for ResourceBag
var playerBag = new ResourceBag<CurrencyId>("player", playerBlueprint, locator: locator);
```

See `Samples~/04-cross-bag` for a full wiring with `TinyLocator`.

## Core task 8 — Server time

The built-in offline cap (Time model, README) only slows clock manipulation down; it
does not stop it. To actually defend against it, inject an `IBagClock` backed by a
server time source instead of letting the bag create its own `BagClock`:

```csharp
public sealed class ServerBagClock : IBagClock
{
    private readonly IServerTime _server;                   // from the infra package
    private readonly BagClock _fallback = new BagClock();    // used before first sync
    private double _timeline;

    public double Now
    {
        get
        {
            double candidate = _server.IsSynced ? _server.UnixSeconds : _fallback.Now;
            if (candidate > _timeline) _timeline = candidate;   // ratchet — the implementation's job
            return _timeline;
        }
    }
}

var bag = new ResourceBag<CurrencyId>("player", blueprint, snapshot, clock: new ServerBagClock(serverTime));
```

- An authority reading that comes in **below** the bag's current timeline stalls
  regen/decay until real time catches up — it never rewinds the timeline.
  `IBagClock` forbids moving backwards, full stop.
- **Cheat detection and handling (wiping a save, flagging an account) belongs to the
  project or the server, not this package** — ResourceBag has no API for it and never
  will.
- The bag never persists an injected clock's state — `SaveTo` does not write
  `snapshot.Clock` for it. That's the infra package's job.

---

## Built-in rules (in `Hlight.ResourceBag.Rules`)

| Rule | Hooks | What it does |
|---|---|---|
| `PeriodicDeltaRule` | OnTick | Signed delta on owner every interval — positive regenerates, negative decays. Pauses its countdown (instead of accruing a debt) while blocked at cap or at zero — but only if `Tick()` is still being called while blocked; the reset lives inside `OnTick`, so a driver that skips ticking during that window replays the whole catch-up as an instant refill on the next tick after a spend. |
| `OverflowConvertRule` | OnBeforeAdd | Surplus → converted resource (ratio + rounding) |
| `BundleResolveRule` | OnBeforeAdd | Pack expands into entries, primary skipped |
| `SubstituteRule` | OnBeforeSpend | Fallback only — fires when the owner resource can't cover a spend; debits a substitute resource instead (ratio) |

All owner-targeted — they read `Owner` (the def whose `Rules[]` contained them) and return `null` from `Attach` when owner is null. Placing them in `BagBlueprint.ExtraRules` silently no-ops.

---

## Recipes

Three behaviours that used to ship as built-in rules. They are three lines of real
logic each, and every project wants a slightly different version, so they live here
as copy-paste starting points instead of as package API.

### Multiply adds while a buff resource is held

```csharp
public class MultiplierBuffRule : ResourceRule
{
    [SerializeField] private ResourceDefinition buffAsset;
    [SerializeField] private int multiplier = 2;

    public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
        => owner != null ? new Instance(this, owner, bag) : null;

    private sealed class Instance : AttachedRule
    {
        private readonly MultiplierBuffRule _cfg;
        public Instance(MultiplierBuffRule cfg, ResourceDefinition owner, ResourceBag bag)
            : base(cfg, owner, bag) { _cfg = cfg; }

        public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
        {
            if (intent.Resource != Owner) return;
            if (_cfg.buffAsset == null || Bag.GetAmount(_cfg.buffAsset) < 1) return;
            intent.Delta *= _cfg.multiplier;
        }
    }
}
```

### Free pass — spend succeeds without deducting

```csharp
public class FreePassRule : ResourceRule
{
    [SerializeField] private ResourceDefinition condition;
    [SerializeField] private int requiredAmount = 1;

    public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
        => owner != null ? new Instance(this, owner, bag) : null;

    private sealed class Instance : AttachedRule
    {
        private readonly FreePassRule _cfg;
        public Instance(FreePassRule cfg, ResourceDefinition owner, ResourceBag bag)
            : base(cfg, owner, bag) { _cfg = cfg; }

        public override void OnBeforeSpend(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
        {
            if (intent.Outcome != SpendOutcome.Continue) return;   // respect Reject / Substitute
            if (intent.Resource != Owner) return;
            if (_cfg.condition == null || Bag.GetAmount(_cfg.condition) < _cfg.requiredAmount) return;
            intent.SkipPrimary = true;      // a skipped debit reports success
        }
    }
}
```

### Expiry at an absolute time

Also the reference example for persisting rule state — a `bool` encodes as `0`/`1`.

Note why this is not a built-in: the timestamp lives on a shared `ResourceRule` SO, so
every bag using that SO expires at the same absolute instant. That is event
configuration, not a reusable primitive. Real designs usually want expiry relative to
when the item was granted, which means per-owner state, which means this rule.

```csharp
public class ExpireRule : ResourceRule
{
    [SerializeField] private long expireAtUnixSeconds;
    [SerializeField] private string stateKey = "expire";

    public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
        => owner != null ? new Instance(this, owner, bag) : null;

    private sealed class Instance : AttachedRule
    {
        private readonly ExpireRule _cfg;
        private bool _hasFired;

        public Instance(ExpireRule cfg, ResourceDefinition owner, ResourceBag bag)
            : base(cfg, owner, bag) { _cfg = cfg; }

        public override string StateKey => _cfg.stateKey;

        // One flag, so the hand-rolled hooks are smaller than a state class — and the save
        // row stays readable. Derive from AttachedRule<TState> once there is more than this.
        public override string SaveState() => _hasFired ? "1" : "0";
        public override void LoadState(string value) => _hasFired = value == "1";

        public override void OnTick(double now)
        {
            if (_hasFired || now < _cfg.expireAtUnixSeconds) return;
            if (Bag.HasAtLeast(Owner, 1)) Bag.ResetAmount(Owner, "_expire");
            _hasFired = true;
        }
    }
}
```

---

## Reasons (well-known strings)

`BagReasons` (Core) and `RuleReasons` (Rules) expose constants for emitted reasons.
**System reasons prefixed with `_`:** `_overflow`, `_unspecified` (`BagReasons`);
`_periodic`, `_resolve_bundle` (`RuleReasons`). `_regen` / `_decay` / `_expire` /
`_substitute` no longer exist anywhere in compiled code — `_periodic` covers both
directions of `PeriodicDeltaRule`, and expiry lives only in the Recipes above.
Project reason strings should NOT start with `_`.

---

## Gotchas

1. **Always subclass `ResourceDefinition<TKey>`.** The non-generic `ResourceDefinition` is the runtime currency (what the bag and rules pass around) but cannot enter a blueprint: the entry slot is typed to the family. A def with a hand-written `Id` and no key is no longer a supported authoring shape.

2. **Same rule SO across multiple defs → independent state.** Don't store mutable state on the `ResourceRule` itself; put it on the nested `Instance : AttachedRule` class. This is enforced by the design — `ResourceRule` is config, `AttachedRule` is state.

3. **Owner-targeted rule placed in `ExtraRules` silently no-ops.** All four built-ins (`PeriodicDeltaRule`, `OverflowConvertRule`, `BundleResolveRule`, `SubstituteRule`) return `null` from `Attach` when owner is null. Use `def.Rules[]` placement, not `blueprint.ExtraRules[]`.

4. **`MaxAmount = 0` means unlimited**, not "cap at zero." `1+` = real cap. `SetMaxAmount` follows the same convention.

5. **Lowering max amount does NOT auto-clamp existing amount.** If `bag.GetAmount(def) = 500` and you call `bag.SetMaxAmount(def, 100)`, the amount stays at 500. Future Adds find no headroom (overflow event fires). Clamp manually if your design requires it.

6. **Every mutation is a transaction; a failure writes nothing and announces nothing.** Entries run against a tentative layer — rules execute for real and their reads see that layer, so two entries competing for one balance resolve in order — and the set is committed only if all of it clears. There is no debit-then-restore pair any more, and a failed single `TrySpend` no longer lets its rules' side effects land. What is *not* transactional: **rule state** (a one-shot marked used during a failed transaction stays marked) and `SetMaxAmount`.
    - `Changed` fires after the write, so a handler reading `GetAmount` sees the settled value; handler re-entry is bounded by `MaxSideEffectDepth`.
    - `bag.Bind(def, amount => …)` returns an unsubscribe `Action`, fires only on a real change (zero-delta overflow does not count), never for a failed transaction, and pushes nothing at subscribe time.

7. **`SpendOutcome.Reject` aborts EVERYTHING.** Side-effects accumulated during the same pipeline call are discarded. Use `Continue`/`Substitute` when secondary effects should fire — there is no `Skip`; set `SkipPrimary` on a debit for the same free-pass effect.

8. **Side-effect depth is capped at 5.** Exceeded → `Debug.LogError` + intent dropped. No exception. Cyclic side-effect rules will hit this.

9. **`Changed` fires per mutation, including a 0-delta overflow event.** When an Add is clamped at cap, a 0-delta event fires with `reason = BagReasons.Overflow`. If a `Changed` handler calls back into the bag (e.g. re-triggering an Add), that re-entry is bounded by the same `MaxSideEffectDepth` as a rule's own side effects — it is not exempt just because it came from an event handler instead of a rule.

10. **`ResourceBag` is pure C#.** No MonoBehaviour, no auto-Tick. `Tick()` takes no arguments — `Update`/`FixedUpdate` ownership is yours.

11. **`Dispose()` is mandatory.** Detaches all rules in reverse order, nulls `Changed`, and disposes an owned `BagClock`'s focus-change subscription. An injected clock is left untouched — its owner disposes it. Skip `Dispose()` and rules' OnDetach hooks leak; subscribers leak.

12. **Locator pulled ONCE in OnAttach, never stored on the SO.** Storing `IBagServiceLocator` as an SO field defeats the per-bag binding. Pull `locator.TryProvide(out _eventBag, "event")` in `OnAttach`, then forget the locator.

13. **`BundleResolveRule` placement matters.** It sets `SkipPrimary` and emits side-effects sized by `intent.Delta`. If a rule earlier in `def.Rules[]` inflates `intent.Delta` first (e.g. a multiplier buff recipe), the inflated amount feeds into the bundle expansion — usually wrong unless that's deliberately wanted. Default: put bundle rules at index 0 of `def.Rules[]`.

14. **`ResourceRule` SO has no `Id` field anymore.** If you need to identify a rule programmatically, use the SO asset reference or `SO.name`.

15. **The offline cap is a speed bump, not a defence.** `BagClock`'s `maxOfflineSeconds` (default 8h) stops one big clock jump from paying out unlimited offline progress, but a player who repeats background → advance → resume still gains every cycle, just capped per cycle. If clock manipulation must be actually prevented rather than merely slowed, inject a server-backed `IBagClock` (Core task 8) — the cap alone is not that defence.

16. **`ResourceDefinition<TKey>.Id` is the enum member name.** It's `Key.ToString()`, so renaming an enum member changes the persistence key and silently orphans any save data written under the old name. Treat an enum rename on a keyed def like a schema migration.

17. **Rule state = derive from `AttachedRule<TState>`.** Declare a `[Serializable]` state class, use `State`, and persistence is done — one `BagSnapshot.RuleState` row keyed `"{ownerId}:{StateKey}"` (`StateKey` defaults to `"state"`; override it when one def carries two rules of the same type, which the bag warns about at save time). A plain `AttachedRule` with no `StateKey` is stateless and writes nothing.
    - `TState` must be `JsonUtility`-round-trippable: public or `[SerializeField]` fields, no dictionaries, no polymorphic fields. No type name enters the save file, so renaming rule classes never breaks a save.
    - Three override points, one job each: `Serialize`/`Deserialize` decide the **format**; `OnStateLoaded` runs after restored data replaced `State` and is where the rule **judges/repairs** it (`PeriodicDeltaRule` resets the interval there when a fire time came from a foreign clock epoch). Don't put validation in `Deserialize` — then every format change has to carry it along.
    - A malformed value warns and keeps defaults; an empty one passes quietly. Neither calls `OnStateLoaded`, which only runs when state was actually restored.
    - A rule that wants full control can override `StateKey` / `SaveState()` / `LoadState(string)` on the non-generic `AttachedRule` directly.
    - `bag.AttachRule` after construction skips `LoadState` — the constructor already ran.

18. **`PeriodicDeltaRule`'s "no instant refill after a spend" guarantee needs at least one `Tick()` while blocked.** The countdown reset happens inside `OnTick`, not on the spend itself. A project that only ticks while some UI showing the resource is open (a valid choice — README blesses "whichever driver you prefer") can sit at cap with a stale next-fire time; the first tick after the spend then replays the whole accumulated catch-up as an instant refill instead of waiting a fresh interval. Tick at least once while blocked (e.g. once per frame regardless of UI state) if this guarantee matters to your design.

19. **A custom rule's `OnAttach` reads amounts as 0, even when a snapshot is about to restore a real balance.** Rules attach before the constructor applies a loaded `BagSnapshot` (this order is deliberate and won't change). Caching `Bag.GetAmount(Owner)` in `OnAttach` therefore caches a stale 0. Read amounts lazily from `OnTick`/`OnBeforeAdd`/`OnBeforeSpend` instead.

---

## Anti-patterns

- ❌ Storing per-bag state on the `ResourceRule` SO — corrupts when the SO is reused across bags or across multiple defs in one bag.
- ❌ Inheriting `ResourceDefinition` directly when you have an enum — use `ResourceDefinition<TKey>` for the one-line subclass + free `Id`.
- ❌ Calling `bag.Add` / `bag.TrySpend` from inside `AttachedRule.OnDetach` — Dispose is tearing down; mutations get inconsistent treatment.
- ❌ Implementing persistence inside the rule — keep persistence in the project's save layer; rules see only the bag's API.
- ❌ Hand-rolling enum→def maps — `bag.Resolve(key)` (or `bag.Blueprint.ResolveByKey(key)`) already does this, built once per blueprint and cached.
- ❌ Catching exceptions inside `Attach` to "fix" missing owner — return `null` instead; bag drops the attachment gracefully.
- ❌ Using `Add` to seed initial amounts at boot — set `initialAmount` on the matching blueprint entry (bypasses pipeline, no rules run) or pass a `BagSnapshot` to the constructor (restore semantics, no `Changed` events).

---

## File map (package)

| Path | Role |
|---|---|
| `Runtime/Core/ResourceDefinition.cs` | Abstract base SO — `Id` abstract |
| `Runtime/Core/ResourceDefinitionGeneric.cs` | `ResourceDefinition<TKey>` enum-keyed base |
| `Runtime/Core/ResourceRule.cs` | Abstract factory SO |
| `Runtime/Core/AttachedRule.cs` | Per-bag runtime instance base |
| `Runtime/Core/AttachedRuleGeneric.cs` | `AttachedRule<TState>` — state as a class; `Serialize`/`Deserialize` overridable |
| `Runtime/Core/BagBlueprint.cs` | Abstract key-agnostic base — `ExtraRules`, `Entries`, `TryGetById` |
| `Runtime/Core/BagBlueprintGeneric.cs` | `BagBlueprint<TKey>` — authored typed `Entry[]` + `ResolveByKey` |
| `Runtime/Core/ResourceEntry.cs` | Untyped entry view the bag consumes |
| `Runtime/Core/ResourceBag.cs` | Runtime — ctor/snapshot load, `Tick`, `SaveTo`, `Dispose` |
| `Runtime/Core/ResourceBag.Pipeline.cs` | partial — `Add`/`TrySpend`/`TrySpendAll` + rule loop + side-effect re-entry |
| `Runtime/Core/ResourceBagGeneric.cs` | `ResourceBag<TKey>` — enum-keyed API, typed blueprint |
| `Runtime/Core/ResourceBagConfigExtensions.cs` | `Grant` / `TrySpendAll` / `CanAfford` over `ResourceAmount` rows |
| `Runtime/Core/BagReasons.cs` | Well-known reason strings (Core) |
| `Runtime/Clock/IBagClock.cs` | Time source contract |
| `Runtime/Clock/BagClock.cs` | Default `IBagClock` — anchor/ratchet/offline cap |
| `Runtime/Locator/IBagServiceLocator.cs` | Cross-bag bridge contract |
| `Runtime/Structs/` | `ResourceIntent`, `ResourceSideEffect`, `ResourceChange`, `SpendOutcome`, `BagSnapshot`, `ResourceAmount` |
| `Rules/` | 4 built-in rule SOs + `RoundingMode`, `RuleReasons` |
| `Tests/` | EditMode tests — basic ops, pipeline, tick, clock, attach/detach, dispose, each rule, persistence |
| `Samples~/01-basic-bag/` | Add / Spend / GetAmount, max-amount clamping |
| `Samples~/02-with-regen/` | `PeriodicDeltaRule` + project-owned Tick driver + snapshot persistence |
| `Samples~/03-bundle-reward/` | BundleResolveRule fan-out |
| `Samples~/04-cross-bag/` | Locator + custom buff rule reading event bag |
| `Samples~/05-enum-keyed/` | `ResourceDefinition<TKey>` + enum-keyed call sites (recommended) |

---

## Decision tree

```
Need to track a count-only resource?
  │
  ├─ One value per resource (no per-instance state)? → YES
  │     │
  │     └─ Always: enum + ResourceDefinition<TKey> + BagBlueprint<TKey>, one line each.
  │        (A designer-set slot on a MonoBehaviour is still `SerializeField ResourceDefinition`.)
  │
  └─ Per-instance state needed (durability, level, unique IDs)? → NOT this package
                                                                  Use a dedicated Inventory of instanced items.

Need a behavior on this resource?
  │
  ├─ One of the 4 built-ins fits? → Drop SO into ResourceDefinition.Rules[]
  ├─ Custom behavior?             → Subclass ResourceRule; state in nested AttachedRule subclass
  └─ Cross-bag behavior?          → Subclass ResourceRule; pull foreign bag via Bag.Locator in OnAttach

Resource list comes from remote config (reward, cost, IAP payload)?
  │
  └─ List of ResourceAmount { id, amount, amountMax } → bag.Grant(...) / bag.TrySpendAll(...).
     id is the enum member name. Pass your own `roll` for random rows; the package owns no RNG.
     Pass a `granted` buffer to learn what a random row rolled to.

Need persistence?
  │
  └─ Pass a BagSnapshot to the ResourceBag constructor to load; call bag.SaveTo(snapshot) to capture.
     Plug into Hlight DataPersistence, PlayerPrefs, files, cloud — bag doesn't care.

Need protection against clock manipulation?
  │
  └─ Inject a server-backed IBagClock (Core task 8). The built-in offline cap alone
     only slows it down.
```

---

## Verification after changes

EditMode tests live in `Packages/com.hlight.resource-bag/Tests/`. Run via:

- `Window → General → Test Runner → EditMode tab → Run All`, or
- `Tools → Hlight → Run Resource Bag Tests` — writes a pass/fail summary to
  `Temp/resource-bag-tests.txt`, useful for driving verification without the Editor UI.

Or via CLI:

```bash
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```

Tests assert: amount math, max-amount clamping, rule pipeline ordering, side-effect depth limit, TrySpendAll atomicity, multi-owner rule independence, dispose detach order, clock anchor/ratchet/offline-cap behavior, snapshot round-trip.

## Further reading

- `Packages/com.hlight.resource-bag/README.md` — public API overview
- `Samples~/*/README.md` — per-sample setup + expected log
- Source files — every public class has xmldoc explaining responsibility
