using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TiaStudio.Application.Ports;
using TiaStudio.Application.Workflow;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;
using TiaStudio.Domain.Workflow;
using TiaStudio.Infrastructure.Generator;
using TiaStudio.Infrastructure.Json;
using TiaStudio.Infrastructure.Native;
using TiaStudio.Infrastructure.Platform;
using TiaStudio.Infrastructure.Sandbox;
using Xunit;

namespace TiaStudio.IntegrationTests
{
    public class RustValidatorIntegrationTests
    {
        [SkippableFact]
        public void SharedCasesThroughPInvoke()
        {
            Skip.IfNot(Env.RustAvailable(), "Rust validator library not built (cargo build --release in src/rust/validator)");
            JObject file = JObject.Parse(File.ReadAllText(Env.PathOf("tests", "contracts", "validation-cases.json")));
            RustStaticValidator rust = new RustStaticValidator();
            MachineProjectJsonValidator csharp = new MachineProjectJsonValidator();
            foreach (JToken testCase in file["cases"])
            {
                byte[] input = testCase["input_raw"] != null
                    ? Encoding.UTF8.GetBytes((string)testCase["input_raw"])
                    : Encoding.UTF8.GetBytes(testCase["input"].ToString(Newtonsoft.Json.Formatting.None));
                ValidationReport report = rust.Validate(input);
                string expected = string.Join("|", testCase["expected"].Select(e => (string)e["code"] + "@" + (string)e["path"]));
                Assert.Equal(expected, string.Join("|", report.Issues.Select(i => i.Code + "@" + i.Path)));
                Assert.Equal("tes_validator", report.Validator);
                Assert.Equal(
                    string.Join("|", csharp.Validate(input).Issues.Select(i => i.Code + "@" + i.Path)),
                    string.Join("|", report.Issues.Select(i => i.Code + "@" + i.Path)));
            }
        }
    }

    public sealed class PythonGeneratorIntegrationTests : IDisposable
    {
        private readonly string temp = Path.Combine(Path.GetTempPath(), "tes-it-" + Guid.NewGuid().ToString("N"));

        public PythonGeneratorIntegrationTests()
        {
            Directory.CreateDirectory(temp);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(temp, true);
            }
            catch (IOException)
            {
            }
        }

        [SkippableTheory]
        [InlineData("V18")]
        [InlineData("V19")]
        [InlineData("V20")]
        public async Task GeneratedPackageVerifiesInCSharp(string version)
        {
            Skip.If(Env.Python == null, "Python not found (set TES_PYTHON)");
            PythonSourceGenerator generator = new PythonSourceGenerator(Env.Python, Env.PathOf("src", "python"), TimeSpan.FromMinutes(2));
            GenerationResult result = await generator.GenerateAsync(
                new GenerationRequest { InputPath = Env.PathOf("examples", "machine-" + version.ToLowerInvariant() + ".json"), OutputDirectory = Path.Combine(temp, "pkg"), RunId = "it" },
                CancellationToken.None);
            Assert.True(result.Success, result.ErrorCode + " " + result.Message + " " + result.Diagnostics);

            PackageVerificationResult package = PackageVerifier.Verify(result.OutputDirectory, result.ManifestPath);
            Assert.True(package.Ok, string.Join(", ", package.Problems));
            Assert.Equal(result.ManifestSha256, package.ManifestSha256);
            Assert.Equal(version, package.Manifest.TargetTiaVersion);
            Assert.Equal(Sha256.OfFile(Env.PathOf("examples", "machine-" + version.ToLowerInvariant() + ".json")), package.Manifest.SourceConfigSha256);
            foreach (ManifestFile file in package.Manifest.Files)
            {
                byte[] golden = File.ReadAllBytes(Env.PathOf("tests", "regression", "golden-scl", version, Path.GetFileName(file.RelativePath)));
                Assert.Equal(Sha256.OfBytes(golden), file.Sha256);
            }
        }

        [SkippableFact]
        public async Task OutputDirectoryIsNeverOverwritten()
        {
            Skip.If(Env.Python == null, "Python not found (set TES_PYTHON)");
            string output = Path.Combine(temp, "existing");
            Directory.CreateDirectory(output);
            GenerationResult result = await new PythonSourceGenerator(Env.Python, Env.PathOf("src", "python"), TimeSpan.FromMinutes(2)).GenerateAsync(
                new GenerationRequest { InputPath = Env.PathOf("examples", "machine-v18.json"), OutputDirectory = output }, CancellationToken.None);
            Assert.False(result.Success);
            Assert.Equal(ErrorCodes.OutputExists, result.ErrorCode);
        }

