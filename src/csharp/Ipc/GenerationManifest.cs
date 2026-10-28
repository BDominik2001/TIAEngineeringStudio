using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TiaStudio.Contracts
{
    /// <summary>contracts/generation-manifest.schema.json (manifest_version 1.0).</summary>
    public sealed class GenerationManifest
    {
        /// <summary>A Python-generátor által írt verzió.</summary>
        public const string CurrentVersion = "1.0";

        /// <summary>1.1: package_kind és fájlonkénti format (könyvtárimport, SimaticML).</summary>
        public const string LibraryVersion = "1.1";

        public const string FileName = "manifest.json";

        public const string KindGenerated = "generated";
        public const string KindLibraryImport = "library_import";

        [JsonProperty("manifest_version")]
        public string ManifestVersion { get; set; }

        [JsonProperty("generator")]
        public string Generator { get; set; }

        [JsonProperty("generator_version")]
        public string GeneratorVersion { get; set; }

        [JsonProperty("schema_version")]
        public string SchemaVersion { get; set; }

        [JsonProperty("project_name")]
        public string ProjectName { get; set; }

        [JsonProperty("target_tia_version")]
        public string TargetTiaVersion { get; set; }

        [JsonProperty("plc_name")]
        public string PlcName { get; set; }

        [JsonProperty("cpu_family")]
        public string CpuFamily { get; set; }

        [JsonProperty("template_set_id")]
        public string TemplateSetId { get; set; }

        [JsonProperty("template_set_version")]
        public string TemplateSetVersion { get; set; }

        [JsonProperty("template_set_sha256")]
        public string TemplateSetSha256 { get; set; }

        [JsonProperty("run_id")]
        public string RunId { get; set; }

        [JsonProperty("generated_at_utc")]
        public string GeneratedAtUtc { get; set; }

        [JsonProperty("source_config_sha256")]
        public string SourceConfigSha256 { get; set; }

        [JsonProperty("encoding")]
        public string Encoding { get; set; }

        [JsonProperty("line_ending")]
        public string LineEnding { get; set; }

        [JsonProperty("files")]
        public List<ManifestFile> Files { get; set; } = new List<ManifestFile>();

        [JsonProperty("validation_result")]
        public JObject ValidationResult { get; set; }

        [JsonProperty("safety_scope")]
        public string SafetyScope { get; set; }

        [JsonProperty("download_allowed")]
        public bool? DownloadAllowed { get; set; }

        /// <summary>1.1-től: generated | library_import. 1.0-ban hiányzik (= generated).</summary>
        [JsonProperty("package_kind", NullValueHandling = NullValueHandling.Ignore)]
        public string PackageKind { get; set; }
    }

    public sealed class ManifestFile
    {
        [JsonProperty("relative_path")]
        public string RelativePath { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("byte_length")]
        public long ByteLength { get; set; }

        [JsonProperty("block_name")]
        public string BlockName { get; set; }

        [JsonProperty("block_type")]
        public string BlockType { get; set; }

        [JsonProperty("import_order")]
        public int ImportOrder { get; set; }

        /// <summary>1.1-től: external_source | simaticml. Hiányzó érték = external_source.</summary>
        [JsonProperty("format", NullValueHandling = NullValueHandling.Ignore)]
        public string Format { get; set; }

        [JsonIgnore]
        public string EffectiveFormat => Format ?? BlockFormats.ExternalSource;
    }

    /// <summary>A Rust/Python/C# validátorok közös riportformátuma (docs/VALIDATION_RULES.md 8.).</summary>
    public sealed class ValidationReport
    {
        [JsonProperty("validator")]
        public string Validator { get; set; }

        [JsonProperty("validator_version")]
        public string ValidatorVersion { get; set; }

        [JsonProperty("rules_version")]
        public string RulesVersion { get; set; }

        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("error_count")]
        public int ErrorCount { get; set; }

        [JsonProperty("warning_count")]
        public int WarningCount { get; set; }

        [JsonProperty("issues")]
        public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();
    }

    public sealed class ValidationIssue
    {
        public const string SeverityError = "error";
        public const string SeverityWarning = "warning";

        public ValidationIssue()
        {
        }

        public ValidationIssue(string severity, string code, string path, string message)
        {
            Severity = severity;
            Code = code;
            Path = path;
            Message = message;
        }

        [JsonProperty("severity")]
        public string Severity { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public override string ToString()
        {
            return Severity + " " + Code + " @ " + Path + ": " + Message;
        }
    }

    /// <summary>
    /// A sandbox projektmappában lévő .tes-sandbox.json marker. Csak a TIA Engineering Studio
    /// sandbox-szolgáltatása hozza létre, egy ismert jó projekt teljes másolatakor.
    /// </summary>
    public sealed class SandboxMarker
    {
        public const string FileName = ".tes-sandbox.json";
        public const string CurrentVersion = "1.0";

        [JsonProperty("marker_version")]
        public string MarkerVersion { get; set; } = CurrentVersion;

        [JsonProperty("tia_version")]
        public string TiaVersion { get; set; }

        [JsonProperty("project_file")]
        public string ProjectFile { get; set; }

        [JsonProperty("source_project_path")]
        public string SourceProjectPath { get; set; }

        [JsonProperty("created_at_utc")]
        public string CreatedAtUtc { get; set; }

        [JsonProperty("created_by")]
        public string CreatedBy { get; set; }
    }
}
