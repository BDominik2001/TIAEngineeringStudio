using System;
using System.IO;

namespace TiaStudio.Contracts.Integrity
{
    /// <summary>
    /// Projektútvonal-szabályok, amelyeket a főalkalmazás jóváhagyáskor és a worker import
    /// előtt is ellenőriz (spec 3.3, 10.1/1-2): verzióhelyes kiterjesztés, sandbox gyökér
    /// alatti hely és érvényes .tes-sandbox.json marker.
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
            string markerPath = Path.Combine(Path.GetDirectoryName(fullProject), SandboxMarker.FileName);
            SandboxMarker marker;
            try
            {
                marker = ProtocolJson.DeserializeLenient<SandboxMarker>(ProtocolJson.ReadAllText(markerPath));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException || ex is ArgumentException)
            {
                return Fail(ErrorCodes.ProjectNotSandbox, "Sandbox marker " + SandboxMarker.FileName + " is missing or unreadable: " + ex.Message);
            }
            if (marker == null || marker.MarkerVersion != SandboxMarker.CurrentVersion)
            {
                return Fail(ErrorCodes.ProjectNotSandbox, "Sandbox marker version is not supported");
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

        private static Tuple<string, string> Fail(string code, string message)
        {
            return Tuple.Create(code, message);
        }
    }
}
