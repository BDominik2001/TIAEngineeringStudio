using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TiaStudio.Application.Library;
using TiaStudio.Application.Ports;
using TiaStudio.Application.Workflow;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;
using TiaStudio.Domain.Workflow;
using TiaStudio.Infrastructure.Json;
using TiaStudio.Infrastructure.Library;
using TiaStudio.Infrastructure.Platform;
using TiaStudio.Infrastructure.Sandbox;
using TiaStudio.TiaAdapter.Common;
using Xunit;

namespace TiaStudio.UnitTests
{
    internal static class LibraryFixtures
    {
        /// <summary>Egy blokk felvétele a könyvtárba úgy, ahogy egy worker-export után történne.</summary>
        public static BlockLibraryItem Add(
            FileBlockLibraryStore store,
            string library,
            string name,
            string blockType = BlockTypes.FB,
            string format = BlockFormats.ExternalSource,
            string sourceVersion = "V18",
            string language = "SCL",
            string dbKind = null,
            string content = null)
        {
            string exportDir = Path.Combine(Path.GetTempPath(), "tes-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportDir);
            try
            {
                string fileName = name + LibraryRules.FileExtension(blockType, format);
                byte[] bytes = Encoding.UTF8.GetBytes(content ?? "// " + name + " " + Guid.NewGuid().ToString("N") + "\r\n");
                File.WriteAllBytes(Path.Combine(exportDir, fileName), bytes);
                return store.AddExportedBlocks(library, exportDir, new[]
                {
                    new ExportedBlockFile
                    {
                        BlockName = name,
                        BlockType = blockType,
                        DbKind = dbKind,
                        ProgrammingLanguage = language,
                        Format = format,
                        RelativePath = fileName,
                        Sha256 = Sha256.OfBytes(bytes),
                        ByteLength = bytes.Length,
                    },
                }, sourceVersion, new BlockLibraryOrigin { ProjectFile = "Demo.ap18", PlcName = "PLC_1" }, null).Single();
            }
            finally
            {
                Directory.Delete(exportDir, true);
            }
        }
    }

    public class LibraryRulesTests
    {
        private static ProjectBlockInfo Block(string name, string type, string language, string dbKind = null)
        {
            return new ProjectBlockInfo { Name = name, BlockType = type, ProgrammingLanguage = language, DbKind = dbKind };
        }

        [Theory]
        [InlineData("FB_Motor", "FB", "SCL", "external_source,simaticml")]
        [InlineData("FB_Lad", "FB", "LAD", "simaticml")]
        [InlineData("FC_Calc", "FC", "FBD", "simaticml")]
        [InlineData("DB_Data", "DB", "DB", "external_source,simaticml", "Global")]
        [InlineData("DB_Motor1", "DB", "DB", "external_source", "Instance")]
        [InlineData("DB_Array", "DB", "DB", "external_source", "Array")]
        [InlineData("UDT_Axis", "UDT", null, "external_source,simaticml")]
        [InlineData("Main", "OB", "LAD", "")]
        [InlineData("F_Guard", "FB", "F_LAD", "")]
        [InlineData("FDB", "DB", "F_DB", "")]
        [InlineData("FB Motor", "FB", "SCL", "")]
        [InlineData("1FB", "FB", "SCL", "")]
        public void ExportableFormats(string name, string type, string language, string expected, string dbKind = null)
        {
            Assert.Equal(expected, string.Join(",", LibraryRules.ExportableFormats(Block(name, type, language, dbKind))));
        }

        [Fact]
        public void InstanceDbAsSimaticMlIsNotImportable()
        {
            BlockLibraryItem item = new BlockLibraryItem { BlockName = "DB_M1", BlockType = "DB", DbKind = DbKinds.Instance, ProgrammingLanguage = "DB", Format = BlockFormats.SimaticMl, SourceTiaVersion = "V18" };
            Assert.Equal(ErrorCodes.LibraryItemNotAllowed, LibraryRules.ImportProblem(item, "V18").Item1);
        }

        [Fact]
        public void KnowHowProtectedAndInconsistentAreNotExportable()
        {
            ProjectBlockInfo protectedBlock = Block("FB_X", "FB", "SCL");
            protectedBlock.IsKnowHowProtected = true;
            Assert.Empty(LibraryRules.ExportableFormats(protectedBlock));
            ProjectBlockInfo inconsistent = Block("FB_Y", "FB", "SCL");
            inconsistent.IsConsistent = false;
            Assert.Contains("inconsistent", LibraryRules.ExportProblem(inconsistent, BlockFormats.SimaticMl));
        }

