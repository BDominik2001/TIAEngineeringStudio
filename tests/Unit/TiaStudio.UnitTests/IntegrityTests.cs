using System;
using System.IO;
using System.Linq;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;
using TiaStudio.Domain.Workflow;
using Xunit;

namespace TiaStudio.UnitTests
{
    public class PackageVerifierTests
    {
        [Fact]
        public void ValidPackageVerifies()
        {
            using (TempDir temp = new TempDir())
            {
                Fixtures.WritePackage(temp.Path);
                PackageVerificationResult result = PackageVerifier.Verify(temp.Path);
                Assert.True(result.Ok);
                Assert.Equal(3, result.Manifest.Files.Count);
                Assert.Equal(64, result.ManifestSha256.Length);
            }
        }

        [Fact]
        public void ModifiedSourceIsHashMismatch()
        {
            using (TempDir temp = new TempDir())
            {
                Fixtures.WritePackage(temp.Path);
                File.AppendAllText(temp.Combine("sources", "FC_Cylinders.scl"), "// tampered\r\n");
                Assert.Equal(ErrorCodes.HashMismatch, PackageVerifier.Verify(temp.Path).FirstErrorCode);
            }
        }

        [Theory]
        [InlineData("../outside.scl")]
        [InlineData("/etc/passwd")]
        [InlineData("C:/Windows/x.scl")]
        [InlineData("sources\\x.scl")]
        [InlineData("sources/./x.scl")]
        public void EscapingPathsAreRejected(string relative)
        {
            using (TempDir temp = new TempDir())
            {
                string manifestPath = Fixtures.WritePackage(temp.Path);
                GenerationManifest manifest = ProtocolJson.DeserializeLenient<GenerationManifest>(File.ReadAllText(manifestPath));
                manifest.Files[0].RelativePath = relative;
                File.WriteAllText(manifestPath, ProtocolJson.Serialize(manifest));
                Assert.Equal(ErrorCodes.PathEscape, PackageVerifier.Verify(temp.Path).FirstErrorCode);
            }
        }

