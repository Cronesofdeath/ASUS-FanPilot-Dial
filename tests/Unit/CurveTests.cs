using System;
using System.Linq;
using FanPilot;

sealed class FakeGpu : IGpuSensor
{
    public int? Value = 50;
    public double Age;
    public bool Closed;
    public GpuSample Read(DateTime now)
    {
        return new GpuSample
        {
            Temperature = Value,
            Timestamp = now.AddSeconds(-Age)
        };
    }

    public void Dispose()
    {
        Closed = true;
    }
}

static partial class Tests
{
    static CurveSettings Profile()
    {
        return new CurveSettings
        {
            SharedControl = false,
            Fan1Source = FanTemperatureSource.Cpu,
            Fan2Source = FanTemperatureSource.Gpu,
            FanShift = 0,
            HysteresisDown = 0,
            HysteresisUp = 0
        };
    }

    static FanController Curved(FakeHardware hardware, FakeGpu graphics, CurveSettings profile = null)
    {
        var result = new FanController(hardware, true, graphics);
        result.Connect();
        result.ConfigureCurve(profile ?? Profile());
        result.EnableCurve(now);
        return result;
    }

    static FanCalibration Table()
    {
        return new FanCalibration
        {
            Id = "test",
            CreatedUtc = now,
            Rpms = new[]
            {
                Enumerable.Range(0, 13).Select(i => 2000 + i * 300).ToArray(),
                Enumerable.Range(0, 13).Select(i => 1800 + i * 250).ToArray()
            }
        };
    }

