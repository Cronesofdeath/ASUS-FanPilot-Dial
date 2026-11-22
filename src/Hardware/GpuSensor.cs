using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FanPilot
{
    public sealed class GpuSample
    {
        public int? Temperature;
        public DateTime Timestamp;
    }

    public interface IGpuSensor : IDisposable
    {
        GpuSample Read(DateTime now);
    }

    public sealed class DemoGpuSensor : IGpuSensor
    {
        public GpuSample Read(DateTime now)
        {
            return new GpuSample
            {
                Temperature = 54,
                Timestamp = now
            };
        }

        public void Dispose()
        {
        }
    }

    // Native GPU reads never block the fan-control thread. Late results are discarded.
    public sealed class CachedGpuSensor : IGpuSensor
    {
        readonly object gate = new object ();
        readonly GpuTemperature reader = new GpuTemperature();
        readonly Timer timer;
        volatile bool disposed;
        int busy;
        int? temperature;
        long measured;
        public CachedGpuSensor()
        {
            timer = new Timer(Refresh, null, 0, 1000);
        }

        void Refresh(object ignored)
        {
            if (disposed || Interlocked.CompareExchange(ref busy, 1, 0) != 0)
                return;
            Task.Run(delegate
            {
                var clock = Stopwatch.StartNew();
                try
                {
                    GpuReading value = reader.Read();
                    lock (gate)
                    {
                        temperature = clock.ElapsedMilliseconds <= 1500 ? value.Temperature : null;
                        measured = Stopwatch.GetTimestamp();
                    }
                }
                catch
                {
                    lock (gate)
                        temperature = null;
                }
                finally
                {
                    Interlocked.Exchange(ref busy, 0);
                    if (disposed)
                        reader.Dispose();
                }
            });
        }

        public GpuSample Read(DateTime now)
        {
            lock (gate)
            {
                double age = (Stopwatch.GetTimestamp() - measured) / (double)Stopwatch.Frequency;
                return new GpuSample
                {
                    Temperature = measured != 0 && age <= 3 ? temperature : null,
                    Timestamp = now.AddSeconds(-Math.Min(age, 3600))
                };
            }
        }

        public void Dispose()
        {
            disposed = true;
            timer.Dispose();
            if (Interlocked.CompareExchange(ref busy, 0, 0) == 0)
                reader.Dispose();
        }
    }
}
