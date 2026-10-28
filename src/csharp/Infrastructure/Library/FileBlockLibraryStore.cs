using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;

namespace TiaStudio.Infrastructure.Library
{
    /// <summary>
    /// Verziófüggetlen blokk-könyvtárak a fájlrendszerben (Git-be tehető):
    /// <code>
    /// &lt;library_root&gt;\&lt;név&gt;\library.json
    /// &lt;library_root&gt;\&lt;név&gt;\items\&lt;blokk&gt;\r&lt;revízió&gt;\&lt;blokk&gt;.scl|.db|.udt|.xml
    /// </code>
    /// Elemfájlt soha nem ír felül: minden újabb export új revízió. Az index atomikusan íródik.
    /// </summary>
    public sealed class FileBlockLibraryStore : IBlockLibraryStore
    {
        private const int MaxDescriptionLength = 200;
        private readonly IClock clock;
        private readonly IUserContext user;
        private readonly object gate = new object();

        public FileBlockLibraryStore(string libraryRoot, IClock clock, IUserContext user)
        {
            LibraryRoot = Path.GetFullPath(libraryRoot);
            this.clock = clock;
            this.user = user;
        }

        public string LibraryRoot { get; }

        public IReadOnlyList<BlockLibrarySummary> ListLibraries()
        {
            if (!Directory.Exists(LibraryRoot))
            {
                return new BlockLibrarySummary[0];
            }
            List<BlockLibrarySummary> result = new List<BlockLibrarySummary>();
            foreach (string directory in Directory.GetDirectories(LibraryRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(Path.Combine(directory, BlockLibraryIndex.FileName)))
                {
                    continue;
                }
                string name = Path.GetFileName(directory);
                try
                {
                    BlockLibraryIndex index = Load(name);
                    result.Add(new BlockLibrarySummary { Name = index.Name, Description = index.Description, ItemCount = index.Items.Count });
                }
                catch (BlockLibraryException ex)
                {
                    result.Add(new BlockLibrarySummary { Name = name, Problem = ex.Message });
                }
            }
            return result;
        }

        public BlockLibraryIndex Create(string name, string description)
        {
            if (!LibraryRules.IsLibraryName(name))
            {
                throw new BlockLibraryException(ErrorCodes.InvalidIdentifier, "Library name '" + name + "' does not match ^[A-Za-z][A-Za-z0-9_-]{0,63}$");
            }
            CheckDescription(description);
            lock (gate)
            {
                string directory = LibraryDirectory(name);
                if (Directory.Exists(directory) || ExistsIgnoringCase(name))
                {
                    throw new BlockLibraryException(ErrorCodes.LibraryExists, "Library '" + name + "' already exists");
                }
                Directory.CreateDirectory(Path.Combine(directory, "items"));
                BlockLibraryIndex index = new BlockLibraryIndex
                {
                    LibraryId = Guid.NewGuid().ToString(),
                    Name = name,
                    Description = string.IsNullOrEmpty(description) ? null : description,
                    CreatedAtUtc = Now(),
                    CreatedBy = user.UserName,
                };
                WriteIndex(name, index);
                return index;
            }
        }