        [Theory]
        [InlineData("simaticml", "V20", "V18", "LIBRARY_VERSION_MISMATCH")]
        [InlineData("simaticml", "V19", "V19", null)]
        [InlineData("external_source", "V20", "V18", null)]
        [InlineData("external_source", "V18", "V21", "UNSUPPORTED_TIA_VERSION")]
        public void ImportCompatibility(string format, string source, string target, string expectedCode)
        {
            BlockLibraryItem item = new BlockLibraryItem { BlockName = "FB_Motor", BlockType = "FB", ProgrammingLanguage = "SCL", Format = format, SourceTiaVersion = source };
            Assert.Equal(expectedCode, LibraryRules.ImportProblem(item, target)?.Item1);
        }

        [Fact]
        public void ImportOrderAndExtensions()
        {
            string[] order = new[]
            {
                Tuple.Create("DB", "Instance"), Tuple.Create("FB", (string)null), Tuple.Create("UDT", (string)null),
                Tuple.Create("DB", "Global"), Tuple.Create("FC", (string)null),
            }.OrderBy(t => LibraryRules.ImportRank(t.Item1, t.Item2)).Select(t => t.Item1 + (t.Item2 ?? string.Empty)).ToArray();
            Assert.Equal(new[] { "UDT", "FC", "FB", "DBGlobal", "DBInstance" }, order);
            Assert.Equal(".scl", LibraryRules.FileExtension("FB", BlockFormats.ExternalSource));
            Assert.Equal(".db", LibraryRules.FileExtension("DB", BlockFormats.ExternalSource));
            Assert.Equal(".udt", LibraryRules.FileExtension("UDT", BlockFormats.ExternalSource));
            Assert.Equal(".xml", LibraryRules.FileExtension("FB", BlockFormats.SimaticMl));
        }

        [Fact]
        public void AsciiAndSafetyDetection()
        {
            Assert.True(LibraryRules.IsAsciiExternalSource(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'A' }));
            Assert.False(LibraryRules.IsAsciiExternalSource(Encoding.UTF8.GetBytes("// Szelep nyitás")));
            Assert.True(LibraryRules.SimaticMlContainsSafety(Encoding.UTF8.GetBytes("<ProgrammingLanguage>F_LAD</ProgrammingLanguage>")));
            Assert.False(LibraryRules.SimaticMlContainsSafety(Encoding.UTF8.GetBytes("<ProgrammingLanguage>LAD</ProgrammingLanguage>")));
        }
    }

    public sealed class FileBlockLibraryStoreTests : IDisposable
    {
        private readonly TempDir temp = new TempDir();
        private readonly FileBlockLibraryStore store;

        public FileBlockLibraryStoreTests()
        {
            store = new FileBlockLibraryStore(temp.Combine("libraries"), new FixedClock(), new TestUser());
        }

        public void Dispose()
        {
            temp.Dispose();
        }

        [Fact]
        public void CreateRejectsDuplicatesAndBadNames()
        {
            store.Create("Motion", "Motion blocks");
            Assert.Equal(ErrorCodes.LibraryExists, Assert.Throws<BlockLibraryException>(() => store.Create("motion", null)).ErrorCode);
            Assert.Equal(ErrorCodes.InvalidIdentifier, Assert.Throws<BlockLibraryException>(() => store.Create("../evil", null)).ErrorCode);
            Assert.Equal(ErrorCodes.InvalidValue, Assert.Throws<BlockLibraryException>(() => store.Create("Other", "line1\nline2")).ErrorCode);
            BlockLibrarySummary summary = Assert.Single(store.ListLibraries());
            Assert.Equal("Motion", summary.Name);
            Assert.Null(summary.Problem);
        }

