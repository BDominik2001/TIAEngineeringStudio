using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace TiaStudio.Contracts
{
    /// <summary>Blokk-tárolási formátumok a könyvtárban és az importcsomagban.</summary>
    public static class BlockFormats
    {
        /// <summary>TIA külső forrás (SCL/DB/UDT szöveg): verziófüggetlen, a célverzióban fordítással igazolandó.</summary>
        public const string ExternalSource = "external_source";

        /// <summary>SimaticML XML: bármely nyelv, de csak a forrás TIA-verziójába importálható.</summary>
        public const string SimaticMl = "simaticml";

        public static readonly IReadOnlyList<string> All = new[] { ExternalSource, SimaticMl };
    }

    public static class BlockTypes
    {
        public const string FB = "FB";
        public const string FC = "FC";
        public const string DB = "DB";
        public const string UDT = "UDT";
        public const string OB = "OB";

        public static readonly IReadOnlyList<string> All = new[] { FB, FC, DB, UDT, OB };
    }

    public static class DbKinds
    {
        public const string Global = "Global";
        public const string Instance = "Instance";
        public const string Array = "Array";
    }

    /// <summary>A PLC egy blokkjának/adattípusának leírása (worker ListBlocks eredménye).</summary>
    public sealed class ProjectBlockInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("block_type")]
        public string BlockType { get; set; }

        [JsonProperty("db_kind")]
        public string DbKind { get; set; }

        [JsonProperty("number")]
        public int? Number { get; set; }

        [JsonProperty("programming_language")]
        public string ProgrammingLanguage { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("is_know_how_protected")]
        public bool IsKnowHowProtected { get; set; }

        [JsonProperty("is_safety")]
        public bool IsSafety { get; set; }

        [JsonProperty("is_consistent")]
        public bool IsConsistent { get; set; } = true;

        [JsonProperty("exportable_formats")]
        public List<string> ExportableFormats { get; set; } = new List<string>();

        [JsonProperty("not_exportable_reason")]
        public string NotExportableReason { get; set; }
    }

    public sealed class ExportRequestItem
    {
        [JsonProperty("block_name", Required = Required.Always)]
        public string BlockName { get; set; }

        [JsonProperty("format", Required = Required.Always)]
        public string Format { get; set; }
    }

    /// <summary>A worker által exportált fájl (export_directory-hoz relatív útvonallal).</summary>
    public sealed class ExportedBlockFile
    {
        [JsonProperty("block_name")]
        public string BlockName { get; set; }

        [JsonProperty("block_type")]
        public string BlockType { get; set; }

        [JsonProperty("db_kind")]
        public string DbKind { get; set; }

        [JsonProperty("programming_language")]
        public string ProgrammingLanguage { get; set; }

        [JsonProperty("format")]
        public string Format { get; set; }

        [JsonProperty("relative_path")]
        public string RelativePath { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("byte_length")]
        public long ByteLength { get; set; }
    }

    /// <summary>contracts/block-library.schema.json – a könyvtár indexe (library.json).</summary>
    public sealed class BlockLibraryIndex
    {
        public const string FileName = "library.json";
        public const string CurrentFormatVersion = "1.0";

        [JsonProperty("library_format_version")]
        public string LibraryFormatVersion { get; set; } = CurrentFormatVersion;

        [JsonProperty("library_id")]
        public string LibraryId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("created_at_utc")]
        public string CreatedAtUtc { get; set; }

        [JsonProperty("created_by")]
        public string CreatedBy { get; set; }

        [JsonProperty("items")]
        public List<BlockLibraryItem> Items { get; set; } = new List<BlockLibraryItem>();

        /// <summary>Az egyes blokknevek legfrissebb revíziói.</summary>
        public IEnumerable<BlockLibraryItem> LatestItems()
        {
            return Items.GroupBy(i => i.BlockName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(i => i.Revision).First())
                .OrderBy(i => i.BlockName, StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class BlockLibraryItem
    {
        [JsonProperty("item_id")]
        public string ItemId { get; set; }

        [JsonProperty("block_name")]
        public string BlockName { get; set; }

        [JsonProperty("block_type")]
        public string BlockType { get; set; }

        [JsonProperty("db_kind")]
        public string DbKind { get; set; }

        [JsonProperty("revision")]
        public int Revision { get; set; }

        [JsonProperty("format")]
        public string Format { get; set; }

        [JsonProperty("source_tia_version")]
        public string SourceTiaVersion { get; set; }

        [JsonProperty("programming_language")]
        public string ProgrammingLanguage { get; set; }

        [JsonProperty("relative_path")]
        public string RelativePath { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("byte_length")]
        public long ByteLength { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("added_at_utc")]
        public string AddedAtUtc { get; set; }

        [JsonProperty("added_by")]
        public string AddedBy { get; set; }

        [JsonProperty("origin")]
        public BlockLibraryOrigin Origin { get; set; }
    }

    public sealed class BlockLibraryOrigin
    {
        [JsonProperty("project_file")]
        public string ProjectFile { get; set; }

        [JsonProperty("plc_name")]
        public string PlcName { get; set; }

        [JsonProperty("job_id")]
        public string JobId { get; set; }
    }

    /// <summary>
    /// A könyvtárba vehető és a könyvtárból importálható blokkok közös szabályai. A főalkalmazás
    /// és a worker is ezt alkalmazza, így az export- és importoldali döntés azonos.
    /// </summary>
    public static class LibraryRules
    {
        public const int MaxBlockNameLength = 125;

        /// <summary>^[A-Za-z][A-Za-z0-9_]{0,124}$ – fájlnévként és külső forrásnévként is biztonságos.</summary>
        public static bool IsLibraryBlockName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxBlockNameLength || !IsAsciiLetter(name[0]))
            {
                return false;
            }
            return name.All(c => IsAsciiLetter(c) || (c >= '0' && c <= '9') || c == '_');
        }

        /// <summary>^[A-Za-z][A-Za-z0-9_\-]{0,63}$ – a könyvtár mappaneve is.</summary>
        public static bool IsLibraryName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64 || !IsAsciiLetter(name[0]))
            {
                return false;
            }
            return name.All(c => IsAsciiLetter(c) || (c >= '0' && c <= '9') || c == '_' || c == '-');
        }

        /// <summary>Exportálhatóság (null = exportálható a megadott formátumban), indoklással.</summary>
        public static string ExportProblem(ProjectBlockInfo block, string format)
        {
            if (!BlockFormats.All.Contains(format))
            {
                return "Unknown format '" + format + "'";
            }
            string general = GeneralProblem(block.Name, block.BlockType, block.ProgrammingLanguage, block.IsSafety, block.IsKnowHowProtected);
            if (general != null)
            {
                return general;
            }
            if (!block.IsConsistent)
            {
                return "Block is inconsistent; compile the project before exporting";
            }
            if (format == BlockFormats.ExternalSource && !SupportsExternalSource(block.BlockType, block.ProgrammingLanguage))
            {
                return "Only SCL code blocks, data blocks and PLC data types can be exported as external source; use SimaticML";
            }
            if (format == BlockFormats.SimaticMl && !SupportsSimaticMl(block.BlockType, block.DbKind))
            {
                return "SimaticML export/import supports FB, FC, global DB and PLC data types only; store instance/array DBs as external source (.db)";
            }
            return null;
        }

        public static List<string> ExportableFormats(ProjectBlockInfo block)
        {
            return BlockFormats.All.Where(f => ExportProblem(block, f) == null).ToList();
        }

        /// <summary>Importálhatóság a célverzióba: (hibakód, üzenet) vagy null.</summary>
        public static Tuple<string, string> ImportProblem(BlockLibraryItem item, string targetTiaVersion)
        {
            if (!TiaVersions.IsSupported(targetTiaVersion))
            {
                return Tuple.Create(ErrorCodes.UnsupportedTiaVersion, "Unsupported target " + targetTiaVersion);
            }
            if (!BlockFormats.All.Contains(item.Format))
            {
                return Tuple.Create(ErrorCodes.LibraryInvalid, "Unknown format '" + item.Format + "'");
            }
            string general = GeneralProblem(item.BlockName, item.BlockType, item.ProgrammingLanguage, false, false);
            if (general != null)
            {
                return Tuple.Create(ErrorCodes.LibraryItemNotAllowed, general);
            }
            if (item.Format == BlockFormats.ExternalSource && !SupportsExternalSource(item.BlockType, item.ProgrammingLanguage))
            {
                return Tuple.Create(ErrorCodes.LibraryItemNotAllowed, "External source is not valid for " + item.BlockType + "/" + item.ProgrammingLanguage);
            }
            if (item.Format == BlockFormats.SimaticMl && !SupportsSimaticMl(item.BlockType, item.DbKind))
            {
                return Tuple.Create(ErrorCodes.LibraryItemNotAllowed, "SimaticML import supports FB, FC, global DB and PLC data types only");
            }
            if (item.Format == BlockFormats.SimaticMl && item.SourceTiaVersion != targetTiaVersion)
            {
                return Tuple.Create(ErrorCodes.LibraryVersionMismatch,
                    "SimaticML of " + item.BlockName + " was exported from " + item.SourceTiaVersion + " and cannot be imported into " + targetTiaVersion +
                    ". Export it again from a " + targetTiaVersion + " project or store it as external source.");
            }
            return null;
        }

        /// <summary>Fájlkiterjesztés: SCL-kód .scl, DB .db, UDT .udt, SimaticML .xml.</summary>
        public static string FileExtension(string blockType, string format)
        {
            if (format == BlockFormats.SimaticMl)
            {
                return ".xml";
            }
            switch (blockType)
            {
                case BlockTypes.DB:
                    return ".db";
                case BlockTypes.UDT:
                    return ".udt";
                default:
                    return ".scl";
            }
        }

        /// <summary>Importálási sorrend: UDT → FC → FB → globális/tömb DB → példány-DB.</summary>
        public static int ImportRank(string blockType, string dbKind)
        {
            switch (blockType)
            {
                case BlockTypes.UDT:
                    return 0;
                case BlockTypes.FC:
                    return 1;
                case BlockTypes.FB:
                    return 2;
                case BlockTypes.DB:
                    return dbKind == DbKinds.Instance ? 4 : 3;
                default:
                    return 5;
            }
        }

        /// <summary>Külső forrás: ASCII (kezdő UTF-8 BOM megengedett) – V18 külső forrás követelménye.</summary>
        public static bool IsAsciiExternalSource(byte[] content)
        {
            int start = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
            for (int i = start; i < content.Length; i++)
            {
                if (content[i] > 0x7F)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>SimaticML-ben F-nyelvű (safety) blokk jelölése.</summary>
        public static bool SimaticMlContainsSafety(byte[] content)
        {
            string text = System.Text.Encoding.UTF8.GetString(content);
            return text.IndexOf("<ProgrammingLanguage>F_", StringComparison.Ordinal) >= 0
                || text.IndexOf("<ProgrammingLanguage>Safety", StringComparison.Ordinal) >= 0;
        }

        public static bool IsSafetyLanguage(string programmingLanguage)
        {
            return !string.IsNullOrEmpty(programmingLanguage)
                && (programmingLanguage.StartsWith("F_", StringComparison.OrdinalIgnoreCase) || programmingLanguage.IndexOf("Safety", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GeneralProblem(string name, string blockType, string language, bool isSafety, bool knowHowProtected)
        {
            if (!IsLibraryBlockName(name))
            {
                return "Block name '" + name + "' does not follow the library naming rule ^[A-Za-z][A-Za-z0-9_]{0,124}$";
            }
            if (blockType == BlockTypes.OB)
            {
                return "Organization blocks are not stored in libraries (they change the PLC execution model)";
            }
            if (!BlockTypes.All.Contains(blockType))
            {
                return "Unsupported block type '" + blockType + "'";
            }
            if (isSafety || IsSafetyLanguage(language))
            {
                return "Safety (F-) blocks are never exported or imported by this tool";
            }
            if (knowHowProtected)
            {
                return "Know-how protected blocks cannot be exported";
            }
            return null;
        }

        /// <summary>
        /// TIA Portal Openness V18 kézikönyv 6.4.1.12/6.4.1.13 (blokkok: FB, FC, OB, globális DB) és
        /// 6.4.1.27/6.4.1.28 (PLC-adattípusok). Példány- és tömb-DB XML-exportja nem dokumentált.
        /// </summary>
        private static bool SupportsSimaticMl(string blockType, string dbKind)
        {
            switch (blockType)
            {
                case BlockTypes.FB:
                case BlockTypes.FC:
                case BlockTypes.UDT:
                    return true;
                case BlockTypes.DB:
                    return dbKind == DbKinds.Global;
                default:
                    return false;
            }
        }

        /// <summary>V18 kézikönyv 5.11.3.18: forrásgenerálás csak STL/SCL blokkokból, DB-kből és UDT-kből (STL itt kizárva).</summary>
        private static bool SupportsExternalSource(string blockType, string language)
        {
            switch (blockType)
            {
                case BlockTypes.DB:
                case BlockTypes.UDT:
                    return true;
                case BlockTypes.FB:
                case BlockTypes.FC:
                    return string.Equals(language, "SCL", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }
    }
}
