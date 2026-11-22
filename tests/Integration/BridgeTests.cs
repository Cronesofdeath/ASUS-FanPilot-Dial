using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using FanPilot;

static class Integration
{
    static int passed, failed;
    static string folder = Environment.GetEnvironmentVariable("FANPILOT_TEST_FOLDER") ?? AppDomain.CurrentDomain.BaseDirectory;
    static string output = Path.Combine(Environment.GetEnvironmentVariable("FANPILOT_TEST_OUTPUT") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output"), "integration");
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    static void Run(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }

    sealed class Session : IDisposable
    {
        public Process Worker;
        public NamedPipeClientStream Pipe;
        public string Audit;
        public Session(int parent, bool authenticate)
        {
            string name = "FanPilot-" + Guid.NewGuid().ToString("N");
            string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            Audit = Path.Combine(output, name + ".audit.txt");
            ProcessStartInfo start = new ProcessStartInfo(Path.Combine(folder, "ASUS FanPilot Dial.exe"), "--demo-bridge " + name + " " + token + " " + WindowsIdentity.GetCurrent().User.Value + " " + parent);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WorkingDirectory = folder;
            start.EnvironmentVariables["FANPILOT_DEMO_AUDIT"] = Audit;
            Worker = Process.Start(start);
            Pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
            Pipe.Connect(5000);
            Protocol.WriteLine(Pipe, authenticate ? token : new string ('0', 64));
            if (authenticate)
            {
                Snapshot first = Protocol.Decode(Protocol.ReadLine(Pipe));
                Check(first.Connected && first.Demo && !first.Manual, "Bad startup snapshot");
                foreach (ProcessModule module in Worker.Modules)
                    Check(!module.ModuleName.Equals("AsusWinIO64.dll", StringComparison.OrdinalIgnoreCase), "Demo loaded native ASUS DLL");
            }
        }

        public Snapshot Send(string command)
        {
            Protocol.WriteLine(Pipe, command);
            return Protocol.Decode(Protocol.ReadLine(Pipe));
        }

        public void WaitForCleanExit(int timeout)
        {
            Check(Worker.WaitForExit(timeout), "Worker failed to exit");
            string audit = File.ReadAllText(Audit);
            Check(audit.Contains("mode:0:0") && audit.Contains("mode:1:0") && audit.Contains("dispose:False:False"), "Reset/dispose audit missing: " + audit);
        }

        public void Dispose()
        {
            if (Pipe != null)
                Pipe.Dispose();
            if (Worker != null)
            {
                if (!Worker.WaitForExit(12000))
                    Worker.Kill();
                Worker.Dispose();
            }
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--parent-probe")
        {
            Thread.Sleep(60000);
            return 0;
        }

        Directory.CreateDirectory(output);
        int parent = Process.GetCurrentProcess().Id;
        Run("curve loop advances independently of UI polling", delegate
        {
            using (Session s = new Session(parent, true))
            {
                var profile = new CurveSettings
                {
                    SharedControl = false,
                    Fan1Source = FanTemperatureSource.Cpu,
                    Fan2Source = FanTemperatureSource.Gpu,
                    HysteresisDown = 0,
                    HysteresisUp = 0,
                    FanShift = 0
                };
                profile.Cpu = new[]
                {
                    new CurvePoint(20, 70),
                    new CurvePoint(100, 70)
                };
                profile.Gpu = new[]
                {
                    new CurvePoint(20, 70),
                    new CurvePoint(100, 70)
                };
                s.Send("CURVE|" + CurveCodec.Encode(profile));
                Check(s.Send("ENABLECURVE").Mode == "Curve", "Curve start failed");
                profile.Cpu[0].Duty = profile.Cpu[1].Duty = 100;
                profile.Gpu[0].Duty = profile.Gpu[1].Duty = 40;
                Snapshot configured = s.Send("CURVE|" + CurveCodec.Encode(profile));
                Check(configured.Duties[0] == 70 && configured.Duties[1] == 70, "Configuration unexpectedly advanced the clock");
                Thread.Sleep(3300);
                Snapshot snapshot = s.Send("POLL");
                Check(snapshot.Mode == "Curve" && snapshot.Duties[0] > 70 && snapshot.Duties[1] < 70, "Worker required UI polls");
                s.Send("QUIT");
                s.WaitForCleanExit(5000);
            }
        });
        Run("calibration disconnect restores both fans", delegate
        {
            using (Session s = new Session(parent, true))
            {
                Check(s.Send("CALIBRATE").Calibrating, "Calibration did not start");
                s.Pipe.Dispose();
                s.WaitForCleanExit(5000);
            }
        });
        Run("real pipe round trip, all fan writes, synchronous QUIT reset", delegate
        {
            using (Session s = new Session(parent, true))
            {
                s.Send("CONFIG|Percent|83");
                Check(s.Send("ENABLE").Manual, "Enable failed");
                Snapshot updated = s.Send("POLL");
                Check(updated.Duties[0] == 83 && updated.Duties[1] == 83, "Missing second fan");
                Snapshot quit = s.Send("QUIT");
                Check(!quit.Manual && quit.ReleaseConfirmed, "QUIT acknowledgement premature");
                s.WaitForCleanExit(5000);
            }
        });
        Run("pipe disconnect restores both fans and exits helper", delegate
        {
            using (Session s = new Session(parent, true))
            {
                s.Send("ENABLE");
                s.Pipe.Dispose();
                s.WaitForCleanExit(5000);
            }
        });
        Run("communication lease expiry restores both fans", delegate
        {
            using (Session s = new Session(parent, true))
            {
                s.Send("ENABLE");
                s.WaitForCleanExit(11000);
            }
        });
        Run("parent-process exit restores both fans", delegate
        {
            using (Process probe = Process.Start(new ProcessStartInfo(typeof(Integration).Assembly.Location, "--parent-probe") { UseShellExecute = false, CreateNoWindow = true }))
            using (Session s = new Session(probe.Id, true))
            {
                s.Send("ENABLE");
                probe.Kill();
                probe.WaitForExit();
                s.WaitForCleanExit(5000);
            }
        });
        Run("malformed authenticated command releases active control", delegate
        {
            using (Session s = new Session(parent, true))
            {
                s.Send("ENABLE");
                Snapshot rejected = s.Send("RUN|anything");
                Check(!rejected.Manual && rejected.ReleaseConfirmed && rejected.Error.Length > 0, "Bad command retained override");
                s.Send("QUIT");
                s.WaitForCleanExit(5000);
            }
        });
        Run("invalid RPM target releases active control", delegate
        {
            using (Session s = new Session(parent, true))
            {
                s.Send("ENABLE");
                Snapshot rejected = s.Send("CONFIG|Rpm|0");
                Check(!rejected.Manual && rejected.ReleaseConfirmed, "Invalid target retained override");
                s.Send("QUIT");
                s.WaitForCleanExit(5000);
            }
        });
        Run("wrong authentication token never initializes hardware", delegate
        {
            using (Session s = new Session(parent, false))
            {
                Check(s.Worker.WaitForExit(5000), "Auth worker stayed alive");
                Check(!File.Exists(s.Audit), "Initialized before authentication");
            }
        });
        Run("one helper per hardware session", delegate
        {
            using (Session s = new Session(parent, true))
            {
                string pipe = "FanPilot-" + Guid.NewGuid().ToString("N"), token = new string ('a', 64);
                using (Process second = Process.Start(new ProcessStartInfo(Path.Combine(folder, "ASUS FanPilot Dial.exe"), "--demo-bridge " + pipe + " " + token + " " + WindowsIdentity.GetCurrent().User.Value + " " + parent) { UseShellExecute = false, CreateNoWindow = true }))
                {
                    Check(second.WaitForExit(5000) && second.ExitCode == 4, "Duplicate helper allowed");
                }

                s.Send("QUIT");
            }
        });
        Run("native bridge refuses non-SYSTEM invocation before loading DLL", delegate
        {
            string pipe = "FanPilot-" + Guid.NewGuid().ToString("N");
            using (Process process = Process.Start(new ProcessStartInfo(Path.Combine(folder, "ASUS FanPilot Dial.exe"), "--bridge " + pipe + " " + new string ('b', 64) + " " + WindowsIdentity.GetCurrent().User.Value + " " + parent) { UseShellExecute = false, CreateNoWindow = true }))
            {
                Check(process.WaitForExit(5000) && process.ExitCode == 3, "Unexpected native invocation");
            }
        });
        Run("bridge rejects malformed launch arguments", delegate
        {
            using (Process process = Process.Start(new ProcessStartInfo(Path.Combine(folder, "ASUS FanPilot Dial.exe"), "--demo-bridge invalid") { UseShellExecute = false, CreateNoWindow = true }))
            {
                Check(process.WaitForExit(5000) && process.ExitCode == 2, "Invalid arguments accepted");
            }
        });
        string result = passed + " passed, " + failed + " failed. Native ASUS DLL not loaded; all fan control simulated.";
        Console.WriteLine("RESULT " + result);
        File.WriteAllText(Path.Combine(output, "summary.txt"), result);
        return failed == 0 ? 0 : 1;
    }
}
