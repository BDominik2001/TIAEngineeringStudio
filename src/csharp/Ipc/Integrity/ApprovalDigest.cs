using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TiaStudio.Contracts.Integrity
{
    /// <summary>
    /// ApprovalDigest v1: a jóváhagyást a konkrét futtatási kérelemhez, projektútvonalhoz és
    /// az összes forrás hash-éhez köti (spec 6.3). Az approved_by_user mező önmagában nem
    /// jogosultság; a worker ezt a kivonatot újraszámolja a saját maga által mért hashekből.
    /// </summary>
    public static class ApprovalDigest
    {
        public const string Version = "tes-approval-v1";

        public static string Compute(TiaCommand command, string manifestSha256, IEnumerable<ManifestFile> files)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }
            return Sha256.OfText(CanonicalText(command, manifestSha256, files));
        }

        public static string CanonicalText(TiaCommand command, string manifestSha256, IEnumerable<ManifestFile> files)
        {
            StringBuilder text = new StringBuilder();
            text.Append(Version).Append('\n');
            Line(text, "job_id", command.JobId);
            Line(text, "operation", command.Operation);
            Line(text, "tia_version", command.TiaVersion);
            Line(text, "project_path", NormalizePath(command.ProjectPath));
            Line(text, "plc_name", command.PlcName);
            Line(text, "overwrite_policy", command.OverwritePolicy);
            Line(text, "manifest_sha256", manifestSha256);
            foreach (ManifestFile file in files.OrderBy(f => f.ImportOrder).ThenBy(f => f.RelativePath, StringComparer.Ordinal))
            {
                Line(text, "file", file.ImportOrder.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + file.RelativePath + ":" + file.Sha256);
            }
            return text.ToString();
        }

        /// <summary>Teljes, kanonikus útvonal; Windows alatt kis/nagybetű-független összevetéshez nagybetűsítve.</summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.DirectorySeparatorChar == '\\' ? full.ToUpperInvariant() : full;
        }

        private static void Line(StringBuilder text, string key, string value)
        {
            if (value != null && (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0))
            {
                throw new ArgumentException("Approval fields must not contain line breaks: " + key);
            }
            text.Append(key).Append('=').Append(value ?? string.Empty).Append('\n');
        }
    }
}
