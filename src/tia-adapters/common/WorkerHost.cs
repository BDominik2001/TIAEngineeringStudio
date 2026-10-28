using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;

namespace TiaStudio.TiaAdapter.Common
{
    public static class WorkerExitCodes
    {
        public const int Success = 0;
        public const int Usage = 1;
        public const int Blocked = 2;
        public const int Failed = 3;
        public const int Unsupported = 4;
        public const int Cancelled = 5;
    }

    /// <summary>
    /// A worker folyamat teljes végrehajtási sorrendje (spec 8.4):
    /// 1. parancs, protokoll és verzió; 2. környezet (DLL, jogosultság, licenc); 3. útvonalak,
    /// kiterjesztés, sandbox; 4. manifest + SHA-256; 5. jóváhagyás; 6. projektnyitás, PLC, offline állapot;
    /// 7. rekurzív ütközésvizsgálat és importterv (dry run itt megáll); 8. import; 9. offline fordítás;
    /// 10. mentés csak teljes siker esetén; minden esetben eredményfájl és erőforrás-felszabadítás.
    /// </summary>
    public sealed class WorkerHost
    {
        private readonly string workerVersion;
        private readonly IOpennessBackend backend;
        private readonly TextWriter log;
        private int cancelRequested;

        public WorkerHost(string workerVersion, IOpennessBackend backend, TextWriter log)
        {
            if (!TiaVersions.IsSupported(workerVersion))
            {
                throw new ArgumentException("Unsupported worker version " + workerVersion, nameof(workerVersion));
            }
            this.workerVersion = workerVersion;
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.log = log ?? TextWriter.Null;
        }

        /// <summary>Ctrl+C / leállítási kérés: a következő biztonságos ponton Cancelled eredmény.</summary>
        public void RequestCancel()
        {
            Interlocked.Exchange(ref cancelRequested, 1);
        }

        public int Run(string[] args)
        {
            string commandPath = null;
            string resultPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--command" && i + 1 < args.Length)
                {
                    commandPath = args[++i];
                }
                else if (args[i] == "--result" && i + 1 < args.Length)
                {
                    resultPath = args[++i];
                }
                else
                {
                    commandPath = null;
                    break;
                }
            }
            if (string.IsNullOrEmpty(commandPath))
            {
                log.WriteLine("Usage: TiaStudio.TiaAdapter." + workerVersion + ".exe --command <job.json> [--result <result.json>]");
                return WorkerExitCodes.Usage;
            }
            resultPath = resultPath ?? DefaultResultPath(commandPath);
            TiaResult result = Execute(commandPath);
            try
            {
                ProtocolJson.WriteAtomic(resultPath, result);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                log.WriteLine("Cannot write result file " + resultPath + ": " + ex.Message);
                return WorkerExitCodes.Failed;
            }
            log.WriteLine(result.Status + " " + (result.ErrorCode ?? "-") + " sandbox=" + result.SandboxState + " result=" + resultPath);
            return ExitCodeFor(result.Status);
        }

        public static string DefaultResultPath(string commandPath)
        {
            const string suffix = ".command.json";
            return commandPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? commandPath.Substring(0, commandPath.Length - suffix.Length) + ".result.json"
                : commandPath + ".result.json";
        }

        public static int ExitCodeFor(string status)
        {
            switch (status)
            {
                case TiaJobStatus.Success:
                case TiaJobStatus.ValidationOnly:
                    return WorkerExitCodes.Success;
                case TiaJobStatus.Blocked:
                    return WorkerExitCodes.Blocked;
                case TiaJobStatus.Unsupported:
                    return WorkerExitCodes.Unsupported;
                case TiaJobStatus.Cancelled:
                    return WorkerExitCodes.Cancelled;
                default:
                    return WorkerExitCodes.Failed;
            }
        }

