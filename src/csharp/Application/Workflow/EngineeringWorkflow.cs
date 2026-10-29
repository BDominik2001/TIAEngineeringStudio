using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;
using Newtonsoft.Json;
using TiaStudio.Domain.Model;
using TiaStudio.Domain.Rules;
using TiaStudio.Domain.Workflow;

namespace TiaStudio.Application.Workflow
{
    public sealed class WorkflowOptions
    {
        public TimeSpan DryRunTimeout { get; set; } = TimeSpan.FromMinutes(15);

        public TimeSpan ImportTimeout { get; set; } = TimeSpan.FromMinutes(45);
    }

    /// <summary>Mi a munkafolyamat bemenete: gépmodell (Python-generálás) vagy könyvtárválasztás.</summary>
    public enum WorkflowInputKind
    {
        None,
        MachineModel,
        Library,
    }

    public sealed class StepResult
    {
        private StepResult(bool ok, string errorCode, string message, IReadOnlyList<string> details)
        {
            Ok = ok;
            ErrorCode = errorCode;
            Message = message;
            Details = details ?? new string[0];
        }

        public bool Ok { get; }

        public string ErrorCode { get; }

        public string Message { get; }

        public IReadOnlyList<string> Details { get; }

        public static StepResult Success(string message, IReadOnlyList<string> details = null)
        {
            return new StepResult(true, null, message, details);
        }

        public static StepResult Error(string errorCode, string message, IReadOnlyList<string> details = null)
        {
            return new StepResult(false, errorCode, message, details);
        }

        public override string ToString()
        {
            return Ok ? "OK: " + Message : ErrorCode + ": " + Message;
        }
    }

    /// <summary>
    /// A spec 7. fejezetének munkafolyamata: DRAFT -> VALIDATED -> CHECKED -> GENERATED -> REVIEWED
    /// -> APPROVED -> READY_TO_IMPORT -> IMPORTED -> COMPILED -> SAVED. Bármely validációs, integritási,
    /// import- vagy fordítási hiba FAILED állapotot ad; onnan csak Reset() (DRAFT) vezet tovább.
    /// A modell bármely módosítása érvényteleníti a generált csomagot és a jóváhagyást.
    /// </summary>
    public sealed class EngineeringWorkflow
    {
        private const string InputFileName = "machine-project.json";
        private const string LibraryInputFileName = "library-selection.json";

        private readonly IMachineProjectSerializer serializer;
        private readonly IModelValidator modelValidator;
        private readonly IStaticValidator staticValidator;
        private readonly ISourceGenerator generator;
        private readonly ITiaWorkerClient workerClient;
        private readonly ISandboxService sandbox;
        private readonly IAuditLog audit;
        private readonly IClock clock;
        private readonly IUserContext user;
        private readonly IWorkspace workspace;
        private readonly WorkflowOptions options;
        private readonly IBlockLibraryStore libraries;
        private List<BlockLibraryItem> selectedItems = new List<BlockLibraryItem>();
        private int busy;

        public EngineeringWorkflow(
            IMachineProjectSerializer serializer,
            IModelValidator modelValidator,
            IStaticValidator staticValidator,
            ISourceGenerator generator,
            ITiaWorkerClient workerClient,
            ISandboxService sandbox,
            IAuditLog audit,
            IClock clock,
            IUserContext user,
            IWorkspace workspace,
            WorkflowOptions options = null,
            IBlockLibraryStore libraries = null)
        {
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            this.modelValidator = modelValidator ?? throw new ArgumentNullException(nameof(modelValidator));
            this.staticValidator = staticValidator ?? throw new ArgumentNullException(nameof(staticValidator));
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            this.workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
            this.sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
            this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.user = user ?? throw new ArgumentNullException(nameof(user));
            this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            this.options = options ?? new WorkflowOptions();
            this.libraries = libraries;
            SessionId = Guid.NewGuid().ToString();
        }

        public event EventHandler StateChanged;

        public string SessionId { get; }

        public WorkflowState State { get; private set; } = WorkflowState.Draft;

        public WorkflowInputKind InputKind { get; private set; }

        /// <summary>Gépmodell-módban a modell; könyvtár-módban null.</summary>
        public MachineProject Project { get; private set; }

        /// <summary>Könyvtár-módban a kiválasztott elemek és a cél.</summary>
        public LibraryImportSelection LibrarySelection { get; private set; }

