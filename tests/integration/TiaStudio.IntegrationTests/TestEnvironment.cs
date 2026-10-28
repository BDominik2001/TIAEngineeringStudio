using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TiaStudio.IntegrationTests
{
    /// <summary>Közös tesztkörnyezet (repó gyökér, Python); a tests/tia projekt is ezt a fájlt fordítja.</summary>
    internal static class Env
    {
        public static string RepoRoot { get; } = FindRoot();

        public static string Python { get; } = FindPython();

        public static string PathOf(params string[] parts)
        {
            return Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());
        }

        public static bool RustAvailable()
        {
            string[] names = { "tes_validator.dll", "libtes_validator.so", "libtes_validator.dylib" };
            return names.Any(n => File.Exists(Path.Combine(AppContext.BaseDirectory, n)));
        }

        private static string FindRoot()
        {
            string current = AppContext.BaseDirectory;
            while (current != null && !File.Exists(Path.Combine(current, "contracts", "error-codes.json")))
            {
                current = Path.GetDirectoryName(current);
            }
            return current;
        }

        private static string FindPython()
        {
            string configured = Environment.GetEnvironmentVariable("TES_PYTHON");
            foreach (string candidate in new[] { configured, "python3", "python" }.Where(c => !string.IsNullOrEmpty(c)))
            {
                try
                {
                    using (Process process = Process.Start(new ProcessStartInfo(candidate, "--version") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }))
                    {
                        process.WaitForExit(10000);
                        if (process.ExitCode == 0)
                        {
                            return candidate;
                        }
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }
            return null;
        }
    }
}