        [Fact]
        public void DownloadAllowedTrueIsRejected()
        {
            using (TempDir temp = new TempDir())
            {
                string manifestPath = Fixtures.WritePackage(temp.Path);
                File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"download_allowed\": false", "\"download_allowed\": true"));
                Assert.Equal(ErrorCodes.ManifestInvalid, PackageVerifier.Verify(temp.Path).FirstErrorCode);
            }
        }

        [Fact]
        public void MissingManifestIsInvalid()
        {
            using (TempDir temp = new TempDir())
            {
                Assert.Equal(ErrorCodes.ManifestInvalid, PackageVerifier.Verify(temp.Path).FirstErrorCode);
            }
        }
    }

    public class ApprovalDigestTests
    {
        private static TiaCommand Command()
        {
            return new TiaCommand
            {
                JobId = "b9f4a91e-8d8e-4f55-9f99-27af49000528",
                Operation = TiaOperations.ImportAndCompile,
                TiaVersion = "V19",
                ProjectPath = Path.Combine(Path.GetTempPath(), "V19", "Demo", "Demo.ap19"),
                PlcName = "PLC_1",
                OverwritePolicy = OverwritePolicies.Deny,
            };
        }

        private static readonly ManifestFile[] Files =
        {
            new ManifestFile { RelativePath = "sources/FB_Cylinder.scl", Sha256 = new string('1', 64), ImportOrder = 1 },
            new ManifestFile { RelativePath = "sources/FC_Cylinders.scl", Sha256 = new string('2', 64), ImportOrder = 2 },
        };

        [Fact]
        public void DigestIsStableAndBindsEveryField()
        {
            string baseline = ApprovalDigest.Compute(Command(), new string('m', 64), Files);
            Assert.Equal(baseline, ApprovalDigest.Compute(Command(), new string('m', 64), Files.Reverse()));

            Func<TiaCommand, TiaCommand>[] mutations =
            {
                c => { c.JobId = Guid.NewGuid().ToString(); return c; },
                c => { c.TiaVersion = "V20"; return c; },
                c => { c.ProjectPath += "x"; return c; },
                c => { c.PlcName = "PLC_2"; return c; },
                c => { c.Operation = TiaOperations.ValidateJob; return c; },
            };
            foreach (Func<TiaCommand, TiaCommand> mutate in mutations)
            {
                Assert.NotEqual(baseline, ApprovalDigest.Compute(mutate(Command()), new string('m', 64), Files));
            }
            Assert.NotEqual(baseline, ApprovalDigest.Compute(Command(), new string('n', 64), Files));
            ManifestFile[] changed = Files.Select(f => new ManifestFile { RelativePath = f.RelativePath, Sha256 = f.Sha256, ImportOrder = f.ImportOrder }).ToArray();
            changed[1].Sha256 = new string('3', 64);
            Assert.NotEqual(baseline, ApprovalDigest.Compute(Command(), new string('m', 64), changed));
        }

        [Fact]
        public void LineBreaksInFieldsAreRejected()
        {
            TiaCommand command = Command();
            command.PlcName = "PLC_1\nfile=evil";
            Assert.Throws<ArgumentException>(() => ApprovalDigest.Compute(command, new string('m', 64), Files));
        }
    }

    public class SandboxRuleTests
    {
        [Fact]
        public void ValidSandboxPasses()
        {
            using (TempDir temp = new TempDir())
            {
                string project = Fixtures.WriteSandboxProject(temp.Path, "V20");
                Assert.Null(SandboxRules.Check(project, "V20", temp.Path));
            }
        }

        [Fact]
        public void WrongExtensionIsVersionMismatch()
        {
            using (TempDir temp = new TempDir())
            {
                string project = Fixtures.WriteSandboxProject(temp.Path, "V20");
                Assert.Equal(ErrorCodes.ProjectVersionMismatch, SandboxRules.Check(project, "V18", temp.Path).Item1);
            }
        }

        [Fact]
        public void MissingMarkerIsNotSandbox()
        {
            using (TempDir temp = new TempDir())
            {
                string project = Fixtures.WriteSandboxProject(temp.Path, "V18", withMarker: false);
                Assert.Equal(ErrorCodes.ProjectNotSandbox, SandboxRules.Check(project, "V18", temp.Path).Item1);
            }
        }

        [Fact]
        public void ProjectOutsideRootIsNotSandbox()
        {
            using (TempDir temp = new TempDir())
            using (TempDir other = new TempDir())
            {
                string project = Fixtures.WriteSandboxProject(other.Path, "V18");
                Assert.Equal(ErrorCodes.ProjectNotSandbox, SandboxRules.Check(project, "V18", temp.Path).Item1);
            }
        }

        [Fact]
        public void MissingProjectFileIsNotFound()
        {
            using (TempDir temp = new TempDir())
            {
                Assert.Equal(ErrorCodes.ProjectNotFound, SandboxRules.Check(temp.Combine("V18", "X", "X.ap18"), "V18", temp.Path).Item1);
            }
        }

        [Theory]
        [InlineData("x.ap18", "V18")]
        [InlineData("x.AP19", "V19")]
        [InlineData("x.ap20", "V20")]
        [InlineData("x.ap21", null)]
        [InlineData("x.ap17", null)]
        [InlineData("x.zap18", null)]
        public void ExtensionMapping(string file, string version)
        {
            Assert.Equal(version, TiaVersions.FromProjectPath(file));
        }
    }

    public class WorkflowStateMachineTests
    {
        [Fact]
        public void HappyPathIsLinear()
        {
            WorkflowState[] path =
            {
                WorkflowState.Draft, WorkflowState.Validated, WorkflowState.Checked, WorkflowState.Generated, WorkflowState.Reviewed,
                WorkflowState.Approved, WorkflowState.ReadyToImport, WorkflowState.Imported, WorkflowState.Compiled, WorkflowState.Saved,
            };
            for (int i = 0; i + 1 < path.Length; i++)
            {
                Assert.True(WorkflowStateMachine.CanTransition(path[i], path[i + 1]));
                for (int j = i + 2; j < path.Length; j++)
                {
                    Assert.False(WorkflowStateMachine.CanTransition(path[i], path[j]), path[i] + " must not skip to " + path[j]);
                }
            }
        }

        [Fact]
        public void FailedOnlyLeadsBackToDraft()
        {
            foreach (WorkflowState target in Enum.GetValues(typeof(WorkflowState)).Cast<WorkflowState>())
            {
                Assert.Equal(target == WorkflowState.Draft, WorkflowStateMachine.CanTransition(WorkflowState.Failed, target));
            }
            Assert.Equal("READY_TO_IMPORT", WorkflowStateMachine.DisplayName(WorkflowState.ReadyToImport));
        }
    }
}
