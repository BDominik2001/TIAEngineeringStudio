using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TiaStudio.Domain.Rules
{
    /// <summary>A docs/VALIDATION_RULES.md szabályainak primitívjei (regex-mentes, determinisztikus).</summary>
    public static class NamingRules
    {
        public const string RulesVersion = "1.1";
        public const int MaxInputBytes = 1048576;
        public const int MaxDevices = 256;
        public const int MaxDeviceIdLength = 32;
        public const int MaxBlockNameLength = 125;
        public const int MaxProjectDescriptionLength = 200;
        public const int MaxDeviceDescriptionLength = 120;
        public const int MonitorTimeMinMs = 100;
        public const int MonitorTimeMaxMs = 60000;

        public static readonly IReadOnlyList<string> ReservedBlockNames = new[] { "FB_Cylinder", "FC_Cylinders" };

        public static bool IsAscii(string value)
        {
            return value.All(c => c < 128);
        }

        public static bool IsPrintableAscii(string value)
        {
            return value.All(c => c >= 0x20 && c <= 0x7E);
        }

        /// <summary>^[A-Za-z][A-Za-z0-9_]{0,63}$</summary>
        public static bool IsProjectName(string value)
        {
            return IsLetterLead(value) && value.Length <= 64 && value.All(c => IsAsciiLetterOrDigit(c) || c == '_');
        }

        /// <summary>^[A-Za-z][A-Za-z0-9_\-]{0,63}$</summary>
        public static bool IsPlcName(string value)
        {
            return IsLetterLead(value) && value.Length <= 64 && value.All(c => IsAsciiLetterOrDigit(c) || c == '_' || c == '-');
        }

        /// <summary>^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$ (a hosszkorlátot külön ellenőrizzük).</summary>
        public static bool IsDeviceId(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] < 'A' || value[0] > 'Z')
            {
                return false;
            }
            char previous = value[0];
            foreach (char c in value.Skip(1))
            {
                bool upperOrDigit = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                if (!upperOrDigit && !(c == '_' && previous != '_'))
                {
                    return false;
                }
                previous = c;
            }
            return previous != '_';
        }

        private static bool IsLetterLead(string value)
        {
            return !string.IsNullOrEmpty(value) && ((value[0] >= 'A' && value[0] <= 'Z') || (value[0] >= 'a' && value[0] <= 'z'));
        }

        private static bool IsAsciiLetterOrDigit(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
        }
    }

    /// <summary>Bemeneti bitcím (I&lt;byte&gt;.&lt;bit&gt;), % prefix és vezető nulla nélkül.</summary>
    public struct IoAddress : IEquatable<IoAddress>
    {
        public IoAddress(int byteIndex, int bit)
        {
            ByteIndex = byteIndex;
            Bit = bit;
        }

        public int ByteIndex { get; }

        public int Bit { get; }

        /// <summary>^I(0|[1-9][0-9]{0,4})\.[0-7]$</summary>
        public static bool TryParseInput(string text, out IoAddress address)
        {
            address = default(IoAddress);
            if (string.IsNullOrEmpty(text) || text.Length < 4 || text[0] != 'I')
            {
                return false;
            }
            int dot = text.IndexOf('.');
            if (dot < 2 || dot != text.Length - 2)
            {
                return false;
            }
            string bytePart = text.Substring(1, dot - 1);
            char bitChar = text[text.Length - 1];
            if (bytePart.Length > 5 || !bytePart.All(c => c >= '0' && c <= '9') || (bytePart.Length > 1 && bytePart[0] == '0'))
            {
                return false;
            }
            if (bitChar < '0' || bitChar > '7')
            {
                return false;
            }
            address = new IoAddress(int.Parse(bytePart, CultureInfo.InvariantCulture), bitChar - '0');
            return true;
        }

        public override string ToString()
        {
            return "I" + ByteIndex.ToString(CultureInfo.InvariantCulture) + "." + Bit.ToString(CultureInfo.InvariantCulture);
        }

        public bool Equals(IoAddress other)
        {
            return ByteIndex == other.ByteIndex && Bit == other.Bit;
        }

        public override bool Equals(object obj)
        {
            return obj is IoAddress other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (ByteIndex * 8) + Bit;
        }
    }
}
