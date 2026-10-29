using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace TiaStudio.Contracts
{
    /// <summary>A protokollfájlok közös JSON-beállításai (UTF-8, BOM nélkül, ismeretlen mező = hiba).</summary>
    public static class ProtocolJson
    {
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);

        public static JsonSerializerSettings StrictSettings()
        {
            return new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Include,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MaxDepth = 32,
            };
        }

        public static JsonSerializerSettings LenientSettings()
        {
            JsonSerializerSettings settings = StrictSettings();
            settings.MissingMemberHandling = MissingMemberHandling.Ignore;
            return settings;
        }

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.Indented, StrictSettings());
        }

        public static T DeserializeStrict<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, StrictSettings());
        }

        public static T DeserializeLenient<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, LenientSettings());
        }

        public static string ReadAllText(string path)
        {
            return File.ReadAllText(path, Utf8NoBom);
        }

        /// <summary>
        /// Átmeneti fájlba ír (lemezre ürítve), majd egy lépésben cseréli a célfájlt: meglévő fájlnál
        /// File.Replace (Windows: ReplaceFile, Unix: rename), így a célfájl egyik pillanatban sem hiányzik,
        /// és félkész JSON sem maradhat hátra. Hiba esetén az eredeti fájl változatlan.
        /// </summary>
        public static void WriteAtomic(string path, object value)
        {
            byte[] content = Utf8NoBom.GetBytes(Serialize(value) + "\n");
            string temp = path + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(content, 0, content.Length);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
    }
}
