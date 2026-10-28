using System.Reflection;

namespace TiaStudio.Desktop
{
    /// <summary>
    /// Az alkalmazás SemVer verziója és a build git-commitja (AssemblyInformationalVersion: "0.1.0+&lt;sha&gt;").
    /// Független a TIA Portal célverziójától (docs/VERSIONING.md).
    /// </summary>
    public static class BuildVersion
    {
        static BuildVersion()
        {
            string informational = typeof(BuildVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            int plus = informational.IndexOf('+');
            Version = plus < 0 ? informational : informational.Substring(0, plus);
            Commit = plus < 0 ? null : informational.Substring(plus + 1);
            string shortCommit = Commit == null ? null : Commit.Substring(0, System.Math.Min(7, Commit.Length));
            DisplayText = "v" + Version + (shortCommit == null ? string.Empty : " · " + shortCommit);
        }

        public static string Version { get; }

        public static string Commit { get; }

        public static string DisplayText { get; }
    }
}
