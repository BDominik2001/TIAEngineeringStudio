using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;
using TiaStudio.Infrastructure.Generator;
using TiaStudio.Infrastructure.Library;
using TiaStudio.Infrastructure.Platform;
using TiaStudio.Infrastructure.Sandbox;
using TiaStudio.Infrastructure.Tia;
using Xunit;

namespace TiaStudio.IntegrationTests
{
    /// <summary>
    /// A spec 11.2 kompatibilitási tesztmátrix automatizálható része VALÓDI TIA Portallal.
    /// Csak akkor fut, ha a környezet be van állítva (scripts/test-tia-integration.ps1):
    ///   TES_TIA_INTEGRATION        = V18 | V19 | V20
    ///   TES_TIA_KNOWN_GOOD_PROJECT = ismert jó, üres PLC-t tartalmazó .apXX projekt (nem módosul)
    ///   TES_TIA_PLC_NAME           = a PLC neve a projektben (alapértelmezés: PLC_1)
    ///   TES_WORKERS_DIR            = mappa, benne V18\TiaStudio.TiaAdapter.V18.exe stb.
    /// Az eredmény a docs/COMPATIBILITY_MATRIX.md-be kézzel rögzítendő.
    /// </summary>
    public sealed class TiaOpennessIntegrationTests : IDisposable
    {
        private readonly string version = Environment.GetEnvironmentVariable("TES_TIA_INTEGRATION");
        private readonly string knownGood = Environment.GetEnvironmentVariable("TES_TIA_KNOWN_GOOD_PROJECT");
        private readonly string plcName = Environment.GetEnvironmentVariable("TES_TIA_PLC_NAME") ?? "PLC_1";
        private readonly string workersDir = Environment.GetEnvironmentVariable("TES_WORKERS_DIR");
        private readonly string temp = Path.Combine(Path.GetTempPath(), "tes-tia-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            // A sandboxot szándékosan meghagyjuk kézi ellenőrzésre (spec 11.4); csak a jobs mappa törölhető.
        }

        private void RequireTia()
        {
            Skip.If(string.IsNullOrEmpty(version) || string.IsNullOrEmpty(knownGood) || string.IsNullOrEmpty(workersDir),
                "TIA integration disabled (set TES_TIA_INTEGRATION, TES_TIA_KNOWN_GOOD_PROJECT, TES_WORKERS_DIR)");
            Skip.If(Env.Python == null, "Python not found");
        }

        private TiaWorkerProcessClient Client()
        {
            Func<string, string> workerPath = v => Path.Combine(workersDir, v, TiaVersions.WorkerExecutableName(v));
            RegistryTiaInstallationDiscovery discovery = new RegistryTiaInstallationDiscovery(new WindowsRegistryReader(), new FileAssemblyInspector(), workerPath, null);
            return new TiaWorkerProcessClient(discovery, workerPath, Path.Combine(temp, "jobs"), TimeSpan.FromMinutes(5));
        }

        private async Task<GenerationResult> Generate()
        {
            string model = File.ReadAllText(Env.PathOf("examples", "machine-" + version.ToLowerInvariant() + ".json")).Replace("\"PLC_1\"", "\"" + plcName + "\"");
            string input = Path.Combine(temp, "machine.json");
            Directory.CreateDirectory(temp);
            File.WriteAllText(input, model);
            GenerationResult result = await new PythonSourceGenerator(Env.Python, Env.PathOf("python"), TimeSpan.FromMinutes(2))
                .GenerateAsync(new GenerationRequest { InputPath = input, OutputDirectory = Path.Combine(temp, "package") }, CancellationToken.None);
            Assert.True(result.Success, result.Message);
            return result;
        }

        private TiaCommand Command(SandboxCopy copy, GenerationResult package, string operation, bool dryRun)
        {
            return new TiaCommand
            {
                JobId = Guid.NewGuid().ToString(),
                Operation = operation,
                TiaVersion = version,
                ProjectPath = copy.ProjectPath,
                SandboxRoot = Path.Combine(temp, "sandbox"),
                PlcName = plcName,
                SourcesDirectory = package.OutputDirectory,
                ManifestPath = package.ManifestPath,
                DryRun = dryRun,
            };
        }

