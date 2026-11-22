using System;
using FanPilot;

sealed class DelayedHardware : IHardware
{
    readonly FakeHardware inner = new FakeHardware();
    readonly Action delay;
    public DelayedHardware(Action delay)
    {
        this.delay = delay;
    }

    public bool Initialize()
    {
        return inner.Initialize();
    }

    public int FanCount()
    {
        return inner.FanCount();
    }

    public uint Temperature()
    {
        return inner.Temperature();
    }

    public bool Select(byte index)
    {
        delay();
        return inner.Select(index);
    }

    public bool TestMode(byte enabled)
    {
        delay();
        return inner.TestMode(enabled);
    }

    public bool Duty(byte value)
    {
        delay();
        return inner.Duty(value);
    }

    public int Rpm()
    {
        int duty = (int)Math.Round(inner.Pwm[inner.Selected] / 255.0 * 100);
        return inner.Active[inner.Selected] ? 1000 + duty * 40 : 3400;
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}

static partial class Tests
{
    static void RunCalibrationTimingTests()
    {
        Run("maximum RPM calibration handles ordinary tachometer noise", delegate
        {
            var hardware = new FakeHardware();
            using (var controller = Open(hardware))
            {
                controller.StartCalibration(now);
                Snapshot result = null;
                for (int second = 1; second <= 40; second++)
                {
                    hardware.Speeds[0] = 5700 + (second % 3 - 1) * 40;
                    hardware.Speeds[1] = 5600;
                    result = controller.Poll(now.AddSeconds(second));
                    if (!result.Calibrating)
                        break;
                }

                Check(result.Calibration != null && result.Calibration.MaximumOnly && result.ReleaseConfirmed, "Noisy full-duty measurement failed: " + result.Error);
                Check(result.Calibration.Maxima[0] >= 5660 && result.Calibration.Maxima[0] <= 5740, "Peak outlier");
            }
        });
        Run("maximum RPM scale is per fan and never increases the PWM limit", delegate
        {
            var scale = new FanCalibration
            {
                Id = "max",
                CreatedUtc = now,
                MaximumOnly = true,
                Maxima = new[]
                {
                    5700,
                    5600
                }
            };
            scale.Validate(2);
            Check(scale.ToRpm(0, 80) == 4560 && scale.ToRpm(1, 80) == 4480, "Per-fan scale");
            Check(scale.ToDuty(0, 9000) == 100 && scale.ToDuty(0, 0) == 40, "Unbounded command");
            var copy = scale.Clone();
            copy.Maxima[0] = 1000;
            Check(scale.Maxima[0] == 5700, "Shared maximum array");
        });
        Run("failed reset after maximum measurement does not publish a new scale", delegate
        {
            var hardware = new FakeHardware();
            using (var controller = Open(hardware))
            {
                controller.LoadCalibration(Table());
                controller.StartCalibration(now);
                hardware.RejectReset = 0;
                Snapshot result = null;
                for (int second = 1; second <= 40; second++)
                {
                    result = controller.Poll(now.AddSeconds(second));
                    if (!result.Calibrating)
                        break;
                }

                Check(!result.ReleaseConfirmed && result.Calibration.Id == "test" && !String.IsNullOrEmpty(result.Error), "Published after unconfirmed reset");
                hardware.RejectReset = -1;
                Check(controller.Disable(), "Reset retry failed");
            }
        });
        Run("calibration keeps its bounded latency allowance across a sample window", delegate
        {
            DateTime time = now;
            int delay = 1;
            using (var controller = new FanController(new DelayedHardware(delegate
            {
                time = time.AddSeconds(delay);
            }), true, new FakeGpu(), delegate
            {
                return time;
            }))
            {
                controller.Connect();
                controller.StartCalibration(time);
                foreach (int seconds in new[]
                {
                    4,
                    2,
                    0,
                    0
                }

                )
                {
                    delay = seconds;
                    time = time.AddSeconds(1);
                    controller.Poll(time);
                }

                Snapshot result = controller.GetSnapshot();
                Check(result.Calibrating && result.CalibrationProgress > 0, "Variable driver latency discarded a valid window: " + result.Error);
            }
        });
        Run("calibration excludes slow driver writes from settling time", delegate
        {
            DateTime time = now;
            using (var controller = new FanController(new DelayedHardware(delegate
            {
                time = time.AddSeconds(1);
            }), true, new FakeGpu(), delegate
            {
                return time;
            }))
            {
                controller.Connect();
                controller.StartCalibration(time);
                Snapshot result = controller.GetSnapshot();
                for (int tick = 0; tick < 100 && result.Calibrating; tick++)
                {
                    time = time.AddSeconds(1);
                    result = controller.Poll(time);
                }

                Check(result.CalibrationProgress == 100 && result.Calibration != null && result.ReleaseConfirmed, "Slow-driver sweep failed: " + result.Error);
                Check(result.Calibration.MaximumOnly && result.Calibration.Maxima[0] == 5000, "Measured wrong duty window");
            }
        });
    }
}
