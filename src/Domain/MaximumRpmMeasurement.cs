using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FanPilot
{
    // Measure a full-duty plateau, rather than requiring thirteen lower-duty windows.
    internal sealed class MaximumRpmMeasurement
    {
        readonly DateTime started;
        DateTime lastIncrease;
        readonly List<int>[] samples;
        readonly int[] peaks;
        readonly int[] settledPeaks;
        int observations;
        public int Progress { get; private set; }

        public MaximumRpmMeasurement(int count, DateTime now)
        {
            started = lastIncrease = now;
            peaks = new int[count];
            settledPeaks = new int[count];
            samples = new List<int>[count];
            for (int i = 0; i < count; i++)
                samples[i] = new List<int>();
        }

        public FanCalibration Observe(int[] rpms, DateTime now)
        {
            if ((now - started).TotalSeconds > 90)
                throw new IOException("Maximum-RPM measurement did not stabilize within 90 seconds.");
            observations++;
            for (int i = 0; i < peaks.Length; i++)
            {
                samples[i].Add(rpms[i]);
                if (samples[i].Count > 3)
                    samples[i].RemoveAt(0);
                if (samples[i].Count < 3)
                    continue;
                int median = samples[i].OrderBy(x => x).ElementAt(1);
                if (median > settledPeaks[i] + Math.Max(75, settledPeaks[i] * 0.02))
                {
                    lastIncrease = now;
                    settledPeaks[i] = median;
                }

                peaks[i] = Math.Max(peaks[i], median);
            }

            double settled = (now - lastIncrease).TotalSeconds;
            Progress = Math.Max(0, Math.Min(95, (int)(settled * 100 / 15)));
            if (observations < 6 || peaks.Any(x => x < 800) || settled < 15)
                return null;
            var result = new FanCalibration
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedUtc = now,
                MaximumOnly = true,
                Maxima = (int[])peaks.Clone()
            };
            result.Validate(peaks.Length);
            Progress = 100;
            return result;
        }
    }
}
