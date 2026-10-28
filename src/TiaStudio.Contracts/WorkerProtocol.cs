using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaStudio.Contracts
{
    public static class ProtocolVersions
    {
        public const string Current = "1.0";
    }

    /// <summary>
    /// A worker műveletei. Letöltés, online kapcsolat, RUN/STOP vagy forcing
    /// szándékosan NEM része a protokollnak (spec 2.3, 11.3/11).
    /// </summary>
    public static class TiaOperations
    {
        public const string GetCapabilities = "GetCapabilities";
        public const string ValidateJob = "ValidateJob";
        public const string ImportAndCompile = "ImportAndCompile";
        public const string UpgradeProject = "UpgradeProject";

        public static readonly IReadOnlyList<string> All = new[] { GetCapabilities, ValidateJob, ImportAndCompile, UpgradeProject };
    }

    public static class TiaJobStatus
    {
        public const string ValidationOnly = "ValidationOnly";
        public const string Success = "Success";
        public const string Blocked = "Blocked";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
        public const string Unsupported = "Unsupported";
    }

    public static class SandboxStates
    {
        /// <summary>A worker meg sem nyitotta a projektet.</summary>
        public const string NotTouched = "NotTouched";
        /// <summary>Megnyitotta, de nem módosította (pl. dry run, ütközés).</summary>
        public const string Unchanged = "Unchanged";
        /// <summary>Import történt, de mentés nem (pl. fordítási hiba); a sandboxot újra kell előállítani.</summary>
        public const string ModifiedNotSaved = "ModifiedNotSaved";
        public const string Saved = "Saved";
        /// <summary>Megszakadt/elakadt művelet: kézi ellenőrzés szükséges.</summary>
        public const string Unknown = "Unknown";
    }

    public static class OverwritePolicies
    {
        public const string Deny = "Deny";
    }

    public static class MessageSeverity
    {
        public const string Info = "Info";
        public const string Warning = "Warning";
        public const string Error = "Error";
    }

    /// <summary>contracts/tia-command.schema.json</summary>
    public sealed class TiaCommand
    {
        [JsonProperty("protocol_version", Required = Required.Always)]
        public string ProtocolVersion { get; set; } = ProtocolVersions.Current;

        [JsonProperty("job_id", Required = Required.Always)]
        public string JobId { get; set; }

        [JsonProperty("operation", Required = Required.Always)]
        public string Operation { get; set; }

        [JsonProperty("tia_version", Required = Required.Always)]
        public string TiaVersion { get; set; }

        [JsonProperty("project_path")]
        public string ProjectPath { get; set; }

        [JsonProperty("sandbox_root")]
        public string SandboxRoot { get; set; }

        [JsonProperty("plc_name")]
        public string PlcName { get; set; }

        [JsonProperty("sources_directory")]
        public string SourcesDirectory { get; set; }

        [JsonProperty("manifest_path")]
        public string ManifestPath { get; set; }

        [JsonProperty("dry_run")]
        public bool DryRun { get; set; } = true;

        [JsonProperty("overwrite_policy")]
        public string OverwritePolicy { get; set; } = OverwritePolicies.Deny;

        [JsonProperty("approved_by_user")]
        public bool ApprovedByUser { get; set; }

        [JsonProperty("approval")]
        public ApprovalRecord Approval { get; set; }
    }

    public sealed class ApprovalRecord
    {
        [JsonProperty("approved_by", Required = Required.Always)]
        public string ApprovedBy { get; set; }

        [JsonProperty("approved_at_utc", Required = Required.Always)]
        public string ApprovedAtUtc { get; set; }

        [JsonProperty("approval_digest", Required = Required.Always)]
        public string ApprovalDigest { get; set; }
    }

    /// <summary>contracts/tia-result.schema.json</summary>
    public sealed class TiaResult
    {
        [JsonProperty("protocol_version")]
        public string ProtocolVersion { get; set; } = ProtocolVersions.Current;

        [JsonProperty("job_id")]
        public string JobId { get; set; }

        [JsonProperty("requested_version")]
        public string RequestedVersion { get; set; }

        [JsonProperty("actual_version")]
        public string ActualVersion { get; set; }

        [JsonProperty("operation")]
        public string Operation { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("error_code")]
        public string ErrorCode { get; set; }

        [JsonProperty("error_count")]
        public int ErrorCount { get; set; }

        [JsonProperty("warning_count")]
        public int WarningCount { get; set; }

        [JsonProperty("messages")]
        public List<TiaMessage> Messages { get; set; } = new List<TiaMessage>();

        [JsonProperty("saved_project")]
        public bool SavedProject { get; set; }

        [JsonProperty("sandbox_state")]
        public string SandboxState { get; set; } = SandboxStates.NotTouched;

        [JsonProperty("import_plan")]
        public List<ImportPlanItem> ImportPlan { get; set; }

        [JsonProperty("compile")]
        public CompileReport Compile { get; set; }

        [JsonProperty("capabilities")]
        public TiaCapabilities Capabilities { get; set; }

        [JsonProperty("worker")]
        public WorkerInfo Worker { get; set; }

        [JsonProperty("started_at_utc")]
        public string StartedAtUtc { get; set; }

        [JsonProperty("finished_at_utc")]
        public string FinishedAtUtc { get; set; }

        /// <summary>Csak a Success és a ValidationOnly számít sikernek; részleges siker nincs (spec 10.1/9).</summary>
        [JsonIgnore]
        public bool IsSuccessful => Status == TiaJobStatus.Success || Status == TiaJobStatus.ValidationOnly;
    }

    public sealed class TiaMessage
    {
        public TiaMessage()
        {
        }

        public TiaMessage(string severity, string code, string text, string path = null)
        {
            Severity = severity;
            Code = code;
            Text = text;
            Path = path;
        }

        [JsonProperty("severity")]
        public string Severity { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        public override string ToString()
        {
            return Severity + " " + (Code ?? "-") + ": " + Text + (Path == null ? "" : " [" + Path + "]");
        }
    }

    public sealed class ImportPlanItem
    {
        public const string ActionCreate = "Create";
        public const string ActionCollision = "Collision";

        [JsonProperty("import_order")]
        public int ImportOrder { get; set; }

        [JsonProperty("block_name")]
        public string BlockName { get; set; }

        [JsonProperty("block_type")]
        public string BlockType { get; set; }

        [JsonProperty("relative_path")]
        public string RelativePath { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("existing_location")]
        public string ExistingLocation { get; set; }
    }

    public sealed class CompileReport
    {
        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("error_count")]
        public int ErrorCount { get; set; }

        [JsonProperty("warning_count")]
        public int WarningCount { get; set; }

        [JsonProperty("messages")]
        public List<CompileMessage> Messages { get; set; } = new List<CompileMessage>();
    }

    /// <summary>A CompilerResult rekurzív üzenetfájának lapított, mélységgel ellátott formája.</summary>
    public sealed class CompileMessage
    {
        [JsonProperty("depth")]
        public int Depth { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("error_count")]
        public int ErrorCount { get; set; }

        [JsonProperty("warning_count")]
        public int WarningCount { get; set; }
    }

    public sealed class WorkerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("openness_linked")]
        public bool OpennessLinked { get; set; }

        [JsonProperty("engineering_assembly_path")]
        public string EngineeringAssemblyPath { get; set; }

        [JsonProperty("engineering_assembly_version")]
        public string EngineeringAssemblyVersion { get; set; }
    }

    /// <summary>A worker képességei (spec 3.2/B). Hiányzó képesség = explicit Unsupported, nincs csendes helyettesítés.</summary>
    public sealed class TiaCapabilities
    {
        [JsonProperty("tia_version")]
        public string TiaVersion { get; set; }

        [JsonProperty("api_level")]
        public string ApiLevel { get; set; } = "V18-common";

        [JsonProperty("operations")]
        public List<string> Operations { get; set; } = new List<string>();

        [JsonProperty("features")]
        public Dictionary<string, bool> Features { get; set; } = new Dictionary<string, bool>();
    }

    /// <summary>Feature-kulcsok a TiaCapabilities.Features szótárban.</summary>
    public static class CapabilityFeatures
    {
        public const string SclExternalSourceImport = "scl_external_source_import";
        public const string OfflineCompile = "offline_compile";
        public const string RecursiveBlockCollisionCheck = "recursive_block_collision_check";
        public const string ProjectUpgrade = "project_upgrade";
        public const string Hmi = "hmi";
        public const string SimaticMlImport = "simaticml_import";
        public const string PlcDownload = "plc_download";
    }
}