        public IReadOnlyList<BlockLibraryItem> SelectedLibraryItems => selectedItems;

        /// <summary>A bemenet kanonikus bájtjai (gépmodell-JSON vagy könyvtárválasztás-JSON).</summary>
        public byte[] ModelBytes { get; private set; }

        public string ModelSha256 { get; private set; }

        public ValidationReport ModelReport { get; private set; }

        public ValidationReport StaticReport { get; private set; }

        public string RunId { get; private set; }

        public string InputPath { get; private set; }

        public GenerationResult Generation { get; private set; }

        public PackageVerificationResult Package { get; private set; }

        public string SandboxProjectPath { get; private set; }

        public TiaCommand ApprovedCommand { get; private set; }

        public TiaResult DryRunResult { get; private set; }

        public TiaResult ImportResult { get; private set; }

        public string FailureCode { get; private set; }

        public string FailureMessage { get; private set; }

        public string TiaVersion => InputKind == WorkflowInputKind.Library ? LibrarySelection?.TargetTiaVersion : Project?.Target?.TiaVersion;

        public string PlcName => InputKind == WorkflowInputKind.Library ? LibrarySelection?.PlcName : Project?.Target?.PlcName;

        /// <summary>Új vagy módosított modell: minden korábbi eredmény érvénytelen, állapot DRAFT.</summary>
        public void SetProject(MachineProject project)
        {
            Project = project ?? throw new ArgumentNullException(nameof(project));
            LibrarySelection = null;
            InputKind = WorkflowInputKind.MachineModel;
            ModelBytes = serializer.Serialize(project);
            ModelSha256 = Sha256.OfBytes(ModelBytes);
            ClearDownstream();
            MoveTo(WorkflowState.Draft, "ModelChanged", null, "model sha256 " + ModelSha256);
        }

        /// <summary>
        /// Könyvtárimport: a kiválasztott elemek a gépmodell helyett a munkafolyamat bemenetei.
        /// VALIDATED = elemszabályok + verziókompatibilitás + könyvtár-integritás, CHECKED = hash-ek
        /// újramérése, GENERATED = importcsomag (manifest 1.1); utána azonos jóváhagyás és import.
        /// </summary>
        public void SetLibrarySelection(LibraryImportSelection selection)
        {
            if (libraries == null)
            {
                throw new InvalidOperationException("No block library store configured");
            }
            LibrarySelection = selection ?? throw new ArgumentNullException(nameof(selection));
            Project = null;
            InputKind = WorkflowInputKind.Library;
            ModelBytes = CanonicalSelection(selection);
            ModelSha256 = Sha256.OfBytes(ModelBytes);
            ClearDownstream();
            MoveTo(WorkflowState.Draft, "LibrarySelectionChanged", null, selection.LibraryName + ": " + selection.ItemIds.Count + " item(s) -> " + selection.TargetTiaVersion);
        }

        /// <summary>FAILED (vagy bármely) állapotból vissza DRAFT-ba; a modell megmarad, minden más törlődik.</summary>
        public void Reset()
        {
            ClearDownstream();
            MoveTo(WorkflowState.Draft, "Reset", null, null);
        }

        public StepResult Validate()
        {
            return Guarded(WorkflowState.Draft, () =>
            {
                ModelReport = InputKind == WorkflowInputKind.Library ? ValidateLibrarySelection() : modelValidator.Validate(ModelBytes);
                if (!ModelReport.Ok)
                {
                    return Fail(FirstErrorCode(ModelReport), "Model validation failed with " + ModelReport.ErrorCount + " error(s)", Describe(ModelReport));
                }
                MoveTo(WorkflowState.Validated, "Validated", null, ModelReport.WarningCount + " warning(s)");
                return StepResult.Success("Model is valid", Describe(ModelReport));
            });
        }

