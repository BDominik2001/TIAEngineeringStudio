using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;
using TiaStudio.TiaAdapter.Common;
using Xunit;

namespace TiaStudio.UnitTests
{
    /// <summary>Hamis Openness backend: rögzíti a hívásokat, hibákat injektálhat.</summary>
    internal sealed class FakeBackend : IOpennessBackend
    {
        public OpennessProblem Problem { get; set; }

        public Exception OpenFailure { get; set; }

        public FakeSession Session { get; } = new FakeSession();

        public int OpenCount { get; private set; }

        public WorkerInfo Describe()
        {
            return new WorkerInfo { Name = "fake", Version = "0", OpennessLinked = true };
        }

        public OpennessProblem ProbeEnvironment()
        {
            return Problem;
        }

        public TiaCapabilities GetCapabilities()
        {
            return new TiaCapabilities { TiaVersion = "V18", Operations = TiaOperations.All.ToList() };
        }

        public IOpennessSession OpenProject(string projectPath)
        {
            OpenCount++;
            if (OpenFailure != null)
            {
                throw OpenFailure;
            }
            return Session;
        }
    }

    internal sealed class FakeSession : IOpennessSession
    {
        public FakePlc Plc { get; } = new FakePlc();

        public bool Saved { get; private set; }

        public bool Disposed { get; private set; }

        public IPlcTarget FindPlc(string plcName)
        {
            return plcName == Plc.Name ? Plc : null;
        }

