using System;
using System.Collections.Generic;
using System.IO;

namespace TiaStudio.Contracts.Integrity
{
    /// <summary>
    /// Projektútvonal-szabályok, amelyeket a főalkalmazás jóváhagyáskor és a worker import
    /// előtt is ellenőriz (spec 3.3, 10.1/1-2): verzióhelyes kiterjesztés, sandbox gyökér
    /// alatti hely, link (junction/symlink) nélküli útvonal és projektmappa, valamint érvényes
    /// .tes-sandbox.json marker sandbox-azonosítóval.
    /// </summary>
    public static class SandboxRules
    {
        /// <summary>null = rendben, különben (hibakód, üzenet).</summary>
        public static Tuple<string, string> Check(string projectPath, string tiaVersion, string sandboxRoot)
        {
            if (!TiaVersions.IsSupported(tiaVersion))
            {
                return Fail(ErrorCodes.UnsupportedTiaVersion, "Unsupported TIA version: " + tiaVersion);
            }
            if (string.IsNullOrWhiteSpace(projectPath) || !Path.IsPathRooted(projectPath))
            {
                return Fail(ErrorCodes.CommandInvalid, "project_path must be an absolute path");
            }
            string fromExtension = TiaVersions.FromProjectPath(projectPath);
            if (fromExtension != tiaVersion)
            {
                return Fail(
                    ErrorCodes.ProjectVersionMismatch,
                    "Project file extension '" + Path.GetExtension(projectPath) + "' does not match requested " + tiaVersion +
                    " (expected " + TiaVersions.ProjectExtension(tiaVersion) + "). Upgrade/downgrade is never implicit.");
            }
            if (!File.Exists(projectPath))
            {
                return Fail(ErrorCodes.ProjectNotFound, "Project file not found: " + projectPath);
            }
            if (string.IsNullOrWhiteSpace(sandboxRoot) || !Path.IsPathRooted(sandboxRoot))
            {
                return Fail(ErrorCodes.ProjectNotSandbox, "sandbox_root must be an absolute path");
            }
            string root = Path.GetFullPath(sandboxRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullProject = Path.GetFullPath(projectPath);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullProject.StartsWith(root, comparison))
            {
                return Fail(ErrorCodes.ProjectNotSandbox, "Project is outside the sandbox root " + sandboxRoot);
            }
            string link;
            try
            {
                link = FirstLink(root, fullProject);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Fail(ErrorCodes.ProjectNotSandbox, "The sandbox folder cannot be inspected for links: " + ex.Message);
            }
            if (link != null)
            {
                // Audit A-06: junction/symlink mögött a sandbox nem bizonyítottan izolált másolat.
                return Fail(ErrorCodes.ProjectNotSandbox, "The sandbox path contains a link (junction/symlink): " + link);
            }
            SandboxMarker marker;
            try
            {
                marker = ReadMarker(fullProject);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException || ex is ArgumentException)
            {
                return Fail(ErrorCodes.ProjectNotSandbox, "Sandbox marker " + SandboxMarker.FileName + " is missing or unreadable: " + ex.Message);
            }
            if (marker == null || marker.MarkerVersion != SandboxMarker.CurrentVersion)
            {
                return Fail(ErrorCodes.ProjectNotSandbox, "Sandbox marker version " + marker?.MarkerVersion + " is not supported (expected " + SandboxMarker.CurrentVersion + "); recreate the sandbox copy");
            }
            if (!Guid.TryParseExact(marker.SandboxId ?? string.Empty, "D", out Guid _) || marker.SandboxId != marker.SandboxId.ToLowerInvariant())
            {
                return Fail(ErrorCodes.ProjectNotSandbox, "Sandbox marker has no valid sandbox_id; recreate the sandbox copy");
            }
            if (marker.TiaVersion != tiaVersion)
            {
                return Fail(ErrorCodes.ProjectVersionMismatch, "Sandbox marker was created for " + marker.TiaVersion + ", not " + tiaVersion);
            }
            if (!string.Equals(marker.ProjectFile, Path.GetFileName(fullProject), comparison))
            {
                return Fail(ErrorCodes.ProjectNotSandbox, "Sandbox marker belongs to project file " + marker.ProjectFile);
            }
            return null;
        }

        /// <summary>A projekt mappájában lévő .tes-sandbox.json (kivétel, ha hiányzik vagy olvashatatlan).</summary>
        public static SandboxMarker ReadMarker(string projectPath)
        {
            string markerPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)), SandboxMarker.FileName);
            return ProtocolJson.DeserializeLenient<SandboxMarker>(ProtocolJson.ReadAllText(markerPath));
        }

        /// <summary>
        /// Az első link (reparse point: junction, symlink, mount point) a sandbox gyökértől a projektmappáig,
        /// illetve a projektmappán belül bármely mélységben; null, ha nincs. Linkbe nem lép be.
        /// </summary>
        public static string FirstLink(string sandboxRoot, string projectPath)
        {
            string root = Path.GetFullPath(sandboxRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            for (string current = projectDirectory; current != null && current.Length >= root.Length; current = Path.GetDirectoryName(current))
            {
                if (IsLink(current))
                {
                    return current;
                }
            }
            Stack<string> pending = new Stack<string>();
            pending.Push(projectDirectory);
            while (pending.Count > 0)
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    if (IsLink(entry))
                    {
                        return entry;
                    }
                    if (Directory.Exists(entry))
                    {
                        pending.Push(entry);
                    }
                }
            }
            return null;
        }

        private static bool IsLink(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static Tuple<string, string> Fail(string code, string message)
        {
            return Tuple.Create(code, message);
        }
    }
}
