using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using TiaStudio.Application.Ports;

namespace TiaStudio.Infrastructure.Logging
{
    /// <summary>
    /// Hozzáfűző JSONL műveleti napló (logs/audit-YYYYMMDD.jsonl). Jelszó, token, kulcs jellegű
    /// mezőket nem ír ki (spec 10.1/10).
    /// </summary>
    public sealed class JsonlAuditLog : IAuditLog
    {
        private static readonly string[] SensitiveKeyParts = { "password", "passwd", "secret", "token", "apikey", "api_key", "credential" };
        private readonly string directory;
        private readonly IClock clock;
        private readonly object gate = new object();

        public JsonlAuditLog(string directory, IClock clock)
        {
            this.directory = directory;
            this.clock = clock;
        }

        public event EventHandler<string> LineWritten;

        public string CurrentFile => Path.Combine(directory, "audit-" + clock.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");

        public void Write(AuditEvent auditEvent)
        {
            SortedDictionary<string, object> record = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                { "timestamp_utc", clock.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) },
                { "event", auditEvent.Event },
                { "session_id", auditEvent.SessionId },
                { "run_id", auditEvent.RunId },
                { "job_id", auditEvent.JobId },
                { "state", auditEvent.State },
                { "tia_version", auditEvent.TiaVersion },
                { "error_code", auditEvent.ErrorCode },
                { "message", auditEvent.Message },
                { "details", Redact(auditEvent.Details) },
            };
            string line = JsonConvert.SerializeObject(record, Formatting.None);
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(CurrentFile, line + "\n", new UTF8Encoding(false));
            }
            LineWritten?.Invoke(this, line);
        }

        internal static IDictionary<string, object> Redact(IDictionary<string, object> details)
        {
            if (details == null)
            {
                return new Dictionary<string, object>();
            }
            return details.ToDictionary(
                pair => pair.Key,
                pair => IsSensitive(pair.Key) ? "***redacted***" : pair.Value);
        }

        private static bool IsSensitive(string key)
        {
            string lower = key.ToLowerInvariant();
            return SensitiveKeyParts.Any(lower.Contains);
        }
    }
}
