using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace TiaStudio.Infrastructure.Localization
{
    /// <summary>
    /// A felületen megjelenő magyar szövegek a munkafolyamat (angol, naplóba kerülő) üzeneteiből.
    /// A hibakódok szövege és teendője a contracts/error-codes.json-ból jön (message_hu, hint_hu),
    /// így a C#, a Python és a Rust ugyanazt a kódkészletet használja; a napló angol marad.
    /// </summary>
    public sealed class MessageCatalog
    {
        private static readonly Regex IssueLine = new Regex(@"^(?<severity>error|warning|info) (?<code>[A-Z0-9_]+) @ (?<path>[^:]*): (?<message>.*)$", RegexOptions.CultureInvariant);

        // Sikeres lépések üzenetei (EngineeringWorkflow, BlockLibraryService); ismeretlen szöveg változatlan marad.
        private static readonly KeyValuePair<Regex, string>[] Successes =
        {
            Pair(@"^Model is valid$", "A gépmodell érvényes."),
            Pair(@"^Static validation passed$", "A statikus ellenőrzés (Rust) rendben."),
            Pair(@"^Library items unchanged since validation$", "A könyvtárelemek a validálás óta nem változtak."),
            Pair(@"^Generated (\d+) SCL source file\(s\)$", "$1 SCL-forrásfájl generálva."),
            Pair(@"^Import package with (\d+) library block\(s\) assembled$", "Importcsomag összeállítva ($1 könyvtári blokk)."),
            Pair(@"^Sources marked as reviewed$", "A források átnézettnek jelölve."),
            Pair(@"^Approved for (.+)$", "Jóváhagyva: $1"),
            Pair(@"^Dry run passed; ready to import into (.+)$", "A száraz futás rendben, importálható ide: $1"),
            Pair(@"^Imported, compiled and saved (.+)$", "Importálva, lefordítva és mentve: $1"),
            Pair(@"^Integrity verified$", "Az integritás rendben."),
            Pair(@"^Library (.+) created$", "A(z) $1 könyvtár létrejött."),
            Pair(@"^Library integrity OK$", "A könyvtár integritása rendben."),
            Pair(@"^(\d+) block\(s\)/type\(s\) in (.+)$", "$1 blokk/típus a(z) $2 PLC-ben."),
            Pair(@"^(.+) r(\d+) removed$", "Törölve: $1 r$2"),
            Pair(@"^(\d+) block\(s\) added to library (.+)$", "$1 blokk került a(z) $2 könyvtárba."),
        };

        private readonly Dictionary<string, Entry> entries;

        private MessageCatalog(Dictionary<string, Entry> entries)
        {
            this.entries = entries;
        }

        /// <summary>A beágyazott contracts/error-codes.json alapján.</summary>
        public static MessageCatalog Default { get; } = LoadEmbedded();

        public static MessageCatalog Parse(string json)
        {
            Dictionary<string, Entry> result = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (JProperty code in ((JObject)JObject.Parse(json)["codes"]).Properties())
            {
                result[code.Name] = new Entry((string)code.Value["message_hu"], (string)code.Value["hint_hu"]);
            }
            return new MessageCatalog(result);
        }

        public bool Contains(string code)
        {
            return code != null && entries.ContainsKey(code);
        }

        /// <summary>A hibakód magyar leírása, vagy null.</summary>
        public string Message(string code)
        {
            return code != null && entries.TryGetValue(code, out Entry entry) ? entry.Message : null;
        }

        /// <summary>A felhasználónak javasolt teendő, vagy null.</summary>
        public string Hint(string code)
        {
            return code != null && entries.TryGetValue(code, out Entry entry) ? entry.Hint : null;
        }

        /// <summary>Állapotsor-szöveg egy lépés eredményéből.</summary>
        public string Status(bool ok, string errorCode, string message)
        {
            if (ok)
            {
                return Success(message);
            }
            string hungarian = Message(errorCode);
            if (hungarian == null)
            {
                return errorCode + ": " + message;
            }
            string hint = Hint(errorCode);
            return hungarian + " [" + errorCode + "]" + (hint == null ? string.Empty : " – " + hint);
        }

        public string Success(string message)
        {
            if (message == null)
            {
                return null;
            }
            foreach (KeyValuePair<Regex, string> pair in Successes)
            {
                if (pair.Key.IsMatch(message))
                {
                    return pair.Key.Replace(message, pair.Value);
                }
            }
            return message;
        }

        /// <summary>
        /// Egy validációs részletsor ("error NO_DEVICES @ devices: At least one device is required")
        /// magyarul; az eredeti, konkrét értékeket is tartalmazó angol szöveg zárójelben marad.
        /// </summary>
        public string Issue(string line)
        {
            Match match = line == null ? Match.Empty : IssueLine.Match(line);
            if (!match.Success)
            {
                return line;
            }
            string code = match.Groups["code"].Value;
            string severity;
            switch (match.Groups["severity"].Value)
            {
                case "error":
                    severity = "Hiba";
                    break;
                case "warning":
                    severity = "Figyelmeztetés";
                    break;
                default:
                    severity = "Info";
                    break;
            }
            string path = match.Groups["path"].Value;
            string hungarian = Message(code) ?? match.Groups["message"].Value;
            return severity + " · " + (path.Length == 0 ? string.Empty : path + " · ") + hungarian
                + (Message(code) == null ? string.Empty : " (" + match.Groups["message"].Value + ")")
                + " [" + code + "]";
        }

        private static KeyValuePair<Regex, string> Pair(string pattern, string replacement)
        {
            return new KeyValuePair<Regex, string>(new Regex(pattern, RegexOptions.CultureInvariant), replacement);
        }

        private static MessageCatalog LoadEmbedded()
        {
            using (Stream stream = typeof(MessageCatalog).Assembly.GetManifestResourceStream("TiaStudio.error-codes.json"))
            using (StreamReader reader = new StreamReader(stream ?? throw new InvalidOperationException("embedded error-codes.json missing")))
            {
                return Parse(reader.ReadToEnd());
            }
        }

        private sealed class Entry
        {
            public Entry(string message, string hint)
            {
                Message = message;
                Hint = hint;
            }

            public string Message { get; }

            public string Hint { get; }
        }
    }
}
