using System;
using System.IO;
using System.Security.Cryptography;

namespace FanPilot
{
    public static class PackageTrust
    {
        public const string AsusHash = "dac0a7765bbdd3b66ed9b1222bb7fa630b5abc195e5bce4758aa05ae880b2166";
        public const string PsExecHash = "078163d5c16f64caa5a14784323fd51451b8c831c73396b967b4e35e6879937b";
        public static void Verify(string path, string expected)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!String.Equals(actual, expected, StringComparison.Ordinal))
                    throw new IOException("Integrity check failed: " + Path.GetFileName(path));
            }
        }
    }
}