        public StepResult Check()
        {
            return Guarded(WorkflowState.Validated, () =>
            {
                if (InputKind == WorkflowInputKind.Library)
                {
                    StaticReport = RecheckLibraryItems();
                    if (!StaticReport.Ok)
                    {
                        return Fail(FirstErrorCode(StaticReport), "Library items changed after validation", Describe(StaticReport));
                    }
                    MoveTo(WorkflowState.Checked, "Checked", null, "library item hashes re-verified");
                    return StepResult.Success("Library items unchanged since validation", Describe(StaticReport));
                }
                StaticReport = staticValidator.Validate(ModelBytes);
                if (!StaticReport.Ok)
                {
                    return Fail(FirstErrorCode(StaticReport), "Static validation (" + StaticReport.Validator + ") failed", Describe(StaticReport));
                }
                if (!SameIssueCodes(ModelReport, StaticReport))
                {
                    return Fail(ErrorCodes.InternalError, "C# and Rust validators disagree; generation is blocked", Describe(StaticReport));
                }
                MoveTo(WorkflowState.Checked, "Checked", null, StaticReport.Validator + " " + StaticReport.ValidatorVersion);
                return StepResult.Success("Static validation passed", Describe(StaticReport));
            });
        }

        public Task<StepResult> GenerateAsync(CancellationToken cancellationToken)
        {
            return GuardedAsync(WorkflowState.Checked, async () =>
            {
                RunId = clock.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string runDirectory = workspace.CreateRunDirectory(RunId);
                bool library = InputKind == WorkflowInputKind.Library;
                InputPath = Path.Combine(runDirectory, "input", library ? LibraryInputFileName : InputFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(InputPath));
                File.WriteAllBytes(InputPath, ModelBytes);

                string packageDirectory = Path.Combine(runDirectory, "package");
                Generation = library
                    ? BuildLibraryPackage(packageDirectory)
                    : await generator.GenerateAsync(
                        new GenerationRequest { InputPath = InputPath, OutputDirectory = packageDirectory, RunId = RunId },
                        cancellationToken).ConfigureAwait(false);
                if (!Generation.Success)
                {
                    return Fail(Generation.ErrorCode ?? ErrorCodes.GeneratorFailed, Generation.Message ?? "Generator failed", Split(Generation.Diagnostics));
                }

                Package = PackageVerifier.Verify(Generation.OutputDirectory, Generation.ManifestPath);
                if (!Package.Ok)
                {
                    return Fail(Package.FirstErrorCode, "Generated package failed verification", Package.Problems.Select(p => p.ToString()).ToList());
                }
                if (Package.ManifestSha256 != Generation.ManifestSha256 || Package.Manifest.SourceConfigSha256 != ModelSha256)
                {
                    return Fail(ErrorCodes.HashMismatch, "Manifest does not belong to the validated model or generator output");
                }
                if (Package.Manifest.TargetTiaVersion != TiaVersion || Package.Manifest.PlcName != PlcName)
                {
                    return Fail(ErrorCodes.ProjectVersionMismatch, "Manifest target differs from the machine model target");
                }
                MoveTo(WorkflowState.Generated, "Generated", null, Package.Manifest.Files.Count + " file(s), manifest " + Package.ManifestSha256);
                return StepResult.Success(library
                    ? "Import package with " + Package.Manifest.Files.Count + " library block(s) assembled"
                    : "Generated " + Package.Manifest.Files.Count + " SCL source file(s)");
            });
        }

        /// <summary>A felhasználó átnézte a kód-előnézetet (REVIEWED). A hasheket újraellenőrizzük.</summary>
        public StepResult MarkReviewed()
        {
            return Guarded(WorkflowState.Generated, () =>
            {
                StepResult integrity = VerifyIntegrity();
                if (!integrity.Ok)
                {
                    return integrity;
                }
                MoveTo(WorkflowState.Reviewed, "Reviewed", null, "reviewed by " + user.UserName);
                return StepResult.Success("Sources marked as reviewed");
            });
        }

