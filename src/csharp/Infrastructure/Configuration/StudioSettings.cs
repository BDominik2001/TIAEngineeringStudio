using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TiaStudio.Contracts;

namespace TiaStudio.Infrastructure.Configuration
{
    /// <summary>
    /// appsettings.json (a Desktop EXE mellett). Relatív útvonalak az EXE mappájához képest
    /// értelmezendők. Az integration_tested_versions csak dokumentált, sikeres verziónkénti
    /// integrációs teszt után bővíthető (docs/COMPATIBILITY.md).
    /// </summary>
    public sealed class StudioSettings
    {
        [JsonProperty("python_executable")]
        public string PythonExecutable { get; set; } = "python";

        [JsonProperty("generator_root")]
        public string GeneratorRoot { get; set; } = "python";

        [JsonProperty("workers_directory")]
        public string WorkersDirectory { get; set; } = "workers";

        [JsonProperty("sandbox_root")]
        public string SandboxRoot { get; set; } = "sandbox";

        [JsonProperty("output_root")]
        public string OutputRoot { get; set; } = "generated";

        [JsonProperty("library_root")]
        public string LibraryRoot { get; set; } = "libraries";

        [JsonProperty("library_worker_timeout_seconds")]
        public int LibraryWorkerTimeoutSeconds { get; set; } = 900;

        [JsonProperty("log_directory")]
        public string LogDirectory { get; set; } = "logs";

        [JsonProperty("job_directory")]
        public string JobDirectory { get; set; } = "jobs";

        [JsonProperty("generator_timeout_seconds")]
        public int GeneratorTimeoutSeconds { get; set; } = 120;

        [JsonProperty("dry_run_timeout_seconds")]
        public int DryRunTimeoutSeconds { get; set; } = 900;

        [JsonProperty("import_timeout_seconds")]
        public int ImportTimeoutSeconds { get; set; } = 2700;

        [JsonProperty("capabilities_timeout_seconds")]
        public int CapabilitiesTimeoutSeconds { get; set; } = 120;

        [JsonProperty("integration_tested_versions")]
        public List<string> IntegrationTestedVersions { get; set; } = new List<string>();

        public static StudioSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                return new StudioSettings();
            }
            return ProtocolJson.DeserializeStrict<StudioSettings>(ProtocolJson.ReadAllText(path)) ?? new StudioSettings();
        }

        public string Resolve(string baseDirectory, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return baseDirectory;
            }
            string expanded = Environment.ExpandEnvironmentVariables(path);
            return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(baseDirectory, expanded));
        }

        /// <summary>workers/V18/TiaStudio.TiaAdapter.V18.exe</summary>
        public string WorkerPath(string baseDirectory, string tiaVersion)
        {
            return Path.Combine(Resolve(baseDirectory, WorkersDirectory), tiaVersion, TiaVersions.WorkerExecutableName(tiaVersion));
        }
    }
}
