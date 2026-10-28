using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaStudio.Contracts.Integrity
{
    public sealed class PackageVerificationResult
    {
        public PackageVerificationResult(GenerationManifest manifest, string manifestSha256, IReadOnlyList<TiaMessage> problems)
        {
            Manifest = manifest;
            ManifestSha256 = manifestSha256;
            Problems = problems;
        }

        public GenerationManifest Manifest { get; }

        public string ManifestSha256 { get; }

        public IReadOnlyList<TiaMessage> Problems { get; }

        public bool Ok => Problems.Count == 0;

        /// <summary>Az első probléma kódja (MANIFEST_INVALID / PATH_ESCAPE / HASH_MISMATCH), vagy null.</summary>
        public string FirstErrorCode => Problems.Count == 0 ? null : Problems[0].Code;
    }

    /// <summary>
    /// Generált csomag ellenőrzése jóváhagyáskor és import előtt (spec 6.5, 10.1/7):
    /// manifest-szerkezet, útvonal-kiszökés, symlink, SHA-256 és bájthossz újraszámolása.
    /// Ugyanazt a szabályt követi, mint a src/python/generator/manifest.py verify_package().
    /// </summary>
    public static class PackageVerifier
    {
        public static PackageVerificationResult Verify(string packageRoot, string manifestPath = null)
        {
            List<TiaMessage> problems = new List<TiaMessage>();
            manifestPath = manifestPath ?? Path.Combine(packageRoot ?? string.Empty, GenerationManifest.FileName);
            GenerationManifest manifest;
            string manifestSha;
            try
            {
                if (string.IsNullOrEmpty(packageRoot) || !Path.IsPathRooted(packageRoot))
                {
                    throw new InvalidDataException("package root must be an absolute path");
                }
                if (!IsInside(Path.GetFullPath(packageRoot), Path.GetFullPath(manifestPath)))
                {
                    throw new InvalidDataException("manifest must be inside the package root");
                }
                byte[] bytes = File.ReadAllBytes(manifestPath);
                manifestSha = Sha256.OfBytes(bytes);
                manifest = ProtocolJson.DeserializeLenient<GenerationManifest>(ProtocolJson.Utf8NoBom.GetString(bytes));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException || ex is InvalidDataException || ex is ArgumentException || ex is NotSupportedException)
            {
                problems.Add(new TiaMessage(MessageSeverity.Error, ErrorCodes.ManifestInvalid, "Manifest cannot be read: " + ex.Message, manifestPath));
                return new PackageVerificationResult(null, null, problems);
            }

            string structural = StructuralProblem(manifest);
            if (structural != null)
            {
                problems.Add(new TiaMessage(MessageSeverity.Error, ErrorCodes.ManifestInvalid, structural, manifestPath));
                return new PackageVerificationResult(manifest, manifestSha, problems);
            }

            string root = Path.GetFullPath(packageRoot);
            foreach (ManifestFile file in manifest.Files)
            {
                if (!PackagePaths.IsSafeRelativePath(file.RelativePath))
                {
                    problems.Add(new TiaMessage(MessageSeverity.Error, ErrorCodes.PathEscape, "Unsafe relative path in manifest", file.RelativePath));
                    continue;
                }
                string full = Path.GetFullPath(Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsInside(root, full) || HasReparsePoint(root, full))
                {
                    problems.Add(new TiaMessage(MessageSeverity.Error, ErrorCodes.PathEscape, "Path leaves the package root or is a link", file.RelativePath));
                    continue;
                }
                try
                {
                    FileInfo info = new FileInfo(full);
                    if (!info.Exists || info.Length != file.ByteLength || Sha256.OfFile(full) != file.Sha256)
                    {
                        problems.Add(new TiaMessage(MessageSeverity.Error, ErrorCodes.HashMismatch, "File is missing or its SHA-256/length differs from the manifest", file.RelativePath));
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    problems.Add(new TiaMessage(MessageSeverity.Error, ErrorCodes.HashMismatch, "File cannot be read: " + ex.Message, file.RelativePath));
                }
            }
            return new PackageVerificationResult(manifest, manifestSha, problems);
        }

        private static string StructuralProblem(GenerationManifest manifest)
        {
            if (manifest == null)
            {
                return "Manifest is empty";
            }
            bool v10 = manifest.ManifestVersion == GenerationManifest.CurrentVersion;
            if (!v10 && manifest.ManifestVersion != GenerationManifest.LibraryVersion)
            {
                return "Unsupported manifest_version: " + manifest.ManifestVersion;
            }
            if (v10 && (manifest.PackageKind != null || manifest.Files != null && manifest.Files.Any(f => f != null && f.Format != null)))
            {
                return "manifest_version 1.0 must not contain package_kind or format";
            }
            if (!v10 && manifest.PackageKind != GenerationManifest.KindGenerated && manifest.PackageKind != GenerationManifest.KindLibraryImport)
            {
                return "Unsupported package_kind: " + manifest.PackageKind;
            }
            if (manifest.DownloadAllowed != false)
            {
                return "download_allowed must be false";
            }
            if (manifest.SafetyScope != "non-safety")
            {
                return "safety_scope must be non-safety";
            }
            if (!TiaVersions.IsSupported(manifest.TargetTiaVersion))
            {
                return "Unsupported target_tia_version: " + manifest.TargetTiaVersion;
            }
            if (manifest.Files == null || manifest.Files.Count == 0)
            {
                return "Manifest lists no files";
            }
            if (manifest.Files.Any(f => f == null || string.IsNullOrEmpty(f.BlockName) || string.IsNullOrEmpty(f.Sha256) || f.ImportOrder < 1))
            {
                return "Manifest file entry is incomplete";
            }
            if (manifest.Files.Any(f => !BlockFormats.All.Contains(f.EffectiveFormat) || !BlockTypes.All.Contains(f.BlockType)))
            {
                return "Manifest file entry has an unknown format or block_type";
            }
            if (manifest.Files.Select(f => f.ImportOrder).Distinct().Count() != manifest.Files.Count)
            {
                return "import_order values must be unique";
            }
            if (manifest.Files.Select(f => f.BlockName.ToUpperInvariant()).Distinct().Count() != manifest.Files.Count)
            {
                return "block_name values must be unique";
            }
            return null;
        }

        private static bool IsInside(string root, string candidate)
        {
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return candidate.StartsWith(prefix, comparison);
        }

        /// <summary>A gyökér alatti útvonal bármely eleme symlink/junction-e.</summary>
        private static bool HasReparsePoint(string root, string full)
        {
            string current = full;
            while (current != null && current.Length > root.TrimEnd(Path.DirectorySeparatorChar).Length)
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }
                current = Path.GetDirectoryName(current);
            }
            return false;
        }
    }

    public static class PackagePaths
    {
        /// <summary>Előre-perjeles relatív útvonal, '..', '.', üres elem, backslash, kettőspont és NUL nélkül.</summary>
        public static bool IsSafeRelativePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || relativePath.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }
            if (relativePath.IndexOfAny(new[] { '\\', ':', '\0' }) >= 0)
            {
                return false;
            }
            return relativePath.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
        }
    }
}
