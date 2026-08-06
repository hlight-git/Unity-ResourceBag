using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// Applies data-driven resource lists (<see cref="ResourceAmount"/>) to a bag — the shape
    /// remote config, an IAP payload or a quest table arrives in.
    /// </summary>
    /// <remarks>
    /// Rows are matched by <see cref="ResourceDefinition.Id"/> through the blueprint, so the
    /// bag never needs to know the key family and this works on a plain
    /// <see cref="ResourceBag"/> as well as a typed one.
    /// <para>
    /// <b>An unknown id is treated differently per direction, on purpose.</b> Granting skips the
    /// bad row and warns: losing a whole reward over one typo is worse than paying most of it.
    /// Spending refuses the whole transaction: skipping a row would silently undercharge.
    /// </para>
    /// </remarks>
    public static class ResourceBagConfigExtensions
    {
        /// <summary>
        /// Credit every row. <paramref name="roll"/> resolves a random row — pass
        /// <c>UnityEngine.Random.Range</c>, a seeded generator, or a server-issued value; the
        /// package owns no RNG. Its contract is <c>(minInclusive, maxExclusive)</c>, matching
        /// <c>Random.Range(int, int)</c>.
        /// </summary>
        /// <remarks>
        /// Omitting <paramref name="roll"/> while a row asks for a range pays the minimum and
        /// warns: a reward that quietly always pays its floor is the kind of bug nobody
        /// reports and everybody feels.
        /// <para>
        /// Pass <paramref name="granted"/> to learn what each row resolved to — a random row is
        /// rolled in here, so this is the only way the caller can show the player what they
        /// won. It reports the resolved <i>request</i>: rules may then transform it (a cap
        /// clamps, a bundle fans out), and <see cref="ResourceBag.Changed"/> is what reports
        /// those actual movements. The list is cleared before use, so one buffer can be reused.
        /// </para>
        /// </remarks>
        public static void Grant(this ResourceBag bag, IReadOnlyList<ResourceAmount> rewards,
                                 string reason = BagReasons.Unspecified, Func<int, int, int> roll = null,
                                 List<(ResourceDefinition resource, int amount)> granted = null)
        {
            granted?.Clear();
            if (bag == null || rewards == null) return;

            for (int i = 0; i < rewards.Count; i++)
            {
                var row = rewards[i];
                if (!bag.Blueprint.TryGetById(row.id, out var def))
                {
                    Debug.LogWarning(
                        $"[ResourceBag:{bag.Id}] Reward row '{row.id}' is not in this bag's blueprint — " +
                        "row skipped, the rest of the reward is still granted.");
                    continue;
                }

                var amount = Resolve(row, roll, bag.Id);
                if (amount <= 0) continue;

                bag.Add(def, amount, reason);
                granted?.Add((def, amount));
            }
        }

        /// <summary>
        /// All-or-nothing debit of every row, mapped onto <see cref="ResourceBag.TrySpendAll"/>.
        /// Returns false — spending nothing — when any row names a resource this bag has no def
        /// for. A cost row is never rolled; only <see cref="ResourceAmount.amount"/> is charged.
        /// </summary>
        public static bool TrySpendAll(this ResourceBag bag, IReadOnlyList<ResourceAmount> costs,
                                       string reason = BagReasons.Unspecified)
        {
            if (bag == null) return false;
            if (costs == null || costs.Count == 0) return true;
            if (!TryMap(bag, costs, out var mapped)) return false;

            // Resolves to the def-keyed instance method, not back into this one: the element
            // type differs, so there is no recursion here.
            return bag.TrySpendAll(mapped, reason);
        }

        /// <summary>
        /// True when every row is both known and currently covered by the bag's own balance.
        /// A read-only check: it does not run the rule pipeline, so a rule that would
        /// substitute or waive part of the cost is not consulted — a spend can still succeed
        /// where this returned false.
        /// </summary>
        public static bool CanAfford(this ResourceBag bag, IReadOnlyList<ResourceAmount> costs)
        {
            if (bag == null) return false;
            if (costs == null || costs.Count == 0) return true;
            if (!TryMap(bag, costs, out var mapped)) return false;

            for (int i = 0; i < mapped.Length; i++)
            {
                if (!bag.HasAtLeast(mapped[i].Item1, mapped[i].Item2)) return false;
            }

            return true;
        }

        private static bool TryMap(ResourceBag bag, IReadOnlyList<ResourceAmount> rows,
                                   out (ResourceDefinition, int)[] mapped)
        {
            mapped = null;
            if (rows == null || rows.Count == 0) return true;

            var result = new (ResourceDefinition, int)[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!bag.Blueprint.TryGetById(row.id, out var def))
                {
                    Debug.LogWarning(
                        $"[ResourceBag:{bag.Id}] Cost row '{row.id}' is not in this bag's blueprint — " +
                        "refusing the whole cost list rather than charging short.");
                    return false;
                }

                result[i] = (def, row.amount);
            }

            mapped = result;
            return true;
        }

        private static int Resolve(ResourceAmount row, Func<int, int, int> roll, string bagId)
        {
            if (!row.IsRandom) return row.amount;

            if (roll == null)
            {
                Debug.LogWarning(
                    $"[ResourceBag:{bagId}] Row '{row.id}' asks for {row.amount}..{row.amountMax} but no " +
                    "roll function was supplied — paying the minimum. Pass UnityEngine.Random.Range, a " +
                    "seeded generator, or a server-issued amount.");
                return row.amount;
            }

            var rolled = roll(row.amount, row.amountMax + 1);   // maxExclusive, so the top value can land
            if (rolled < row.amount) rolled = row.amount;
            if (rolled > row.amountMax) rolled = row.amountMax;
            return rolled;
        }
    }
}
