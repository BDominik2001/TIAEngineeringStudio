using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;

namespace TiaStudio.Infrastructure.Tia
{
    /// <summary>Csak olvasó registry-hozzáférés (tesztelhetőség miatt absztrahálva).</summary>
    public interface IRegistryReader
    {
        /// <summary>HKLM alatti relatív kulcs alkulcsai; null, ha a kulcs nem létezik.</summary>
        IReadOnlyList<string> GetSubKeyNames(string keyPath);

        /// <summary>A kulcs szöveges értékei (név -> adat); null, ha a kulcs nem létezik.</summary>
        IReadOnlyDictionary<string, string> GetStringValues(string keyPath);
    }

    public sealed class WindowsRegistryReader : IRegistryReader
    {
        public IReadOnlyList<string> GetSubKeyNames(string keyPath)
        {
            using (RegistryKey key = Open(keyPath))
            {
                return key?.GetSubKeyNames();
            }
        }

        public IReadOnlyDictionary<string, string> GetStringValues(string keyPath)
        {
            using (RegistryKey key = Open(keyPath))
            {
                if (key == null)
                {
                    return null;
                }
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in key.GetValueNames())
                {
                    if (key.GetValue(name) is string text)
                    {
                        values[name] = text;
                    }
                }
                return values;
            }
        }

