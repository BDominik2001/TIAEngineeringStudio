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

        /// <summary>Átmeneti fájlba ír, majd átnevez – félkész JSON nem maradhat hátra.</summary>
        public static void WriteAtomic(string path, object value)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(value) + "\n", Utf8NoBom);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temp, path);
        }
    }
}