        [Fact]
        public void ExportsBecomeNewRevisionsAndFilesAreNeverOverwritten()
        {
            store.Create("Std", null);
            BlockLibraryItem first = LibraryFixtures.Add(store, "Std", "FB_Motor");
            string firstContent = File.ReadAllText(store.ItemPath("Std", first));
            BlockLibraryItem second = LibraryFixtures.Add(store, "Std", "FB_Motor");
            Assert.Equal(1, first.Revision);
            Assert.Equal(2, second.Revision);
            Assert.Equal("items/FB_Motor/r1/FB_Motor.scl", first.RelativePath);
            Assert.Equal(firstContent, File.ReadAllText(store.ItemPath("Std", first)));
            Assert.Empty(store.Verify("Std"));
            Assert.Equal(new[] { second.ItemId }, store.Load("Std").LatestItems().Select(i => i.ItemId));
        }

        [Fact]
        public void TamperedItemIsDetected()
        {
            store.Create("Std", null);
            BlockLibraryItem item = LibraryFixtures.Add(store, "Std", "FC_Scale", BlockTypes.FC);
            File.AppendAllText(store.ItemPath("Std", item), "// tampered");
            Assert.Equal(ErrorCodes.HashMismatch, Assert.Single(store.Verify("Std")).Code);
        }

        [Fact]
        public void ManipulatedIndexPathIsRejected()
        {
            store.Create("Std", null);
            LibraryFixtures.Add(store, "Std", "FB_Motor");
            string indexPath = temp.Combine("libraries", "Std", BlockLibraryIndex.FileName);
            File.WriteAllText(indexPath, File.ReadAllText(indexPath).Replace("items/FB_Motor/r1/FB_Motor.scl", "../../outside.scl"));
            Assert.Equal(ErrorCodes.PathEscape, Assert.Single(store.Verify("Std")).Code);
        }

        [Fact]
        public void ExportFileChangedAfterExportAddsNothing()
        {
            store.Create("Std", null);
            string exportDir = temp.Combine("export");
            Directory.CreateDirectory(exportDir);
            File.WriteAllText(Path.Combine(exportDir, "FB_A.scl"), "a");
            File.WriteAllText(Path.Combine(exportDir, "FB_B.scl"), "b");
            ExportedBlockFile[] files =
            {
                new ExportedBlockFile { BlockName = "FB_A", BlockType = "FB", ProgrammingLanguage = "SCL", Format = BlockFormats.ExternalSource, RelativePath = "FB_A.scl", Sha256 = Sha256.OfText("a"), ByteLength = 1 },
                new ExportedBlockFile { BlockName = "FB_B", BlockType = "FB", ProgrammingLanguage = "SCL", Format = BlockFormats.ExternalSource, RelativePath = "FB_B.scl", Sha256 = Sha256.OfText("changed"), ByteLength = 1 },
            };
            Assert.Equal(ErrorCodes.HashMismatch, Assert.Throws<BlockLibraryException>(() => store.AddExportedBlocks("Std", exportDir, files, "V18", null, null)).ErrorCode);
            Assert.Empty(store.Load("Std").Items);
            Assert.False(File.Exists(temp.Combine("libraries", "Std", "items", "FB_A", "r1", "FB_A.scl")));
        }

        [Fact]
        public void OrganizationBlocksAreRefused()
        {
            store.Create("Std", null);
            Assert.Equal(ErrorCodes.LibraryItemNotAllowed,
                Assert.Throws<BlockLibraryException>(() => LibraryFixtures.Add(store, "Std", "Main", BlockTypes.OB, BlockFormats.SimaticMl, language: "LAD")).ErrorCode);
        }

        [Fact]
        public void RemoveItemDeletesFileAndIndexEntry()
        {
            store.Create("Std", null);
            BlockLibraryItem item = LibraryFixtures.Add(store, "Std", "FB_Motor");
            string file = store.ItemPath("Std", item);
            store.RemoveItem("Std", item.ItemId);
            Assert.False(File.Exists(file));
            Assert.Empty(store.Load("Std").Items);
            Assert.Equal(ErrorCodes.LibraryItemNotFound, Assert.Throws<BlockLibraryException>(() => store.RemoveItem("Std", item.ItemId)).ErrorCode);
        }

