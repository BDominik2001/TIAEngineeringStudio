using System;
using System.Globalization;
using System.IO;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;
using TiaStudio.Contracts.Integrity;

namespace TiaStudio.Infrastructure.Sandbox
{
    /// <summary>
    /// Ismert jó TIA-projekt teljes mappájának másolása a sandbox gyökér alá, .tes-sandbox.json
    /// markerrel. Az eredeti projekthez soha nem nyúl; verziófrissítést nem végez (spec 3.3).
    /// A TIA Portalban a forrásprojektet másolás előtt be kell zárni.
    /// </summary>
    public sealed class SandboxService : ISandboxService
    {
        private readonly IClock clock;
        private readonly IUserContext user;

        public SandboxService(string sandboxRoot, IClock clock, IUserContext user)
        {
            SandboxRoot = Path.GetFullPath(sandboxRoot);
            this.clock = clock;
            this.user = user;
        }

        public string SandboxRoot { get; }

        public SandboxCopy CreateSandboxCopy(string sourceProjectPath, string tiaVersion)
        {
            if (TiaVersions.FromProjectPath(sourceProjectPath) != tiaVersion)
            {
                throw new SandboxException(ErrorCodes.ProjectVersionMismatch, "Source project extension does not match " + tiaVersion + " (" + TiaVersions.ProjectExtension(tiaVersion) + " expected)");
            }
            string sourceFile = Path.GetFullPath(sourceProjectPath);
            if (!File.Exists(sourceFile))
            {
                throw new SandboxException(ErrorCodes.ProjectNotFound, "Source project not found: " + sourceFile);
            }
            string sourceDirectory = Path.GetDirectoryName(sourceFile);
            if (IsInside(SandboxRoot, sourceDirectory) || IsInside(sourceDirectory, SandboxRoot))
            {
                throw new SandboxException(ErrorCodes.ProjectNotSandbox, "Source project and sandbox root must not contain each other");
            }
            string name = Path.GetFileName(sourceDirectory) + "_" + clock.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string target = Path.Combine(SandboxRoot, tiaVersion, name);
            if (Directory.Exists(target))
            {
                throw new SandboxException(ErrorCodes.OutputExists, "Sandbox directory already exists: " + target);
            }
            string staging = target + ".partial";
            CopyDirectory(sourceDirectory, staging);
            SandboxMarker marker = new SandboxMarker
            {
                TiaVersion = tiaVersion,
                ProjectFile = Path.GetFileName(sourceFile),
                SourceProjectPath = sourceFile,
                CreatedAtUtc = clock.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                CreatedBy = user.UserName,
            };
            ProtocolJson.WriteAtomic(Path.Combine(staging, SandboxMarker.FileName), marker);
            Directory.Move(staging, target);
            return new SandboxCopy
            {
                ProjectPath = Path.Combine(target, Path.GetFileName(sourceFile)),
                SandboxDirectory = target,
                Marker = marker,
            };
        }

        public Tuple<string, string> Verify(string projectPath, string tiaVersion)
        {
            return SandboxRules.Check(projectPath, tiaVersion, SandboxRoot);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                string name = Path.GetFileName(file);
                if (name == SandboxMarker.FileName)
                {
                    continue; // a forrás markere nem öröklődik
                }
                File.Copy(file, Path.Combine(destination, name), false);
            }
            foreach (string directory in Directory.GetDirectories(source))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new SandboxException(ErrorCodes.PathEscape, "Project folder contains a link: " + directory);
                }
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }

        private static bool IsInside(string root, string candidate)
        {
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, comparison);
        }
    }

    public sealed class SandboxException : Exception
    {
        public SandboxException(string errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}
