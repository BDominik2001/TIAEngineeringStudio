using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TiaStudio.Infrastructure.Localization;

namespace TiaStudio.Infrastructure.Logging
{
    /// <summary>A JSONL műveleti napló egy sora olvasható formában (a Napló nézethez); a nyers sor megmarad.</summary>
    public sealed class AuditEntry
    {
        private static readonly Dictionary<string, string> EventNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ModelChanged", "Modell módosult" },
            { "LibrarySelectionChanged", "Könyvtárválasztás módosult" },
            { "Validated", "Validálva" },
            { "Checked", "Ellenőrizve" },
            { "Generated", "Generálva" },
            { "Reviewed", "Átnézve" },
            { "Approved", "Jóváhagyva" },
            { "ApprovalRejected", "Jóváhagyás elutasítva" },
            { "ReadyToImport", "Importra kész" },
            { "Imported", "Importálva" },
            { "Compiled", "Lefordítva" },
            { "Saved", "Mentve" },
            { "Failed", "Hiba" },
            { "Reset", "Újrakezdés" },
            { "LibraryCreated", "Könyvtár létrehozva" },
            { "LibraryExported", "Blokkok a könyvtárba" },
            { "LibraryExportFailed", "Könyvtár-export hiba" },
            { "LibraryItemRemoved", "Könyvtárelem törölve" },
            { "LibraryVerified", "Könyvtár ellenőrizve" },
            { "PrepareImportBlocked", "Import-előkészítés elutasítva" },
        };

        public string Time { get; private set; }

        public string Event { get; private set; }

        public string EventCode { get; private set; }

        public string State { get; private set; }

        public string TiaVersion { get; private set; }

        public string ErrorCode { get; private set; }

        public string Message { get; private set; }

        public bool IsError => !string.IsNullOrEmpty(ErrorCode);

        public string Raw { get; private set; }

        /// <summary>Egy naplósor feldolgozása; nem JSON sornál a nyers szöveg az üzenet.</summary>
        public static AuditEntry Parse(string line, MessageCatalog catalog)
        {
            AuditEntry entry = new AuditEntry { Raw = line, Message = line };
            JObject record;
            try
            {
                record = JObject.Parse(line);
            }
            catch (JsonReaderException)
            {
                return entry;
            }
            entry.EventCode = (string)record["event"];
            entry.Event = entry.EventCode != null && EventNames.TryGetValue(entry.EventCode, out string name) ? name : entry.EventCode;
            entry.State = (string)record["state"];
            entry.TiaVersion = (string)record["tia_version"];
            entry.ErrorCode = (string)record["error_code"];
            string message = (string)record["message"];
            string hungarian = catalog?.Message(entry.ErrorCode);
            entry.Message = hungarian == null ? message : hungarian + (string.IsNullOrEmpty(message) ? string.Empty : " (" + message + ")");
            JToken timestamp = record["timestamp_utc"];
            if (timestamp != null && DateTime.TryParse(timestamp.ToString(Formatting.None).Trim('"'), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime utc))
            {
                entry.Time = utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }
            entry.Raw = record.ToString(Formatting.Indented);
            return entry;
        }
    }
}