        [Fact]
        public void ImportPackageIsOrderedAndVerifiable()
        {
            store.Create("Std", null);
            BlockLibraryItem db = LibraryFixtures.Add(store, "Std", "DB_Motor1", BlockTypes.DB, language: "DB", dbKind: DbKinds.Instance);
            BlockLibraryItem fb = LibraryFixtures.Add(store, "Std", "FB_Motor", BlockTypes.FB, BlockFormats.SimaticMl, "V19", "LAD");
            BlockLibraryItem udt = LibraryFixtures.Add(store, "Std", "UDT_Motor", BlockTypes.UDT, language: null);
            string output = temp.Combine("run", "package");
            LibraryImportSelection selection = new LibraryImportSelection { LibraryName = "Std", TargetTiaVersion = "V19", PlcName = "PLC_1", ItemIds = { db.ItemId, fb.ItemId, udt.ItemId } };

            GenerationResult result = store.BuildImportPackage(selection, new[] { db, fb, udt }, output, "run", new string('c', 64), null);

            PackageVerificationResult package = PackageVerifier.Verify(result.OutputDirectory, result.ManifestPath);
            Assert.True(package.Ok, string.Join(", ", package.Problems));
            Assert.Equal(GenerationManifest.LibraryVersion, package.Manifest.ManifestVersion);
            Assert.Equal(GenerationManifest.KindLibraryImport, package.Manifest.PackageKind);
            Assert.Equal(new[] { "UDT_Motor", "FB_Motor", "DB_Motor1" }, package.Manifest.Files.Select(f => f.BlockName));
            Assert.Equal(new[] { "sources/UDT_Motor.udt", "sources/FB_Motor.xml", "sources/DB_Motor1.db" }, package.Manifest.Files.Select(f => f.RelativePath));
            Assert.Equal(BlockFormats.SimaticMl, package.Manifest.Files[1].Format);
            Assert.Equal("utf-8", package.Manifest.Encoding);
            Assert.DoesNotContain(Directory.EnumerateDirectories(temp.Combine("run")), d => d.Contains("staging"));
            Assert.Equal(ErrorCodes.OutputExists, Assert.Throws<BlockLibraryException>(() => store.BuildImportPackage(selection, new[] { db }, output, "run", new string('c', 64), null)).ErrorCode);
        }

        [Fact]
        public void BrokenLibraryIsListedWithProblem()
        {
            Directory.CreateDirectory(temp.Combine("libraries", "Broken"));
            File.WriteAllText(temp.Combine("libraries", "Broken", BlockLibraryIndex.FileName), "{ not json");
            BlockLibrarySummary summary = Assert.Single(store.ListLibraries());
            Assert.NotNull(summary.Problem);
            Assert.Equal(ErrorCodes.LibraryInvalid, Assert.Throws<BlockLibraryException>(() => store.Load("Broken")).ErrorCode);
        }