    static void RunCurveTests()
    {
        Run("curve interpolates and clamps to end points", delegate
        {
            var p = Profile();
            Check(CurveMath.Interpolate(p.Cpu, 65) == 62.5, "Interpolation");
            Check(CurveMath.Interpolate(p.Cpu, 0) == 40 && CurveMath.Interpolate(p.Cpu, 125) == 100, "Endpoints");
        });
        Run("fan shift uses only hotter sensor", delegate
        {
            Check(CurveMath.Shift(50, 70, 0) == 50 && CurveMath.Shift(50, 70, 50) == 60 && CurveMath.Shift(50, 70, 100) == 70 && CurveMath.Shift(70, 50, 100) == 70, "Shift");
        });
        Run("independent numbered fans use their selected temperature sources", delegate
        {
            var p = Profile();
            p.CpuFan = 1;
            p.GpuFan = 0;
            var d = CurveMath.Targets(p, 70, 50, 3);
            Check(d.SequenceEqual(new[] { 70, 45, 70 }), "Mapping");
        });
        Run("curve rejects unsafe duty bounds", delegate
        {
            var p = Profile();
            p.Cpu[0].Duty = 39;
            Throws(delegate
            {
                p.Validate(2, true);
            });
            p = Profile();
            p.Cpu[7].Duty = 101;
            Throws(delegate
            {
                p.Validate(2, true);
            });
        });
        Run("curve rejects descending temperatures and duty", delegate
        {
            var p = Profile();
            p.Cpu[1].Temperature = 30;
            Throws(delegate
            {
                p.Validate(2, true);
            });
            p = Profile();
            p.Gpu[5].Duty = 40;
            Throws(delegate
            {
                p.Validate(2, true);
            });
        });
        Run("fan-numbered control does not require CPU GPU assignment", delegate
        {
            var profile = Profile();
            profile.MappingConfirmed = false;
            profile.CpuFan = 99;
            profile.GpuFan = -1;
            profile.Validate(2, true);
            Check(CurveMath.Targets(profile, 70, 50, 2).SequenceEqual(new[] { 70, 45 }), "Used obsolete fan identity");
        });
        Run("shared control uses the hotter component and higher request", delegate
        {
            var profile = Profile();
            profile.SharedControl = true;
            profile.Gpu = new[]
            {
                new CurvePoint(20, 80),
                new CurvePoint(100, 80)
            };
            profile.Validate(2, true);
            Check(CurveMath.Targets(profile, 70, 40, 2).SequenceEqual(new[] { 80, 80 }), "Shared demand");
            profile.Fan1Source = (FanTemperatureSource)99;
            Throws(delegate
            {
                profile.Validate(2, true);
            });
        });
        Run("curve rejects invalid response settings", delegate
        {
            var p = Profile();
            p.FanShift = 101;
            Throws(delegate
            {
                p.Validate(2, true);
            });
            p = Profile();
            p.HysteresisDown = 61;
            Throws(delegate
            {
                p.Validate(2, true);
            });
        });
        Run("curve codec round trip isolates edits", delegate
        {
            var p = Profile();
            var q = p.Clone();
            q.Cpu[0].Duty = 45;
            Check(p.Cpu[0].Duty == 40, "Shared draft");
            Throws(delegate
            {
                CurveCodec.Decode<CurveSettings>("invalid");
            });
        });
        Run("response delays then limits falling speed", delegate
        {
            var r = new CurveResponse(new[] { 100 });
            Check(r.Step(new[] { 40 }, 2, 8, now)[0] == 100, "Early step");
            Check(r.Step(new[] { 40 }, 2, 8, now.AddSeconds(7))[0] == 100, "Early step");
            Check(r.Step(new[] { 40 }, 2, 8, now.AddSeconds(8))[0] == 97, "Down rate");
            Check(r.Step(new[] { 40 }, 2, 8, now.AddSeconds(9))[0] == 94, "Delay restarted");
        });
        Run("response reverses direction and uses deadband", delegate
        {
            var r = new CurveResponse(new[] { 60 });
            Check(r.Step(new[] { 61 }, 2, 8, now)[0] == 60, "Noise");
            Check(r.Step(new[] { 90 }, 2, 8, now)[0] == 60, "Up delay");
            Check(r.Step(new[] { 90 }, 2, 8, now.AddSeconds(2))[0] == 70, "Up rate");
            Check(r.Step(new[] { 40 }, 2, 8, now.AddSeconds(3))[0] == 70, "Reverse delay");
        });
        Run("curve preview does not write fans", delegate
        {
            var f = new FakeHardware();
            using (var c = Open(f))
            {
                c.ConfigureCurve(Profile());
                Check(c.GetSnapshot().PreviewDuties.Length == 2, "Preview");
                Check(!f.Calls.Any(x => x.StartsWith("duty:")), "Write");
            }
        });
        Run("curve starts at its calculated target without a full-speed burst", delegate
        {
            var f = new FakeHardware();
            using (var c = Curved(f, new FakeGpu()))
            {
                Check(c.GetSnapshot().Duties.SequenceEqual(new[] { 55, 45 }), "Startup must use current temperatures");
                Check(!f.Calls.Any(x => x.EndsWith(":255")), "Forced full-duty startup write");
                Snapshot s = null;
                for (int i = 1; i <= 25; i++)
                    s = c.Poll(now.AddSeconds(i));
                Check(s.Mode == "Curve" && s.Duties.SequenceEqual(new[] { 55, 45 }), "Independent outputs " + String.Join(",", s.Duties));
                int writes = f.Calls.Count(x => x.StartsWith("duty:"));
                c.Poll(now.AddSeconds(26));
                Check(f.Calls.Count(x => x.StartsWith("duty:")) == writes + 2, "Curve command was not refreshed for both fans");
            }
        });
        Run("curve polling faster than cadence does not advance outputs", delegate
        {
            var f = new FakeHardware();
            using (var c = Curved(f, new FakeGpu()))
            {
                c.Poll(now);
                int before = c.GetSnapshot().Duties[0];
                for (int i = 1; i < 10; i++)
                    c.Poll(now.AddMilliseconds(i * 50));
                Check(c.GetSnapshot().Duties[0] == before, "Fast write");
            }
        });
        Run("GPU loss releases curve and latches off", delegate
        {
            var g = new FakeGpu();
            using (var c = Curved(new FakeHardware(), g))
            {
                g.Value = null;
                var s = c.Poll(now.AddSeconds(1));
                Check(!s.Manual && s.ReleaseConfirmed, "Lost GPU override");
                g.Value = 50;
                Check(!c.Poll(now.AddSeconds(2)).Manual, "Auto enabled");
            }
        });
        Run("stale GPU sample releases curve", delegate
        {
            var g = new FakeGpu();
            using (var c = Curved(new FakeHardware(), g))
            {
                g.Age = 4;
                Check(!c.Poll(now.AddSeconds(1)).Manual, "Stale accepted");
            }
        });
        Run("explicit CPU fallback permits missing GPU", delegate
        {
            var g = new FakeGpu
            {
                Value = null
            };
            var p = Profile();
            p.CpuFallback = true;
            using (var c = Curved(new FakeHardware(), g, p))
                Check(c.Poll(now.AddSeconds(1)).Manual, "Fallback failed");
        });
        Run("GPU hot guard bypasses response delay", delegate
        {
            var g = new FakeGpu();
            using (var c = Curved(new FakeHardware(), g))
            {
                for (int i = 1; i < 20; i++)
                    c.Poll(now.AddSeconds(i));
                g.Value = 85;
                var s = c.Poll(now.AddSeconds(20));
                Check(s.CoolingPriority && s.Duties.All(x => x == 100), "Thermal bypass");
                g.Value = 93;
                Check(!c.Poll(now.AddSeconds(21)).Manual, "GPU hard guard");
            }
        });
        Run("brief CPU spike boosts cooling without giving up the curve", delegate
        {
            var f = new FakeHardware();
            using (var c = Curved(f, new FakeGpu()))
            {
                f.Temp = 89;
                Check(!c.Poll(now.AddSeconds(1)).CoolingPriority, "Premature full cooling");
                f.Temp = 92;
                var spike = c.Poll(now.AddSeconds(2));
                Check(spike.Manual && spike.CoolingPriority && spike.Duties.All(x => x == 100), "No immediate cooling");
                f.Temp = 84;
                var cooled = c.Poll(now.AddSeconds(3));
                Check(cooled.Manual && !cooled.CoolingPriority, "Spike stopped the curve");
            }
        });
        Run("sustained severe heat hands off only after full cooling", delegate
        {
            var f = new FakeHardware();
            using (var c = Curved(f, new FakeGpu()))
            {
                f.Temp = 95;
                for (int second = 1; second <= 6; second++)
                    Check(c.Poll(now.AddSeconds(second)).Manual, "Handoff happened before the full cooling window");
                var result = c.Poll(now.AddSeconds(7));
                Check(!result.Manual && result.ReleaseConfirmed && result.Error.Contains("despite full cooling"), "No sustained-heat handoff");
            }
        });
        Run("CPU invalidation resets a running curve", delegate
        {
            var f = new FakeHardware();
            using (var c = Curved(f, new FakeGpu()))
            {
                f.Temp = 0;
                var s = c.Poll(now.AddSeconds(1));
                Check(!s.Manual && s.ReleaseConfirmed && !s.Valid, "CPU reset");
            }
        });
        Run("curve partial write failure resets all fans", delegate
        {
            var f = new FakeHardware
            {
                RejectDuty = 1
            };
            using (var c = Curved(f, new FakeGpu()))
            {
                Check(!c.GetSnapshot().Manual && c.GetSnapshot().ReleaseConfirmed, "Failure rollback");
                EveryReset(f, 2);
            }
        });
        Run("calibration interpolates per fan with bounded inverse", delegate
        {
            var table = Table();
            table.Validate(2);
            Check(table.ToRpm(0, 100) == 5600 && table.ToRpm(1, 100) == 4800, "Per fan maxima");
            Check(table.ToDuty(0, 9000) == 100 && table.ToDuty(0, 0) == 40 && table.ToRpm(0, 42.5) == 2150, "Limits/interpolation");
        });
        Run("flat and nonmonotonic calibration rejected", delegate
        {
            var table = Table();
            table.Rpms[0] = Enumerable.Repeat(3000, 13).ToArray();
            Throws(delegate
            {
                table.Validate(2);
            });
            table = Table();
            table.Rpms[1][4] = 200;
            Throws(delegate
            {
                table.Validate(2);
            });
        });
        Run("calibration requires cool CPU and live cool GPU", delegate
        {
            var f = new FakeHardware
            {
                Temp = 70
            };
            using (var c = Open(f))
            {
                c.StartCalibration(now);
                Check(!c.GetSnapshot().Calibrating && !f.Active.Any(x => x), "Hot CPU start");
            }

            var g = new FakeGpu
            {
                Value = null
            };
            using (var c = new FanController(new FakeHardware(), true, g))
            {
                c.Connect();
                c.StartCalibration(now);
                Check(!c.GetSnapshot().Calibrating, "No GPU start");
            }
        });
        Run("calibration measures maximum RPM at full duty and restores firmware", delegate
        {
            var f = new FakeHardware();
            using (var c = Open(f))
            {
                c.StartCalibration(now);
                Check(c.GetSnapshot().Calibrating, "Start");
                Snapshot s = null;
                for (int t = 1; t <= 65; t++)
                {
                    int duty = c.GetSnapshot().Duties[0];
                    f.Speeds[0] = 2000 + (duty - 40) * 60;
                    f.Speeds[1] = 1800 + (duty - 40) * 50;
                    s = c.Poll(now.AddSeconds(t));
                }

                Check(!s.Manual && s.ReleaseConfirmed && s.CalibrationProgress == 100 && s.Calibration != null, "Completion " + s.Error);
                Check(s.Calibration.MaximumOnly && s.Calibration.Maxima[0] == 5600 && s.Calibration.Maxima[1] == 4800, "Measured range");
                foreach (string call in f.Calls.Where(x => x.StartsWith("duty:")))
                    Check(int.Parse(call.Split(':')[2]) == Rules.Pwm(100), "Floor");
                EveryReset(f, 2);
            }
        });
        Run("calibration cancellation preserves prior table", delegate
        {
            using (var c = Open(new FakeHardware()))
            {
                c.LoadCalibration(Table());
                c.StartCalibration(now);
                c.Poll(now.AddSeconds(1));
                c.Disable();
                var s = c.GetSnapshot();
                Check(!s.Calibrating && s.ReleaseConfirmed && s.Calibration.Id == "test", "Prior table lost");
            }
        });
        Run("calibration aborts on GPU temperature or loss", delegate
        {
            var g = new FakeGpu();
            using (var c = new FanController(new FakeHardware(), true, g))
            {
                c.Connect();
                c.StartCalibration(now);
                g.Value = 70;
                var s = c.Poll(now.AddSeconds(1));
                Check(!s.Calibrating && s.ReleaseConfirmed, "GPU heat");
            }

            g = new FakeGpu();
            using (var c = new FanController(new FakeHardware(), true, g))
            {
                c.Connect();
                c.StartCalibration(now);
                g.Value = null;
                Check(!c.Poll(now.AddSeconds(1)).Calibrating, "GPU loss");
            }
        });
        Run("calibration aborts on CPU heat", delegate
        {
            var f = new FakeHardware();
            using (var c = Open(f))
            {
                c.StartCalibration(now);
                f.Temp = 75;
                var s = c.Poll(now.AddSeconds(1));
                Check(!s.Manual && s.ReleaseConfirmed, "CPU heat");
            }
        });
        Run("calibration unstable RPM times out", delegate
        {
            var f = new FakeHardware();
            using (var c = Open(f))
            {
                c.StartCalibration(now);
                Snapshot s = null;
                for (int t = 1; t <= 91; t++)
                {
                    f.Speeds[0] = 3000 + t * 20;
                    s = c.Poll(now.AddSeconds(t));
                }

                Check(!s.Calibrating && s.ReleaseConfirmed && s.Error.Contains("90 seconds"), "Unstable timeout");
            }
        });
        Run("calibration stalled sampling times out", delegate
        {
            using (var c = Open(new FakeHardware()))
            {
                c.StartCalibration(now);
                var s = c.Poll(now.AddSeconds(91));
                Check(!s.Manual && s.ReleaseConfirmed, "Sampling deadline");
            }
        });
        Run("fixed configuration cannot interrupt calibration", delegate
        {
            using (var c = Open(new FakeHardware()))
            {
                c.StartCalibration(now);
                Throws(delegate
                {
                    c.Configure(40);
                });
                Check(c.GetSnapshot().Calibrating, "State changed");
            }
        });
        Run("calibration table snapshots are isolated", delegate
        {
            using (var c = Open(new FakeHardware()))
            {
                c.LoadCalibration(Table());
                c.GetSnapshot().Calibration.Rpms[0][0] = 0;
                Check(c.GetSnapshot().Calibration.Rpms[0][0] == 2000, "Mutable calibration");
            }
        });
    }
}
