using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TiaStudio.Contracts;
using TiaStudio.Domain.Model;

namespace TiaStudio.Application.Ports
{
    /// <summary>Gépmodell ↔ kanonikus JSON (schema 1.1) átalakítás és tárolás.</summary>
    public interface IMachineProjectSerializer
    {
        /// <summary>Determinisztikus UTF-8 JSON (BOM nélkül); azonos modell -> azonos bájtok.</summary>
        byte[] Serialize(MachineProject project);

        /// <summary>Csak hibamentesen validált bájtokra hívható.</summary>
        MachineProject Deserialize(byte[] json);
    }

    /// <summary>Séma- és adatmodell-ellenőrzés (DRAFT -> VALIDATED), C# implementáció.</summary>
    public interface IModelValidator
    {
        ValidationReport Validate(byte[] json);
    }

    /// <summary>Determinisztikus statikus/ütközésvalidáció (VALIDATED -> CHECKED), Rust natív DLL.</summary>
    public interface IStaticValidator
    {
        ValidationReport Validate(byte[] json);
    }

    public sealed class GenerationRequest
    {
        public string InputPath { get; set; }

        public string OutputDirectory { get; set; }

        public string RunId { get; set; }
    }

    public sealed class GenerationResult
    {
        public bool Success { get; set; }

        public string ErrorCode { get; set; }

        public string Message { get; set; }

        public string OutputDirectory { get; set; }

        public string ManifestPath { get; set; }

        public string ManifestSha256 { get; set; }

        public ValidationReport Validation { get; set; }

        public string Diagnostics { get; set; }
    }

    /// <summary>Python SCL-generátor külön folyamatban (CHECKED -> GENERATED).</summary>
    public interface ISourceGenerator
    {
        Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken);
    }

    public sealed class TiaJobRequest
    {
        public TiaJobRequest(TiaCommand command, TimeSpan timeout)
        {
            Command = command;
            Timeout = timeout;
        }

        public TiaCommand Command { get; }

        public TimeSpan Timeout { get; }
    }

    /// <summary>Telepített TIA/Openness verziók felderítése (registry alapján, spec 3.4).</summary>
    public interface ITiaInstallationDiscovery
    {
        IReadOnlyList<TiaInstallInfo> DiscoverInstalledVersions();
    }

    /// <summary>
    /// A verziónkénti worker folyamatok elérése (spec 8.5). Siemens-típus nem szivároghat át rajta.
    /// </summary>
    public interface ITiaWorkerClient
    {
        IReadOnlyList<TiaInstallInfo> DiscoverInstalledVersions();

        Task<TiaResult> GetCapabilitiesAsync(string tiaVersion, CancellationToken cancellationToken);

        Task<TiaResult> ValidateJobAsync(TiaJobRequest request, CancellationToken cancellationToken);

        Task<TiaResult> ImportAndCompileAsync(TiaJobRequest request, CancellationToken cancellationToken);

        /// <summary>Csak olvasó: a sandbox-projekt PLC-jének blokkjai.</summary>
        Task<TiaResult> ListBlocksAsync(TiaJobRequest request, CancellationToken cancellationToken);

        /// <summary>Csak olvasó: kijelölt blokkok exportja az export_directory mappába.</summary>
        Task<TiaResult> ExportBlocksAsync(TiaJobRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Könyvtárművelet hibája stabil hibakóddal.</summary>
    public sealed class BlockLibraryException : Exception
    {
        public BlockLibraryException(string errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }

    public sealed class BlockLibrarySummary
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public int ItemCount { get; set; }

        /// <summary>null = a library.json betölthető; különben a hiba leírása.</summary>
        public string Problem { get; set; }
    }

    /// <summary>Mely könyvtárelemek importálandók melyik célverzióba és PLC-be.</summary>
    public sealed class LibraryImportSelection
    {
        public string LibraryName { get; set; }

        public List<string> ItemIds { get; set; } = new List<string>();

        public string TargetTiaVersion { get; set; }

        public string PlcName { get; set; }
    }

    /// <summary>
    /// Verziófüggetlen blokk-könyvtárak tárolása (contracts/block-library.schema.json):
    /// &lt;root&gt;\&lt;név&gt;\library.json + items\&lt;blokk&gt;\r&lt;revízió&gt;\fájl. Elemfájlt soha nem ír felül.
    /// </summary>
    public interface IBlockLibraryStore
    {
        string LibraryRoot { get; }

        IReadOnlyList<BlockLibrarySummary> ListLibraries();

        BlockLibraryIndex Create(string name, string description);

        /// <summary>BlockLibraryException(LIBRARY_INVALID), ha nem létezik vagy sérült.</summary>
        BlockLibraryIndex Load(string name);

        /// <summary>Teljes integritás-ellenőrzés: útvonalak, SHA-256, bájthossz, elemszabályok. Üres = rendben.</summary>
        IReadOnlyList<TiaMessage> Verify(string name);

        /// <summary>Worker-exportból új revíziók felvétele; minden fájl hash-e újramérve.</summary>
        IReadOnlyList<BlockLibraryItem> AddExportedBlocks(
            string name,
            string exportDirectory,
            IReadOnlyList<ExportedBlockFile> files,
            string sourceTiaVersion,
            BlockLibraryOrigin origin,
            string description);

        void RemoveItem(string name, string itemId);

        string ItemPath(string name, BlockLibraryItem item);

        /// <summary>Importcsomag (manifest 1.1, package_kind = library_import) összeállítása egy új mappába.</summary>
        GenerationResult BuildImportPackage(
            LibraryImportSelection selection,
            IReadOnlyList<BlockLibraryItem> items,
            string outputDirectory,
            string runId,
            string selectionSha256,
            ValidationReport validation);
    }

    public sealed class SandboxCopy
    {
        public string ProjectPath { get; set; }

        public string SandboxDirectory { get; set; }

        public SandboxMarker Marker { get; set; }
    }

    /// <summary>Teljes, önálló projektmásolatok kezelése; éles projekt soha nem módosul (spec 10.1/1).</summary>
    public interface ISandboxService
    {
        string SandboxRoot { get; }

        SandboxCopy CreateSandboxCopy(string sourceProjectPath, string tiaVersion);

        /// <summary>null = érvényes sandbox, különben (hibakód, üzenet).</summary>
        Tuple<string, string> Verify(string projectPath, string tiaVersion);
    }

    public sealed class AuditEvent
    {
        public string Event { get; set; }

        public string SessionId { get; set; }

        public string RunId { get; set; }

        public string JobId { get; set; }

        public string State { get; set; }

        public string TiaVersion { get; set; }

        public string ErrorCode { get; set; }

        public string Message { get; set; }

        public IDictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>Visszakereshető, hozzáfűző (JSONL) műveleti napló.</summary>
    public interface IAuditLog
    {
        void Write(AuditEvent auditEvent);
    }

    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public interface IUserContext
    {
        string UserName { get; }
    }

    public interface IWorkspace
    {
        /// <summary>Új, egyedi futtatási mappa a generált kimenetekhez (generated/&lt;run_id&gt;).</summary>
        string CreateRunDirectory(string runId);
    }
}
