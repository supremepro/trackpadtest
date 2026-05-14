using System;
using System.Collections.Generic;

namespace TrackpadWindowControl
{
    /// <summary>
    /// Tracks multi-finger touch contacts and raises events when a 3-finger
    /// drag gesture is detected.  A "drag" is distinguished from a "swipe"
    /// by requiring a minimum dwell time before movement is emitted.
    /// </summary>
    internal sealed class TouchTracker
    {
        // Set to false to suspend all gesture detection.
        public bool Enabled { get; set; } = true;

        // How many contacts trigger the gesture.
        private const int REQUIRED_FINGERS = 3;

        // Minimum milliseconds fingers must be held before we start moving.
        private const int DWELL_MS = 80;

        // Minimum logical-unit movement before we consider it a "move" vs noise.
        private const float NOISE_THRESHOLD = 4f;

        // ── Events ──────────────────────────────────────────────────────────
        public event Action?           GestureStarted;
        public event Action<float, float>? GestureMoved;   // (dx, dy) in logical units
        public event Action?           GestureEnded;

        // ── State ────────────────────────────────────────────────────────────
        private readonly object _lock = new();

        private Dictionary<int, (float x, float y)> _active = new();

        private bool  _gesturing;
        private bool  _committed;   // dwell elapsed → we are actually moving a window
        private float _prevCx, _prevCy;
        private long  _gestureStartTick;

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Update internal state from the latest batch of contacts.</summary>
        public void Update(IReadOnlyList<TouchContact> contacts)
        {
            if (!Enabled || contacts.Count == 0) return;

            lock (_lock)
            {
                // Determine which contacts report data in this frame
                // (contact count field is tracked implicitly by IsTouching).
                foreach (var c in contacts)
                {
                    if (c.IsTouching)
                        _active[c.Id] = (c.RawX, c.RawY);
                    else
                        _active.Remove(c.Id);
                }

                int fingerCount = _active.Count;

                if (!_gesturing)
                {
                    if (fingerCount >= REQUIRED_FINGERS)
                        StartGesture();
                }
                else
                {
                    if (fingerCount < REQUIRED_FINGERS)
                        EndGesture();
                    else
                        UpdateGesture();
                }
            }
        }

        /// <summary>
        /// Called when contacts suddenly stop (e.g. lift from touchpad).
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                _active.Clear();
                if (_gesturing) EndGesture();
            }
        }

        // ── Private ──────────────────────────────────────────────────────────

        private void StartGesture()
        {
            _gesturing = true;
            _committed = false;
            _gestureStartTick = Environment.TickCount64;
            (float cx, float cy) = Centroid();
            _prevCx = cx;
            _prevCy = cy;
        }

        private void UpdateGesture()
        {
            (float cx, float cy) = Centroid();
            float dx = cx - _prevCx;
            float dy = cy - _prevCy;
            _prevCx = cx;
            _prevCy = cy;

            long elapsed = Environment.TickCount64 - _gestureStartTick;

            if (!_committed)
            {
                if (elapsed < DWELL_MS) return;  // still in dwell window
                _committed = true;
                GestureStarted?.Invoke();
            }

            if (Math.Abs(dx) < NOISE_THRESHOLD && Math.Abs(dy) < NOISE_THRESHOLD)
                return;

            GestureMoved?.Invoke(dx, dy);
        }

        private void EndGesture()
        {
            bool wasCommitted = _committed;
            _gesturing = false;
            _committed = false;
            _active.Clear();

            if (wasCommitted)
                GestureEnded?.Invoke();
        }

        private (float cx, float cy) Centroid()
        {
            float sumX = 0, sumY = 0;
            foreach (var kv in _active) { sumX += kv.Value.x; sumY += kv.Value.y; }
            int n = _active.Count;
            return n > 0 ? (sumX / n, sumY / n) : (0, 0);
        }
    }
}