        [SkippableFact]
        public async Task FullWorkflowWithRealValidatorsAndGenerator()
        {
            Skip.If(Env.Python == null, "Python not found (set TES_PYTHON)");
            Skip.IfNot(Env.RustAvailable(), "Rust validator library not built");
            SystemClock clock = new SystemClock();
            EnvironmentUserContext user = new EnvironmentUserContext();
            SandboxService sandbox = new SandboxService(Path.Combine(temp, "sandbox"), clock, user);
            EchoWorker worker = new EchoWorker();
            EngineeringWorkflow workflow = new EngineeringWorkflow(
                new MachineProjectSerializer(),
                new MachineProjectJsonValidator(),
                new RustStaticValidator(),
                new PythonSourceGenerator(Env.Python, Env.PathOf("src", "python"), TimeSpan.FromMinutes(2)),
                worker,
                sandbox,
                new NullAudit(),
                clock,
                user,
                new FileWorkspace(Path.Combine(temp, "generated")));

            workflow.SetProject(new MachineProjectSerializer().Deserialize(File.ReadAllBytes(Env.PathOf("examples", "machine-v20.json"))));
            Assert.True(workflow.Validate().Ok);
            StepResult check = workflow.Check();
            Assert.True(check.Ok, check.ToString());
            StepResult generate = await workflow.GenerateAsync(CancellationToken.None);
            Assert.True(generate.Ok, generate + " " + string.Join(" ", generate.Details));
            Assert.True(workflow.MarkReviewed().Ok);

            string knownGood = Path.Combine(temp, "known-good", "Demo");
            Directory.CreateDirectory(knownGood);
            File.WriteAllText(Path.Combine(knownGood, "Demo.ap20"), "fake");
            SandboxCopy copy = sandbox.CreateSandboxCopy(Path.Combine(knownGood, "Demo.ap20"), "V20");
            Assert.True(workflow.Approve(copy.ProjectPath).Ok);
            Assert.True((await workflow.PrepareImportAsync(CancellationToken.None)).Ok);
            Assert.True((await workflow.ImportAndCompileAsync(CancellationToken.None)).Ok);
            Assert.Equal(WorkflowState.Saved, workflow.State);
            Assert.Equal(4, worker.LastPlanSize);
        }

        private sealed class NullAudit : IAuditLog
        {
            public void Write(AuditEvent auditEvent)
            {
            }
        }

        /// <summary>Openness nélküli worker-helyettesítő: a valódi csomagot és jóváhagyást ellenőrzi.</summary>
        private sealed class EchoWorker : ITiaWorkerClient
        {
            public int LastPlanSize { get; private set; }

            public IReadOnlyList<TiaInstallInfo> DiscoverInstalledVersions()
            {
                return TiaVersions.Supported.Select(v => new TiaInstallInfo { TiaVersion = v, Status = InstallStatus.NotTested }).ToList();
            }

            public Task<TiaResult> GetCapabilitiesAsync(string tiaVersion, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<TiaResult> ValidateJobAsync(TiaJobRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(Check(request.Command, TiaJobStatus.ValidationOnly, SandboxStates.Unchanged));
            }

            public Task<TiaResult> ImportAndCompileAsync(TiaJobRequest request, CancellationToken cancellationToken)
            {
                PackageVerificationResult package = PackageVerifier.Verify(request.Command.SourcesDirectory, request.Command.ManifestPath);
                Assert.Equal(ApprovalDigest.Compute(request.Command, package.ManifestSha256, package.Manifest.Files, SandboxRules.ReadMarker(request.Command.ProjectPath).SandboxId), request.Command.Approval.ApprovalDigest);
                TiaResult result = Check(request.Command, TiaJobStatus.Success, SandboxStates.Saved);
                result.SavedProject = true;
                return Task.FromResult(result);
            }

            public Task<TiaResult> ListBlocksAsync(TiaJobRequest request, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<TiaResult> ExportBlocksAsync(TiaJobRequest request, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            private TiaResult Check(TiaCommand command, string status, string state)
            {
                Assert.Null(SandboxRules.Check(command.ProjectPath, command.TiaVersion, command.SandboxRoot));
                PackageVerificationResult package = PackageVerifier.Verify(command.SourcesDirectory, command.ManifestPath);
                Assert.True(package.Ok);
                LastPlanSize = package.Manifest.Files.Count;
                return new TiaResult
                {
                    JobId = command.JobId,
                    RequestedVersion = command.TiaVersion,
                    ActualVersion = command.TiaVersion,
                    Operation = command.Operation,
                    Status = status,
                    SandboxState = state,
                };
            }
        }
    }
}
