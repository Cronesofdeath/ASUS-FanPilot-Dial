using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;

namespace FanPilot
{
    public static class WorkerLauncher
    {
        public static int Launch(string[] args)
        {
            if (args == null || args.Length != 7)
                return 2;
            string[] bridge = new[]
            {
                "--bridge",
                args[1],
                args[2],
                args[3],
                args[4],
                Path.GetFullPath(args[5])
            };
            if (!BridgeHost.ValidArguments(bridge))
                return 2;
            string psexec = Path.GetFullPath(args[6]);
            PackageTrust.Verify(psexec, PackageTrust.PsExecHash);
            PackageTrust.Verify(bridge[5], PackageTrust.AsusHash);
            ProcessStartInfo info = new ProcessStartInfo(psexec, "-accepteula -nobanner -s -d " + Arguments(new[] { AppPaths.Executable }) + " " + Arguments(bridge));
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using (Process p = Process.Start(info))
            {
                Task<string> output = p.StandardOutput.ReadToEndAsync(), error = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(15000))
                    throw new IOException("Windows helper launch timed out.");
                AppPaths.Trace(output.Result + error.Result + " PsExec result=" + p.ExitCode);
                // Detached PsExec results can be the child PID. Authenticate the pipe to confirm success.
                return 0;
            }
        }

        public static string Arguments(string[] values)
        {
            var result = new StringBuilder();
            foreach (string value in values)
            {
                if (result.Length > 0)
                    result.Append(' ');
                result.Append('"');
                int slashes = 0;
                foreach (char character in value)
                {
                    if (character == '\\')
                    {
                        slashes++;
                        continue;
                    }

                    result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                    result.Append(character);
                    slashes = 0;
                }

                result.Append('\\', slashes * 2);
                result.Append('"');
            }

            return result.ToString();
        }
    }
}
