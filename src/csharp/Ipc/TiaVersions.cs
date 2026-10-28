using System;
using System.Collections.Generic;
using System.IO;

namespace TiaStudio.Contracts
{
    /// <summary>A támogatott TIA Portal célverziók és a hozzájuk tartozó projektkiterjesztések.</summary>
    public static class TiaVersions
    {
        public const string V18 = "V18";
        public const string V19 = "V19";
        public const string V20 = "V20";

        public static readonly IReadOnlyList<string> Supported = new[] { V18, V19, V20 };

        public static bool IsSupported(string version)
        {
            return version == V18 || version == V19 || version == V20;
        }

        /// <summary>V18 -> 18. Nem támogatott verziónál kivétel (a program soha nem találgat verziót).</summary>
        public static int Major(string version)
        {
            if (!IsSupported(version))
            {
                throw new ArgumentException("Unsupported TIA version: " + version, nameof(version));
            }
            return int.Parse(version.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>V18 -> ".ap18".</summary>
        public static string ProjectExtension(string version)
        {
            return ".ap" + Major(version).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A projektfájl kiterjesztéséből olvasott verzió, vagy null, ha nem .ap18/.ap19/.ap20.</summary>
        public static string FromProjectPath(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
            {
                return null;
            }
            string extension = Path.GetExtension(projectPath).ToLowerInvariant();
            foreach (string version in Supported)
            {
                if (extension == ProjectExtension(version))
                {
                    return version;
                }
            }
            return null;
        }

        /// <summary>A worker futtatható állományának neve, pl. TiaStudio.TiaAdapter.V18.exe.</summary>
        public static string WorkerExecutableName(string version)
        {
            Major(version);
            return "TiaStudio.TiaAdapter." + version + ".exe";
        }
    }
}
