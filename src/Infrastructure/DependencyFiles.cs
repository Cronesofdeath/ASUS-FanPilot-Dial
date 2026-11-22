using System;
using System.IO;

namespace FanPilot
{
    // Third-party binaries are supplied locally; never embedded in public builds.
    public static class DependencyFiles
    {
        public static string Resolve(string fileName, string expectedHash)
        {
            string folder = Environment.GetEnvironmentVariable("FANPILOT_DEPENDENCIES");
            string[] candidates = new[]
            {
                String.IsNullOrEmpty(folder) ? null : Path.Combine(Path.GetFullPath(folder), fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", fileName),
                Path.Combine(AppPaths.Cache, fileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanPilotExperimental", "runtime-v2", fileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanPilot", "runtime-v2", fileName)
            };
            foreach (string path in candidates)
            {
                if (String.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;
                PackageTrust.Verify(path, expectedHash);
                return path;
            }

            throw new FileNotFoundException(fileName + " is missing. Complete dependency setup before using hardware control.");
        }
    }
}