        public TiaResult Execute(string commandPath)
        {
            TiaResult result = new TiaResult { ActualVersion = workerVersion, StartedAtUtc = Now() };
            try
            {
                result.Worker = backend.Describe();
                TiaCommand command = ReadCommand(commandPath, result);
                if (command != null)
                {
                    Dispatch(command, result);
                }
            }
            catch (Exception ex)
            {
                // Váratlan hiba: ha már történt import, a sandbox állapota ismeretlen.
                string state = result.SandboxState == SandboxStates.NotTouched ? SandboxStates.NotTouched : SandboxStates.Unknown;
                Finish(result, TiaJobStatus.Failed, OpennessErrorClassifier.Classify(ex, ErrorCodes.InternalError), "Unexpected worker error: " + ex.Message, state);
            }
            result.FinishedAtUtc = Now();
            result.ErrorCount = result.Messages.Count(m => m.Severity == MessageSeverity.Error) + (result.Compile?.ErrorCount ?? 0);
            result.WarningCount = result.Messages.Count(m => m.Severity == MessageSeverity.Warning) + (result.Compile?.WarningCount ?? 0);
            return result;
        }

        private TiaCommand ReadCommand(string commandPath, TiaResult result)
        {
            TiaCommand command;
            try
            {
                command = ProtocolJson.DeserializeStrict<TiaCommand>(ProtocolJson.ReadAllText(commandPath));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException || ex is ArgumentException)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.CommandInvalid, "Command file is unreadable or violates tia-command.schema.json: " + ex.Message, SandboxStates.NotTouched);
                return null;
            }
            if (command == null)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.CommandInvalid, "Command file is empty", SandboxStates.NotTouched);
                return null;
            }
            result.JobId = command.JobId;
            result.RequestedVersion = command.TiaVersion;
            result.Operation = command.Operation;
            if (!ProtocolVersions.Supported.Contains(command.ProtocolVersion))
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.ProtocolVersionUnsupported, "protocol_version " + command.ProtocolVersion + " is not supported", SandboxStates.NotTouched);
                return null;
            }
            if (!Guid.TryParseExact(command.JobId ?? string.Empty, "D", out Guid _) || command.JobId != command.JobId.ToLowerInvariant())
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.CommandInvalid, "job_id must be a lowercase UUID", SandboxStates.NotTouched);
                return null;
            }
            if (!TiaOperations.All.Contains(command.Operation))
            {
                Finish(result, TiaJobStatus.Unsupported, ErrorCodes.OperationNotSupported, "Operation '" + command.Operation + "' is not part of the protocol (download/online/force operations do not exist)", SandboxStates.NotTouched);
                return null;
            }
            if (command.TiaVersion != workerVersion)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.WorkerVersionMismatch, "This is the " + workerVersion + " worker; requested " + command.TiaVersion + ". No other version is substituted.", SandboxStates.NotTouched);
                return null;
            }
            return command;
        }

        private void Dispatch(TiaCommand command, TiaResult result)
        {
            switch (command.Operation)
            {
                case TiaOperations.GetCapabilities:
                    result.Capabilities = backend.GetCapabilities();
                    OpennessProblem problem = backend.ProbeEnvironment();
                    if (problem != null)
                    {
                        Finish(result, TiaJobStatus.Blocked, problem.ErrorCode, problem.Message, SandboxStates.NotTouched);
                    }
                    else
                    {
                        Finish(result, TiaJobStatus.Success, null, "Capabilities reported", SandboxStates.NotTouched);
                    }
                    return;
                case TiaOperations.ListBlocks:
                case TiaOperations.ExportBlocks:
                    RunReadOnlyJob(command, result);
                    return;
                case TiaOperations.UpgradeProject:
                    Finish(result, TiaJobStatus.Unsupported, ErrorCodes.OperationNotSupported,
                        "UpgradeProject is not part of the MVP. Project upgrades must be performed manually on an approved, saved copy.", SandboxStates.NotTouched);
                    return;
                default:
                    RunJob(command, result);
                    return;
            }
        }

        private void RunJob(TiaCommand command, TiaResult result)
        {
            bool isImport = command.Operation == TiaOperations.ImportAndCompile;
            if (!isImport && !command.DryRun)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.CommandInvalid, "ValidateJob requires dry_run=true", SandboxStates.NotTouched);
                return;
            }

            // 1. Környezet
            OpennessProblem problem = backend.ProbeEnvironment();
            if (problem != null)
            {
                Finish(result, TiaJobStatus.Blocked, problem.ErrorCode, problem.Message, SandboxStates.NotTouched);
                return;
            }

            // 2-3. Parancsmezők, útvonalak, kiterjesztés, sandbox
            string missing = new[]
            {
                Tuple.Create("project_path", command.ProjectPath),
                Tuple.Create("sandbox_root", command.SandboxRoot),
                Tuple.Create("plc_name", command.PlcName),
                Tuple.Create("sources_directory", command.SourcesDirectory),
                Tuple.Create("manifest_path", command.ManifestPath),
            }.Where(t => string.IsNullOrWhiteSpace(t.Item2)).Select(t => t.Item1).FirstOrDefault();
            if (missing != null)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.CommandInvalid, "Required field '" + missing + "' is missing", SandboxStates.NotTouched);
                return;
            }
            if (command.OverwritePolicy != OverwritePolicies.Deny)
            {
                Finish(result, TiaJobStatus.Unsupported, ErrorCodes.OperationNotSupported, "Only overwrite_policy=Deny is supported", SandboxStates.NotTouched);
                return;
            }
            Tuple<string, string> sandboxProblem = SandboxRules.Check(command.ProjectPath, workerVersion, command.SandboxRoot);
            if (sandboxProblem != null)
            {
                Finish(result, TiaJobStatus.Blocked, sandboxProblem.Item1, sandboxProblem.Item2, SandboxStates.NotTouched);
                return;
            }

            // 4. Manifest és hash-ek
            PackageVerificationResult package = PackageVerifier.Verify(command.SourcesDirectory, command.ManifestPath);
            if (!package.Ok)
            {
                result.Messages.AddRange(package.Problems);
                Finish(result, TiaJobStatus.Blocked, package.FirstErrorCode, "Source package verification failed", SandboxStates.NotTouched);
                return;
            }
            GenerationManifest manifest = package.Manifest;
            if (manifest.TargetTiaVersion != workerVersion)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.ProjectVersionMismatch,
                    "Sources were generated for " + manifest.TargetTiaVersion + "; a " + workerVersion + " import needs sources generated for " + workerVersion, SandboxStates.NotTouched);
                return;
            }
            if (manifest.PlcName != command.PlcName)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.CommandInvalid, "plc_name differs from the manifest (" + manifest.PlcName + ")", SandboxStates.NotTouched);
                return;
            }
            List<ManifestFile> files = manifest.Files.OrderBy(f => f.ImportOrder).ToList();
            string nonAscii = files.Where(f => f.EffectiveFormat == BlockFormats.ExternalSource)
                .Select(f => f.RelativePath)
                .FirstOrDefault(p => !LibraryRules.IsAsciiExternalSource(File.ReadAllBytes(SourcePath(command, p))));
            if (nonAscii != null)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.NonAsciiSource, "External SCL sources must be ASCII: " + nonAscii, SandboxStates.NotTouched);
                return;
            }

            // 5. Jóváhagyás (csak a valódi, módosító importhoz)
            if (isImport && !command.DryRun)
            {
                string expected = ApprovalDigest.Compute(command, package.ManifestSha256, manifest.Files);
                if (!command.ApprovedByUser || command.Approval == null || !string.Equals(command.Approval.ApprovalDigest, expected, StringComparison.Ordinal))
                {
                    Finish(result, TiaJobStatus.Blocked, ErrorCodes.ApprovalInvalid,
                        "Import requires a user approval bound to this job, project path, PLC, manifest and every source hash", SandboxStates.NotTouched);
                    return;
                }
            }

            // 6-10. Openness
            IOpennessSession session;
            try
            {
                session = backend.OpenProject(Path.GetFullPath(command.ProjectPath));
            }
            catch (Exception ex)
            {
                Finish(result, TiaJobStatus.Blocked, OpennessErrorClassifier.Classify(ex, ErrorCodes.ProjectOpenFailed), "Project cannot be opened without upgrade (it must not be open in another TIA Portal instance and must be writable): " + ex.Message, SandboxStates.NotTouched);
                return;
            }
            try
            {
                result.SandboxState = SandboxStates.Unchanged;
                ImportWithSession(command, result, session, files);
            }
            finally
            {
                try
                {
                    session.Dispose();
                }
                catch (Exception ex)
                {
                    result.Messages.Add(new TiaMessage(MessageSeverity.Warning, null, "Closing the project/TIA Portal instance failed: " + ex.Message));
                }
            }
        }

        private void ImportWithSession(TiaCommand command, TiaResult result, IOpennessSession session, List<ManifestFile> files)
        {
            IPlcTarget plc = session.FindPlc(command.PlcName);
            if (plc == null)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.PlcNotFound, "PLC '" + command.PlcName + "' not found in the project", SandboxStates.Unchanged);
                return;
            }
            if (!plc.IsOffline())
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.PlcOnline, "PLC '" + plc.Name + "' is online; import and compile are offline-only", SandboxStates.Unchanged);
                return;
            }

            result.ImportPlan = BuildPlan(files, plc.GetAllBlocksAndTypes(), plc.GetExternalSources());
            List<ImportPlanItem> collisions = result.ImportPlan.Where(p => p.Action == ImportPlanItem.ActionCollision).ToList();
            if (collisions.Count > 0)
            {
                foreach (ImportPlanItem collision in collisions)
                {
                    result.Messages.Add(new TiaMessage(MessageSeverity.Error, ErrorCodes.BlockCollision, collision.BlockName + " already exists at " + collision.ExistingLocation, collision.RelativePath));
                }
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.BlockCollision, collisions.Count + " generated name(s) already exist; overwriting is denied", SandboxStates.Unchanged);
                return;
            }
            if (command.DryRun)
            {
                Finish(result, TiaJobStatus.ValidationOnly, null, "Dry run: " + files.Count + " block(s) would be created; project not modified", SandboxStates.Unchanged);
                return;
            }

            foreach (ManifestFile file in files)
            {
                if (Volatile.Read(ref cancelRequested) == 1)
                {
                    Finish(result, TiaJobStatus.Cancelled, ErrorCodes.Cancelled, "Cancelled before " + file.BlockName + "; project not saved", result.SandboxState);
                    return;
                }
                string path = SourcePath(command, file.RelativePath);
                if (Sha256.OfFile(path) != file.Sha256)
                {
                    Finish(result, TiaJobStatus.Failed, ErrorCodes.HashMismatch, "Source changed during import: " + file.RelativePath, result.SandboxState);
                    return;
                }
                result.SandboxState = SandboxStates.ModifiedNotSaved;
                try
                {
                    if (file.EffectiveFormat == BlockFormats.SimaticMl)
                    {
                        plc.ImportSimaticMl(file.BlockType, path);
                    }
                    else
                    {
                        plc.ImportExternalSource(ExternalSourceName(file), path);
                    }
                }
                catch (Exception ex)
                {
                    string code = OpennessErrorClassifier.Classify(ex, ErrorCodes.ImportFailed);
                    Finish(result, TiaJobStatus.Failed, code, "Import of " + file.RelativePath + " failed: " + ex.Message, StateAfterFailure(code, SandboxStates.ModifiedNotSaved));
                    return;
                }
                result.Messages.Add(new TiaMessage(MessageSeverity.Info, null,
                    (file.EffectiveFormat == BlockFormats.SimaticMl ? "Imported " : "Generated ") + file.BlockType + " " + file.BlockName + " from " + file.EffectiveFormat, file.RelativePath));
            }

            CompileReport compile;
            try
            {
                compile = plc.CompileOffline();
            }
            catch (Exception ex)
            {
                string code = OpennessErrorClassifier.Classify(ex, ErrorCodes.CompileError);
                Finish(result, TiaJobStatus.Failed, code, "Offline compile could not run (a password-protected safety program also prevents compiling): " + ex.Message, StateAfterFailure(code, SandboxStates.ModifiedNotSaved));
                return;
            }
            result.Compile = compile;
            if (compile == null || compile.State == "Error" || compile.ErrorCount > 0)
            {
                Finish(result, TiaJobStatus.Failed, ErrorCodes.CompileError, "Offline compile reported " + (compile?.ErrorCount ?? 0) + " error(s); project NOT saved", SandboxStates.ModifiedNotSaved);
                return;
            }

            try
            {
                session.Save();
            }
            catch (Exception ex)
            {
                Finish(result, TiaJobStatus.Failed, OpennessErrorClassifier.Classify(ex, ErrorCodes.InternalError), "Saving the sandbox project failed: " + ex.Message, SandboxStates.Unknown);
                return;
            }
            result.SavedProject = true;
            Finish(result, TiaJobStatus.Success, null, "Imported " + files.Count + " block(s), compiled offline (" + compile.WarningCount + " warning(s)) and saved", SandboxStates.Saved);
        }

        /// <summary>Rekurzív (alcsoportokat is tartalmazó) név-ütközésvizsgálat, kis/nagybetűtől függetlenül.</summary>
        public static List<ImportPlanItem> BuildPlan(IEnumerable<ManifestFile> files, IReadOnlyList<ExistingObject> existingBlocks, IReadOnlyList<ExistingObject> existingSources)
        {
            Dictionary<string, ExistingObject> blocks = new Dictionary<string, ExistingObject>(StringComparer.OrdinalIgnoreCase);
            foreach (ExistingObject block in existingBlocks)
            {
                if (!blocks.ContainsKey(block.Name))
                {
                    blocks[block.Name] = block;
                }
            }
            Dictionary<string, ExistingObject> sources = new Dictionary<string, ExistingObject>(StringComparer.OrdinalIgnoreCase);
            foreach (ExistingObject source in existingSources)
            {
                if (!sources.ContainsKey(source.Name))
                {
                    sources[source.Name] = source;
                }
            }
            List<ImportPlanItem> plan = new List<ImportPlanItem>();
            foreach (ManifestFile file in files.OrderBy(f => f.ImportOrder))
            {
                ExistingObject hit;
                string location = null;
                if (blocks.TryGetValue(file.BlockName, out hit))
                {
                    location = hit.Location + "/" + hit.Name;
                }
                else if (file.EffectiveFormat == BlockFormats.ExternalSource && sources.TryGetValue(ExternalSourceName(file), out hit))
                {
                    location = "External source files/" + hit.Name;
                }
                plan.Add(new ImportPlanItem
                {
                    ImportOrder = file.ImportOrder,
                    BlockName = file.BlockName,
                    BlockType = file.BlockType,
                    RelativePath = file.RelativePath,
                    Action = location == null ? ImportPlanItem.ActionCreate : ImportPlanItem.ActionCollision,
                    ExistingLocation = location,
                });
            }
            return plan;
        }

        /// <summary>A külső forrás neve a projektben = a forrásfájl neve (pl. FB_Cylinder.scl, DB_Data.db).</summary>
        public static string ExternalSourceName(ManifestFile file)
        {
            return Path.GetFileName(file.RelativePath);
        }

        /// <summary>
        /// ListBlocks / ExportBlocks: csak olvasó műveletek egy sandbox-projekten. A projektet
        /// nem menti; az export a projektbe semmit nem ír (sandbox_state = Unchanged).
        /// </summary>
        private void RunReadOnlyJob(TiaCommand command, TiaResult result)
        {
            bool export = command.Operation == TiaOperations.ExportBlocks;
            OpennessProblem problem = backend.ProbeEnvironment();
            if (problem != null)
            {
                Finish(result, TiaJobStatus.Blocked, problem.ErrorCode, problem.Message, SandboxStates.NotTouched);
                return;
            }
            string missing = new[]
            {
                Tuple.Create("project_path", command.ProjectPath),
                Tuple.Create("sandbox_root", command.SandboxRoot),
                Tuple.Create("plc_name", command.PlcName),
                Tuple.Create("export_directory", export ? command.ExportDirectory : "-"),
            }.Where(t => string.IsNullOrWhiteSpace(t.Item2)).Select(t => t.Item1).FirstOrDefault();
            if (missing != null)
            {
                Finish(result, TiaJobStatus.Blocked, ErrorCodes.CommandInvalid, "Required field '" + missing + "' is missing", SandboxStates.NotTouched);
                return;
            }
            Tuple<string, string> sandboxProblem = SandboxRules.Check(command.ProjectPath, workerVersion, command.SandboxRoot);
            if (sandboxProblem != null)
            {
                Finish(result, TiaJobStatus.Blocked, sandboxProblem.Item1, sandboxProblem.Item2, SandboxStates.NotTouched);
                return;
            }
            if (export)
            {
                string exportProblem = ExportRequestProblem(command);
                if (exportProblem != null)
                {
                    Finish(result, TiaJobStatus.Blocked, ErrorCodes.CommandInvalid, exportProblem, SandboxStates.NotTouched);
                    return;
                }
            }

            IOpennessSession session;
            try
            {
                session = backend.OpenProject(Path.GetFullPath(command.ProjectPath));
            }
            catch (Exception ex)
            {
                Finish(result, TiaJobStatus.Blocked, OpennessErrorClassifier.Classify(ex, ErrorCodes.ProjectOpenFailed), "Project cannot be opened without upgrade (it must not be open in another TIA Portal instance and must be writable): " + ex.Message, SandboxStates.NotTouched);
                return;
            }
            try
            {
                result.SandboxState = SandboxStates.Unchanged;
                IPlcTarget plc = session.FindPlc(command.PlcName);
                if (plc == null)
                {
                    Finish(result, TiaJobStatus.Blocked, ErrorCodes.PlcNotFound, "PLC '" + command.PlcName + "' not found in the project", SandboxStates.Unchanged);
                    return;
                }
                if (export && !plc.IsOffline())
                {
                    // V18 kézikönyv 5.11.3.18 / 6.4.1.13: forrásgenerálás és export csak offline PLC-vel.
                    Finish(result, TiaJobStatus.Blocked, ErrorCodes.PlcOnline, "PLC '" + plc.Name + "' is online; export is offline-only", SandboxStates.Unchanged);
                    return;
                }
                List<ProjectBlockInfo> blocks = plc.DescribeBlocks().ToList();
                foreach (ProjectBlockInfo block in blocks)
                {
                    block.ExportableFormats = LibraryRules.ExportableFormats(block);
                    block.NotExportableReason = block.ExportableFormats.Count == 0 ? LibraryRules.ExportProblem(block, BlockFormats.SimaticMl) : null;
                }
                if (!export)
                {
                    result.Blocks = blocks;
                    Finish(result, TiaJobStatus.Success, null, blocks.Count + " block(s)/type(s) listed; project not modified", SandboxStates.Unchanged);
                    return;
                }
                ExportSelected(command, result, plc, blocks);
            }
            finally
            {
                try
                {
                    session.Dispose();
                }
                catch (Exception ex)
                {
                    result.Messages.Add(new TiaMessage(MessageSeverity.Warning, null, "Closing the project/TIA Portal instance failed: " + ex.Message));
                }
            }
        }

        private static string ExportRequestProblem(TiaCommand command)
        {
            if (command.ExportBlocks == null || command.ExportBlocks.Count == 0)
            {
                return "export_blocks must list at least one block";
            }
            if (command.ExportBlocks.Any(b => b == null || string.IsNullOrEmpty(b.BlockName) || !BlockFormats.All.Contains(b.Format)))
            {
                return "export_blocks contains an empty block name or an unknown format";
            }
            if (command.ExportBlocks.Select(b => b.BlockName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != command.ExportBlocks.Count)
            {
                return "export_blocks lists a block more than once";
            }
            if (!Path.IsPathRooted(command.ExportDirectory) || !Directory.Exists(command.ExportDirectory))
            {
                return "export_directory must be an existing absolute directory";
            }
            if (Directory.EnumerateFileSystemEntries(command.ExportDirectory).Any())
            {
                return "export_directory must be empty";
            }
            return null;
        }

        private void ExportSelected(TiaCommand command, TiaResult result, IPlcTarget plc, List<ProjectBlockInfo> blocks)
        {
            List<Tuple<ExportRequestItem, ProjectBlockInfo>> selected = new List<Tuple<ExportRequestItem, ProjectBlockInfo>>();
            foreach (ExportRequestItem request in command.ExportBlocks)
            {
                ProjectBlockInfo block = blocks.FirstOrDefault(b => string.Equals(b.Name, request.BlockName, StringComparison.Ordinal))
                    ?? blocks.SingleOrDefault(b => string.Equals(b.Name, request.BlockName, StringComparison.OrdinalIgnoreCase));
                if (block == null)
                {
                    Finish(result, TiaJobStatus.Blocked, ErrorCodes.BlockNotFound, "Block '" + request.BlockName + "' not found in PLC '" + plc.Name + "'", SandboxStates.Unchanged);
                    return;
                }
                string problem = LibraryRules.ExportProblem(block, request.Format);
                if (problem != null)
                {
                    Finish(result, TiaJobStatus.Blocked, ErrorCodes.LibraryItemNotAllowed, block.Name + ": " + problem, SandboxStates.Unchanged);
                    return;
                }
                selected.Add(Tuple.Create(request, block));
            }

            result.ExportedFiles = new List<ExportedBlockFile>();
            foreach (Tuple<ExportRequestItem, ProjectBlockInfo> pair in selected)
            {
                if (Volatile.Read(ref cancelRequested) == 1)
                {
                    Finish(result, TiaJobStatus.Cancelled, ErrorCodes.Cancelled, "Export cancelled; no library entry may be created from a partial export", SandboxStates.Unchanged);
                    return;
                }
                ProjectBlockInfo block = pair.Item2;
                string format = pair.Item1.Format;
                string fileName = block.Name + LibraryRules.FileExtension(block.BlockType, format);
                string path = Path.Combine(command.ExportDirectory, fileName);
                try
                {
                    plc.ExportBlock(block, format, path);
                }
                catch (Exception ex)
                {
                    string code = OpennessErrorClassifier.Classify(ex, ErrorCodes.ExportFailed);
                    Finish(result, TiaJobStatus.Failed, code, "Export of " + block.Name + " failed: " + ex.Message, StateAfterFailure(code, SandboxStates.Unchanged));
                    return;
                }
                if (!File.Exists(path))
                {
                    Finish(result, TiaJobStatus.Failed, ErrorCodes.ExportFailed, "Export of " + block.Name + " produced no file", SandboxStates.Unchanged);
                    return;
                }
                byte[] content = File.ReadAllBytes(path);
                if (format == BlockFormats.SimaticMl && LibraryRules.SimaticMlContainsSafety(content))
                {
                    Finish(result, TiaJobStatus.Failed, ErrorCodes.LibraryItemNotAllowed, block.Name + " contains safety (F-) content", SandboxStates.Unchanged);
                    return;
                }
                if (format == BlockFormats.ExternalSource && !LibraryRules.IsAsciiExternalSource(content))
                {
                    Finish(result, TiaJobStatus.Failed, ErrorCodes.LibraryItemNotAllowed,
                        block.Name + " source contains non-ASCII characters (e.g. accented comments) and could not be re-imported as external source; export it as SimaticML", SandboxStates.Unchanged);
                    return;
                }
                result.ExportedFiles.Add(new ExportedBlockFile
                {
                    BlockName = block.Name,
                    BlockType = block.BlockType,
                    DbKind = block.DbKind,
                    ProgrammingLanguage = block.ProgrammingLanguage,
                    Format = format,
                    RelativePath = fileName,
                    Sha256 = Sha256.OfBytes(content),
                    ByteLength = content.Length,
                });
            }
            result.Blocks = selected.Select(p => p.Item2).ToList();
            Finish(result, TiaJobStatus.Success, null, "Exported " + selected.Count + " block(s); project not modified", SandboxStates.Unchanged);
        }

        /// <summary>Ha a TIA Portal leállt, semmit nem állíthatunk a sandboxról.</summary>
        private static string StateAfterFailure(string errorCode, string otherwise)
        {
            return errorCode == ErrorCodes.TiaPortalTerminated ? SandboxStates.Unknown : otherwise;
        }

        private static string SourcePath(TiaCommand command, string relativePath)
        {
            return Path.GetFullPath(Path.Combine(command.SourcesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private void Finish(TiaResult result, string status, string errorCode, string message, string sandboxState)
        {
            result.Status = status;
            result.ErrorCode = errorCode;
            result.SandboxState = sandboxState;
            string severity = status == TiaJobStatus.Success || status == TiaJobStatus.ValidationOnly ? MessageSeverity.Info : MessageSeverity.Error;
            result.Messages.Add(new TiaMessage(severity, errorCode, message));
            log.WriteLine(status + ": " + message);
        }

        private static string Now()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }
    }
}
