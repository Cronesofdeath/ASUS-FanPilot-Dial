using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FanPilot;

// Explicit, supervised validation only. Never included in the portable application.
static class HardwareValidation
{
    static void Print(Snapshot s)
    {
        Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + s.Mode + " CPU=" + s.Temperature + " GPU=" + s.GpuTemperature + " RPM=" + String.Join(",", s.Rpms) + " PWM=" + String.Join(",", s.Duties) + " progress=" + s.CalibrationProgress + " " + s.Error);
    }

    static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--hardware-validate")
            return 2;
        string folder = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(folder);
        using (var bridge = new BridgeClient())
        {
            try
            {
                Snapshot s = bridge.Connect(false);
                Print(s);
                for (int i = 0; i < 3; i++)
                {
                    Thread.Sleep(1000);
                    s = bridge.Exchange("POLL");
                    Print(s);
                }

                s = bridge.Exchange("CALIBRATE");
                Print(s);
                if (!s.Calibrating)
                    throw new Exception("Calibration did not start: " + s.Error);
                var elapsed = Stopwatch.StartNew();
                while (s.Calibrating && elapsed.Elapsed.TotalSeconds < 105)
                {
                    Thread.Sleep(1000);
                    s = bridge.Exchange("POLL");
                    Print(s);
                }

                if (s.Calibrating || s.Calibration == null || s.CalibrationProgress != 100 || !s.ReleaseConfirmed)
                    throw new Exception("Calibration did not complete: " + s.Error);
                File.WriteAllText(Path.Combine(folder, "calibration.base64"), CurveCodec.Encode(s.Calibration));
                var settings = Preferences.Load();
                settings.Calibration = s.Calibration.Clone();
                settings.Save();
                Console.WriteLine("MEASURED MAX RPM " + String.Join(",", s.Calibration.Maxima));
                // Equal targets avoid making any assumption about CPU/GPU fan ordering.
                var profile = new CurveSettings
                {
                    MappingConfirmed = true,
                    FanShift = 100,
                    HysteresisUp = 0,
                    HysteresisDown = 0,
                    Cpu = new[]
                    {
                        new CurvePoint(20, 80),
                        new CurvePoint(100, 80)
                    },
                    Gpu = new[]
                    {
                        new CurvePoint(20, 80),
                        new CurvePoint(100, 80)
                    }
                };
                bridge.Exchange("CURVE|" + CurveCodec.Encode(profile));
                s = bridge.Exchange("ENABLECURVE");
                Print(s);
                int[][] final = new int[5][];
                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(1000);
                    s = bridge.Exchange("POLL");
                    Print(s);
                    if (s.Mode != "Curve" || !s.Manual || !String.IsNullOrEmpty(s.Error))
                        throw new Exception("Curve control failed: " + s.Error);
                    if (i >= 15)
                        final[i - 15] = (int[])s.Rpms.Clone();
                }

                if (s.Duties.Any(d => d != 80))
                    throw new Exception("Curve did not reach its target duty.");
                for (int fan = 0; fan < s.Rpms.Length; fan++)
                {
                    int index = fan;
                    int median = final.Select(x => x[index]).OrderBy(x => x).ElementAt(2);
                    if (median < 200 || median >= s.Calibration.Maxima[fan] - 100)
                        throw new Exception("Fan " + (fan + 1) + " did not show a measurable response to 80% PWM.");
                }

                s = bridge.Exchange("DISABLE");
                Print(s);
                if (!s.ReleaseConfirmed || s.Manual)
                    throw new Exception("Firmware reset was not acknowledged.");
                Console.WriteLine("PASS: real maximum-RPM calibration, equal 80% curve commands, measured fan response, firmware reset acknowledgment.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
            finally
            {
                if (bridge.Connected)
                {
                    Snapshot reset = bridge.Exchange("DISABLE");
                    Print(reset);
                    if (reset.ReleaseConfirmed)
                        bridge.Exchange("QUIT");
                }
            }
        }
    }
}
