using UnityEngine;

namespace Hlight.ResourceBag.Rules
{
    /// <summary>
    /// Rounding strategies for fractional ratio conversions in rules
    /// (e.g., <c>OverflowConvertRule</c>, <c>SubstituteRule</c>).
    /// </summary>
    public enum RoundingMode
    {
        /// <summary>Round toward negative infinity (truncate toward zero for non-negatives).</summary>
        Floor = 0,

        /// <summary>Banker's rounding (System.Math.Round, ToEven).</summary>
        Round = 1,

        /// <summary>Round toward positive infinity (ceiling).</summary>
        Ceil = 2
    }

    /// <summary>
    /// Helper for applying <see cref="RoundingMode"/> to a float value, returning int.
    /// Allocation-free.
    /// </summary>
    public static class RoundingHelper
    {
        /// <summary>Apply <paramref name="mode"/> to <paramref name="value"/>, return as int.</summary>
        public static int Round(float value, RoundingMode mode)
        {
            switch (mode)
            {
                case RoundingMode.Floor: return Mathf.FloorToInt(value);
                case RoundingMode.Ceil: return Mathf.CeilToInt(value);
                case RoundingMode.Round:
                default:
                    return Mathf.RoundToInt(value);
            }
        }
    }
}