        private static void Approve(TiaCommand command)
        {
            PackageVerificationResult package = PackageVerifier.Verify(command.SourcesDirectory, command.ManifestPath);
            command.ApprovedByUser = true;
            command.Approval = new ApprovalRecord
            {
                ApprovedBy = Environment.UserName,
                ApprovedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ApprovalDigest = ApprovalDigest.Compute(command, package.ManifestSha256, package.Manifest.Files),
            };
        }

        [SkippableFact]
        public async Task MatrixForConfiguredVersion()
        {
            RequireTia();
            TiaWorkerProcessClient client = Client();

            // Telepítés / registry-felderítés + API DLL betöltése
            TiaInstallInfo install = client.DiscoverInstalledVersions().Single(i => i.TiaVersion == version);
            Assert.True(InstallStatus.IsUsable(install.Status), install.Status + ": " + string.Join("; ", install.Notes));
            TiaResult capabilities = await client.GetCapabilitiesAsync(version, CancellationToken.None);
            Assert.Equal(TiaJobStatus.Success, capabilities.Status);
            Assert.True(capabilities.Worker.OpennessLinked);
            Assert.StartsWith(TiaVersions.Major(version) + ".", capabilities.Worker.EngineeringAssemblyVersion);
            Assert.False(capabilities.Capabilities.Features[CapabilityFeatures.PlcDownload]);

            GenerationResult package = await Generate();
            SandboxService sandbox = new SandboxService(Path.Combine(temp, "sandbox"), new SystemClock(), new EnvironmentUserContext());
            SandboxCopy copy = sandbox.CreateSandboxCopy(knownGood, version);

            // Üres sandbox megnyitása, PLC felismerése, száraz futás
            TiaResult dryRun = await client.ValidateJobAsync(new TiaJobRequest(Command(copy, package, TiaOperations.ValidateJob, true), TimeSpan.FromMinutes(10)), CancellationToken.None);
            Assert.Equal(TiaJobStatus.ValidationOnly, dryRun.Status);
            Assert.All(dryRun.ImportPlan, p => Assert.Equal(ImportPlanItem.ActionCreate, p.Action));

            // Jóváhagyás nélküli import elutasítása
            TiaResult unapproved = await client.ImportAndCompileAsync(new TiaJobRequest(Command(copy, package, TiaOperations.ImportAndCompile, false), TimeSpan.FromMinutes(10)), CancellationToken.None);
            Assert.Equal(ErrorCodes.ApprovalInvalid, unapproved.ErrorCode);

            // SCL import + generált blokkok fordítása + mentés
            TiaCommand import = Command(copy, package, TiaOperations.ImportAndCompile, false);
            Approve(import);
            TiaResult imported = await client.ImportAndCompileAsync(new TiaJobRequest(import, TimeSpan.FromMinutes(30)), CancellationToken.None);
            Assert.True(imported.Status == TiaJobStatus.Success, string.Join("\n", imported.Messages) + "\n" + string.Join("\n", imported.Compile?.Messages.Select(m => m.Path + ": " + m.Description) ?? new string[0]));
            Assert.Equal(SandboxStates.Saved, imported.SandboxState);
            Assert.Equal(0, imported.Compile.ErrorCount);

            // Blokkütközés elutasítása (a blokkok már léteznek)
            TiaResult again = await client.ValidateJobAsync(new TiaJobRequest(Command(copy, package, TiaOperations.ValidateJob, true), TimeSpan.FromMinutes(10)), CancellationToken.None);
            Assert.Equal(ErrorCodes.BlockCollision, again.ErrorCode);

            // Hibás forráshash elutasítása
            string fc = Path.Combine(package.OutputDirectory, "sources", "FC_Cylinders.scl");
            File.AppendAllText(fc, "// tampered\r\n");
            TiaResult tampered = await client.ValidateJobAsync(new TiaJobRequest(Command(copy, package, TiaOperations.ValidateJob, true), TimeSpan.FromMinutes(10)), CancellationToken.None);
            Assert.Equal(ErrorCodes.HashMismatch, tampered.ErrorCode);
        }

