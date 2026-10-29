using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;
using TiaStudio.Infrastructure.Generator;
using TiaStudio.Infrastructure.Logging;
using TiaStudio.Infrastructure.Processes;
using TiaStudio.Infrastructure.Sandbox;
using TiaStudio.Infrastructure.Tia;
using Xunit;

namespace TiaStudio.UnitTests
{
    internal sealed class FakeRegistry : IRegistryReader
    {
        public Dictionary<string, List<string>> SubKeys { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Dictionary<string, string>> Values { get; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public void AddInstallation(string versionKey, string apiVersion, string dllPath)
        {
            string root = RegistryTiaInstallationDiscovery.OpennessKey;
            if (!SubKeys.ContainsKey(root))
            {
                SubKeys[root] = new List<string>();
            }
            SubKeys[root].Add(versionKey);
            string publicApi = root + "\\" + versionKey + "\\PublicAPI";
            if (!SubKeys.ContainsKey(publicApi))
            {
                SubKeys[publicApi] = new List<string>();
            }
            SubKeys[publicApi].Add(apiVersion);
            Values[publicApi + "\\" + apiVersion] = new Dictionary<string, string> { { "Siemens.Engineering", dllPath } };
        }

        public IReadOnlyList<string> GetSubKeyNames(string keyPath)
        {
            return SubKeys.TryGetValue(keyPath, out List<string> keys) ? keys : null;
        }

        public IReadOnlyDictionary<string, string> GetStringValues(string keyPath)
        {
            return Values.TryGetValue(keyPath, out Dictionary<string, string> values) ? values : null;
        }
    }

    internal sealed class FakeAssemblies : IAssemblyInspector
    {
        public Dictionary<string, Tuple<Version, string>> Files { get; } = new Dictionary<string, Tuple<Version, string>>();

        public HashSet<string> OtherFiles { get; } = new HashSet<string>();

        /// <summary>TIA nélkül fordított workerek (a link-jelölő openness_linked = false vagy hiányzik).</summary>
        public HashSet<string> UnlinkedWorkers { get; } = new HashSet<string>();

        public bool FileExists(string path)
        {
            return Files.ContainsKey(path) || OtherFiles.Contains(path);
        }

        public Tuple<Version, string> Inspect(string path)
        {
            return Files[path];
        }

        public bool IsOpennessLinkedWorker(string workerPath)
        {
            return !UnlinkedWorkers.Contains(workerPath);
        }
    }

    public class RegistryDiscoveryTests
    {
        private const string Token = RegistryTiaInstallationDiscovery.SiemensPublicKeyToken;

        private static RegistryTiaInstallationDiscovery Discovery(FakeRegistry registry, FakeAssemblies assemblies, params string[] tested)
        {
            foreach (string v in TiaVersions.Supported)
            {
                assemblies.OtherFiles.Add("workers/" + v + ".exe");
            }
            return new RegistryTiaInstallationDiscovery(registry, assemblies, v => "workers/" + v + ".exe", tested);
        }

        [Fact]
        public void NothingInstalled()
        {
            IReadOnlyList<TiaInstallInfo> result = Discovery(new FakeRegistry(), new FakeAssemblies()).DiscoverInstalledVersions();
            Assert.Equal(new[] { "V18", "V19", "V20" }, result.Select(r => r.TiaVersion));
            Assert.All(result, r => Assert.Equal(InstallStatus.NotFound, r.Status));
            Assert.All(result, r => Assert.Equal(ErrorCodes.TiaNotInstalled, r.ErrorCode));
        }

        [Fact]
        public void V19InstalledButNotTested()
        {
            FakeRegistry registry = new FakeRegistry();
            FakeAssemblies assemblies = new FakeAssemblies();
            registry.AddInstallation("19.0", "19.0.0.0", @"C:\P\V19\Siemens.Engineering.dll");
            assemblies.Files[@"C:\P\V19\Siemens.Engineering.dll"] = Tuple.Create(new Version(19, 0, 0, 0), Token);
            TiaInstallInfo v19 = Discovery(registry, assemblies).DiscoverInstalledVersions().Single(i => i.TiaVersion == "V19");
            Assert.Equal(InstallStatus.NotTested, v19.Status);
            Assert.Null(v19.ErrorCode);
            Assert.Equal("19.0.0.0", v19.AssemblyVersion);
        }

        [Fact]
        public void TestedVersionIsInstalled()
        {
            FakeRegistry registry = new FakeRegistry();
            FakeAssemblies assemblies = new FakeAssemblies();
            registry.AddInstallation("18.0", "18.0.0.0", "dll18");
            assemblies.Files["dll18"] = Tuple.Create(new Version(18, 0, 0, 0), Token);
            Assert.Equal(InstallStatus.Installed, Discovery(registry, assemblies, "V18").DiscoverInstalledVersions()[0].Status);
        }

        [Fact]
        public void OlderApiInsideNewerInstallIsNotTreatedAsOlderTia()
        {
            // V20 telepítés V18-as kompatibilitási API-t is szállít: ez NEM jelent V18-as TIA-t.
            FakeRegistry registry = new FakeRegistry();
            FakeAssemblies assemblies = new FakeAssemblies();
            registry.AddInstallation("20.0", "18.0.0.0", "v20-compat-v18");
            registry.AddInstallation("20.0", "20.0.0.0", "v20");
            assemblies.Files["v20"] = Tuple.Create(new Version(20, 0, 0, 0), Token);
            IReadOnlyList<TiaInstallInfo> result = Discovery(registry, assemblies).DiscoverInstalledVersions();
            Assert.Equal(InstallStatus.NotFound, result.Single(i => i.TiaVersion == "V18").Status);
            Assert.Equal(InstallStatus.NotTested, result.Single(i => i.TiaVersion == "V20").Status);
        }

        [Fact]
        public void MissingDllOrWrongTokenIsUnavailable()
        {
            FakeRegistry registry = new FakeRegistry();
            FakeAssemblies assemblies = new FakeAssemblies();
            registry.AddInstallation("18.0", "18.0.0.0", "missing.dll");
            registry.AddInstallation("19.0", "19.0.0.0", "forged.dll");
            assemblies.Files["forged.dll"] = Tuple.Create(new Version(19, 0, 0, 0), "0000000000000000");
            IReadOnlyList<TiaInstallInfo> result = Discovery(registry, assemblies).DiscoverInstalledVersions();
            Assert.Equal(InstallStatus.Unavailable, result[0].Status);
            Assert.Equal(InstallStatus.Unavailable, result[1].Status);
            Assert.Equal(ErrorCodes.ApiDllMismatch, result[1].ErrorCode);
        }

        [Fact]
        public void RegistryAssemblyVersionMustMatchDll()
        {
            FakeRegistry registry = new FakeRegistry();
            FakeAssemblies assemblies = new FakeAssemblies();
            registry.AddInstallation("18.0", "18.0.0.0", "dll18");
            registry.Values[RegistryTiaInstallationDiscovery.OpennessKey + "\\18.0\\PublicAPI\\18.0.0.0"]["AssemblyVersion"] = "18.0.0.0";
            registry.Values[RegistryTiaInstallationDiscovery.OpennessKey + "\\18.0\\PublicAPI\\18.0.0.0"]["PublicKeyToken"] = Token;
            assemblies.Files["dll18"] = Tuple.Create(new Version(18, 0, 0, 0), Token);
            Assert.Equal(InstallStatus.NotTested, Discovery(registry, assemblies).DiscoverInstalledVersions()[0].Status);
            assemblies.Files["dll18"] = Tuple.Create(new Version(18, 0, 1, 0), Token);
            TiaInstallInfo mismatch = Discovery(registry, assemblies).DiscoverInstalledVersions()[0];
            Assert.Equal(InstallStatus.Unavailable, mismatch.Status);
            Assert.Equal(ErrorCodes.ApiDllMismatch, mismatch.ErrorCode);
        }

        [Fact]
        public void UnlinkedWorkerIsUnavailableEvenWithTiaInstalled()
        {
            // Audit A-04: a TIA nélkül épített worker .exe.config-írással sem válik Openness-workerré.
            FakeRegistry registry = new FakeRegistry();
            FakeAssemblies assemblies = new FakeAssemblies();
            registry.AddInstallation("18.0", "18.0.0.0", "dll18");
            assemblies.Files["dll18"] = Tuple.Create(new Version(18, 0, 0, 0), Token);
            assemblies.UnlinkedWorkers.Add("workers/V18.exe");
            TiaInstallInfo info = Discovery(registry, assemblies, "V18").DiscoverInstalledVersions()[0];
            Assert.Equal(InstallStatus.Unavailable, info.Status);
            Assert.Equal(ErrorCodes.WorkerBuiltWithoutOpenness, info.ErrorCode);
            Assert.Contains(info.Notes, n => n.Contains("-RequireOpenness"));
        }

        [Theory]
        [InlineData("{ \"tia_version\": \"V18\", \"openness_linked\": true }", true)]
        [InlineData("\uFEFF{ \"tia_version\": \"V18\", \"openness_linked\": true }", true)]
        [InlineData("{ \"tia_version\": \"V18\", \"openness_linked\": false }", false)]
        [InlineData("{ \"openness_linked\": \"true\" }", false)]
        [InlineData("not json", false)]
        [InlineData(null, false)]
        public void LinkMarkerNextToTheWorkerIsReadFailClosed(string marker, bool linked)
        {
            using (TempDir temp = new TempDir())
            {
                string worker = temp.Combine("TiaStudio.TiaAdapter.V18.exe");
                if (marker != null)
                {
                    File.WriteAllText(temp.Combine("TiaStudio.TiaAdapter.V18.openness.json"), marker);
                }
                Assert.Equal(linked, new FileAssemblyInspector().IsOpennessLinkedWorker(worker));
            }
        }

        [Fact]
        public void MissingWorkerIsUnavailable()
        {
            FakeRegistry registry = new FakeRegistry();
            FakeAssemblies assemblies = new FakeAssemblies();
            registry.AddInstallation("18.0", "18.0.0.0", "dll18");
            assemblies.Files["dll18"] = Tuple.Create(new Version(18, 0, 0, 0), Token);
            TiaInstallInfo info = new RegistryTiaInstallationDiscovery(registry, assemblies, v => "nowhere.exe", null).DiscoverInstalledVersions()[0];
            Assert.Equal(InstallStatus.Unavailable, info.Status);
        }
    }

    public class GeneratorParsingTests
    {
        [Fact]
        public void SuccessLineIsParsed()
        {
            string output = Path.Combine(Path.GetTempPath(), "out");
            ProcessRunResult run = new ProcessRunResult
            {
                ExitCode = 0,
                StandardOutput = "{\"status\": \"Success\", \"output_directory\": " + Newtonsoft.Json.JsonConvert.ToString(output) + ", \"manifest_path\": \"m\", \"manifest_sha256\": \"abc\"}\n",
            };
            GenerationResult result = PythonSourceGenerator.Parse(run, new GenerationRequest { OutputDirectory = output });
            Assert.True(result.Success);
            Assert.Equal("abc", result.ManifestSha256);
        }

        [Fact]
        public void ValidationFailureCarriesCode()
        {
            ProcessRunResult run = new ProcessRunResult { ExitCode = 2, StandardOutput = "{\"status\": \"ValidationFailed\", \"code\": \"UNSUPPORTED_TIA_VERSION\"}" };
            GenerationResult result = PythonSourceGenerator.Parse(run, new GenerationRequest { OutputDirectory = "x" });
            Assert.False(result.Success);
            Assert.Equal(ErrorCodes.UnsupportedTiaVersion, result.ErrorCode);
        }

        [Fact]
        public void GarbageOutputIsGeneratorFailure()
        {
            GenerationResult result = PythonSourceGenerator.Parse(new ProcessRunResult { ExitCode = 1, StandardOutput = "Traceback..." }, new GenerationRequest());
            Assert.Equal(ErrorCodes.GeneratorFailed, result.ErrorCode);
        }

        [Fact]
        public void UnexpectedOutputDirectoryIsRejected()
        {
            ProcessRunResult run = new ProcessRunResult { ExitCode = 0, StandardOutput = "{\"status\": \"Success\", \"output_directory\": \"/elsewhere\"}" };
            Assert.False(PythonSourceGenerator.Parse(run, new GenerationRequest { OutputDirectory = "/expected" }).Success);
        }
    }

    public class MiscInfrastructureTests
    {
        [Theory]
        [InlineData(new[] { "a", "b" }, "a b")]
        [InlineData(new[] { "C:\\Program Files\\x" }, "\"C:\\Program Files\\x\"")]
        [InlineData(new[] { "say \"hi\"" }, "\"say \\\"hi\\\"\"")]
        [InlineData(new[] { "" }, "\"\"")]
        [InlineData(new[] { "dir with space\\" }, "\"dir with space\\\\\"")]
        public void WindowsArgumentQuoting(string[] arguments, string expected)
        {
            Assert.Equal(expected, ProcessRunner.JoinArguments(arguments));
        }

        [Fact]
        public void AuditLogRedactsSecretsAndAppendsJsonl()
        {
            using (TempDir temp = new TempDir())
            {
                JsonlAuditLog log = new JsonlAuditLog(temp.Path, new FixedClock());
                log.Write(new AuditEvent { Event = "A", Details = new Dictionary<string, object> { { "password", "hunter2" }, { "api_token", "x" }, { "plc", "PLC_1" } } });
                log.Write(new AuditEvent { Event = "B" });
                string[] lines = File.ReadAllLines(log.CurrentFile);
                Assert.Equal(2, lines.Length);
                Assert.DoesNotContain("hunter2", lines[0]);
                Assert.DoesNotContain("\"x\"", lines[0]);
                Assert.Contains("PLC_1", lines[0]);
                Newtonsoft.Json.Linq.JObject.Parse(lines[1]);
            }
        }

        [Fact]
        public void SandboxCopyIsCompleteAndMarked()
        {
            using (TempDir temp = new TempDir())
            {
                string source = temp.Combine("known-good", "Demo");
                Directory.CreateDirectory(Path.Combine(source, "System"));
                File.WriteAllText(Path.Combine(source, "Demo.ap19"), "project");
                File.WriteAllText(Path.Combine(source, "System", "data.bin"), "data");
                SandboxService service = new SandboxService(temp.Combine("sandbox"), new FixedClock(), new TestUser());

                SandboxCopy copy = service.CreateSandboxCopy(Path.Combine(source, "Demo.ap19"), "V19");

                Assert.True(File.Exists(Path.Combine(copy.SandboxDirectory, "System", "data.bin")));
                Assert.Null(service.Verify(copy.ProjectPath, "V19"));
                Assert.Equal(ErrorCodes.ProjectVersionMismatch, service.Verify(copy.ProjectPath, "V20").Item1);
                Assert.Equal("project", File.ReadAllText(Path.Combine(source, "Demo.ap19")));
                Assert.False(File.Exists(Path.Combine(source, SandboxMarker.FileName)));
                Assert.True(Guid.TryParseExact(copy.Marker.SandboxId, "D", out Guid _));
                Assert.Equal(copy.Marker.SandboxId, SandboxRules.ReadMarker(copy.ProjectPath).SandboxId);
            }
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void SandboxCopyRejectsLinkedFiles()
        {
            // Audit A-06: linkelt fájlt tartalmazó ismert jó projektből nem készül sandbox.
            if (!OperatingSystem.IsLinux())
            {
                return;
            }
            using (TempDir temp = new TempDir())
            {
                string source = temp.Combine("known-good", "Demo");
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "Demo.ap18"), "project");
                File.WriteAllText(temp.Combine("elsewhere.bin"), "data");
                File.CreateSymbolicLink(Path.Combine(source, "data.bin"), temp.Combine("elsewhere.bin"));
                SandboxService service = new SandboxService(temp.Combine("sandbox"), new FixedClock(), new TestUser());
                Assert.Equal(ErrorCodes.PathEscape, Assert.Throws<SandboxException>(() => service.CreateSandboxCopy(Path.Combine(source, "Demo.ap18"), "V18")).ErrorCode);
            }
        }
#endif

        [Fact]
        public void WriteAtomicReplacesWithoutLeavingTemporaryFiles()
        {
            // Audit A-08: a célfájl csere közben sem hiányzik; sikertelen írásnál az eredeti marad.
            using (TempDir temp = new TempDir())
            {
                string path = temp.Combine("library.json");
                ProtocolJson.WriteAtomic(path, new SandboxMarker { TiaVersion = "V18" });
                ProtocolJson.WriteAtomic(path, new SandboxMarker { TiaVersion = "V19" });
                Assert.Equal("V19", ProtocolJson.DeserializeLenient<SandboxMarker>(ProtocolJson.ReadAllText(path)).TiaVersion);
                Assert.Equal(new[] { "library.json" }, Directory.GetFiles(temp.Path).Select(Path.GetFileName).ToArray());

                Assert.ThrowsAny<Exception>(() => ProtocolJson.WriteAtomic(path, new SelfReferencing()));
                Assert.Equal("V19", ProtocolJson.DeserializeLenient<SandboxMarker>(ProtocolJson.ReadAllText(path)).TiaVersion);
            }
        }

        private sealed class SelfReferencing
        {
            public SelfReferencing Self => this;
        }

        [Fact]
        public void SandboxCopyRejectsWrongVersionAndNesting()
        {
            using (TempDir temp = new TempDir())
            {
                string source = temp.Combine("p");
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "Demo.ap20"), "x");
                SandboxService service = new SandboxService(temp.Combine("sandbox"), new FixedClock(), new TestUser());
                Assert.Equal(ErrorCodes.ProjectVersionMismatch, Assert.Throws<SandboxException>(() => service.CreateSandboxCopy(Path.Combine(source, "Demo.ap20"), "V18")).ErrorCode);
                SandboxService nested = new SandboxService(Path.Combine(source, "sandbox"), new FixedClock(), new TestUser());
                Assert.Equal(ErrorCodes.ProjectNotSandbox, Assert.Throws<SandboxException>(() => nested.CreateSandboxCopy(Path.Combine(source, "Demo.ap20"), "V20")).ErrorCode);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task WorkerClientReportsMissingWorker()
        {
            TiaCommand command = new TiaCommand { JobId = Guid.NewGuid().ToString(), Operation = TiaOperations.ImportAndCompile, TiaVersion = "V18" };
            TiaWorkerProcessClient client = new TiaWorkerProcessClient(null, v => "/does/not/exist.exe", Path.GetTempPath(), TimeSpan.FromSeconds(1));
            TiaResult result = await client.ImportAndCompileAsync(new TiaJobRequest(command, TimeSpan.FromSeconds(1)), System.Threading.CancellationToken.None);
            Assert.Equal(ErrorCodes.TiaNotInstalled, result.ErrorCode);
            Assert.Equal(SandboxStates.NotTouched, result.SandboxState);
        }
    }
}

namespace TiaStudio.UnitTests
{
    public class ProcessTreeTests
    {
        /// <summary>
        /// Audit A-07: időtúllépésnél a worker által indított gyerekfolyamat (a TIA Portal helyett itt egy
        /// ping.exe) is leáll. Windows Job Object nélkül (más OS) a futtató csak a fő folyamatot állítja le.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task TimeoutTerminatesTheWholeProcessTreeOnWindows()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return;
            }
            using (TempDir temp = new TempDir())
            {
                string pidFile = temp.Combine("child.pid");
                string script = "$c = [System.Diagnostics.Process]::Start('ping.exe', '-n 300 127.0.0.1'); Set-Content -LiteralPath '" + pidFile
                    + "' -Value $c.Id; Start-Sleep -Seconds 300";
                ProcessRunResult run = await ProcessRunner.RunAsync(
                    "powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", script }, temp.Path, null, TimeSpan.FromSeconds(20), System.Threading.CancellationToken.None);
                Assert.True(run.TimedOut);
                Assert.True(File.Exists(pidFile), "the child process did not start in time");
                Assert.Equal(0, run.RemainingProcesses);
                Assert.Contains("process tree", run.ProcessTreeNote);
                int pid = int.Parse(File.ReadAllText(pidFile).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                Assert.Throws<ArgumentException>(() => System.Diagnostics.Process.GetProcessById(pid));
            }
        }
    }
}

namespace TiaStudio.UnitTests
{
    public class SourcePreviewTests
    {
        /// <summary>Audit A-12: hibás vagy eltérő tartalom nem jelenik meg forrásként és nem számít átnézettnek.</summary>
        [Fact]
        public void OnlyReadableContentMatchingTheManifestIsShown()
        {
            using (TempDir temp = new TempDir())
            {
                string path = temp.Combine("FB_Cylinder.scl");
                File.WriteAllText(path, "FUNCTION_BLOCK \"FB_Cylinder\"\r\n");
                string hash = TiaStudio.Contracts.Integrity.Sha256.OfFile(path);

                Assert.Equal("FUNCTION_BLOCK \"FB_Cylinder\"\r\n", SourcePreview.Read(path, hash, out string error));
                Assert.Null(error);

                File.AppendAllText(path, "// edit\r\n");
                Assert.Null(SourcePreview.Read(path, hash, out error));
                Assert.Contains("SHA-256", error);

                Assert.Null(SourcePreview.Read(temp.Combine("missing.scl"), hash, out error));
                Assert.Contains("nem olvasható", error);
            }
        }
    }
}