        public void Save()
        {
            Saved = true;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    internal sealed class FakePlc : IPlcTarget
    {
        public string Name { get; set; } = "PLC_1";

        public bool Offline { get; set; } = true;

        public List<ExistingObject> Blocks { get; } = new List<ExistingObject> { new ExistingObject("Main", "Program blocks") };

        public List<ExistingObject> Sources { get; } = new List<ExistingObject>();

        public List<string> Imported { get; } = new List<string>();

        public CompileReport Compile { get; set; } = new CompileReport { State = "Warning", ErrorCount = 0, WarningCount = 2 };

        public string FailImportOf { get; set; }

        public bool IsOffline()
        {
            return Offline;
        }

        public IReadOnlyList<ExistingObject> GetAllBlocksAndTypes()
        {
            return Blocks;
        }

        public IReadOnlyList<ExistingObject> GetExternalSources()
        {
            return Sources;
        }

        public Exception FailImportWith { get; set; }

        public void ImportExternalSource(string externalSourceName, string filePath)
        {
            if (FailImportWith != null)
            {
                throw FailImportWith;
            }
            if (externalSourceName == FailImportOf)
            {
                throw new InvalidOperationException("Syntax error in source");
            }
            Assert.True(File.Exists(filePath));
            Imported.Add(externalSourceName);
        }

        public CompileReport CompileOffline()
        {
            return Compile;
        }

        public List<ProjectBlockInfo> Described { get; } = new List<ProjectBlockInfo>();

        public List<string> ImportedXml { get; } = new List<string>();

        public List<string> Exported { get; } = new List<string>();

        /// <summary>Az exportált fájl tartalma blokknév szerint (alapértelmezés: ASCII SCL).</summary>
        public Func<ProjectBlockInfo, string, byte[]> ExportContent { get; set; } =
            (block, format) => System.Text.Encoding.ASCII.GetBytes(format == BlockFormats.SimaticMl
                ? "<Document><ProgrammingLanguage>" + block.ProgrammingLanguage + "</ProgrammingLanguage></Document>"
                : "FUNCTION_BLOCK \"" + block.Name + "\"\r\nEND_FUNCTION_BLOCK\r\n");

        public IReadOnlyList<ProjectBlockInfo> DescribeBlocks()
        {
            return Described;
        }

        public void ExportBlock(ProjectBlockInfo block, string format, string filePath)
        {
            Exported.Add(block.Name + ":" + format);
            File.WriteAllBytes(filePath, ExportContent(block, format));
        }

        public void ImportSimaticMl(string blockType, string filePath)
        {
            Assert.True(File.Exists(filePath));
            ImportedXml.Add(Path.GetFileName(filePath));
        }
    }

    /// <summary>A spec 11.3 forgatókönyvei a worker oldalon, TIA nélkül.</summary>
    public sealed class WorkerHostTests : IDisposable
    {
        private readonly TempDir temp = new TempDir();
        private readonly FakeBackend backend = new FakeBackend();
        private readonly string sandboxRoot;
        private readonly string packageRoot;
        private readonly string manifestPath;
        private readonly string projectPath;

        public WorkerHostTests()
        {
            sandboxRoot = temp.Combine("sandbox");
            packageRoot = temp.Combine("package");
            manifestPath = Fixtures.WritePackage(packageRoot);
            projectPath = Fixtures.WriteSandboxProject(sandboxRoot);
        }

        public void Dispose()
        {
            temp.Dispose();
        }

        private TiaCommand ImportCommand(bool dryRun = false, bool approve = true)
        {
            TiaCommand command = new TiaCommand
            {
                JobId = Guid.NewGuid().ToString(),
                Operation = dryRun ? TiaOperations.ValidateJob : TiaOperations.ImportAndCompile,
                TiaVersion = "V18",
                ProjectPath = projectPath,
                SandboxRoot = sandboxRoot,
                PlcName = "PLC_1",
                SourcesDirectory = packageRoot,
                ManifestPath = manifestPath,
                DryRun = dryRun,
            };
            if (approve && !dryRun)
            {
                PackageVerificationResult package = PackageVerifier.Verify(packageRoot, manifestPath);
                command.ApprovedByUser = true;
                command.Approval = new ApprovalRecord
                {
                    ApprovedBy = "tester",
                    ApprovedAtUtc = "2026-10-28T00:00:00Z",
                    ApprovalDigest = ApprovalDigest.Compute(command, package.ManifestSha256, package.Manifest.Files),
                };
            }
            return command;
        }

        private TiaResult Run(TiaCommand command, string workerVersion = "V18")
        {
            string commandPath = temp.Combine(command.JobId + ".command.json");
            File.WriteAllText(commandPath, ProtocolJson.Serialize(command));
            string resultPath = WorkerHost.DefaultResultPath(commandPath);
            int exit = new WorkerHost(workerVersion, backend, TextWriter.Null).Run(new[] { "--command", commandPath });
            TiaResult result = ProtocolJson.DeserializeLenient<TiaResult>(File.ReadAllText(resultPath));
            Assert.Equal(WorkerHost.ExitCodeFor(result.Status), exit);
            Assert.Equal(command.JobId, result.JobId);
            Assert.Equal(workerVersion, result.ActualVersion);
            return result;
        }

        [Fact]
        public void SuccessfulImportCompilesAndSaves()
        {
            TiaResult result = Run(ImportCommand());
            Assert.Equal(TiaJobStatus.Success, result.Status);
            Assert.Null(result.ErrorCode);
            Assert.True(result.SavedProject);
            Assert.Equal(SandboxStates.Saved, result.SandboxState);
            Assert.Equal(new[] { "FB_Cylinder.scl", "DB_Cylinder_CYL_01.db", "FC_Cylinders.scl" }, backend.Session.Plc.Imported);
            Assert.True(backend.Session.Saved);
            Assert.True(backend.Session.Disposed);
            Assert.Equal(2, result.WarningCount);
        }

        [Fact]
        public void DryRunDoesNotModifyProject()
        {
            TiaResult result = Run(ImportCommand(dryRun: true));
            Assert.Equal(TiaJobStatus.ValidationOnly, result.Status);
            Assert.Equal(SandboxStates.Unchanged, result.SandboxState);
            Assert.Empty(backend.Session.Plc.Imported);
            Assert.False(backend.Session.Saved);
            Assert.All(result.ImportPlan, p => Assert.Equal(ImportPlanItem.ActionCreate, p.Action));
            Assert.Equal(3, result.ImportPlan.Count);
        }

        [Fact]
        public void ImportWithDryRunFlagAlsoStopsBeforeImport()
        {
            TiaCommand command = ImportCommand(approve: false);
            command.DryRun = true;
            TiaResult result = Run(command);
            Assert.Equal(TiaJobStatus.ValidationOnly, result.Status);
            Assert.Empty(backend.Session.Plc.Imported);
        }

        [Fact]
        public void ProjectExtensionMismatchIsRejected()
        {
            string ap20 = Fixtures.WriteSandboxProject(sandboxRoot, "V20");
            TiaCommand command = ImportCommand();
            command.ProjectPath = ap20;
            TiaResult result = Run(command);
            Assert.Equal(ErrorCodes.ProjectVersionMismatch, result.ErrorCode);
            Assert.Equal(0, backend.OpenCount);
        }

        [Fact]
        public void WrongWorkerVersionIsRejected()
        {
            TiaCommand command = ImportCommand();
            command.TiaVersion = "V19";
            TiaResult result = Run(command, "V18");
            Assert.Equal(TiaJobStatus.Blocked, result.Status);
            Assert.Equal(ErrorCodes.WorkerVersionMismatch, result.ErrorCode);
        }

        [Fact]
        public void MissingTiaInstallationStopsBeforeAnything()
        {
            backend.Problem = new OpennessProblem(ErrorCodes.TiaNotInstalled, "TIA Portal V18 not installed");
            TiaResult result = Run(ImportCommand());
            Assert.Equal(ErrorCodes.TiaNotInstalled, result.ErrorCode);
            Assert.Equal(SandboxStates.NotTouched, result.SandboxState);
            Assert.Equal(0, backend.OpenCount);
        }

        [Fact]
        public void LicenseErrorOnOpenIsClassified()
        {
            backend.OpenFailure = new InvalidOperationException("No valid license found for TIA Portal Openness");
            TiaResult result = Run(ImportCommand());
            Assert.Equal(ErrorCodes.LicenseMissing, result.ErrorCode);
            Assert.False(result.SavedProject);
        }

        [Fact]
        public void OpenFailureFallsBackToProjectOpenFailed()
        {
            backend.OpenFailure = new InvalidOperationException("The project is already opened by another instance");
            Assert.Equal(ErrorCodes.ProjectOpenFailed, Run(ImportCommand()).ErrorCode);
        }

        [Fact]
        public void TiaTerminationDuringImportMakesSandboxUnknown()
        {
            backend.Session.Plc.FailImportWith = new Siemens.Engineering.NonRecoverableException("TIA Portal has been closed");
            TiaResult result = Run(ImportCommand());
            Assert.Equal(ErrorCodes.TiaPortalTerminated, result.ErrorCode);
            Assert.Equal(SandboxStates.Unknown, result.SandboxState);
        }

        [Fact]
        public void LicenseNotFoundExceptionIsClassifiedByType()
        {
            backend.OpenFailure = new Siemens.Engineering.LicenseNotFoundException("STEP 7");
            Assert.Equal(ErrorCodes.LicenseMissing, Run(ImportCommand()).ErrorCode);
        }

        [Fact]
        public void UpgradeRequiredOnOpenIsClassified()
        {
            backend.OpenFailure = new InvalidOperationException("The project must be upgraded before it can be opened");
            Assert.Equal(ErrorCodes.ProjectUpgradeRequired, Run(ImportCommand()).ErrorCode);
        }

        [Fact]
        public void NestedGroupCollisionBlocksImport()
        {
            backend.Session.Plc.Blocks.Add(new ExistingObject("db_cylinder_cyl_01", "Program blocks/Station1/Deep"));
            TiaResult result = Run(ImportCommand());
            Assert.Equal(TiaJobStatus.Blocked, result.Status);
            Assert.Equal(ErrorCodes.BlockCollision, result.ErrorCode);
            Assert.Equal(SandboxStates.Unchanged, result.SandboxState);
            Assert.Empty(backend.Session.Plc.Imported);
            ImportPlanItem collision = result.ImportPlan.Single(p => p.Action == ImportPlanItem.ActionCollision);
            Assert.Equal("Program blocks/Station1/Deep/db_cylinder_cyl_01", collision.ExistingLocation);
        }

        [Fact]
        public void ExternalSourceNameCollisionBlocksImport()
        {
            backend.Session.Plc.Sources.Add(new ExistingObject("FB_Cylinder.scl", "External source files"));
            Assert.Equal(ErrorCodes.BlockCollision, Run(ImportCommand()).ErrorCode);
        }

        [Fact]
        public void SourceChangedAfterApprovalIsHashMismatch()
        {
            TiaCommand command = ImportCommand();
            File.AppendAllText(Path.Combine(packageRoot, "sources", "FC_Cylinders.scl"), "// changed\r\n");
            TiaResult result = Run(command);
            Assert.Equal(ErrorCodes.HashMismatch, result.ErrorCode);
            Assert.Equal(0, backend.OpenCount);
        }

        [Fact]
        public void ApprovedFlagWithoutValidDigestIsRejected()
        {
            TiaCommand command = ImportCommand(approve: false);
            command.ApprovedByUser = true;
            TiaResult result = Run(command);
            Assert.Equal(ErrorCodes.ApprovalInvalid, result.ErrorCode);

            TiaCommand forged = ImportCommand();
            forged.ProjectPath = Fixtures.WriteSandboxProject(Path.Combine(sandboxRoot, "other"));
            Assert.Equal(ErrorCodes.ApprovalInvalid, Run(forged).ErrorCode);
            Assert.Equal(0, backend.OpenCount);
        }

        [Fact]
        public void ProjectOutsideSandboxIsRejected()
        {
            TiaCommand command = ImportCommand();
            command.SandboxRoot = temp.Combine("elsewhere");
            Assert.Equal(ErrorCodes.ProjectNotSandbox, Run(command).ErrorCode);
        }

        [Fact]
        public void PackageForOtherVersionIsRejected()
        {
            string otherRoot = temp.Combine("pkg20");
            string otherManifest = Fixtures.WritePackage(otherRoot, "V20");
            TiaCommand command = ImportCommand(dryRun: true);
            command.SourcesDirectory = otherRoot;
            command.ManifestPath = otherManifest;
            Assert.Equal(ErrorCodes.ProjectVersionMismatch, Run(command).ErrorCode);
        }

        [Fact]
        public void CompileErrorFailsWithoutSaving()
        {
            backend.Session.Plc.Compile = new CompileReport { State = "Error", ErrorCount = 1, Messages = { new CompileMessage { Description = "Unknown tag" } } };
            TiaResult result = Run(ImportCommand());
            Assert.Equal(TiaJobStatus.Failed, result.Status);
            Assert.Equal(ErrorCodes.CompileError, result.ErrorCode);
            Assert.False(result.SavedProject);
            Assert.False(backend.Session.Saved);
            Assert.Equal(SandboxStates.ModifiedNotSaved, result.SandboxState);
            Assert.True(backend.Session.Disposed);
        }

        [Fact]
        public void ImportFailureStopsAndDoesNotSave()
        {
            backend.Session.Plc.FailImportOf = "DB_Cylinder_CYL_01.db";
            TiaResult result = Run(ImportCommand());
            Assert.Equal(ErrorCodes.ImportFailed, result.ErrorCode);
            Assert.Equal(new[] { "FB_Cylinder.scl" }, backend.Session.Plc.Imported);
            Assert.False(backend.Session.Saved);
            Assert.Equal(SandboxStates.ModifiedNotSaved, result.SandboxState);
        }

        [Fact]
        public void OnlinePlcIsRejected()
        {
            backend.Session.Plc.Offline = false;
            TiaResult result = Run(ImportCommand());
            Assert.Equal(ErrorCodes.PlcOnline, result.ErrorCode);
            Assert.Empty(backend.Session.Plc.Imported);
        }

        [Fact]
        public void UnknownPlcIsRejected()
        {
            backend.Session.Plc.Name = "PLC_9";
            Assert.Equal(ErrorCodes.PlcNotFound, Run(ImportCommand()).ErrorCode);
        }

        [Fact]
        public void UpgradeProjectIsUnsupported()
        {
            TiaCommand command = ImportCommand();
            command.Operation = TiaOperations.UpgradeProject;
            TiaResult result = Run(command);
            Assert.Equal(TiaJobStatus.Unsupported, result.Status);
            Assert.Equal(0, backend.OpenCount);
        }

        [Fact]
        public void DownloadOperationDoesNotExist()
        {
            TiaCommand command = ImportCommand();
            command.Operation = "DownloadToPlc";
            TiaResult result = Run(command);
            Assert.Equal(TiaJobStatus.Unsupported, result.Status);
            Assert.Equal(ErrorCodes.OperationNotSupported, result.ErrorCode);
            Assert.Equal(0, backend.OpenCount);
        }

        [Fact]
        public void UnknownCommandFieldIsRejected()
        {
            string commandPath = temp.Combine("bad.command.json");
            File.WriteAllText(commandPath, "{\"protocol_version\":\"1.0\",\"job_id\":\"b9f4a91e-8d8e-4f55-9f99-27af49000528\",\"operation\":\"GetCapabilities\",\"tia_version\":\"V18\",\"download\":true}");
            Assert.Equal(WorkerExitCodes.Blocked, new WorkerHost("V18", backend, TextWriter.Null).Run(new[] { "--command", commandPath }));
            TiaResult result = ProtocolJson.DeserializeLenient<TiaResult>(File.ReadAllText(WorkerHost.DefaultResultPath(commandPath)));
            Assert.Equal(ErrorCodes.CommandInvalid, result.ErrorCode);
        }

        [Fact]
        public void UnlinkedWorkerRejectsEverything()
        {
            UnlinkedOpennessBackend unlinked = new UnlinkedOpennessBackend("V18");
            TiaCommand command = ImportCommand();
            string commandPath = temp.Combine("u.command.json");
            File.WriteAllText(commandPath, ProtocolJson.Serialize(command));
            TiaResult result = new WorkerHost("V18", unlinked, TextWriter.Null).Execute(commandPath);
            Assert.Equal(ErrorCodes.WorkerBuiltWithoutOpenness, result.ErrorCode);
            Assert.False(unlinked.GetCapabilities().Features[CapabilityFeatures.PlcDownload]);
        }

        [Fact]
        public void UsageErrorWithoutCommand()
        {
            Assert.Equal(WorkerExitCodes.Usage, new WorkerHost("V18", backend, TextWriter.Null).Run(new string[0]));
        }
    }
}

namespace Siemens.Engineering
{
    /// <summary>Tesztcélú, azonos nevű kivételtípusok (a besorolás típusnév alapján történik).</summary>
    internal sealed class NonRecoverableException : System.Exception
    {
        public NonRecoverableException(string message)
            : base(message)
        {
        }
    }

    internal sealed class LicenseNotFoundException : System.Exception
    {
        public LicenseNotFoundException(string message)
            : base(message)
        {
        }
    }
}