        /// <summary>
        /// Blokk-könyvtár körutazás valódi TIA-val: generált blokkok importja sandbox A-ba, export a
        /// könyvtárba (SCL forrás + SimaticML), majd import a könyvtárból egy friss sandbox B-be.
        /// </summary>
        [SkippableFact]
        public async Task LibraryRoundTripForConfiguredVersion()
        {
            RequireTia();
            TiaWorkerProcessClient client = Client();
            SandboxService sandbox = new SandboxService(Path.Combine(temp, "sandbox"), new SystemClock(), new EnvironmentUserContext());
            GenerationResult package = await Generate();

            SandboxCopy source = sandbox.CreateSandboxCopy(knownGood, version);
            TiaCommand import = Command(source, package, TiaOperations.ImportAndCompile, false);
            Approve(import);
            Assert.Equal(TiaJobStatus.Success, (await client.ImportAndCompileAsync(new TiaJobRequest(import, TimeSpan.FromMinutes(30)), CancellationToken.None)).Status);

            TiaCommand list = Command(source, package, TiaOperations.ListBlocks, true);
            TiaResult listed = await client.ListBlocksAsync(new TiaJobRequest(list, TimeSpan.FromMinutes(10)), CancellationToken.None);
            Assert.Equal(TiaJobStatus.Success, listed.Status);
            Assert.Contains(BlockFormats.ExternalSource, listed.Blocks.Single(b => b.Name == "FB_Cylinder").ExportableFormats);

            string exportDir = Path.Combine(temp, "export");
            Directory.CreateDirectory(exportDir);
            TiaCommand export = Command(source, package, TiaOperations.ExportBlocks, true);
            export.ExportDirectory = exportDir;
            export.ExportBlocks = new System.Collections.Generic.List<ExportRequestItem>
            {
                new ExportRequestItem { BlockName = "FB_Cylinder", Format = BlockFormats.ExternalSource },
                new ExportRequestItem { BlockName = "DB_Cylinder_CYL_01", Format = BlockFormats.ExternalSource },
                new ExportRequestItem { BlockName = "FC_Cylinders", Format = BlockFormats.SimaticMl },
            };
            TiaResult exported = await client.ExportBlocksAsync(new TiaJobRequest(export, TimeSpan.FromMinutes(10)), CancellationToken.None);
            Assert.True(exported.Status == TiaJobStatus.Success, string.Join("\n", exported.Messages));

            FileBlockLibraryStore store = new FileBlockLibraryStore(Path.Combine(temp, "libraries"), new SystemClock(), new EnvironmentUserContext());
            store.Create("RoundTrip", null);
            System.Collections.Generic.IReadOnlyList<BlockLibraryItem> items = store.AddExportedBlocks("RoundTrip", exportDir, exported.ExportedFiles, version, null, null);
            Assert.Empty(store.Verify("RoundTrip"));

            SandboxCopy target = sandbox.CreateSandboxCopy(knownGood, version);
            LibraryImportSelection selection = new LibraryImportSelection { LibraryName = "RoundTrip", TargetTiaVersion = version, PlcName = plcName, ItemIds = items.Select(i => i.ItemId).ToList() };
            GenerationResult libraryPackage = store.BuildImportPackage(selection, items, Path.Combine(temp, "library-package"), "it", new string('0', 64), null);
            TiaCommand libraryImport = Command(target, libraryPackage, TiaOperations.ImportAndCompile, false);
            Approve(libraryImport);
            TiaResult result = await client.ImportAndCompileAsync(new TiaJobRequest(libraryImport, TimeSpan.FromMinutes(30)), CancellationToken.None);
            Assert.True(result.Status == TiaJobStatus.Success, string.Join("\n", result.Messages));
            Assert.Equal(SandboxStates.Saved, result.SandboxState);
        }

        [SkippableFact]
        public async Task WrongProjectVersionIsRejected()
        {
            RequireTia();
            string otherVersion = TiaVersions.Supported.First(v => v != version);
            string directory = Path.Combine(temp, "sandbox", otherVersion, "Wrong");
            Directory.CreateDirectory(directory);
            string wrong = Path.Combine(directory, "Wrong" + TiaVersions.ProjectExtension(otherVersion));
            File.WriteAllText(wrong, "not a project");
            GenerationResult package = await Generate();
            TiaCommand command = Command(new SandboxCopy { ProjectPath = wrong }, package, TiaOperations.ValidateJob, true);
            TiaResult result = await Client().ValidateJobAsync(new TiaJobRequest(command, TimeSpan.FromMinutes(5)), CancellationToken.None);
            Assert.Equal(ErrorCodes.ProjectVersionMismatch, result.ErrorCode);
        }
    }
}