        [Fact]
        public void Manifest10WithFormatIsRejected()
        {
            string manifestPath = Fixtures.WritePackage(temp.Combine("pkg"));
            File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"import_order\": 1", "\"import_order\": 1, \"format\": \"simaticml\""));
            Assert.Equal(ErrorCodes.ManifestInvalid, PackageVerifier.Verify(temp.Combine("pkg")).FirstErrorCode);
        }
    }

    /// <summary>ListBlocks / ExportBlocks és formátumfüggő import a worker oldalon (hamis Openness).</summary>
    public sealed class WorkerLibraryTests : IDisposable
    {
        private readonly TempDir temp = new TempDir();
        private readonly FakeBackend backend = new FakeBackend();
        private readonly string sandboxRoot;
        private readonly string projectPath;
        private readonly string exportDir;

        public WorkerLibraryTests()
        {
            sandboxRoot = temp.Combine("sandbox");
            projectPath = Fixtures.WriteSandboxProject(sandboxRoot);
            exportDir = temp.Combine("export");
            Directory.CreateDirectory(exportDir);
            backend.Session.Plc.Described.AddRange(new[]
            {
                new ProjectBlockInfo { Name = "FB_Motor", BlockType = "FB", ProgrammingLanguage = "SCL", Location = "Program blocks/Drives" },
                new ProjectBlockInfo { Name = "FC_Lad", BlockType = "FC", ProgrammingLanguage = "LAD", Location = "Program blocks" },
                new ProjectBlockInfo { Name = "Main", BlockType = "OB", ProgrammingLanguage = "LAD", Location = "Program blocks" },
                new ProjectBlockInfo { Name = "UDT_Motor", BlockType = "UDT", Location = "PLC data types" },
            });
        }

        public void Dispose()
        {
            temp.Dispose();
        }

        private TiaCommand Command(string operation, params ExportRequestItem[] blocks)
        {
            return new TiaCommand
            {
                JobId = Guid.NewGuid().ToString(),
                Operation = operation,
                TiaVersion = "V18",
                ProjectPath = projectPath,
                SandboxRoot = sandboxRoot,
                PlcName = "PLC_1",
                ExportDirectory = operation == TiaOperations.ExportBlocks ? exportDir : null,
                ExportBlocks = blocks.Length == 0 ? null : blocks.ToList(),
            };
        }

        private TiaResult Run(TiaCommand command)
        {
            string path = temp.Combine(command.JobId + ".command.json");
            File.WriteAllText(path, ProtocolJson.Serialize(command));
            return new WorkerHost("V18", backend, TextWriter.Null).Execute(path);
        }

        private static ExportRequestItem Request(string name, string format)
        {
            return new ExportRequestItem { BlockName = name, Format = format };
        }

        [Fact]
        public void ListBlocksReportsExportability()
        {
            TiaResult result = Run(Command(TiaOperations.ListBlocks));
            Assert.Equal(TiaJobStatus.Success, result.Status);
            Assert.Equal(SandboxStates.Unchanged, result.SandboxState);
            Assert.Equal(new[] { "external_source", "simaticml" }, result.Blocks.Single(b => b.Name == "FB_Motor").ExportableFormats);
            Assert.Equal(new[] { "simaticml" }, result.Blocks.Single(b => b.Name == "FC_Lad").ExportableFormats);
            ProjectBlockInfo ob = result.Blocks.Single(b => b.Name == "Main");
            Assert.Empty(ob.ExportableFormats);
            Assert.Contains("Organization", ob.NotExportableReason);
            Assert.False(backend.Session.Saved);
        }

        [Fact]
        public void ExportWritesHashedFilesWithoutSaving()
        {
            TiaResult result = Run(Command(TiaOperations.ExportBlocks, Request("FB_Motor", BlockFormats.ExternalSource), Request("fc_lad", BlockFormats.SimaticMl), Request("UDT_Motor", BlockFormats.ExternalSource)));
            Assert.Equal(TiaJobStatus.Success, result.Status);
            Assert.Equal(new[] { "FB_Motor.scl", "FC_Lad.xml", "UDT_Motor.udt" }, result.ExportedFiles.Select(f => f.RelativePath));
            foreach (ExportedBlockFile file in result.ExportedFiles)
            {
                Assert.Equal(Sha256.OfFile(Path.Combine(exportDir, file.RelativePath)), file.Sha256);
            }
            Assert.False(backend.Session.Saved);
            Assert.True(backend.Session.Disposed);
            Assert.Equal(SandboxStates.Unchanged, result.SandboxState);
        }

        [Fact]
        public void ExportOfOrganizationBlockIsRefusedBeforeAnyExport()
        {
            TiaResult result = Run(Command(TiaOperations.ExportBlocks, Request("FB_Motor", BlockFormats.SimaticMl), Request("Main", BlockFormats.SimaticMl)));
            Assert.Equal(ErrorCodes.LibraryItemNotAllowed, result.ErrorCode);
            Assert.Empty(backend.Session.Plc.Exported);
        }

        [Fact]
        public void ExportOfLadderAsExternalSourceIsRefused()
        {
            Assert.Equal(ErrorCodes.LibraryItemNotAllowed, Run(Command(TiaOperations.ExportBlocks, Request("FC_Lad", BlockFormats.ExternalSource))).ErrorCode);
        }

        [Fact]
        public void MissingBlockAndNonEmptyDirectoryAreRejected()
        {
            Assert.Equal(ErrorCodes.BlockNotFound, Run(Command(TiaOperations.ExportBlocks, Request("FB_Nope", BlockFormats.SimaticMl))).ErrorCode);
            File.WriteAllText(Path.Combine(exportDir, "old.xml"), "x");
            Assert.Equal(ErrorCodes.CommandInvalid, Run(Command(TiaOperations.ExportBlocks, Request("FB_Motor", BlockFormats.SimaticMl))).ErrorCode);
        }

        [Fact]
        public void SafetyContentInExportIsRefused()
        {
            backend.Session.Plc.ExportContent = (block, format) => Encoding.UTF8.GetBytes("<ProgrammingLanguage>F_LAD</ProgrammingLanguage>");
            Assert.Equal(ErrorCodes.LibraryItemNotAllowed, Run(Command(TiaOperations.ExportBlocks, Request("FC_Lad", BlockFormats.SimaticMl))).ErrorCode);
        }

        [Fact]
        public void NonAsciiExternalSourceIsRefusedAtExport()
        {
            backend.Session.Plc.ExportContent = (block, format) => Encoding.UTF8.GetBytes("// Motor indítás\r\n");
            Assert.Equal(ErrorCodes.LibraryItemNotAllowed, Run(Command(TiaOperations.ExportBlocks, Request("FB_Motor", BlockFormats.ExternalSource))).ErrorCode);
        }

        [Fact]
        public void ExportRequiresOfflinePlcButListingDoesNot()
        {
            backend.Session.Plc.Offline = false;
            Assert.Equal(TiaJobStatus.Success, Run(Command(TiaOperations.ListBlocks)).Status);
            Assert.Equal(ErrorCodes.PlcOnline, Run(Command(TiaOperations.ExportBlocks, Request("FB_Motor", BlockFormats.SimaticMl))).ErrorCode);
            Assert.Empty(backend.Session.Plc.Exported);
        }

        [Fact]
        public void ProtocolVersion10IsStillAccepted()
        {
            TiaCommand command = Command(TiaOperations.ListBlocks);
            command.ProtocolVersion = "1.0";
            Assert.Equal(TiaJobStatus.Success, Run(command).Status);
        }

        [Fact]
        public void LibraryPackageImportUsesFormatPerFile()
        {
            FileBlockLibraryStore store = new FileBlockLibraryStore(temp.Combine("libraries"), new FixedClock(), new TestUser());
            store.Create("Std", null);
            BlockLibraryItem udt = LibraryFixtures.Add(store, "Std", "UDT_Motor", BlockTypes.UDT, language: null);
            BlockLibraryItem fb = LibraryFixtures.Add(store, "Std", "FB_Motor", BlockTypes.FB, BlockFormats.SimaticMl, "V18", "LAD");
            string package = temp.Combine("pkg");
            GenerationResult generated = store.BuildImportPackage(
                new LibraryImportSelection { LibraryName = "Std", TargetTiaVersion = "V18", PlcName = "PLC_1" }, new[] { fb, udt }, package, "r", new string('a', 64), null);
            PackageVerificationResult verified = PackageVerifier.Verify(package);
            TiaCommand command = new TiaCommand
            {
                JobId = Guid.NewGuid().ToString(),
                Operation = TiaOperations.ImportAndCompile,
                TiaVersion = "V18",
                ProjectPath = projectPath,
                SandboxRoot = sandboxRoot,
                PlcName = "PLC_1",
                SourcesDirectory = package,
                ManifestPath = generated.ManifestPath,
                DryRun = false,
                ApprovedByUser = true,
            };
            command.Approval = new ApprovalRecord { ApprovedBy = "t", ApprovedAtUtc = "x", ApprovalDigest = ApprovalDigest.Compute(command, verified.ManifestSha256, verified.Manifest.Files) };

            TiaResult result = Run(command);

            Assert.Equal(TiaJobStatus.Success, result.Status);
            Assert.Equal(new[] { "UDT_Motor.udt" }, backend.Session.Plc.Imported);
            Assert.Equal(new[] { "FB_Motor.xml" }, backend.Session.Plc.ImportedXml);
            Assert.True(backend.Session.Saved);
        }
    }

    /// <summary>Export a könyvtárba és könyvtárimport a teljes jóváhagyási láncon.</summary>
    public sealed class LibraryWorkflowTests : IDisposable
    {
        private readonly TempDir temp = new TempDir();
        private readonly RecordingAudit audit = new RecordingAudit();
        private readonly FakeWorkerClient worker = new FakeWorkerClient();
        private readonly SandboxService sandbox;
        private readonly FileBlockLibraryStore store;
        private readonly EngineeringWorkflow workflow;
        private readonly BlockLibraryService service;

        public LibraryWorkflowTests()
        {
            sandbox = new SandboxService(temp.Combine("sandbox"), new FixedClock(), new TestUser());
            store = new FileBlockLibraryStore(temp.Combine("libraries"), new FixedClock(), new TestUser());
            FileWorkspace workspace = new FileWorkspace(temp.Combine("generated"));
            workflow = new EngineeringWorkflow(
                new MachineProjectSerializer(), new MachineProjectJsonValidator(), new MirrorStaticValidator(), new FakeGenerator(),
                worker, sandbox, audit, new FixedClock(), new TestUser(), workspace, null, store);
            service = new BlockLibraryService(store, worker, sandbox, workspace, audit, new FixedClock(), new TestUser(), TimeSpan.FromMinutes(1));
            store.Create("Std", "Standard blocks");
        }

        public void Dispose()
        {
            temp.Dispose();
        }

        [Fact]
        public async Task ExportFromSandboxAddsItems()
        {
            string project = Fixtures.WriteSandboxProject(sandbox.SandboxRoot, "V19");
            worker.OnExport = command =>
            {
                Assert.Equal(TiaOperations.ExportBlocks, command.Operation);
                Assert.True(Directory.Exists(command.ExportDirectory));
                TiaResult result = FakeWorkerClient.Result(command, TiaJobStatus.Success, null, SandboxStates.Unchanged, false);
                result.ExportedFiles = command.ExportBlocks.Select(r =>
                {
                    string name = r.BlockName + LibraryRules.FileExtension("FB", r.Format);
                    File.WriteAllText(Path.Combine(command.ExportDirectory, name), "FUNCTION_BLOCK \"" + r.BlockName + "\"\r\n");
                    return new ExportedBlockFile
                    {
                        BlockName = r.BlockName, BlockType = "FB", ProgrammingLanguage = "SCL", Format = r.Format, RelativePath = name,
                        Sha256 = Sha256.OfFile(Path.Combine(command.ExportDirectory, name)), ByteLength = new FileInfo(Path.Combine(command.ExportDirectory, name)).Length,
                    };
                }).ToList();
                return result;
            };

            OperationResult<IReadOnlyList<BlockLibraryItem>> exported = await service.ExportToLibraryAsync(
                "Std", "V19", project, "PLC_1", new[] { new ExportRequestItem { BlockName = "FB_Motor", Format = BlockFormats.SimaticMl } }, "motor", CancellationToken.None);

            Assert.True(exported.Ok, exported.Step.ToString());
            BlockLibraryItem item = Assert.Single(exported.Value);
            Assert.Equal("V19", item.SourceTiaVersion);
            Assert.Equal("Demo.ap19", item.Origin.ProjectFile);
            Assert.Empty(store.Verify("Std"));
            Assert.Contains(audit.Events, e => e.Event == "LibraryExported");
        }

        [Fact]
        public async Task FailedExportAddsNothing()
        {
            string project = Fixtures.WriteSandboxProject(sandbox.SandboxRoot, "V18");
            worker.OnExport = command => FakeWorkerClient.Result(command, TiaJobStatus.Failed, ErrorCodes.ExportFailed, SandboxStates.Unchanged, false);
            OperationResult<IReadOnlyList<BlockLibraryItem>> exported = await service.ExportToLibraryAsync(
                "Std", "V18", project, "PLC_1", new[] { new ExportRequestItem { BlockName = "FB_Motor", Format = BlockFormats.SimaticMl } }, null, CancellationToken.None);
            Assert.Equal(ErrorCodes.ExportFailed, exported.Step.ErrorCode);
            Assert.Empty(store.Load("Std").Items);
        }

        [Fact]
        public async Task ExportFromNonSandboxIsRefused()
        {
            string outside = Fixtures.WriteSandboxProject(temp.Combine("production"), "V18");
            OperationResult<IReadOnlyList<ProjectBlockInfo>> listed = await service.ListProjectBlocksAsync("V18", outside, "PLC_1", CancellationToken.None);
            Assert.Equal(ErrorCodes.ProjectNotSandbox, listed.Step.ErrorCode);
            Assert.Empty(worker.Commands);
        }

        [Fact]
        public async Task LibraryImportRunsThroughApprovalChainToSaved()
        {
            BlockLibraryItem udt = LibraryFixtures.Add(store, "Std", "UDT_Motor", BlockTypes.UDT, language: null);
            BlockLibraryItem fb = LibraryFixtures.Add(store, "Std", "FB_Motor", BlockTypes.FB, BlockFormats.SimaticMl, "V20", "LAD");
            workflow.SetLibrarySelection(new LibraryImportSelection { LibraryName = "Std", TargetTiaVersion = "V20", PlcName = "PLC_1", ItemIds = { fb.ItemId, udt.ItemId } });

            Assert.True(workflow.Validate().Ok);
            Assert.True(workflow.Check().Ok);
            StepResult generated = await workflow.GenerateAsync(CancellationToken.None);
            Assert.True(generated.Ok, generated.ToString());
            Assert.Equal(GenerationManifest.KindLibraryImport, workflow.Package.Manifest.PackageKind);
            Assert.True(workflow.MarkReviewed().Ok);
            Assert.True(workflow.Approve(Fixtures.WriteSandboxProject(sandbox.SandboxRoot, "V20")).Ok);
            Assert.True((await workflow.PrepareImportAsync(CancellationToken.None)).Ok);
            Assert.True((await workflow.ImportAndCompileAsync(CancellationToken.None)).Ok);
            Assert.Equal(WorkflowState.Saved, workflow.State);
            Assert.Equal(WorkflowInputKind.Library, workflow.InputKind);
            Assert.Equal("PLC_1", worker.Commands.Last().PlcName);
        }

        [Fact]
        public void SimaticMlFromOtherVersionFailsValidation()
        {
            BlockLibraryItem fb = LibraryFixtures.Add(store, "Std", "FB_Motor", BlockTypes.FB, BlockFormats.SimaticMl, "V20", "LAD");
            workflow.SetLibrarySelection(new LibraryImportSelection { LibraryName = "Std", TargetTiaVersion = "V18", PlcName = "PLC_1", ItemIds = { fb.ItemId } });
            StepResult result = workflow.Validate();
            Assert.Equal(ErrorCodes.LibraryVersionMismatch, result.ErrorCode);
            Assert.Equal(WorkflowState.Failed, workflow.State);
        }

        [Fact]
        public void ExternalSourceFromOtherVersionIsAllowedWithWarning()
        {
            BlockLibraryItem fb = LibraryFixtures.Add(store, "Std", "FB_Motor", sourceVersion: "V20");
            workflow.SetLibrarySelection(new LibraryImportSelection { LibraryName = "Std", TargetTiaVersion = "V18", PlcName = "PLC_1", ItemIds = { fb.ItemId } });
            Assert.True(workflow.Validate().Ok);
            Assert.Equal(ErrorCodes.LibraryVersionMismatch, Assert.Single(workflow.ModelReport.Issues).Code);
        }

        [Fact]
        public void TwoRevisionsOfSameBlockAreRejected()
        {
            BlockLibraryItem r1 = LibraryFixtures.Add(store, "Std", "FB_Motor");
            BlockLibraryItem r2 = LibraryFixtures.Add(store, "Std", "FB_Motor");
            workflow.SetLibrarySelection(new LibraryImportSelection { LibraryName = "Std", TargetTiaVersion = "V18", PlcName = "PLC_1", ItemIds = { r1.ItemId, r2.ItemId } });
            Assert.Equal(ErrorCodes.LibraryDuplicateBlock, workflow.Validate().ErrorCode);
        }

        [Fact]
        public void ItemChangedAfterValidationFailsCheck()
        {
            BlockLibraryItem fb = LibraryFixtures.Add(store, "Std", "FB_Motor");
            workflow.SetLibrarySelection(new LibraryImportSelection { LibraryName = "Std", TargetTiaVersion = "V18", PlcName = "PLC_1", ItemIds = { fb.ItemId } });
            Assert.True(workflow.Validate().Ok);
            File.AppendAllText(store.ItemPath("Std", fb), "// changed\r\n");
            Assert.Equal(ErrorCodes.HashMismatch, workflow.Check().ErrorCode);
            Assert.Equal(WorkflowState.Failed, workflow.State);
        }

        [Fact]
        public void SwitchingBackToModelClearsLibraryMode()
        {
            BlockLibraryItem fb = LibraryFixtures.Add(store, "Std", "FB_Motor");
            workflow.SetLibrarySelection(new LibraryImportSelection { LibraryName = "Std", TargetTiaVersion = "V18", PlcName = "PLC_1", ItemIds = { fb.ItemId } });
            workflow.SetProject(new MachineProjectSerializer().Deserialize(File.ReadAllBytes(Repo.PathOf("examples", "machine-v18.json"))));
            Assert.Equal(WorkflowInputKind.MachineModel, workflow.InputKind);
            Assert.Null(workflow.LibrarySelection);
            Assert.True(workflow.Validate().Ok);
        }
    }
}
