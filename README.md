# Resource Bag

Reusable cross-genre **stackable-resource** management primitive for Unity (currencies,
boosters, consumables, event items). Standalone package — no Tricky-Prank coupling.
Composable through ScriptableObject **rules**. No per-item state; if you need
equipment-style instances, this isn't the package for you.

**Namespaces:** `Hlight.ResourceBag` (Core), `.Rules`
**Unity:** 6000.0
**Dependencies:** none (Core is pure C# / UnityEngine)

## Goals

- One primitive that ships across puzzle / RPG / idle / casino projects.
- Authoring in the Editor: resources, rules, blueprints are ScriptableObjects.
- Composable behaviour via SO-based `ResourceRule` subclasses (built-in + project-side).
- Deterministic, side-effect-bounded pipeline (max depth 5) with per-change events.
- Persistence-agnostic: bridge via a single `BagSnapshot` DTO (constructor load +
  `SaveTo`). Plug into Hlight DataPersistence, PlayerPrefs, files, cloud — whatever the
  project already has.
- Zero coroutines / no built-in MonoBehaviours in Core.
- Time-aware rules run on an injectable `IBagClock`, so offline regen/decay survive
  app close and a server clock can be swapped in without touching rule code.

## Non-goals

- No instanced items (no per-item state like equipment durability).
- No multi-currency arithmetic beyond `TrySpendAll`.
- No built-in RNG / probabilistic rules — projects supply their own (see [Extending](#extending--write-a-custom-resourcerule)).
- No built-in tick driver — the project decides when to call `bag.Tick()`.
- Remote config, in three levels: **resource lists** (rewards, costs) have a first-class
  path — `ResourceAmount` + `Grant`/`TrySpendAll`, see [Remote config](#remote-config); a
  **cap** has a runtime plug-point (`SetMaxAmount`); **rule parameters** have neither, so a
  rule that must follow remote config pulls its provider through `Bag.Locator` (see
  [IBagServiceLocator](#cross-bag-ibagservicelocator) for the lazy-resolve pattern). A
  **server-defined scope** — the resource list itself coming from the server — is still not
  supported: the constructor needs an authored blueprint.
- No cheat detection. A clock reading behind the bag's own timeline stalls a rule
  rather than rewinding it — see [Time model](#time-model). Deciding what to do about
  the player who caused that (wipe a save, flag an account) is the project's or
  server's job, not this package's.

## Quick start

Three one-line declarations set up a key family, then the bag is five lines:

```csharp
public enum CurrencyId { Coin, Gem, Heart }

public sealed class CurrencyDef : ResourceDefinition<CurrencyId> { }          // the resources

[CreateAssetMenu(menuName = "MyGame/Currency Blueprint")]
public sealed class CurrencyBlueprint : BagBlueprint<CurrencyId> { }          // the scope
```

```csharp
public class PlayerBagOwner : MonoBehaviour
{
    [SerializeField] private CurrencyBlueprint blueprint;
    private ResourceBag<CurrencyId> _bag;
    private readonly BagSnapshot _snapshot = new BagSnapshot();   // never null — SaveTo requires that

    private void Awake()     => _bag = new ResourceBag<CurrencyId>("player", blueprint, _snapshot);
    private void Update()    => _bag.Tick();
    private void OnDestroy() => _bag.Dispose();

    public void Flush()      => _bag.SaveTo(_snapshot);
}
```

No step requires external ordering, and resume-from-background is handled
automatically by the bag's clock. `_snapshot` is a `BagSnapshot` — see
[Persistence](#persistence) for where a loaded one comes from (pass `null` to the
constructor for a brand-new bag with nothing to restore; `SaveTo` has no such escape
hatch — see below).

Call sites name resources by enum, and the compiler holds them to this bag's family:

```csharp
bag.Add(CurrencyId.Coin, 100, "reward");
int hearts = bag.GetAmount(CurrencyId.Heart);
bag.Add(BoosterId.Hammer, 1);              // does not compile — wrong family
```

`reason` is optional everywhere (defaults to `BagReasons.Unspecified` = `"_unspecified"`, a
real string rather than `null` so an event stream never carries nulls and the call sites still
missing a breadcrumb stay greppable). A plain `ResourceBag` + `BagBlueprint` still exists for
code that works from def references — every rule does — and the def-keyed API is inherited
unchanged.

## Time model

Time-dependent rules (`PeriodicDeltaRule`, and any custom rule that persists a fire
time) read `ResourceBag.Clock`, an `IBagClock`.

**`IBagClock` contract** — every implementation, built-in or supplied by an
infrastructure package, must honor:

- **Never decreases.** A rule compares a persisted fire time against `Now`; one step
  backwards stalls that rule permanently.
- **Domain is seconds since the Unix epoch (UTC).** Rule state is persisted, so
  swapping implementations across sessions (local → server or back) requires the same
  epoch. Lagging real UTC is allowed — a capped offline grant does exactly that — but
  a since-boot counter is not a valid domain.
- **Read on the main thread.** `ResourceBag` is not thread-safe.
- **Forward jumps are allowed** — offline credit, or an authoritative time arriving
  late — and rules absorb them with a clamped catch-up.

**`BagClock`** (the default implementation) anchors on device UTC at construction and
again on every app resume; in between, time advances from a monotonic in-process
source. **Moving the device clock while the app is in the foreground has no effect at
all.** Offline time accumulated between anchors is capped at `maxOfflineSeconds`
(default 8h), and the remainder beyond the cap is **forfeited, not banked** — repeating
background → advance → resume never pays out the same offline window twice.

**The cap is a speed bump, not a defence.** It stops one large clock jump from paying
out unlimited offline progress, but a player who repeats background/advance/resume
still gains — just capped per cycle. **The only real defence against clock
manipulation is an authoritative server clock**, injected as an `IBagClock`.

Resume is detected automatically — `BagClock` subscribes to `Application.focusChanged`
itself, so a project needs no wiring for the common case. `Reanchor()` is public for a
project that prefers to drive it explicitly from `OnApplicationPause` instead.

**Ownership.** A bag creates its own `BagClock` unless one is passed to the `clock`
constructor parameter. The bag persists clock state (`BagSnapshot.Clock`) **only when
it owns the clock** — an injected clock's persistence is that clock owner's
responsibility, not the bag's.

### Server time

`IBagClock` is the seam for wiring in an authoritative clock from an infrastructure
package. The adapter always returns the best value currently known and ratchets
forward as authority arrives — no "waiting for sync" state needed:

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
```

Server time arriving after fallback time was in use is just a forward jump, so it
lands on the same clamped catch-up path as offline credit. If the authority instead
reports a time *behind* the current timeline (a player advanced their own clock
earlier), the contract forbids moving backwards — the timeline holds and regen/decay
**stalls until real time catches up. It does not rewind.** That's the correct
behaviour: a player who cheated ends up with stalled progress, not a free reset.
Detecting and punishing the cheat itself (wiping a save, flagging an account) belongs
to the project or the server — this package has no API for it and never will.

## Core concepts

**`ResourceDefinition`** — Abstract ScriptableObject: identity (`Id`), `displayName`,
`icon`, `rules`. It is the runtime currency — what the bag, the pipeline and rules pass
around — but **authoring always goes through `ResourceDefinition<TKey>`**, because a
blueprint only accepts keyed defs. A cap is not here: it is per-scope policy and lives on the
blueprint entry.

**`ResourceDefinition<TKey>`** — What you subclass, one line per key family:

```csharp
public sealed class CurrencyDef : ResourceDefinition<CurrencyId> { }
```

`Id` returns `key.ToString()`, so renaming an enum member orphans save data written under the
old name — treat it as a schema migration. Two consequences worth keeping in mind: **no two
families may share a member name** (`Id` *is* the member name, so `CurrencyId.Ticket` and
`EventId.Ticket` would become one save key — a blueprint holding both warns when first used),
and the member name is also the string remote config uses to name a resource (see
[Remote config](#remote-config)).

**`BagBlueprint<TKey>`** — The authored scope, subclassed once per family:

```csharp
[CreateAssetMenu(menuName = "MyGame/Currency Blueprint")]
public sealed class CurrencyBlueprint : BagBlueprint<CurrencyId> { }
```

Each entry is `(ResourceDefinition<TKey> resource, int initialAmount, int maxAmount)`. The
resource field being typed is the point: **the Inspector refuses a def from another family
outright**, so a scope cannot be authored wrong. `ResourceBag<TKey>` takes only a blueprint of
its own family, so wiring the wrong one is a compile error rather than a first-call surprise.
The key-agnostic base `BagBlueprint` carries `ExtraRules`, `Entries` and the persistence
lookup — that is all the bag reads.

**`ResourceRule`** — Abstract ScriptableObject acting as a **factory**: implements
`Attach(bag, owner) → AttachedRule`. The SO holds shared config (interval, ratio, etc.);
the same SO can be placed in multiple `ResourceDefinition.Rules` lists and the bag will
produce a separate `AttachedRule` instance per owner — each with its own state as
ordinary fields (no `object` casts). Four built-ins under `Hlight.ResourceBag.Rules` — see
[Built-in rules](#built-in-rules).

**`AttachedRule`** — Per-bag runtime instance with hooks `OnAttach` / `OnDetach` /
`OnTick(double now)` / `OnBeforeAdd` / `OnBeforeSpend`. Knows its `Source` (rule SO) and
`Owner` (the def whose `rules` list contained the source — `null` for blueprint
extraRules). All four built-ins are owner-targeted: `Attach` returns `null` when owner
is null, so placing one in `BagBlueprint.ExtraRules` silently no-ops. Rule state rides in
`BagSnapshot.RuleState`: a rule with a non-null `StateKey` gets one row, produced by
`SaveState()` and handed back to `LoadState(string)`. Default `StateKey` is `null`
(stateless, nothing written). See [Rule state](#rule-state).

**`ResourceIntent`** — Mutable struct threaded through the pipeline by `ref`; one struct
covers both directions via a signed `Delta` (positive credits, negative debits). It
replaces the four 1.x intent/side-effect structs. Rules assign fields directly
(`intent.Delta *= 2;`); the 1.x fluent `With*` helpers are gone except `WithSubstitute`
(it sets three fields at once, so it survives as a helper). `SkipPrimary` skips the
primary mutation while side effects still run — on a debit, that's the free-pass case:
`TrySpend` reports success without deducting anything. `Outcome` (a `SpendOutcome`) is
only consulted when `Delta` is negative.

**`ResourceSideEffect`** — Immutable struct a rule appends to fan out a secondary change.
Re-enters the pipeline at `depth + 1`, bounded by `ResourceBag.MaxSideEffectDepth` (5).

**`SpendOutcome`** — Three values: `Continue` (default), `Reject`, `Substitute`. There
is no `Skip` — it duplicated `ResourceIntent.SkipPrimary`; set `SkipPrimary` on a debit for
the same free-pass effect instead. `Reject` is **sticky**: the pipeline restores it
after every rule call in the same transaction, so a later rule cannot resurrect a debit
an earlier rule rejected. `Substitute` is not restored the same way — a rule that
overwrites it back to `Continue` redirects the spend onto the owner resource, which is the
safe direction to leave unguarded.

**`ResourceBag`** — Runtime, `IDisposable`, pure C# (no MonoBehaviour). Ready immediately
after construction — there is no `Open()` and never was in 2.0. Constructing with a
`BagSnapshot` loads it inline, before any subscriber could exist, so loading fires
**no** `Changed` events. Pipeline order: rules iterate over the intent → primary
mutation if `!intent.SkipPrimary` → side effects re-enter the pipeline at `depth+1`.
Side-effect depth is capped at 5; exceeded → log + drop. Emits `Changed` per mutation,
including a 0-delta event when a credit is clamped at the cap (`BagReasons.Overflow`).

**`IBagServiceLocator`** — Project bridge to Hlight `IServiceLocator` (or any locator).
Rules call `bag.Locator.TryProvide(out var dep, key)` to pull cross-bag or cross-system
deps. Lifetime is project-owned (convention 7).

**`IBagClock` / `BagClock`** — time source for time-dependent rules; see
[Time model](#time-model).

## Built-in rules

Four, under `Hlight.ResourceBag.Rules`. All are owner-targeted — `Attach` returns `null`
when placed in `BagBlueprint.ExtraRules` instead of `def.Rules[]`.

| Rule | Hooks | What it does |
|---|---|---|
| `PeriodicDeltaRule` | `OnTick` | Signed delta on the owner every interval — positive regenerates, negative decays. Pauses its countdown (instead of accruing a debt) while blocked at cap or at zero, so draining a full resource costs a whole interval rather than banking a burst — **conditional on `Tick()` being called at least once while blocked**; the reset lives inside `OnTick`, so a driver that only ticks while some UI is open can sit blocked with a stale countdown and see the whole catch-up replay as an instant refill on the next tick after a spend. |
| `OverflowConvertRule` | `OnBeforeAdd` | Surplus above cap → converted resource (ratio + rounding) |
| `BundleResolveRule` | `OnBeforeAdd` | Pack expands into entries as side effects; primary skipped |
| `SubstituteRule` | `OnBeforeSpend` | Fallback, not a preference — fires only when the owner resource can't cover a spend, then debits a substitute resource (ratio + rounding) instead |

Three behaviours that shipped as built-ins in 1.x — a multiplier buff, a free pass, and
absolute-time expiry — are gone. Each is three lines of real logic with a
project-specific twist that every project wants slightly differently, so they now live
as copy-paste starting points in `.claude/skills/resource-bag/SKILL.md` → **Recipes**
instead of as package API.

## Asmdef layout

| Asmdef | Purpose | Depends on |
|---|---|---|
| `Hlight.ResourceBag.Core` | `ResourceDefinition`, `ResourceBag`, `ResourceRule`, `AttachedRule`, `BagBlueprint`, locator iface, structs, `Runtime/Clock/` (`IBagClock`, `BagClock`) | — |
| `Hlight.ResourceBag.Rules` | 4 built-in rule SOs | Core |
| `Hlight.ResourceBag.Tests` | EditMode tests | Core, Rules |

## Authoring workflow

1. **Define a key family** — an enum, plus a one-line `ResourceDefinition<TKey>` subclass and
   a one-line `BagBlueprint<TKey>` subclass. Author one def SO per enum member you need.
2. **Author rules** — instantiate a rule SO (`PeriodicDeltaRule`, `OverflowConvertRule`,
   `BundleResolveRule`, `SubstituteRule`) per behaviour, or write a custom `ResourceRule`
   (see [Extending](#extending--write-a-custom-resourcerule)) for anything else.
3. **Drop rules into `def.rules`** — order matters (convention 1). Rules earlier in the
   list run first in the pipeline.
4. **Author a blueprint** — an asset of your `BagBlueprint<TKey>` subclass. Each entry has
   `initialAmount` for a preset starting value and `maxAmount` for the cap, both 0 by default;
   use `extraRules` for blueprint-only cross-cutting rules. The resource slot only accepts defs
   of this family — the Inspector will not take anything else.
5. **Instantiate at runtime** — `var bag = new ResourceBag<TKey>(id, blueprint, snapshot, clock, locator);`.
   Only `id` and `blueprint` are required; the rest default to a fresh bag, a bag-owned
   `BagClock`, and no locator.

## Persistence

The package ships **no** persistence layer — bring your own (Hlight DataPersistence,
PlayerPrefs, files, cloud, etc.). `BagSnapshot` is the entire bridge — a plain
`[Serializable]` DTO, not a persistence layer itself; the package never performs IO.

```csharp
[Serializable]
public class BagSnapshot
{
    public int Version;
    public List<AmountEntry> Amounts;         // id = ResourceDefinition.Id
    public List<RuleStateEntry> RuleState;    // key = "{ownerId}:{StateKey}", value = text
    public BagClockState Clock;               // meaningful only when the bag owns its clock
}
```

- Pass a `BagSnapshot` to the **constructor** to restore — there is no separate
  `LoadFrom` method. Loading happens inline, before any subscriber could exist, so it
  fires **no** `Changed` events. Amounts are clamped to `[0, cap]` on load: the save
  payload is player-controlled data, so a negative or over-cap value never reaches a
  rule's maths unclamped.
- `bag.SaveTo(snapshot)` writes amounts, one row per stateful `AttachedRule`, and clock
  state — but clock state is written **only when the bag owns its
  clock**. An injected clock's persistence is the injecting project's job; `SaveTo`
  never touches `snapshot.Clock` when the clock came in from outside.
- **`SaveTo(null)` throws `ArgumentNullException`.** This is the opposite of the
  constructor's nullable `snapshot`, where null means "fresh bag, nothing to read." At
  save time there is nothing to write into, and a save field that starts null would
  otherwise lose every flush with no log line — failing loudly on the first save beats
  losing data forever.

### Boot pattern

```csharp
var snapshot = LoadSnapshotSomehow();          // null on first run
var bag = new ResourceBag<CurrencyId>("player", blueprint, snapshot);

// ... gameplay ...

snapshot ??= new BagSnapshot();
bag.SaveTo(snapshot);
SaveSnapshotSomehow(snapshot);

bag.Dispose();
```

### Bridge to Hlight DataPersistence

`DataEntry<T>` requires `where T : class, new()`. `BagSnapshot` is a class with a
parameterless constructor, so it plugs straight in — no intermediate wrapper class:

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

For PlayerPrefs / files / cloud the shape is identical — serialize the `BagSnapshot`
POCO with whatever the project already uses. The one requirement placed on the
serializer is that it handle a `List<T>` of `[Serializable]` structs; every serializer
does, which is exactly why `BagSnapshot` uses lists rather than a `Dictionary`
(`JsonUtility` cannot serialize one, and support elsewhere varies).

## Rule state

Amounts are the bag's data — it clamps them, maps them onto defs, knows they are integers.
Anything else a rule needs to remember is the **rule's** data, and the bag only carries it:
one row per stateful rule, key scoped by owner, value produced by the rule.

That split is why a coin costs one row and a regenerating heart costs two, instead of every
resource paying for the heaviest one's fields:

```json
"Amounts":   [ {"id":"Coin","amount":120}, {"id":"Heart","amount":3} ],
"RuleState": [ {"key":"Heart:periodic","value":"{\"nextFireAt\":1780512345}"} ]
```

Derive from `AttachedRule<TState>` and there is no persistence code to write — declare the
fields, use `State`:

```csharp
[Serializable] private class RegenState { public double nextFireAt; public int streak; }

private sealed class Instance : AttachedRule<RegenState>
{
    public override string StateKey => "regen";          // default is "state"
    public override void OnTick(double now) => State.streak++;
}
```

Three `protected virtual` hooks, one job each. `Serialize` / `Deserialize` change the
**format**. `OnStateLoaded` runs right after restored data replaced `State` and is where a
rule **judges** that data — a thing the format layer cannot know. `PeriodicDeltaRule` uses
the third: a fire time only means something on this bag's clock, so one from a foreign epoch
starts the interval over instead:

```csharp
protected override void OnStateLoaded()
{
    var now = Bag.Clock.Now;
    if (State.nextFireAt > 0 && Math.Abs(State.nextFireAt - now) <= MaxPlausibleDrift) return;
    Debug.LogWarning(...);
    State.nextFireAt = now + _cfg.intervalSec;
}
```

Worth knowing:

- Default `Serialize`/`Deserialize` are `JsonUtility`, so `TState` follows its rules: public
  or `[SerializeField]` fields, no dictionaries, no polymorphic fields. Nothing about the
  rule's *type* enters the file, so renaming rule classes never breaks a save.
- A malformed value warns and leaves the constructor's state in place; an empty one passes
  quietly, since "nothing was persisted" is not corruption. Either way `OnStateLoaded` is
  not called — it only runs when state was actually restored.
- Keys are `"{ownerId}:{StateKey}"`, so the same rule SO on two defs persists separately.
  Two rules sharing one key is a collision that warns at save time — override `StateKey`.
- A rule that would rather hand-roll all of it can override `StateKey` / `SaveState()` /
  `LoadState(string)` on the non-generic `AttachedRule` directly.
- Rules attached after construction (`bag.AttachRule`) do not get `LoadState` — the
  constructor has already run. Amounts still restore normally.

## Transactions

Every public mutation runs as a transaction. Rules execute for real inside it — substituting,
waving a cost through, rejecting, emitting side effects — but their writes land on a tentative
layer, and reads (`GetAmount`, `HasAtLeast`, and therefore every rule) see that layer. The
whole set is written only if it clears:

```csharp
bool ok = bag.TrySpendAll(new[] { (coinDef, 30), (gemDef, 1) }, "buy");
// not enough gems → ok is false, coin was never charged, and no Changed event was raised
```

Why the entries run rather than being pre-checked against balances: balances do not decide the
outcome. A `SubstituteRule` can pay a cost the balance cannot cover, a free-pass rule can waive
it, and a rule can reject a spend the balance could afford. Running them on a tentative layer
is also what makes two entries competing for the same substitute resolve correctly — the second
is judged against what the first already took.

Consequences worth knowing:

- **A failed transaction leaves no trace and announces nothing.** There is no debit-then-restore
  pair to filter out, which is what made a rolled-back purchase look like two real movements.
- **A failed single `TrySpend` no longer lets its rules' side effects land.** Before, a debit
  that failed on a short balance (rather than an outright `Reject`) still paid out whatever its
  rules credited.
- **`Changed` fires after the write, in order, once per accepted mutation.** A handler reading
  `GetAmount` inside the callback already sees the settled value.
- A handler may call back into the bag; that re-entry is bounded by `MaxSideEffectDepth` like a
  rule's side effect.
- **Rule *state* is not transactional.** A rule that marks its one-shot as used during a failed
  transaction stays marked — amounts roll back, rule state does not.

### Binding one resource

```csharp
_unbind = bag.Bind(coinDef, amount => label.text = amount.ToString());   // OnDestroy: _unbind()
```

Fires only when the amount actually moves — a credit clamped at the cap reports a zero delta to
`Changed` (analytics wants it) but is not a change to bind against — and never for a
transaction that failed. Nothing is pushed at subscribe time: read `GetAmount` for the current
value. `ResourceBag<TKey>` has the same method keyed by enum.

## Remote config

A resource list that arrives as data rather than as authored assets — an event reward, an IAP
payload, a crafting cost — is a list of `ResourceAmount`. Rows name resources by
`ResourceDefinition.Id`, which for a keyed def is the enum member name, so config never needs
an asset reference:

```json
{ "rewards": [
    { "id": "Coin",  "amount": 100, "amountMax": 300 },
    { "id": "Gem",   "amount": 5 },
    { "id": "Heart", "amount": 1 }
]}
```

```csharp
bag.Grant(config.rewards, "event_reward", roll: UnityEngine.Random.Range);
bool paid    = bag.TrySpendAll(config.costs, "craft");
bool affords = bag.CanAfford(config.costs);

// To show the player what they won — the roll happened inside Grant, so ask for it back:
var granted = new List<(ResourceDefinition resource, int amount)>();
bag.Grant(config.rewards, "event_reward", UnityEngine.Random.Range, granted);
foreach (var (resource, amount) in granted) rewardPopup.AddRow(resource.Icon, amount);
```

- **The package owns no RNG.** `roll` is yours — `Random.Range`, a seeded generator, or a
  server-issued value; its contract is `(minInclusive, maxExclusive)`. Omitting it while a row
  asks for a range pays the minimum **and warns**, because a reward that quietly always pays
  its floor is a bug nobody reports.
- **An unknown id is handled differently per direction, deliberately.** `Grant` warns and skips
  that row — losing a whole reward over one typo is worse than paying most of it.
  `TrySpendAll` / `CanAfford` refuse the whole list — skipping a cost row would undercharge.
- A cost row is never rolled; only `amount` is charged.
- `granted` reports what each row **resolved to** — the request after the roll. Rules may then
  transform it (a cap clamps, a bundle def fans out), and `Changed` is what reports those
  actual movements. The buffer is cleared on entry, so one list can be reused.
- `CanAfford` reads balances only: it does not run the rule pipeline, so a rule that would
  substitute or waive part of the cost is not consulted and a spend can still succeed where
  this returned false.
- A typo in config is a runtime warning, not a compile error. Config is data; that is inherent.

Loot tables ("pick 1 of these 3 by weight") are a different shape — selection rather than
accumulation, plus weights — and belong to the project. `BundleResolveRule` remains the
SO-authored path for a chest whose contents designers edit in the Inspector.

## Cross-bag (`IBagServiceLocator`)

A rule that needs to read another bag (e.g. event token affects player coin Add) pulls
the foreign bag from the locator **once** in the AttachedRule's `OnAttach`, then
forgets the locator:

```csharp
private sealed class Instance : AttachedRule
{
    private ResourceBag _eventBag;
    public Instance(MyRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag) { }

    public override void OnAttach()
    {
        Bag.Locator?.TryProvide(out _eventBag, "event");   // key disambiguates bags-of-bags
    }
}
```

See `Samples~/04-cross-bag` for a full wiring.

**Register bags under the base type.** A rule sees its own bag as `ResourceBag` — it cannot
know the key family — so that is the type it asks the locator for:

```csharp
locator.Register<ResourceBag>(playerBag, "player");   // even though playerBag is ResourceBag<CurrencyId>
```

Registering under `ResourceBag<CurrencyId>` instead makes the rule's request miss, and a miss
is silent: `TryProvide` returns false and the rule simply never does its job.

## Extending — write a custom `ResourceRule`

```csharp
[CreateAssetMenu(menuName = "MyGame/Rules/FirstPurchaseBonus", fileName = "FirstPurchaseBonus")]
public class FirstPurchaseBonusRule : ResourceRule
{
    [SerializeField] private int bonusAmount = 100;

    public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
        => owner != null ? new Instance(this, owner, bag) : null;

    // Place rule in CoinDef.rules → Owner = CoinDef → buff applies to coin Adds.
    private sealed class Instance : AttachedRule
    {
        private readonly FirstPurchaseBonusRule _cfg;
        private bool _claimed;

        public Instance(FirstPurchaseBonusRule cfg, ResourceDefinition owner, ResourceBag bag)
            : base(cfg, owner, bag) { _cfg = cfg; }

        public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
        {
            if (intent.Resource != Owner) return;
            if (_claimed) return;
            _claimed = true;
            sideEffects.Add(new ResourceSideEffect(Owner, _cfg.bonusAmount, "first_purchase_bonus"));
        }
    }
}
```

State is plain fields on the nested Instance class — no `object` casts, no `CreateState`
boilerplate. Configuration lives on the SO; per-bag state lives on the AttachedRule.

Random rules: subclass and inject your own `IRandom` / `UnityEngine.Random` /
server-issued payload — the package is RNG-agnostic on purpose (reproducibility +
testability stay with the project).

## Key conventions

1. **Rule order = attach order.** Expand `def.rules` first, then `blueprint.extraRules`.
2. **`AttachedRule.OnAttach` must not mutate bag state.** `OnDetach` must not call
   `Bag.Add`/`Bag.TrySpend` either — during `Dispose` the bag is tearing down.
   **It also should not rely on reading amounts.** Rules attach before the constructor
   applies a loaded snapshot (deliberately — restructuring that order is fragile and out
   of scope), so a custom rule that caches `Bag.GetAmount(Owner)` in `OnAttach` silently
   reads 0 even when a snapshot is about to restore a real balance. Read amounts lazily
   (in `OnTick` / `OnBeforeAdd` / `OnBeforeSpend`) instead of caching them at attach time.
3. **Rule state persists once a rule has a `StateKey`.** It rides in
   `BagSnapshot.RuleState` (keyed `"{ownerId}:{StateKey}"`), captured by `SaveTo` and
   restored by the constructor — the project never serializes rule state itself. See
   [Rule state](#rule-state).
4. **Max amount is not auto-clamped when lowered.** Lowering a max below the current
   amount leaves the amount as-is; clamp manually if your design requires it.
5. **Domain reload during Play resets the bag.** Save before reload — the project owns
   this lifecycle, not the package.
6. **Owner-targeted rules need `def.rules` placement.** All four built-ins
   (`PeriodicDeltaRule`, `OverflowConvertRule`, `BundleResolveRule`, `SubstituteRule`)
   return `null` from `Attach` when owner is null — placing them in
   `blueprint.extraRules` silently no-ops. Cross-cutting rules with no implicit target
   can ignore this.
7. **`IBagServiceLocator` lifetime is owned by the project.** AttachedRules pull once in
   `OnAttach` and forget the locator — never store it as a long-lived field on the SO.
8. **Side-effect depth is capped at 5.** Exceeded → `Debug.LogError` and the offending
   intent is dropped (no exception thrown).
9. **`SpendOutcome.Reject` aborts everything.** Side-effects accumulated during the
   pipeline are discarded — primary failure means the whole transaction fails. To emit
   secondary effects conditionally, use `Continue` / `Substitute` instead (there is no
   `Skip`; set `SkipPrimary` on a debit for the same free-pass effect).
10. **The project owns tick cadence.** Call `bag.Tick()` — no arguments — from whichever
    driver you prefer; the bag reads its own `Clock` once per call. The package ships
    no MonoBehaviour ticker.
11. **Random / probabilistic rules are project-side.** Subclass `ResourceRule` with your own
    RNG source; Core has no opinion on reproducibility.
12. **`ResourceDefinition<TKey>.Id` is the enum member name.** It's `Key.ToString()`, so
    renaming an enum member orphans any save data written under the old name.

## Reasons

`reason` is optional on `Add` / `TrySpend` / `TrySpendAll` / `ResetAmount`; omitting it sends
`BagReasons.Unspecified` (`_unspecified`) — a real string rather than `null`, so an event
stream never carries nulls and the call sites still missing a breadcrumb stay greppable.

`BagReasons` (Core) and `RuleReasons` (Rules) expose well-known reason strings.
**System reasons are prefixed with `_`** to distinguish them from project reasons:
`BagReasons.Overflow` (`_overflow`), `BagReasons.Unspecified` (`_unspecified`),
`RuleReasons.Periodic` (`_periodic` — emitted by `PeriodicDeltaRule` for either
direction; the accompanying `Delta`'s sign says which), `RuleReasons.ResolveBundle`
(`_resolve_bundle`). `_expire` no longer exists anywhere in compiled code — expiry
moved out to a project-side recipe (see SKILL.md → Recipes). Project code should avoid
leading underscores for its own reason strings — the one exception is SKILL.md's
`ExpireRule` recipe, which deliberately reuses the literal `"_expire"` string: it is
reproducing the reason a now-deleted built-in rule used to emit, not authoring a new
project reason, so the leading underscore there is intentional rather than a violation
of this convention.

## Samples

Importable via Package Manager → ResourceBag → Samples. Five samples shipped:

| # | Name | Demonstrates |
|---|---|---|
| 01 | Basic Bag | Add / Spend / GetAmount / max-amount clamping |
| 02 | With Regen | `PeriodicDeltaRule` + project-owned tick driver + snapshot persistence |
| 03 | Bundle Reward | `BundleResolveRule` side-effect fan-out |
| 04 | Cross Bag | `IBagServiceLocator` + custom buff rule |
| 05 | Enum Keyed | `ResourceDefinition<TKey>` + enum-keyed call sites (recommended pattern) |

Each sample folder has its own `README.md` listing the SO resources to create + expected
log output. For a custom-rule recipe see [Extending](#extending--write-a-custom-resourcerule)
above.

## File layout

```
Packages/com.hlight.resource-bag/
├── Runtime/                Core asmdef
│   ├── Core/               ResourceBag (partial: Core / Pipeline), ResourceBagGeneric,
│   │                       ResourceBagConfigExtensions, ResourceDefinition(+Generic),
│   │                       ResourceRule, AttachedRule(+Generic), BagBlueprint(+Generic),
│   │                       ResourceEntry, BagReasons
│   ├── Structs/            ResourceIntent, ResourceSideEffect, ResourceChange, SpendOutcome,
│   │                       BagSnapshot, ResourceAmount
│   ├── Clock/              IBagClock, BagClock
│   └── Locator/            IBagServiceLocator
├── Rules/                  4 built-in rules (separate asmdef): PeriodicDeltaRule,
│                           OverflowConvertRule, BundleResolveRule, SubstituteRule,
│                           RoundingMode, RuleReasons
├── Tests/                  EditMode tests + TestFixtures
├── Samples~/               5 importable samples (asmdef per sample)
├── package.json
└── README.md (this file)
```

## License / status

Internal Hlight package.
