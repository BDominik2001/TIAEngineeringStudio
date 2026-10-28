using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TiaStudio.Contracts.Integrity
{
    public static class Sha256
    {
        public static string OfBytes(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(data));
            }
        }

        public static string OfText(string text)
        {
            return OfBytes(new UTF8Encoding(false).GetBytes(text));
        }

        public static string OfFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        private static string ToHex(byte[] hash)
        {
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
