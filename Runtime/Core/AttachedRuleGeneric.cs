using System;
using UnityEngine;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// <see cref="AttachedRule"/> that persists a plain state class: declare the fields, use
    /// <see cref="State"/>, and the base handles the rest.
    /// <code>
    /// [Serializable] private class RegenState { public double nextFireAt; public int streak; }
    ///
    /// private sealed class Instance : AttachedRule&lt;RegenState&gt;
    /// {
    ///     public override void OnTick(double now) =&gt; State.streak++;
    /// }
    /// </code>
    /// </summary>
    /// <remarks>
    /// The state lands in one <see cref="BagSnapshot.RuleState"/> row as text, so the outside
    /// serializer only ever sees a string — no polymorphic list, and no type names in the save
    /// file, which would otherwise make renaming a rule class a save migration.
    /// <para>
    /// <see cref="Serialize"/> / <see cref="Deserialize"/> default to
    /// <see cref="JsonUtility"/>, which means <typeparamref name="TState"/> follows its rules:
    /// public or <c>[SerializeField]</c> fields, no dictionaries, no polymorphic fields.
    /// Override the pair for a different format — or to validate what comes back, the way
    /// <c>PeriodicDeltaRule</c> rejects a fire time from a foreign clock epoch.
    /// </para>
    /// </remarks>
    public abstract class AttachedRule<TState> : AttachedRule where TState : class, new()
    {
        /// <summary>Live state. Mutate freely; the bag persists it on <see cref="ResourceBag.SaveTo"/>.</summary>
        protected TState State { get; private set; } = new TState();

        /// <inheritdoc />
        /// <remarks>Override when one def carries two rules of this type, which would
        /// otherwise collide on <c>"{Owner.Id}:state"</c> — the bag warns when they do.</remarks>
        public override string StateKey => "state";

        protected AttachedRule(ResourceRule source, ResourceDefinition owner, ResourceBag bag)
            : base(source, owner, bag) { }

        /// <summary>Encode <paramref name="state"/> for the save file. JSON by default.</summary>
        /// <remarks>Format only — a rule that also needs to police the values it gets back
        /// does that in <see cref="OnStateLoaded"/>, so changing the format and checking the
        /// data stay independent.</remarks>
        protected virtual string Serialize(TState state) => JsonUtility.ToJson(state);

        /// <summary>
        /// Decode a value written by <see cref="Serialize"/>. Returning <c>null</c> means the
        /// text carried no usable state, and the current state is kept — an override should
        /// prefer that over throwing, since this runs inside the bag's constructor and only
        /// <see cref="ArgumentException"/> (what <see cref="JsonUtility"/> raises) is caught.
        /// </summary>
        protected virtual TState Deserialize(string value) => JsonUtility.FromJson<TState>(value);

        /// <summary>
        /// Called right after <see cref="State"/> was replaced by restored data — the place to
        /// check it and repair what the format layer cannot judge (a fire time from another
        /// clock epoch, a value beyond a cap that has since changed). Not called when nothing
        /// was restored.
        /// </summary>
        protected virtual void OnStateLoaded() { }

        /// <inheritdoc />
        public sealed override string SaveState() => Serialize(State);

        /// <inheritdoc />
        public sealed override void LoadState(string value)
        {
            // Trust boundary: player-side data. A corrupt value leaves the constructor's
            // defaults in place rather than throwing out of the bag's constructor.
            try
            {
                var loaded = Deserialize(value);
                if (loaded == null) return;
                State = loaded;
            }
            catch (ArgumentException)
            {
                Debug.LogWarning(
                    $"[ResourceBag] Rule state '{StateKey}' for '{Owner?.Id}' is not valid " +
                    $"{typeof(TState).Name} data — starting from defaults. Usually a corrupt save.");
                return;
            }

            OnStateLoaded();
        }
    }
}