        /// <summary>
        /// Jóváhagyás egy konkrét sandbox-projektre. A jóváhagyás a projektútvonalhoz, a sandbox-másolat
        /// azonosítójához, a PLC-hez, a manifesthez, az összes forráshashhez és az új job_id-hoz kötött
        /// (ApprovalDigest v2).
        /// </summary>
        public StepResult Approve(string sandboxProjectPath)
        {
            return Guarded(WorkflowState.Reviewed, () =>
            {
                Tuple<string, string> sandboxProblem = sandbox.Verify(sandboxProjectPath, TiaVersion);
                if (sandboxProblem != null)
                {
                    // Felhasználói választási hiba: nem érinti a generált csomagot, az állapot marad.
                    Log("ApprovalRejected", sandboxProblem.Item1, sandboxProblem.Item2);
                    return StepResult.Error(sandboxProblem.Item1, sandboxProblem.Item2);
                }
                StepResult integrity = VerifyIntegrity();
                if (!integrity.Ok)
                {
                    return integrity;
                }
                SandboxProjectPath = Path.GetFullPath(sandboxProjectPath);
                string sandboxId = SandboxRules.ReadMarker(SandboxProjectPath).SandboxId;
                TiaCommand command = new TiaCommand
                {
                    JobId = Guid.NewGuid().ToString(),
                    Operation = TiaOperations.ImportAndCompile,
                    TiaVersion = TiaVersion,
                    ProjectPath = SandboxProjectPath,
                    SandboxRoot = sandbox.SandboxRoot,
                    PlcName = PlcName,
                    SourcesDirectory = Generation.OutputDirectory,
                    ManifestPath = Generation.ManifestPath,
                    DryRun = false,
                    OverwritePolicy = OverwritePolicies.Deny,
                    ApprovedByUser = true,
                };
                command.Approval = new ApprovalRecord
                {
                    ApprovedBy = user.UserName,
                    ApprovedAtUtc = clock.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    ApprovalDigest = ApprovalDigest.Compute(command, Package.ManifestSha256, Package.Manifest.Files, sandboxId),
                };
                ApprovedCommand = command;
                MoveTo(WorkflowState.Approved, "Approved", command.JobId, "project " + SandboxProjectPath + ", sandbox " + sandboxId + ", digest " + command.Approval.ApprovalDigest);
                return StepResult.Success("Approved for " + SandboxProjectPath);
            });
        }

        /// <summary>Célverzió-ellenőrzés + száraz futás a workerrel (APPROVED -> READY_TO_IMPORT).</summary>
        public Task<StepResult> PrepareImportAsync(CancellationToken cancellationToken)
        {
            return GuardedAsync(WorkflowState.Approved, async () =>
            {
                TiaInstallInfo install = workerClient.DiscoverInstalledVersions().FirstOrDefault(i => i.TiaVersion == TiaVersion);
                if (install == null || !InstallStatus.IsUsable(install.Status))
                {
                    string code = install?.ErrorCode ?? ErrorCodes.TiaNotInstalled;
                    string message = "TIA Portal " + TiaVersion + " is not usable on this machine (" + (install?.Status ?? InstallStatus.NotFound) + "). No other version is substituted.";
                    Log("PrepareImportBlocked", code, message);
                    return StepResult.Error(code, message, install?.Notes);
                }
                StepResult integrity = VerifyIntegrity();
                if (!integrity.Ok)
                {
                    return integrity;
                }
                TiaCommand dryRun = CloneForDryRun(ApprovedCommand);
                DryRunResult = await workerClient.ValidateJobAsync(new TiaJobRequest(dryRun, options.DryRunTimeout), cancellationToken).ConfigureAwait(false);
                if (DryRunResult.Status != TiaJobStatus.ValidationOnly)
                {
                    return Fail(DryRunResult.ErrorCode ?? ErrorCodes.InternalError, "Dry run failed: " + DryRunResult.Status, DryRunResult.Messages.Select(m => m.ToString()).ToList());
                }
                MoveTo(WorkflowState.ReadyToImport, "ReadyToImport", dryRun.JobId, (DryRunResult.ImportPlan?.Count ?? 0) + " block(s) planned, no collision");
                return StepResult.Success("Dry run passed; ready to import into " + SandboxProjectPath);
            });
        }

