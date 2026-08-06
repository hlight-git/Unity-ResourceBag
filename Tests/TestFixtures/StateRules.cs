using System;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Test-only rule persisting several fields through <see cref="AttachedRule{TState}"/>.
    /// Seed feeds a fresh instance; Mirrored exposes what the live one holds, since the bag
    /// does not hand out its attachments.
    /// </summary>
    internal sealed class MultiFieldStateRule : ResourceRule
    {
        [Serializable]
        internal sealed class Data
        {
            public double next;
            public int streak;
            public string tag;
            public bool flagged;
        }

        public Data Seed = new Data();
        public Data Mirrored;

        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => new Instance(this, owner, bag);

        private sealed class Instance : AttachedRule<Data>
        {
            private readonly MultiFieldStateRule _cfg;

            public Instance(MultiFieldStateRule cfg, ResourceDefinition owner, ResourceBag bag)
                : base(cfg, owner, bag)
            {
                _cfg = cfg;
                State.next = cfg.Seed.next;
                State.streak = cfg.Seed.streak;
                State.tag = cfg.Seed.tag;
                State.flagged = cfg.Seed.flagged;
                _cfg.Mirrored = State;
            }

            public override void OnTick(double now) => _cfg.Mirrored = State;
        }
    }

    /// <summary>
    /// Test-only rule overriding <c>Serialize</c>/<c>Deserialize</c> to prove the format is the
    /// rule's choice — here a bare number instead of JSON.
    /// </summary>
    internal sealed class CustomFormatStateRule : ResourceRule
    {
        [Serializable]
        internal sealed class Data { public double value; }

        public double Seed;
        public double Mirrored;
        public string LastWritten;

        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => new Instance(this, owner, bag);

        private sealed class Instance : AttachedRule<Data>
        {
            private readonly CustomFormatStateRule _cfg;

            public Instance(CustomFormatStateRule cfg, ResourceDefinition owner, ResourceBag bag)
                : base(cfg, owner, bag)
            {
                _cfg = cfg;
                State.value = cfg.Seed;
            }

            protected override string Serialize(Data state)
            {
                _cfg.LastWritten = state.value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                return _cfg.LastWritten;
            }

            protected override Data Deserialize(string value)
                => double.TryParse(value, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                   ? new Data { value = parsed }
                   : null;                       // unparsable text carries no state

            // Judging the value is a separate job from decoding it.
            protected override void OnStateLoaded()
            {
                if (State.value < 0) State.value = 0;
            }

            public override void OnTick(double now) => _cfg.Mirrored = State.value;
        }
    }

    /// <summary>Writes an empty value — must be treated as "nothing to persist", not as a row.</summary>
    internal sealed class EmptyValueStateRule : ResourceRule
    {
        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => new Instance(this, owner, bag);

        private sealed class Instance : AttachedRule
        {
            public Instance(ResourceRule source, ResourceDefinition owner, ResourceBag bag)
                : base(source, owner, bag) { }

            public override string StateKey => "empty";
            public override string SaveState() => "";
        }
    }

    /// <summary>Two attachments on one def sharing a state key — the collision the bag warns about.</summary>
    internal sealed class FixedKeyStateRule : ResourceRule
    {
        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => new Instance(this, owner, bag);

        private sealed class Instance : AttachedRule
        {
            public Instance(ResourceRule source, ResourceDefinition owner, ResourceBag bag)
                : base(source, owner, bag) { }

            public override string StateKey => "fixed";
            public override string SaveState() => "1";
        }
    }
}
