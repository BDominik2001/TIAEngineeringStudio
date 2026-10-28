using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaStudio.Contracts
{
    /// <summary>A GUI-ban megjelenő telepítési állapot (spec 3.4/5).</summary>
    public static class InstallStatus
    {
        /// <summary>Telepítve, és ezen a gépen dokumentált integrációs teszt is igazolja.</summary>
        public const string Installed = "Installed";
        /// <summary>Nincs Openness registry-bejegyzés ehhez a verzióhoz.</summary>
        public const string NotFound = "Not found";
        /// <summary>Van bejegyzés, de a DLL hiányzik vagy verziója/kulcsa nem egyezik, vagy a worker hiányzik.</summary>
        public const string Unavailable = "Unavailable";
        /// <summary>Telepítve, de az integrációs tesztmátrix ezen a gépen még nem igazolta.</summary>
        public const string NotTested = "Not tested";

        public static bool IsUsable(string status)
        {
            return status == Installed || status == NotTested;
        }
    }

    public sealed class TiaInstallInfo
    {
        [JsonProperty("tia_version")]
        public string TiaVersion { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("error_code")]
        public string ErrorCode { get; set; }

        [JsonProperty("registry_key")]
        public string RegistryKey { get; set; }

        [JsonProperty("engineering_dll_path")]
        public string EngineeringDllPath { get; set; }

        [JsonProperty("assembly_version")]
        public string AssemblyVersion { get; set; }

        [JsonProperty("public_key_token")]
        public string PublicKeyToken { get; set; }

        [JsonProperty("worker_path")]
        public string WorkerPath { get; set; }

        [JsonProperty("notes")]
        public List<string> Notes { get; set; } = new List<string>();
    }
}