        public BlockLibraryIndex Load(string name)
        {
            if (!LibraryRules.IsLibraryName(name))
            {
                throw new BlockLibraryException(ErrorCodes.LibraryInvalid, "Invalid library name '" + name + "'");
            }
            string path = Path.Combine(LibraryDirectory(name), BlockLibraryIndex.FileName);
            BlockLibraryIndex index;
            try
            {
                index = ProtocolJson.DeserializeLenient<BlockLibraryIndex>(ProtocolJson.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException || ex is ArgumentException)
            {
                throw new BlockLibraryException(ErrorCodes.LibraryInvalid, "Library '" + name + "' cannot be read: " + ex.Message);
            }
            if (index == null || index.LibraryFormatVersion != BlockLibraryIndex.CurrentFormatVersion)
            {
                throw new BlockLibraryException(ErrorCodes.LibraryInvalid, "Library '" + name + "' has an unsupported library_format_version");
            }
            if (!string.Equals(index.Name, name, StringComparison.Ordinal) || index.Items == null || index.Items.Any(i => i == null))
            {
                throw new BlockLibraryException(ErrorCodes.LibraryInvalid, "Library '" + name + "' index is inconsistent (name or items)");
            }
            return index;
        }

        public IReadOnlyList<TiaMessage> Verify(string name)
        {
            BlockLibraryIndex index = Load(name);
            List<TiaMessage> problems = new List<TiaMessage>();
            string root = LibraryDirectory(name);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> revisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BlockLibraryItem item in index.Items)
            {
                string label = item.BlockName + " r" + item.Revision.ToString(CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(item.ItemId) || !ids.Add(item.ItemId) || !revisions.Add(item.BlockName + "#" + item.Revision.ToString(CultureInfo.InvariantCulture)) || item.Revision < 1)
                {
                    problems.Add(Problem(ErrorCodes.LibraryInvalid, "Duplicate or missing item_id / revision", label));
                    continue;
                }
                if (!LibraryRules.IsLibraryBlockName(item.BlockName) || !BlockTypes.All.Contains(item.BlockType) || !BlockFormats.All.Contains(item.Format)
                    || !TiaVersions.IsSupported(item.SourceTiaVersion))
                {
                    problems.Add(Problem(ErrorCodes.LibraryInvalid, "Item metadata is invalid (name, block_type, format or source_tia_version)", label));
                    continue;
                }
                string expected = ExpectedRelativePath(item.BlockName, item.Revision, item.BlockType, item.Format);
                if (!PackagePaths.IsSafeRelativePath(item.RelativePath) || !string.Equals(item.RelativePath, expected, StringComparison.Ordinal))
                {
                    problems.Add(Problem(ErrorCodes.PathEscape, "relative_path must be " + expected, label));
                    continue;
                }
                string file = Path.Combine(root, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if ((File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0))
                {
                    problems.Add(Problem(ErrorCodes.PathEscape, "Item file is a link", label));
                    continue;
                }
                FileInfo info = new FileInfo(file);
                if (!info.Exists || info.Length != item.ByteLength || Sha256.OfFile(file) != item.Sha256)
                {
                    problems.Add(Problem(ErrorCodes.HashMismatch, "Item file is missing or its SHA-256/length differs from library.json", label));
                }
            }
            return problems;
        }

        public IReadOnlyList<BlockLibraryItem> AddExportedBlocks(
            string name,
            string exportDirectory,
            IReadOnlyList<ExportedBlockFile> files,
            string sourceTiaVersion,
            BlockLibraryOrigin origin,
            string description)
        {
            CheckDescription(description);
            if (!TiaVersions.IsSupported(sourceTiaVersion))
            {
                throw new BlockLibraryException(ErrorCodes.UnsupportedTiaVersion, "Unsupported source TIA version " + sourceTiaVersion);
            }
            lock (gate)
            {
                BlockLibraryIndex index = Load(name);
                string root = LibraryDirectory(name);
                string exportRoot = Path.GetFullPath(exportDirectory);
                List<BlockLibraryItem> added = new List<BlockLibraryItem>();
                List<string> written = new List<string>();
                try
                {
                    foreach (ExportedBlockFile file in files)
                    {
                        string problem = LibraryRules.ExportProblem(
                            new ProjectBlockInfo { Name = file.BlockName, BlockType = file.BlockType, DbKind = file.DbKind, ProgrammingLanguage = file.ProgrammingLanguage },
                            file.Format);
                        if (problem != null)
                        {
                            throw new BlockLibraryException(ErrorCodes.LibraryItemNotAllowed, file.BlockName + ": " + problem);
                        }
                        if (!PackagePaths.IsSafeRelativePath(file.RelativePath) || file.RelativePath.Contains("/"))
                        {
                            throw new BlockLibraryException(ErrorCodes.PathEscape, "Unsafe export file name " + file.RelativePath);
                        }
                        string source = Path.Combine(exportRoot, file.RelativePath);
                        byte[] content = File.ReadAllBytes(source);
                        if (Sha256.OfBytes(content) != file.Sha256 || content.Length != file.ByteLength)
                        {
                            throw new BlockLibraryException(ErrorCodes.HashMismatch, "Exported file " + file.RelativePath + " changed after export");
                        }
                        if (added.Any(a => string.Equals(a.BlockName, file.BlockName, StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new BlockLibraryException(ErrorCodes.LibraryDuplicateBlock, file.BlockName + " is exported twice");
                        }
                        int revision = index.Items.Where(i => string.Equals(i.BlockName, file.BlockName, StringComparison.OrdinalIgnoreCase))
                            .Select(i => i.Revision).DefaultIfEmpty(0).Max() + 1;
                        string relative = ExpectedRelativePath(file.BlockName, revision, file.BlockType, file.Format);
                        string target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (FileStream stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                        {
                            stream.Write(content, 0, content.Length);
                            stream.Flush(true);
                        }
                        written.Add(target);
                        added.Add(new BlockLibraryItem
                        {
                            ItemId = Guid.NewGuid().ToString(),
                            BlockName = file.BlockName,
                            BlockType = file.BlockType,
                            DbKind = file.DbKind,
                            Revision = revision,
                            Format = file.Format,
                            SourceTiaVersion = sourceTiaVersion,
                            ProgrammingLanguage = file.ProgrammingLanguage,
                            RelativePath = relative,
                            Sha256 = file.Sha256,
                            ByteLength = content.Length,
                            Description = string.IsNullOrEmpty(description) ? null : description,
                            AddedAtUtc = Now(),
                            AddedBy = user.UserName,
                            Origin = origin,
                        });
                    }
                    index.Items.AddRange(added);
                    WriteIndex(name, index);
                    return added;
                }
                catch (Exception ex) when (ex is BlockLibraryException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    foreach (string path in written)
                    {
                        TryDelete(path);
                    }
                    if (ex is BlockLibraryException)
                    {
                        throw;
                    }
                    throw new BlockLibraryException(ErrorCodes.LibraryInvalid, "Adding exported blocks failed: " + ex.Message);
                }
            }
        }

        public void RemoveItem(string name, string itemId)
        {
            lock (gate)
            {
                BlockLibraryIndex index = Load(name);
                BlockLibraryItem item = index.Items.FirstOrDefault(i => i.ItemId == itemId)
                    ?? throw new BlockLibraryException(ErrorCodes.LibraryItemNotFound, "Library item " + itemId + " not found");
                index.Items.Remove(item);
                WriteIndex(name, index);
                if (PackagePaths.IsSafeRelativePath(item.RelativePath))
                {
                    string file = ItemPath(name, item);
                    TryDelete(file);
                    string revisionDirectory = Path.GetDirectoryName(file);
                    TryDeleteEmpty(revisionDirectory);
                    TryDeleteEmpty(Path.GetDirectoryName(revisionDirectory));
                }
            }
        }

        public string ItemPath(string name, BlockLibraryItem item)
        {
            if (!PackagePaths.IsSafeRelativePath(item.RelativePath))
            {
                throw new BlockLibraryException(ErrorCodes.PathEscape, "Unsafe library item path " + item.RelativePath);
            }
            return Path.Combine(LibraryDirectory(name), item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        public GenerationResult BuildImportPackage(
            LibraryImportSelection selection,
            IReadOnlyList<BlockLibraryItem> items,
            string outputDirectory,
            string runId,
            string selectionSha256,
            ValidationReport validation)
        {
            if (Directory.Exists(outputDirectory))
            {
                throw new BlockLibraryException(ErrorCodes.OutputExists, "Package directory already exists: " + outputDirectory);
            }
            if (items == null || items.Count == 0)
            {
                throw new BlockLibraryException(ErrorCodes.LibraryItemNotFound, "No library items selected");
            }
            string indexPath = Path.Combine(LibraryDirectory(selection.LibraryName), BlockLibraryIndex.FileName);
            BlockLibraryIndex index = Load(selection.LibraryName);
            string staging = outputDirectory + ".staging-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(Path.Combine(staging, "sources"));
            try
            {
                List<BlockLibraryItem> ordered = items
                    .OrderBy(i => LibraryRules.ImportRank(i.BlockType, i.DbKind))
                    .ThenBy(i => i.BlockName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                GenerationManifest manifest = new GenerationManifest
                {
                    ManifestVersion = GenerationManifest.LibraryVersion,
                    PackageKind = GenerationManifest.KindLibraryImport,
                    Generator = "tes_library",
                    GeneratorVersion = typeof(FileBlockLibraryStore).Assembly.GetName().Version.ToString(3),
                    SchemaVersion = null,
                    ProjectName = selection.LibraryName,
                    TargetTiaVersion = selection.TargetTiaVersion,
                    PlcName = selection.PlcName,
                    CpuFamily = null,
                    TemplateSetId = "library:" + index.Name,
                    TemplateSetVersion = index.LibraryId,
                    TemplateSetSha256 = Sha256.OfFile(indexPath),
                    RunId = runId,
                    GeneratedAtUtc = Now(),
                    SourceConfigSha256 = selectionSha256,
                    Encoding = ordered.All(i => i.Format == BlockFormats.ExternalSource) ? "ascii" : "utf-8",
                    LineEnding = "as-exported",
                    ValidationResult = validation == null ? null : JObject.FromObject(validation),
                    SafetyScope = "non-safety",
                    DownloadAllowed = false,
                };
                int order = 1;
                foreach (BlockLibraryItem item in ordered)
                {
                    byte[] content = File.ReadAllBytes(ItemPath(selection.LibraryName, item));
                    if (Sha256.OfBytes(content) != item.Sha256)
                    {
                        throw new BlockLibraryException(ErrorCodes.HashMismatch, "Library item " + item.BlockName + " r" + item.Revision + " changed");
                    }
                    string relative = "sources/" + Path.GetFileName(item.RelativePath);
                    File.WriteAllBytes(Path.Combine(staging, "sources", Path.GetFileName(item.RelativePath)), content);
                    manifest.Files.Add(new ManifestFile
                    {
                        RelativePath = relative,
                        Sha256 = item.Sha256,
                        ByteLength = content.Length,
                        BlockName = item.BlockName,
                        BlockType = item.BlockType,
                        ImportOrder = order++,
                        Format = item.Format,
                    });
                }
                string manifestJson = ProtocolJson.Serialize(manifest) + "\n";
                File.WriteAllText(Path.Combine(staging, GenerationManifest.FileName), manifestJson, ProtocolJson.Utf8NoBom);
                Directory.Move(staging, outputDirectory);
                string manifestPath = Path.Combine(outputDirectory, GenerationManifest.FileName);
                return new GenerationResult
                {
                    Success = true,
                    OutputDirectory = outputDirectory,
                    ManifestPath = manifestPath,
                    ManifestSha256 = Sha256.OfFile(manifestPath),
                    Validation = validation,
                    Message = manifest.Files.Count + " library block(s)",
                };
            }
            catch
            {
                try
                {
                    Directory.Delete(staging, true);
                }
                catch (IOException)
                {
                }
                throw;
            }
        }

        public static string ExpectedRelativePath(string blockName, int revision, string blockType, string format)
        {
            return "items/" + blockName + "/r" + revision.ToString(CultureInfo.InvariantCulture) + "/" + blockName + LibraryRules.FileExtension(blockType, format);
        }

        private string LibraryDirectory(string name)
        {
            return Path.Combine(LibraryRoot, name);
        }

        private bool ExistsIgnoringCase(string name)
        {
            return Directory.Exists(LibraryRoot)
                && Directory.GetDirectories(LibraryRoot).Any(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
        }

        private void WriteIndex(string name, BlockLibraryIndex index)
        {
            ProtocolJson.WriteAtomic(Path.Combine(LibraryDirectory(name), BlockLibraryIndex.FileName), index);
        }

        private static void CheckDescription(string description)
        {
            if (description != null && (description.Length > MaxDescriptionLength || description.Any(char.IsControl)))
            {
                throw new BlockLibraryException(ErrorCodes.InvalidValue, "Description must be a single line of at most " + MaxDescriptionLength + " characters");
            }
        }

        private static TiaMessage Problem(string code, string text, string path)
        {
            return new TiaMessage(MessageSeverity.Error, code, text, path);
        }

        private string Now()
        {
            return clock.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void TryDeleteEmpty(string directory)
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