        /// <summary>Valódi import + offline fordítás + mentés a jóváhagyott sandboxba.</summary>
        public Task<StepResult> ImportAndCompileAsync(CancellationToken cancellationToken)
        {
            return GuardedAsync(WorkflowState.ReadyToImport, async () =>
            {
                StepResult integrity = VerifyIntegrity();
                if (!integrity.Ok)
                {
                    return integrity;
                }
                Tuple<string, string> sandboxProblem = sandbox.Verify(SandboxProjectPath, TiaVersion);
                if (sandboxProblem != null)
                {
                    return Fail(sandboxProblem.Item1, sandboxProblem.Item2);
                }
                string digest = ApprovalDigest.Compute(ApprovedCommand, Package.ManifestSha256, Package.Manifest.Files, SandboxRules.ReadMarker(SandboxProjectPath).SandboxId);
                if (digest != ApprovedCommand.Approval.ApprovalDigest)
                {
                    return Fail(ErrorCodes.ApprovalInvalid, "Approval digest no longer matches the package or the sandbox copy (it was recreated after the approval); approve again");
                }
                ImportResult = await workerClient.ImportAndCompileAsync(new TiaJobRequest(ApprovedCommand, options.ImportTimeout), cancellationToken).ConfigureAwait(false);
                List<string> details = ImportResult.Messages.Select(m => m.ToString()).ToList();
                details.Add("sandbox_state=" + ImportResult.SandboxState);

                bool imported = ImportResult.SandboxState == SandboxStates.Saved || ImportResult.SandboxState == SandboxStates.ModifiedNotSaved;
                if (imported)
                {
                    MoveTo(WorkflowState.Imported, "Imported", ApprovedCommand.JobId, null);
                }
                if (ImportResult.Status != TiaJobStatus.Success || !ImportResult.SavedProject || ImportResult.SandboxState != SandboxStates.Saved)
                {
                    string message = "Import/compile did not succeed: " + ImportResult.Status + " (sandbox " + ImportResult.SandboxState + ")";
                    if (ImportResult.SandboxState == SandboxStates.ModifiedNotSaved || ImportResult.SandboxState == SandboxStates.Unknown)
                    {
                        message += ". Recreate the sandbox from the known-good project copy before retrying.";
                    }
                    return Fail(ImportResult.ErrorCode ?? ErrorCodes.InternalError, message, details);
                }
                MoveTo(WorkflowState.Compiled, "Compiled", ApprovedCommand.JobId, (ImportResult.Compile?.WarningCount ?? 0) + " compiler warning(s)");
                MoveTo(WorkflowState.Saved, "Saved", ApprovedCommand.JobId, SandboxProjectPath);
                return StepResult.Success("Imported, compiled and saved " + SandboxProjectPath, details);
            });
        }

        private static TiaCommand CloneForDryRun(TiaCommand approved)
        {
            return new TiaCommand
            {
                JobId = Guid.NewGuid().ToString(),
                Operation = TiaOperations.ValidateJob,
                TiaVersion = approved.TiaVersion,
                ProjectPath = approved.ProjectPath,
                SandboxRoot = approved.SandboxRoot,
                PlcName = approved.PlcName,
                SourcesDirectory = approved.SourcesDirectory,
                ManifestPath = approved.ManifestPath,
                DryRun = true,
                OverwritePolicy = approved.OverwritePolicy,
                ApprovedByUser = false,
                Approval = null,
            };
        }

        /// <summary>Forráshash-ek, bemeneti modell és manifest újraellenőrzése (spec 6.2, 10.1/7).</summary>
        private StepResult VerifyIntegrity()
        {
            if (InputPath == null || !File.Exists(InputPath) || Sha256.OfFile(InputPath) != ModelSha256)
            {
                return Fail(ErrorCodes.HashMismatch, "The machine model input changed after validation");
            }
            PackageVerificationResult current = PackageVerifier.Verify(Generation.OutputDirectory, Generation.ManifestPath);
            if (!current.Ok)
            {
                return Fail(current.FirstErrorCode, "Generated sources changed or are unreadable", current.Problems.Select(p => p.ToString()).ToList());
            }
            if (current.ManifestSha256 != Package.ManifestSha256)
            {
                return Fail(ErrorCodes.HashMismatch, "manifest.json changed after generation");
            }
            return StepResult.Success("Integrity verified");
        }

