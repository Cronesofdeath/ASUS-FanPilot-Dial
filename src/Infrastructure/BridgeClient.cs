using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;

namespace FanPilot
{
    public sealed class BridgeClient : IDisposable
    {
        NamedPipeClientStream pipe;
        System.Threading.Timer timeout;
        long exchangeTicks;
        volatile bool disposed;
        public bool Connected
        {
            get
            {
                return !disposed && pipe != null && pipe.IsConnected;
            }
        }

        public Snapshot Connect(bool demo)
        {
            string name = "FanPilot-" + Guid.NewGuid().ToString("N"), token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            string tail = name + " " + token + " " + WindowsIdentity.GetCurrent().User.Value + " " + Process.GetCurrentProcess().Id;
            if (demo)
            {
                using (Process child = Process.Start(new ProcessStartInfo(AppPaths.Executable, "--demo-bridge " + tail) { UseShellExecute = false, CreateNoWindow = true }))
                {
                }
            }
            else
            {
                // Resolve in the signed-in user's profile before crossing the UAC/SYSTEM boundary.
                string[] launchArguments = new[]
                {
                    "--launch-bridge",
                    name,
                    token,
                    WindowsIdentity.GetCurrent().User.Value,
                    Process.GetCurrentProcess().Id.ToString(),
                    DependencyFiles.Resolve("AsusWinIO64.dll", PackageTrust.AsusHash),
                    DependencyFiles.Resolve("PsExec.exe", PackageTrust.PsExecHash)
                };
                if (AppPaths.Administrator)
                    WorkerLauncher.Launch(launchArguments);
                else
                    using (Process launch = Process.Start(new ProcessStartInfo(AppPaths.Executable, WorkerLauncher.Arguments(launchArguments)) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                    {
                        if (!launch.WaitForExit(30000))
                            throw new IOException("Windows permission prompt timed out. Try again.");
                        if (launch.ExitCode != 0)
                            throw new IOException("The elevated launcher failed (" + launch.ExitCode + "). See startup.log.");
                    }
            }

            pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
            try
            {
                pipe.Connect(15000);
            }
            catch (Exception ex)
            {
                AppPaths.Trace(ex.ToString());
                throw new IOException("Hardware helper did not respond. See startup.log for Windows launch details.", ex);
            }

            timeout = new System.Threading.Timer(delegate
            {
                long t = Interlocked.Read(ref exchangeTicks);
                if (t > 0 && (DateTime.UtcNow - new DateTime(t, DateTimeKind.Utc)).TotalSeconds > 8)
                    Dispose();
            }, null, 1000, 1000);
            Interlocked.Exchange(ref exchangeTicks, DateTime.UtcNow.Ticks);
            Protocol.WriteLine(pipe, token);
            Snapshot s = Protocol.Decode(Protocol.ReadLine(pipe));
            Interlocked.Exchange(ref exchangeTicks, 0);
            if (!String.IsNullOrEmpty(s.Error))
                throw new IOException(s.Error);
            return s;
        }

        public Snapshot Exchange(string command)
        {
            if (!Connected)
                throw new IOException("Hardware helper is unavailable.");
            Interlocked.Exchange(ref exchangeTicks, DateTime.UtcNow.Ticks);
            try
            {
                Protocol.WriteLine(pipe, command);
                Snapshot s = Protocol.Decode(Protocol.ReadLine(pipe));
                Interlocked.Exchange(ref exchangeTicks, 0);
                return s;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            disposed = true;
            if (timeout != null)
                timeout.Dispose();
            if (pipe != null)
                try
                {
                    pipe.Dispose();
                }
                catch
                {
                }
        }
    }
}
