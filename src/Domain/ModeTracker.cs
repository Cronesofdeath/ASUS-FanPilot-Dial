namespace FanPilot
{
    internal sealed class ModeTracker
    {
        int missingSamples;
        public PerformanceMode? DetectedMode { get; private set; }
        public PerformanceMode? PendingMode { get; private set; }

        // A missing mode takes three reads to confirm. A change during active control takes two.
        public bool Observe(PerformanceMode? mode, bool confirmChange)
        {
            if (!mode.HasValue)
            {
                PendingMode = null;
                if (missingSamples < 3)
                    missingSamples++;
                if (missingSamples < 3)
                    return false;
                DetectedMode = null;
                return true;
            }

            missingSamples = 0;
            if (confirmChange && PendingMode != mode)
            {
                PendingMode = mode;
                return false;
            }

            DetectedMode = mode;
            PendingMode = null;
            return true;
        }
    }
}