        private StepResult Guarded(WorkflowState required, Func<StepResult> step)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1)
            {
                return StepResult.Error(ErrorCodes.WorkflowStateInvalid, "Another workflow step is running");
            }
            try
            {
                StepResult precondition = Precondition(required);
                return precondition ?? step();
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Fail(ErrorCodes.InternalError, ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        }

        private async Task<StepResult> GuardedAsync(WorkflowState required, Func<Task<StepResult>> step)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1)
            {
                return StepResult.Error(ErrorCodes.WorkflowStateInvalid, "Another workflow step is running");
            }
            try
            {
                StepResult precondition = Precondition(required);
                return precondition ?? await step().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Fail(ErrorCodes.Cancelled, "Cancelled by the user; if a TIA operation was running, the sandbox state is UNKNOWN");
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Fail(ErrorCodes.InternalError, ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        }

        private StepResult Precondition(WorkflowState required)
        {
            if (ModelBytes == null)
            {
                return StepResult.Error(ErrorCodes.WorkflowStateInvalid, "No machine project or library selection loaded");
            }
            if (State != required)
            {
                return StepResult.Error(
                    ErrorCodes.WorkflowStateInvalid,
                    "Step requires state " + WorkflowStateMachine.DisplayName(required) + " but the workflow is " + WorkflowStateMachine.DisplayName(State));
            }
            return null;
        }

        private StepResult Fail(string code, string message, IReadOnlyList<string> details = null)
        {
            FailureCode = code;
            FailureMessage = message;
            if (State != WorkflowState.Failed)
            {
                MoveTo(WorkflowState.Failed, "Failed", ApprovedCommand?.JobId, message, code);
            }
            return StepResult.Error(code, message, details);
        }

        private void MoveTo(WorkflowState next, string eventName, string jobId, string message, string errorCode = null)
        {
            WorkflowState previous = State;
            State = WorkflowStateMachine.Transition(previous, next);
            audit.Write(new AuditEvent
            {
                Event = eventName,
                SessionId = SessionId,
                RunId = RunId,
                JobId = jobId,
                State = WorkflowStateMachine.DisplayName(State),
                TiaVersion = TiaVersion,
                ErrorCode = errorCode,
                Message = message,
                Details = new Dictionary<string, object>
                {
                    { "previous_state", WorkflowStateMachine.DisplayName(previous) },
                    { "user", user.UserName },
                    { "model_sha256", ModelSha256 },
                },
            });
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Log(string eventName, string errorCode, string message)
        {
            audit.Write(new AuditEvent
            {
                Event = eventName,
                SessionId = SessionId,
                RunId = RunId,
                State = WorkflowStateMachine.DisplayName(State),
                TiaVersion = TiaVersion,
                ErrorCode = errorCode,
                Message = message,
            });
        }

        private void ClearDownstream()
        {
            ModelReport = null;
            StaticReport = null;
            RunId = null;
            InputPath = null;
            Generation = null;
            Package = null;
            SandboxProjectPath = null;
            ApprovedCommand = null;
            DryRunResult = null;
            ImportResult = null;
            FailureCode = null;
            FailureMessage = null;
            selectedItems = new List<BlockLibraryItem>();
        }

        private static byte[] CanonicalSelection(LibraryImportSelection selection)
        {
            string json = JsonConvert.SerializeObject(new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                { "item_ids", selection.ItemIds.OrderBy(i => i, StringComparer.Ordinal).ToList() },
                { "library_name", selection.LibraryName },
                { "plc_name", selection.PlcName },
                { "selection_version", "1.0" },
                { "target_tia_version", selection.TargetTiaVersion },
            }, Formatting.Indented);
            return new System.Text.UTF8Encoding(false).GetBytes(json + "\n");
        }

        private ValidationReport ValidateLibrarySelection()
        {
            List<ValidationIssue> issues = new List<ValidationIssue>();
            Action<string, string, string> error = (code, path, message) => issues.Add(new ValidationIssue(ValidationIssue.SeverityError, code, path, message));
            LibraryImportSelection selection = LibrarySelection;
            selectedItems = new List<BlockLibraryItem>();
            if (!TiaVersions.IsSupported(selection.TargetTiaVersion))
            {
                error(ErrorCodes.UnsupportedTiaVersion, "target_tia_version", "TIA version '" + selection.TargetTiaVersion + "' is not supported (V18, V19, V20)");
            }
            if (string.IsNullOrEmpty(selection.PlcName) || !NamingRules.IsPlcName(selection.PlcName))
            {
                error(ErrorCodes.InvalidIdentifier, "plc_name", "PLC name '" + selection.PlcName + "' does not match ^[A-Za-z][A-Za-z0-9_-]{0,63}$");
            }
            if (selection.ItemIds.Count == 0)
            {
                error(ErrorCodes.LibraryItemNotFound, "items", "Select at least one library item");
            }
            BlockLibraryIndex index = null;
            try
            {
                index = libraries.Load(selection.LibraryName);
            }
            catch (BlockLibraryException ex)
            {
                error(ex.ErrorCode, "library", ex.Message);
            }
            if (index != null)
            {
                foreach (TiaMessage problem in libraries.Verify(selection.LibraryName))
                {
                    error(problem.Code ?? ErrorCodes.LibraryInvalid, "library" + (problem.Path == null ? string.Empty : ":" + problem.Path), problem.Text);
                }
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int position = 0;
                foreach (string id in selection.ItemIds)
                {
                    string path = "items[" + position++ + "]";
                    BlockLibraryItem item = index.Items.FirstOrDefault(i => i.ItemId == id);
                    if (item == null)
                    {
                        error(ErrorCodes.LibraryItemNotFound, path, "Library item " + id + " not found");
                        continue;
                    }
                    Tuple<string, string> importProblem = LibraryRules.ImportProblem(item, selection.TargetTiaVersion);
                    if (importProblem != null)
                    {
                        error(importProblem.Item1, path, importProblem.Item2);
                        continue;
                    }
                    if (!names.Add(item.BlockName))
                    {
                        error(ErrorCodes.LibraryDuplicateBlock, path, "Block " + item.BlockName + " is selected more than once (choose one revision)");
                        continue;
                    }
                    string file = libraries.ItemPath(selection.LibraryName, item);
                    if (item.Format == BlockFormats.ExternalSource && File.Exists(file) && !LibraryRules.IsAsciiExternalSource(File.ReadAllBytes(file)))
                    {
                        error(ErrorCodes.NonAsciiSource, path, item.BlockName + " is not an ASCII external source");
                        continue;
                    }
                    selectedItems.Add(item);
                }
            }
            if (issues.Count == 0 && selectedItems.Any(i => i.Format == BlockFormats.ExternalSource && i.SourceTiaVersion != selection.TargetTiaVersion))
            {
                issues.Add(new ValidationIssue(ValidationIssue.SeverityWarning, ErrorCodes.LibraryVersionMismatch, "items",
                    "Some external sources were exported from another TIA version; the offline compile in " + selection.TargetTiaVersion + " decides whether they are compatible"));
            }
            return LibraryReport("library-selection", issues);
        }

        private ValidationReport RecheckLibraryItems()
        {
            List<ValidationIssue> issues = new List<ValidationIssue>();
            BlockLibraryIndex index;
            try
            {
                index = libraries.Load(LibrarySelection.LibraryName);
            }
            catch (BlockLibraryException ex)
            {
                issues.Add(new ValidationIssue(ValidationIssue.SeverityError, ex.ErrorCode, "library", ex.Message));
                return LibraryReport("library-integrity", issues);
            }
            foreach (BlockLibraryItem item in selectedItems)
            {
                BlockLibraryItem current = index.Items.FirstOrDefault(i => i.ItemId == item.ItemId);
                string file = libraries.ItemPath(LibrarySelection.LibraryName, item);
                if (current == null || current.Sha256 != item.Sha256 || !File.Exists(file) || Sha256.OfFile(file) != item.Sha256)
                {
                    issues.Add(new ValidationIssue(ValidationIssue.SeverityError, ErrorCodes.HashMismatch, item.BlockName, "Library item changed or was removed since validation"));
                }
            }
            return LibraryReport("library-integrity", issues);
        }

        private GenerationResult BuildLibraryPackage(string packageDirectory)
        {
            try
            {
                return libraries.BuildImportPackage(LibrarySelection, selectedItems, packageDirectory, RunId, ModelSha256, ModelReport);
            }
            catch (BlockLibraryException ex)
            {
                return new GenerationResult { Success = false, ErrorCode = ex.ErrorCode, Message = ex.Message };
            }
        }

        private static ValidationReport LibraryReport(string validator, List<ValidationIssue> issues)
        {
            int errors = issues.Count(i => i.Severity == ValidationIssue.SeverityError);
            return new ValidationReport
            {
                Validator = validator,
                ValidatorVersion = "1.0",
                RulesVersion = BlockLibraryIndex.CurrentFormatVersion,
                Ok = errors == 0,
                ErrorCount = errors,
                WarningCount = issues.Count - errors,
                Issues = issues,
            };
        }

        private static string FirstErrorCode(ValidationReport report)
        {
            return report.Issues.FirstOrDefault(i => i.Severity == ValidationIssue.SeverityError)?.Code ?? ErrorCodes.InternalError;
        }

        private static bool SameIssueCodes(ValidationReport a, ValidationReport b)
        {
            return a.Issues.Select(i => i.Code + "@" + i.Path).SequenceEqual(b.Issues.Select(i => i.Code + "@" + i.Path));
        }

        private static IReadOnlyList<string> Describe(ValidationReport report)
        {
            return report.Issues.Select(i => i.ToString()).ToList();
        }

        private static IReadOnlyList<string> Split(string text)
        {
            return string.IsNullOrEmpty(text) ? new string[0] : text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
