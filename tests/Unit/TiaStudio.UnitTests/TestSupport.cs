using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;

namespace TiaStudio.UnitTests
{
    internal static class Repo
    {
        public static string Root { get; } = FindRoot();

        public static string PathOf(params string[] parts)
        {
            return Path.Combine(new[] { Root }.Concat(parts).ToArray());
        }

        private static string FindRoot()
        {
            string current = AppContext.BaseDirectory;
            while (current != null && !File.Exists(Path.Combine(current, "contracts", "error-codes.json")))
            {
                current = Path.GetDirectoryName(current);
            }
            return current ?? throw new InvalidOperationException("Repository root not found");
        }
    }

    /// <summary>Ideiglenes könyvtár, amely Dispose-kor törlődik.</summary>
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tes-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] parts)
        {
            return System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Generált csomag és sandbox előállítása a Python-generátor nélkül (tesztadat).</summary>
    internal static class Fixtures
    {
        public static readonly string[] Blocks = { "FB_Cylinder", "DB_Cylinder_CYL_01", "FC_Cylinders" };

        public static string WritePackage(string root, string tiaVersion = "V18", string plcName = "PLC_1")
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, "sources"));
            GenerationManifest manifest = new GenerationManifest
            {
                ManifestVersion = "1.0",
                Generator = "tes_generator",
                GeneratorVersion = "0.1.0",
                SchemaVersion = "1.1",
                ProjectName = "Cell",
                TargetTiaVersion = tiaVersion,
                PlcName = plcName,
                CpuFamily = "S7-1500",
                TemplateSetId = "tes-scl-core",
                TemplateSetVersion = "1.0.0",
                TemplateSetSha256 = new string('a', 64),
                RunId = "run",
                GeneratedAtUtc = "2026-10-28T00:00:00Z",
                SourceConfigSha256 = new string('b', 64),
                Encoding = "ascii",
                LineEnding = "CRLF",
                SafetyScope = "non-safety",
                DownloadAllowed = false,
            };
            int order = 1;
            foreach (string block in Blocks)
            {
                string relative = "sources/" + block + ".scl";
                byte[] content = Encoding.ASCII.GetBytes("// " + block + "\r\n");
                File.WriteAllBytes(System.IO.Path.Combine(root, "sources", block + ".scl"), content);
                manifest.Files.Add(new ManifestFile
                {
                    RelativePath = relative,
                    Sha256 = Sha256.OfBytes(content),
                    ByteLength = content.Length,
                    BlockName = block,
                    BlockType = block.Substring(0, 2),
                    ImportOrder = order++,
                });
            }
            string manifestPath = System.IO.Path.Combine(root, GenerationManifest.FileName);
            File.WriteAllText(manifestPath, ProtocolJson.Serialize(manifest), new UTF8Encoding(false));
            return manifestPath;
        }

        /// <summary>sandboxRoot/V18/Demo/Demo.ap18 + marker.</summary>
        public static string WriteSandboxProject(string sandboxRoot, string tiaVersion = "V18", bool withMarker = true, string extension = null)
        {
            string directory = System.IO.Path.Combine(sandboxRoot, tiaVersion, "Demo");
            Directory.CreateDirectory(directory);
            string project = System.IO.Path.Combine(directory, "Demo" + (extension ?? TiaVersions.ProjectExtension(tiaVersion)));
            File.WriteAllText(project, "fake TIA project");
            if (withMarker)
            {
                ProtocolJson.WriteAtomic(System.IO.Path.Combine(directory, SandboxMarker.FileName), new SandboxMarker
                {
                    TiaVersion = tiaVersion,
                    ProjectFile = System.IO.Path.GetFileName(project),
                    SourceProjectPath = "C:/known-good/Demo" + TiaVersions.ProjectExtension(tiaVersion),
                    CreatedAtUtc = "2026-10-28T00:00:00Z",
                    CreatedBy = "test",
                });
            }
            return project;
        }

        public static IEnumerable<ManifestFile> ReadManifestFiles(string manifestPath)
        {
            return ProtocolJson.DeserializeLenient<GenerationManifest>(File.ReadAllText(manifestPath)).Files;
        }
    }
}