        private static RegistryKey Open(string keyPath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }
            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                return hklm.OpenSubKey(keyPath, false);
            }
        }
    }

    /// <summary>Az assembly-metaadatok kiolvasása betöltés nélkül (tesztelhetőség miatt absztrahálva).</summary>
    public interface IAssemblyInspector
    {
        bool FileExists(string path);

        /// <summary>(verzió, public key token hex) vagy kivétel, ha nem .NET assembly.</summary>
        Tuple<Version, string> Inspect(string path);

        /// <summary>
        /// true, ha a worker mellett lévő link-jelölő (&lt;worker&gt;.openness.json) szerint a worker Openness-szel
        /// fordult. Hiányzó vagy olvashatatlan jelölő = false (fail-closed).
        /// </summary>
        bool IsOpennessLinkedWorker(string workerPath);
    }

    public sealed class FileAssemblyInspector : IAssemblyInspector
    {
        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public Tuple<Version, string> Inspect(string path)
        {
            AssemblyName name = AssemblyName.GetAssemblyName(path);
            byte[] token = name.GetPublicKeyToken() ?? new byte[0];
            return Tuple.Create(name.Version, string.Concat(token.Select(b => b.ToString("x2", CultureInfo.InvariantCulture))));
        }

        public bool IsOpennessLinkedWorker(string workerPath)
        {
            string marker = WorkerLinkMarker.PathFor(workerPath);
            if (!File.Exists(marker))
            {
                return false;
            }
            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(marker)))
                {
                    return document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("openness_linked", out JsonElement linked)
                        && linked.ValueKind == JsonValueKind.True;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>A worker build által írt link-jelölő (src/tia-adapters/openness-shared/OpennessWorker.targets).</summary>
    public static class WorkerLinkMarker
    {
        public static string PathFor(string workerPath)
        {
            return Path.ChangeExtension(workerPath, ".openness.json");
        }
    }

    /// <summary>
    /// Telepített TIA Portal Openness verziók felderítése a
    /// HKLM\SOFTWARE\Siemens\Automation\Openness\&lt;verzió&gt;\PublicAPI\&lt;assembly-verzió&gt; ágból
    /// (spec 3.4, Openness V18 kézikönyv 5.2.2: "Siemens.Engineering", "PublicKeyToken", "AssemblyVersion" értékek).
    /// A tényleges kulcsszerkezetet toleránsan kezeli; verziót soha nem helyettesít másikkal.
    /// </summary>
    public sealed class RegistryTiaInstallationDiscovery : ITiaInstallationDiscovery
    {
        public const string OpennessKey = @"SOFTWARE\Siemens\Automation\Openness";
        public const string SiemensPublicKeyToken = "d29ec89bac048f84";

        private readonly IRegistryReader registry;
        private readonly IAssemblyInspector assemblies;
        private readonly Func<string, string> workerPathResolver;
        private readonly ISet<string> integrationTestedVersions;

        public RegistryTiaInstallationDiscovery(
            IRegistryReader registry,
            IAssemblyInspector assemblies,
            Func<string, string> workerPathResolver,
            IEnumerable<string> integrationTestedVersions)
        {
            this.registry = registry;
            this.assemblies = assemblies;
            this.workerPathResolver = workerPathResolver;
            this.integrationTestedVersions = new HashSet<string>(integrationTestedVersions ?? Enumerable.Empty<string>());
        }

        public IReadOnlyList<TiaInstallInfo> DiscoverInstalledVersions()
        {
            IReadOnlyList<string> versionKeys = registry.GetSubKeyNames(OpennessKey) ?? new string[0];
            return TiaVersions.Supported.Select(v => Discover(v, versionKeys)).ToList();
        }

        private TiaInstallInfo Discover(string tiaVersion, IReadOnlyList<string> versionKeys)
        {
            int major = TiaVersions.Major(tiaVersion);
            string prefix = major.ToString(CultureInfo.InvariantCulture) + ".";
            TiaInstallInfo info = new TiaInstallInfo { TiaVersion = tiaVersion, Status = InstallStatus.NotFound, ErrorCode = ErrorCodes.TiaNotInstalled };
            info.WorkerPath = workerPathResolver?.Invoke(tiaVersion);

            string versionKey = versionKeys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).OrderByDescending(k => k, StringComparer.Ordinal).FirstOrDefault();
            if (versionKey == null)
            {
                info.Notes.Add("No Openness registry entry for TIA Portal " + tiaVersion + " under HKLM\\" + OpennessKey);
                return info;
            }
            string publicApiKey = OpennessKey + "\\" + versionKey + "\\PublicAPI";
            IReadOnlyList<string> apiVersions = registry.GetSubKeyNames(publicApiKey) ?? new string[0];
            // Csak a saját (natív) API-verzió számít; egy V20 telepítés V18-as API-ja nem V18-as TIA.
            string apiKey = apiVersions.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).OrderByDescending(k => k, StringComparer.Ordinal).FirstOrDefault();
            info.Status = InstallStatus.Unavailable;
            info.ErrorCode = ErrorCodes.ApiDllMismatch;
            if (apiKey == null)
            {
                info.RegistryKey = "HKLM\\" + publicApiKey;
                info.Notes.Add("Openness entry found, but no native " + tiaVersion + " PublicAPI version key");
                return info;
            }
            info.RegistryKey = "HKLM\\" + publicApiKey + "\\" + apiKey;
            IReadOnlyDictionary<string, string> values = registry.GetStringValues(publicApiKey + "\\" + apiKey) ?? new Dictionary<string, string>();
            string dll = values.TryGetValue("Siemens.Engineering", out string direct)
                ? direct
                : values.Values.FirstOrDefault(p => p.EndsWith("\\Siemens.Engineering.dll", StringComparison.OrdinalIgnoreCase));
            info.EngineeringDllPath = dll;
            if (string.IsNullOrEmpty(dll) || !assemblies.FileExists(dll))
            {
                info.Notes.Add("Siemens.Engineering.dll is not registered or the file is missing");
                return info;
            }
            Tuple<Version, string> identity;
            try
            {
                identity = assemblies.Inspect(dll);
            }
            catch (Exception ex) when (ex is BadImageFormatException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                info.Notes.Add("Siemens.Engineering.dll cannot be inspected: " + ex.Message);
                return info;
            }
            info.AssemblyVersion = identity.Item1.ToString();
            info.PublicKeyToken = identity.Item2;
            if (identity.Item1.Major != major)
            {
                info.Notes.Add("Assembly version " + identity.Item1 + " does not belong to " + tiaVersion);
                return info;
            }
            if (!string.Equals(identity.Item2, SiemensPublicKeyToken, StringComparison.OrdinalIgnoreCase))
            {
                info.Notes.Add("Unexpected public key token " + identity.Item2);
                return info;
            }
            // V18 kézikönyv 5.2.2: a kulcs PublicKeyToken és AssemblyVersion értéket is tartalmaz.
            if (values.TryGetValue("PublicKeyToken", out string registryToken) && !string.Equals(registryToken, identity.Item2, StringComparison.OrdinalIgnoreCase))
            {
                info.Notes.Add("Registry PublicKeyToken " + registryToken + " differs from the DLL (" + identity.Item2 + ")");
                return info;
            }
            if (values.TryGetValue("AssemblyVersion", out string registryVersion) && registryVersion != identity.Item1.ToString())
            {
                info.Notes.Add("Registry AssemblyVersion " + registryVersion + " differs from the DLL (" + identity.Item1 + ")");
                return info;
            }
            if (string.IsNullOrEmpty(info.WorkerPath) || !assemblies.FileExists(info.WorkerPath))
            {
                info.ErrorCode = ErrorCodes.TiaNotInstalled;
                info.Notes.Add("Worker executable " + TiaVersions.WorkerExecutableName(tiaVersion) + " is missing (" + info.WorkerPath + ")");
                return info;
            }
            if (!assemblies.IsOpennessLinkedWorker(info.WorkerPath))
            {
                // Audit A-04: TIA nélkül fordított worker; .exe.config-írás nem teszi használhatóvá.
                info.ErrorCode = ErrorCodes.WorkerBuiltWithoutOpenness;
                info.Notes.Add("Worker " + TiaVersions.WorkerExecutableName(tiaVersion) + " was built without Openness (" + WorkerLinkMarker.PathFor(info.WorkerPath)
                    + "); rebuild it on this machine with scripts\\build-tia-workers.ps1 -Versions " + tiaVersion + " -RequireOpenness");
                return info;
            }
            info.ErrorCode = null;
            info.Status = integrationTestedVersions.Contains(tiaVersion) ? InstallStatus.Installed : InstallStatus.NotTested;
            if (info.Status == InstallStatus.NotTested)
            {
                info.Notes.Add("Installed, but the " + tiaVersion + " integration test matrix has not been confirmed on this machine (docs/COMPATIBILITY.md)");
            }
            return info;
        }
    }
}
