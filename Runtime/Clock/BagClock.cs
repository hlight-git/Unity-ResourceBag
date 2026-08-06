using System;
using UnityEngine;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// Persistable state of a <see cref="BagClock"/>. Struct so it drops straight
    /// into <see cref="BagSnapshot"/>.
    /// </summary>
    [Serializable]
    public struct BagClockState
    {
        /// <summary>The clock rules read. Advances only by time that was actually granted.</summary>
        public double timeline;

        /// <summary>Wall clock at the last anchor. Used only to measure the gap since then.</summary>
        public double lastSeenUtc;
    }

    /// <summary>
    /// Default <see cref="IBagClock"/>. Anchors on device UTC at construction and at
    /// every app resume; in between, time advances from a monotonic in-process source,
    /// so changing the device clock mid-session has no effect at all.
    /// </summary>
    /// <remarks>
    /// Offline time is clamped to <c>maxOfflineSeconds</c> and the uncredited remainder
    /// is discarded rather than banked. The cap is a speed bump, not a defence — a
    /// player who repeats background/advance/resume still gains. The only real defence
    /// is an authoritative server clock injected as an <see cref="IBagClock"/>.
    /// </remarks>
    public sealed class BagClock : IBagClock, IDisposable
    {
        /// <summary>Eight hours.</summary>
        public const double DefaultMaxOfflineSeconds = 8 * 3600;

        private readonly double _maxOffline;

        private BagClockState _state;
        private double _anchor;         // _state.timeline at the last anchor
        private double _monoAtAnchor;   // Time.unscaledTimeAsDouble at the last anchor
        private bool _anchored;
        private bool _suspectSuspend;
        private bool _disposed;

        public BagClock(BagClockState saved = default,
                        double maxOfflineSeconds = DefaultMaxOfflineSeconds)
        {
            _maxOffline = maxOfflineSeconds < 0 ? 0 : maxOfflineSeconds;
            _state = Sanitize(saved, out var repaired);
            if (repaired)
            {
                Debug.LogWarning(
                    $"[BagClock] Saved state is implausible (timeline={saved.timeline}, " +
                    $"lastSeenUtc={saved.lastSeenUtc}) — resetting to a first run instead of " +
                    "propagating it. Usually a hand-edited or corrupted save.");
            }
            Anchor();
            Application.focusChanged += OnFocusChanged;
        }

        // A plausible Unix timestamp is nowhere near this. The ceiling sits far below the
        // magnitude at which adding an offline grant falls under the double ULP and the
        // clock silently freezes.
        private const double MaxPlausibleUnixSeconds = 1e12;

        // Trust boundary: this arrives from a save file. A non-finite value poisons the
        // timeline permanently — the ratchet cannot heal NaN and SaveTo would write it
        // straight back — so repair rather than propagate, and say so out loud.
        private static BagClockState Sanitize(BagClockState saved, out bool repaired)
        {
            repaired = false;
            if (!IsPlausible(saved.timeline)) { saved.timeline = 0; repaired = true; }
            if (!IsPlausible(saved.lastSeenUtc)) { saved.lastSeenUtc = 0; repaired = true; }
            return saved;
        }

        private static bool IsPlausible(double seconds)
            => !double.IsNaN(seconds) && !double.IsInfinity(seconds)
               && seconds >= 0 && seconds < MaxPlausibleUnixSeconds;

        /// <summary>Monotonic. Reading this also advances the ratchet.</summary>
        public double Now
        {
            get
            {
                var candidate = _anchor + (Time.unscaledTimeAsDouble - _monoAtAnchor);
                if (candidate > _state.timeline) _state.timeline = candidate;
                return _state.timeline;
            }
        }

        /// <summary>State to persist. Reading it includes reading <see cref="Now"/>.</summary>
        public BagClockState State
        {
            get
            {
                _ = Now;
                return _state;
            }
        }

        /// <summary>
        /// Re-read the wall clock and grant capped offline time. Called automatically on
        /// regaining focus; public so a project can drive it from OnApplicationPause.
        /// </summary>
        public void Reanchor() => Anchor();

        /// <summary>
        /// How much offline time an anchor may grant. <paramref name="inSession"/> is the
        /// monotonic time the app spent *running* since the last anchor.
        /// </summary>
        /// <remarks>
        /// Subtracting <paramref name="inSession"/> is the whole point. Running time is
        /// already on the timeline — <see cref="Now"/> credits it from the monotonic source —
        /// so the wall-clock gap alone counts it a second time. That paid a player twice for
        /// every re-anchor: in the Editor <c>Application.focusChanged</c> fires whenever focus
        /// moves between the Editor and the Game view, so a five-second-interval regen granted
        /// two on its first fire instead of one.
        /// </remarks>
        internal static double ComputeOfflineGrant(double utc, double lastSeenUtc,
                                                   double inSession, double maxOffline)
        {
            var offline = (utc - lastSeenUtc) - inSession;
            if (offline < 0) offline = 0;                      // clock moved back, or no real gap
            if (offline > maxOffline) offline = maxOffline;
            return offline;
        }

        private void Anchor()
        {
            var mono = Time.unscaledTimeAsDouble;
            var utc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

            // Read this before _monoAtAnchor is overwritten below.
            var inSession = _anchored ? mono - _monoAtAnchor : 0.0;

            if (_state.lastSeenUtc <= 0)
            {
                // First ever run: no offline credit.
                if (_state.timeline <= 0) _state.timeline = utc;
            }
            else
            {
                // Credit the running time first, then add only the part of the wall-clock gap
                // that the app was not running for.
                if (_anchored) _ = Now;
                _state.timeline += ComputeOfflineGrant(utc, _state.lastSeenUtc, inSession, _maxOffline);
            }

            // Always reset to now, so the remainder beyond the cap is forfeited
            // instead of banked for later launches.
            _state.lastSeenUtc = utc;

            _anchor = _state.timeline;
            _monoAtAnchor = Time.unscaledTimeAsDouble;
            _anchored = true;
        }

        // Only accept a wall-clock jump after focus was lost. A device-clock change
        // while the app is in the foreground never reaches the timeline.
        private void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
            {
                _suspectSuspend = true;
                return;
            }

            if (!_suspectSuspend) return;
            _suspectSuspend = false;
            Anchor();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Application.focusChanged -= OnFocusChanged;
        }
    }
}
