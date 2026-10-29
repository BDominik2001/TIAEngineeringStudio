using System;
using System.IO;
using System.Text;
using TiaStudio.Contracts.Integrity;

namespace TiaStudio.Infrastructure.Generator
{
    /// <summary>
    /// Generált forrás előnézete az átnézéshez (audit A-12): csak az olvasható, a manifestben rögzített
    /// SHA-256-tal egyező tartalom jelenhet meg és számíthat átnézettnek; hiba esetén nincs forrásszöveg.
    /// </summary>
    public static class SourcePreview
    {
        /// <summary>A forrás szövege, vagy null és a <paramref name="error"/> hibaüzenet.</summary>
        public static string Read(string path, string expectedSha256, out string error)
        {
            byte[] content;
            try
            {
                content = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = "A fájl nem olvasható, ezért nem jelölhető átnézettnek: " + ex.Message;
                return null;
            }
            if (!string.Equals(Sha256.OfBytes(content), expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "A fájl tartalma eltér a manifestben rögzítettől (SHA-256), ezért nem jelölhető átnézettnek. Generáld újra a forrásokat.";
                return null;
            }
            error = null;
            return new UTF8Encoding(false, false).GetString(content);
        }
    }
}
