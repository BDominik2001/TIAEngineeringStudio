using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaStudio.Application.Ports;
using TiaStudio.Application.Workflow;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;
using TiaStudio.Domain.Model;
using TiaStudio.Domain.Workflow;
using TiaStudio.Infrastructure.Json;
using TiaStudio.Infrastructure.Platform;
using TiaStudio.Infrastructure.Sandbox;
using Xunit;

namespace TiaStudio.UnitTests
{
    internal sealed class RecordingAudit : IAuditLog
    {
        public List<AuditEvent> Events { get; } = new List<AuditEvent>();

        public void Write(AuditEvent auditEvent)
        {
            Events.Add(auditEvent);
        }
    }

    internal sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    }

    internal sealed class TestUser : IUserContext
    {
        public string UserName => "DOMAIN\\engineer";
    }

    /// <summary>A Rust validátort a C# validátorral helyettesíti (azonos szabálykészlet).</summary>
    internal sealed class MirrorStaticValidator : IStaticValidator
    {
        public ValidationReport Validate(byte[] json)
        {
            ValidationReport report = new MachineProjectJsonValidator().Validate(json);
            report.Validator = "mirror";
            return report;
        }
    }

    /// <summary>A Python-generátort helyettesítő hamis generátor: csomagot ír a modell hash-ével.</summary>
    internal sealed class FakeGenerator : ISourceGenerator
    {
        public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
        {
            byte[] model = File.ReadAllBytes(request.InputPath);
            MachineProject project = new MachineProjectSerializer().Deserialize(model);
            string manifestPath = Fixtures.WritePackage(request.OutputDirectory, project.Target.TiaVersion, project.Target.PlcName);
            GenerationManifest manifest = ProtocolJson.DeserializeLenient<GenerationManifest>(File.ReadAllText(manifestPath));
            manifest.SourceConfigSha256 = Sha256.OfBytes(model);
            File.WriteAllText(manifestPath, ProtocolJson.Serialize(manifest));
            return Task.FromResult(new GenerationResult
            {
                Success = true,
                OutputDirectory = request.OutputDirectory,
                ManifestPath = manifestPath,
                ManifestSha256 = Sha256.OfFile(manifestPath),
            });
        }
    }

    internal sealed class FakeWorkerClient : ITiaWorkerClient
    {
        public string InstallStatusValue { get; set; } = InstallStatus.NotTested;

        public Func<TiaCommand, TiaResult> OnImport { get; set; }

        public List<TiaCommand> Commands { get; } = new List<TiaCommand>();

        public IReadOnlyList<TiaInstallInfo> DiscoverInstalledVersions()
        {
            return TiaVersions.Supported.Select(v => new TiaInstallInfo { TiaVersion = v, Status = InstallStatusValue, ErrorCode = InstallStatus.IsUsable(InstallStatusValue) ? null : ErrorCodes.TiaNotInstalled }).ToList();
        }

        public Task<TiaResult> GetCapabilitiesAsync(string tiaVersion, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TiaResult> ValidateJobAsync(TiaJobRequest request, CancellationToken cancellationToken)
        {
            Commands.Add(request.Command);
            Assert.True(request.Command.DryRun);
            Assert.Null(request.Command.Approval);
            return Task.FromResult(Result(request.Command, TiaJobStatus.ValidationOnly, null, SandboxStates.Unchanged, false));
        }

        public Task<TiaResult> ImportAndCompileAsync(TiaJobRequest request, CancellationToken cancellationToken)
        {
            Commands.Add(request.Command);
            TiaResult result = OnImport != null
                ? OnImport(request.Command)
                : Result(request.Command, TiaJobStatus.Success, null, SandboxStates.Saved, true);
            return Task.FromResult(result);
        }

        public static TiaResult Result(TiaCommand command, string status, string code, string sandboxState, bool saved)
        {
            return new TiaResult
            {
                JobId = command.JobId,
                RequestedVersion = command.TiaVersion,
                ActualVersion = command.TiaVersion,
                Operation = command.Operation,
                Status = status,
                ErrorCode = code,
                SandboxState = sandboxState,
                SavedProject = saved,
                ImportPlan = new List<ImportPlanItem>(),
            };
        }
    }

    public sealed class EngineeringWorkflowTests : IDisposable
    {
        private readonly TempDir temp = new TempDir();
        private readonly RecordingAudit audit = new RecordingAudit();
        private readonly FakeWorkerClient worker = new FakeWorkerClient();
        private readonly SandboxService sandbox;
        private readonly EngineeringWorkflow workflow;

        public EngineeringWorkflowTests()
        {
            sandbox = new SandboxService(temp.Combine("sandbox"), new FixedClock(), new TestUser());
            workflow = new EngineeringWorkflow(
                new MachineProjectSerializer(),
                new MachineProjectJsonValidator(),
                new MirrorStaticValidator(),
                new FakeGenerator(),
                worker,
                sandbox,
                audit,
                new FixedClock(),
                new TestUser(),
                new FileWorkspace(temp.Combine("generated")));
        }

        public void Dispose()
        {
            temp.Dispose();
        }

        private static MachineProject Example(string version = "V18")
        {
            return new MachineProjectSerializer().Deserialize(File.ReadAllBytes(Repo.PathOf("examples", "machine-" + version.ToLowerInvariant() + ".json")));
        }

        private async Task<string> AdvanceToReviewed(string version = "V18")
        {
            workflow.SetProject(Example(version));
            Assert.True(workflow.Validate().Ok);
            Assert.True(workflow.Check().Ok);
            StepResult generated = await workflow.GenerateAsync(CancellationToken.None);
            Assert.True(generated.Ok, generated.ToString());
            Assert.True(workflow.MarkReviewed().Ok);
            return Fixtures.WriteSandboxProject(sandbox.SandboxRoot, version);
        }

        [Theory]
        [InlineData("V18")]
        [InlineData("V19")]
        [InlineData("V20")]
        public async Task HappyPathReachesSaved(string version)
        {
            string project = await AdvanceToReviewed(version);
            Assert.True(workflow.Approve(project).Ok);
            Assert.Equal(WorkflowState.Approved, workflow.State);
            Assert.True((await workflow.PrepareImportAsync(CancellationToken.None)).Ok);
            Assert.Equal(WorkflowState.ReadyToImport, workflow.State);
            StepResult imported = await workflow.ImportAndCompileAsync(CancellationToken.None);
            Assert.True(imported.Ok, imported.ToString());
            Assert.Equal(WorkflowState.Saved, workflow.State);

            TiaCommand import = worker.Commands.Last();
            Assert.Equal(version, import.TiaVersion);
            Assert.False(import.DryRun);
            Assert.True(import.ApprovedByUser);
            Assert.Equal(ApprovalDigest.Compute(import, workflow.Package.ManifestSha256, workflow.Package.Manifest.Files), import.Approval.ApprovalDigest);
            Assert.NotEqual(worker.Commands[0].JobId, import.JobId);

            string[] states = audit.Events.Select(e => e.State).ToArray();
            Assert.Equal(new[] { "DRAFT", "VALIDATED", "CHECKED", "GENERATED", "REVIEWED", "APPROVED", "READY_TO_IMPORT", "IMPORTED", "COMPILED", "SAVED" }, states);
        }

        [Fact]
        public async Task StepsCannotBeSkipped()
        {
            workflow.SetProject(Example());
            StepResult skipped = await workflow.GenerateAsync(CancellationToken.None);
            Assert.Equal(ErrorCodes.WorkflowStateInvalid, skipped.ErrorCode);
            Assert.Equal(WorkflowState.Draft, workflow.State);
            Assert.Equal(ErrorCodes.WorkflowStateInvalid, workflow.Approve("x.ap18").ErrorCode);
        }

        [Fact]
        public void InvalidModelFails()
        {
            MachineProject project = Example();
            ((CylinderDevice)project.Devices[1]).RetractedInput = "I0.0";
            workflow.SetProject(project);
            StepResult result = workflow.Validate();
            Assert.Equal(ErrorCodes.DuplicateIoAddress, result.ErrorCode);
            Assert.Equal(WorkflowState.Failed, workflow.State);
            Assert.Equal(ErrorCodes.WorkflowStateInvalid, workflow.Check().ErrorCode);
            workflow.Reset();
            Assert.Equal(WorkflowState.Draft, workflow.State);
        }

        [Fact]
        public async Task ModelChangeInvalidatesApproval()
        {
            string project = await AdvanceToReviewed();
            Assert.True(workflow.Approve(project).Ok);
            workflow.SetProject(Example());
            Assert.Equal(WorkflowState.Draft, workflow.State);
            Assert.Null(workflow.ApprovedCommand);
            Assert.Null(workflow.Package);
        }

        [Fact]
        public async Task SourceChangedAfterApprovalFails()
        {
            string project = await AdvanceToReviewed();
            Assert.True(workflow.Approve(project).Ok);
            File.AppendAllText(Path.Combine(workflow.Generation.OutputDirectory, "sources", "FC_Cylinders.scl"), "// edit\r\n");
            StepResult result = await workflow.PrepareImportAsync(CancellationToken.None);
            Assert.Equal(ErrorCodes.HashMismatch, result.ErrorCode);
            Assert.Equal(WorkflowState.Failed, workflow.State);
            Assert.Empty(worker.Commands);
        }

        [Fact]
        public async Task WrongProjectVersionIsRejectedWithoutLosingReview()
        {
            await AdvanceToReviewed("V18");
            string ap20 = Fixtures.WriteSandboxProject(sandbox.SandboxRoot, "V20");
            StepResult result = workflow.Approve(ap20);
            Assert.Equal(ErrorCodes.ProjectVersionMismatch, result.ErrorCode);
            Assert.Equal(WorkflowState.Reviewed, workflow.State);
        }

        [Fact]
        public async Task MissingTiaStopsWithoutGuessing()
        {
            string project = await AdvanceToReviewed();
            Assert.True(workflow.Approve(project).Ok);
            worker.InstallStatusValue = InstallStatus.NotFound;
            StepResult result = await workflow.PrepareImportAsync(CancellationToken.None);
            Assert.Equal(ErrorCodes.TiaNotInstalled, result.ErrorCode);
            Assert.Equal(WorkflowState.Approved, workflow.State);
            Assert.Empty(worker.Commands);
        }

        [Fact]
        public async Task CompileErrorEndsFailedAfterImported()
        {
            string project = await AdvanceToReviewed();
            Assert.True(workflow.Approve(project).Ok);
            Assert.True((await workflow.PrepareImportAsync(CancellationToken.None)).Ok);
            worker.OnImport = c => FakeWorkerClient.Result(c, TiaJobStatus.Failed, ErrorCodes.CompileError, SandboxStates.ModifiedNotSaved, false);
            StepResult result = await workflow.ImportAndCompileAsync(CancellationToken.None);
            Assert.Equal(ErrorCodes.CompileError, result.ErrorCode);
            Assert.Equal(WorkflowState.Failed, workflow.State);
            Assert.Contains(audit.Events, e => e.State == "IMPORTED");
            Assert.DoesNotContain(audit.Events, e => e.State == "SAVED");
            Assert.Contains("Recreate the sandbox", result.Message);
        }

        [Fact]
        public async Task PartialSuccessIsNotSuccess()
        {
            string project = await AdvanceToReviewed();
            Assert.True(workflow.Approve(project).Ok);
            Assert.True((await workflow.PrepareImportAsync(CancellationToken.None)).Ok);
            worker.OnImport = c => FakeWorkerClient.Result(c, TiaJobStatus.Success, null, SandboxStates.Unknown, false);
            Assert.False((await workflow.ImportAndCompileAsync(CancellationToken.None)).Ok);
            Assert.Equal(WorkflowState.Failed, workflow.State);
        }
    }
}
