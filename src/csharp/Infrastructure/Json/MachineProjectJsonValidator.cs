using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;
using TiaStudio.Domain.Model;
using TiaStudio.Domain.Rules;

namespace TiaStudio.Infrastructure.Json
{
    /// <summary>
    /// docs/VALIDATION_RULES.md C#-implementációja (DRAFT -> VALIDATED). Kimenete a
    /// tests/contracts/validation-cases.json szerint azonos a Rust és a Python validátoréval.
    /// Szigorú RFC 8259 JSON (System.Text.Json): komment, záró vessző, NaN nem megengedett.
    /// </summary>
    public sealed class MachineProjectJsonValidator : IModelValidator
    {
        public const string ValidatorName = "TiaStudio.MachineProjectJsonValidator";

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static readonly string[] RootFields = { "schema_version", "project_name", "description", "target", "devices" };
        private static readonly string[] TargetFields = { "tia_version", "plc_name", "cpu_family" };
        private static readonly string[] CylinderFields = { "id", "kind", "description", "inputs", "parameters" };
        private static readonly string[] CylinderInputFields = { "retracted", "extended" };
        private static readonly string[] CylinderParameterFields = { "monitor_time_ms" };

        public ValidationReport Validate(byte[] json)
        {
            List<ValidationIssue> issues = new List<ValidationIssue>();
            if (json.Length > NamingRules.MaxInputBytes)
            {
                issues.Add(Error(ErrorCodes.InputTooLarge, "$", "Input is " + json.Length + " bytes; limit is " + NamingRules.MaxInputBytes));
                return Report(issues);
            }
            int offset = json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF ? 3 : 0;
            JsonDocument document;
            try
            {
                // A System.Text.Json a string-tartalmat csak olvasáskor dekódolja: előbb szigorú UTF-8 ellenőrzés.
                StrictUtf8.GetCharCount(json, offset, json.Length - offset);
                document = JsonDocument.Parse(
                    new ReadOnlyMemory<byte>(json, offset, json.Length - offset),
                    new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 128 });
            }
            catch (JsonException ex)
            {
                issues.Add(Error(ErrorCodes.InvalidJson, "$", "Input is not valid UTF-8 JSON: " + ex.Message));
                return Report(issues);
            }
            catch (ArgumentException ex) // DecoderFallbackException is ArgumentException
            {
                issues.Add(Error(ErrorCodes.InvalidJson, "$", "Input is not valid UTF-8 JSON: " + ex.Message));
                return Report(issues);
            }
            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    issues.Add(Error(ErrorCodes.InvalidJson, "$", "JSON root must be an object"));
                    return Report(issues);
                }
                Walker walker = new Walker(issues);
                walker.Root(ToMap(document.RootElement));
            }
            if (issues.Count == 0)
            {
                issues.Add(new ValidationIssue(ValidationIssue.SeverityWarning, ErrorCodes.IoHwNotVerified, "devices", "I/O addresses were not checked against the TIA hardware configuration"));
            }
            return Report(issues);
        }

        private static ValidationReport Report(List<ValidationIssue> issues)
        {
            int errors = issues.Count(i => i.Severity == ValidationIssue.SeverityError);
            return new ValidationReport
            {
                Validator = ValidatorName,
                ValidatorVersion = typeof(MachineProjectJsonValidator).Assembly.GetName().Version.ToString(3),
                RulesVersion = NamingRules.RulesVersion,
                Ok = errors == 0,
                ErrorCount = errors,
                WarningCount = issues.Count - errors,
                Issues = issues,
            };
        }

        private static ValidationIssue Error(string code, string path, string message)
        {
            return new ValidationIssue(ValidationIssue.SeverityError, code, path, message);
        }

        /// <summary>Ismétlődő kulcsnál az utolsó érték érvényes (Python/Rust viselkedéssel egyezően).</summary>
        private static Dictionary<string, JsonElement> ToMap(JsonElement element)
        {
            Dictionary<string, JsonElement> map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                map[property.Name] = property.Value;
            }
            return map;
        }

        private sealed class Walker
        {
            private readonly List<ValidationIssue> issues;
            private readonly Dictionary<string, string> seenIds = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> seenAddresses = new Dictionary<string, string>(StringComparer.Ordinal);
            private int? inputByteLimit;

            public Walker(List<ValidationIssue> issues)
            {
                this.issues = issues;
            }

            public void Root(Dictionary<string, JsonElement> doc)
            {
                if (TryGet(doc, "schema_version", "schema_version", true, out JsonElement schema) && IsString(schema, "schema_version")
                    && schema.GetString() != MachineProject.CurrentSchemaVersion)
                {
                    Add(ErrorCodes.UnsupportedSchemaVersion, "schema_version", "schema_version '" + schema.GetString() + "' is not supported (expected '1.1')");
                }
                if (TryGet(doc, "project_name", "project_name", true, out JsonElement projectName))
                {
                    Identifier(projectName, "project_name", NamingRules.IsProjectName, "^[A-Za-z][A-Za-z0-9_]{0,63}$");
                }
                if (TryGet(doc, "description", "description", false, out JsonElement description))
                {
                    Description(description, "description", NamingRules.MaxProjectDescriptionLength);
                }
                if (TryGet(doc, "target", "target", true, out JsonElement target) && IsObject(target, "target"))
                {
                    Target(ToMap(target));
                }
                if (TryGet(doc, "devices", "devices", true, out JsonElement devices))
                {
                    Devices(devices);
                }
                UnknownFields(doc, RootFields, string.Empty);
            }

            private void Target(Dictionary<string, JsonElement> target)
            {
                if (TryGet(target, "tia_version", "target.tia_version", true, out JsonElement version) && IsString(version, "target.tia_version")
                    && !TiaVersions.IsSupported(version.GetString()))
                {
                    Add(ErrorCodes.UnsupportedTiaVersion, "target.tia_version", "TIA version '" + version.GetString() + "' is not supported (V18, V19, V20)");
                }
                if (TryGet(target, "plc_name", "target.plc_name", true, out JsonElement plcName))
                {
                    Identifier(plcName, "target.plc_name", NamingRules.IsPlcName, "^[A-Za-z][A-Za-z0-9_-]{0,63}$");
                }
                if (TryGet(target, "cpu_family", "target.cpu_family", true, out JsonElement cpu) && IsString(cpu, "target.cpu_family"))
                {
                    inputByteLimit = CpuFamilies.MaxInputByte(cpu.GetString());
                    if (inputByteLimit == null)
                    {
                        Add(ErrorCodes.UnsupportedCpuFamily, "target.cpu_family", "CPU family '" + cpu.GetString() + "' is not supported (S7-1200, S7-1500)");
                    }
                }
                UnknownFields(target, TargetFields, "target");
            }

            private void Devices(JsonElement devices)
            {
                if (devices.ValueKind != JsonValueKind.Array)
                {
                    Add(ErrorCodes.InvalidType, "devices", "Expected array, got " + TypeName(devices));
                    return;
                }
                int count = devices.GetArrayLength();
                if (count == 0)
                {
                    Add(ErrorCodes.NoDevices, "devices", "At least one device is required");
                    return;
                }
                if (count > NamingRules.MaxDevices)
                {
                    Add(ErrorCodes.TooManyDevices, "devices", count + " devices exceed the limit of " + NamingRules.MaxDevices);
                    return;
                }
                int index = 0;
                foreach (JsonElement device in devices.EnumerateArray())
                {
                    Device(device, "devices[" + index + "]");
                    index++;
                }
            }

            private void Device(JsonElement element, string path)
            {
                if (!IsObject(element, path))
                {
                    return;
                }
                Dictionary<string, JsonElement> device = ToMap(element);
                string validId = null;
                if (TryGet(device, "id", path + ".id", true, out JsonElement id)
                    && Identifier(id, path + ".id", NamingRules.IsDeviceId, "^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$"))
                {
                    string value = id.GetString();
                    if (value.Length > NamingRules.MaxDeviceIdLength)
                    {
                        Add(ErrorCodes.InvalidIdentifier, path + ".id", "Identifier '" + value + "' is longer than 32 characters");
                    }
                    else
                    {
                        validId = value;
                        string key = value.ToUpperInvariant();
                        if (seenIds.TryGetValue(key, out string first))
                        {
                            Add(ErrorCodes.DuplicateDeviceId, path + ".id", "Device id '" + value + "' is already used by " + first);
                        }
                        else
                        {
                            seenIds[key] = path + ".id";
                        }
                    }
                }
                if (!TryGet(device, "kind", path + ".kind", true, out JsonElement kind) || !IsString(kind, path + ".kind"))
                {
                    return;
                }
                if (!DeviceKinds.Supported.Contains(kind.GetString()))
                {
                    Add(ErrorCodes.UnsupportedDeviceKind, path + ".kind", "Device kind '" + kind.GetString() + "' is not supported");
                    return;
                }
                if (validId != null)
                {
                    GeneratedName("DB_Cylinder_" + validId, path + ".id");
                }
                Cylinder(device, path);
            }

            private void GeneratedName(string blockName, string path)
            {
                if (blockName.Length > NamingRules.MaxBlockNameLength)
                {
                    Add(ErrorCodes.GeneratedNameTooLong, path, "Generated block name '" + blockName + "' exceeds " + NamingRules.MaxBlockNameLength + " characters");
                }
                if (NamingRules.ReservedBlockNames.Any(n => string.Equals(n, blockName, StringComparison.OrdinalIgnoreCase)))
                {
                    Add(ErrorCodes.GeneratedNameCollision, path, "Generated block name '" + blockName + "' is reserved");
                }
            }

            private void Cylinder(Dictionary<string, JsonElement> device, string path)
            {
                if (TryGet(device, "description", path + ".description", false, out JsonElement description))
                {
                    Description(description, path + ".description", NamingRules.MaxDeviceDescriptionLength);
                }
                if (TryGet(device, "inputs", path + ".inputs", true, out JsonElement inputsElement) && IsObject(inputsElement, path + ".inputs"))
                {
                    Dictionary<string, JsonElement> inputs = ToMap(inputsElement);
                    foreach (string name in CylinderInputFields)
                    {
                        string inputPath = path + ".inputs." + name;
                        if (TryGet(inputs, name, inputPath, true, out JsonElement address))
                        {
                            InputAddress(address, inputPath);
                        }
                    }
                    UnknownFields(inputs, CylinderInputFields, path + ".inputs");
                }
                if (TryGet(device, "parameters", path + ".parameters", false, out JsonElement parametersElement) && IsObject(parametersElement, path + ".parameters"))
                {
                    Dictionary<string, JsonElement> parameters = ToMap(parametersElement);
                    string p = path + ".parameters.monitor_time_ms";
                    if (TryGet(parameters, "monitor_time_ms", p, false, out JsonElement monitor))
                    {
                        string raw = monitor.ValueKind == JsonValueKind.Number ? monitor.GetRawText() : null;
                        if (raw == null || raw.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
                        {
                            Add(ErrorCodes.InvalidType, p, "Expected integer, got " + TypeName(monitor));
                        }
                        else if (!monitor.TryGetInt64(out long value) || value < NamingRules.MonitorTimeMinMs || value > NamingRules.MonitorTimeMaxMs)
                        {
                            Add(ErrorCodes.InvalidValue, p, "monitor_time_ms " + raw + " is outside " + NamingRules.MonitorTimeMinMs + ".." + NamingRules.MonitorTimeMaxMs);
                        }
                    }
                    UnknownFields(parameters, CylinderParameterFields, path + ".parameters");
                }
                UnknownFields(device, CylinderFields, path);
            }

            private void InputAddress(JsonElement element, string path)
            {
                if (!IsString(element, path))
                {
                    return;
                }
                string value = element.GetString();
                if (!NamingRules.IsAscii(value) || !IoAddress.TryParseInput(value, out IoAddress address))
                {
                    Add(ErrorCodes.InvalidIoAddress, path, "'" + value + "' is not a valid input address (e.g. I0.0)");
                    return;
                }
                if (inputByteLimit.HasValue && address.ByteIndex > inputByteLimit.Value)
                {
                    Add(ErrorCodes.IoAddressOutOfRange, path, "Input byte " + address.ByteIndex + " exceeds the CPU family limit " + inputByteLimit.Value);
                    return;
                }
                if (seenAddresses.TryGetValue(value, out string first))
                {
                    Add(ErrorCodes.DuplicateIoAddress, path, "I/O address " + value + " is already used by " + first);
                }
                else
                {
                    seenAddresses[value] = path;
                }
            }

            private bool Identifier(JsonElement element, string path, Func<string, bool> rule, string ruleText)
            {
                if (!IsString(element, path))
                {
                    return false;
                }
                string value = element.GetString();
                if (!NamingRules.IsAscii(value))
                {
                    Add(ErrorCodes.NonAscii, path, "Identifier '" + value + "' contains non-ASCII characters");
                    return false;
                }
                if (!rule(value))
                {
                    Add(ErrorCodes.InvalidIdentifier, path, "Identifier '" + value + "' does not match " + ruleText);
                    return false;
                }
                return true;
            }

            private void Description(JsonElement element, string path, int maxLength)
            {
                if (!IsString(element, path))
                {
                    return;
                }
                string value = element.GetString();
                if (!NamingRules.IsAscii(value))
                {
                    Add(ErrorCodes.NonAscii, path, "Description contains non-ASCII characters");
                }
                else if (!NamingRules.IsPrintableAscii(value))
                {
                    Add(ErrorCodes.InvalidValue, path, "Description may contain printable ASCII characters only");
                }
                else if (value.Length > maxLength)
                {
                    Add(ErrorCodes.InvalidValue, path, "Description is longer than " + maxLength + " characters");
                }
            }

            private bool TryGet(Dictionary<string, JsonElement> obj, string key, string path, bool required, out JsonElement value)
            {
                if (!obj.TryGetValue(key, out value))
                {
                    if (required)
                    {
                        Add(ErrorCodes.MissingField, path, "Required field '" + key + "' is missing");
                    }
                    return false;
                }
                if (value.ValueKind == JsonValueKind.Null)
                {
                    Add(ErrorCodes.NullValue, path, "Field '" + key + "' must not be null");
                    return false;
                }
                return true;
            }

            private bool IsString(JsonElement element, string path)
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    Add(ErrorCodes.InvalidType, path, "Expected string, got " + TypeName(element));
                    return false;
                }
                return true;
            }

            private bool IsObject(JsonElement element, string path)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    Add(ErrorCodes.InvalidType, path, "Expected object, got " + TypeName(element));
                    return false;
                }
                return true;
            }

            private void UnknownFields(Dictionary<string, JsonElement> obj, string[] known, string prefix)
            {
                IEnumerable<string> unknown = obj.Keys.Where(k => !known.Contains(k)).OrderBy(k => k, Utf8ByteOrder.Instance);
                foreach (string key in unknown)
                {
                    Add(ErrorCodes.UnknownField, prefix.Length == 0 ? key : prefix + "." + key, "Unknown field '" + key + "'");
                }
            }

            private void Add(string code, string path, string message)
            {
                issues.Add(Error(code, path, message));
            }

            private static string TypeName(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Null:
                        return "null";
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        return "boolean";
                    case JsonValueKind.Number:
                        return "number";
                    case JsonValueKind.String:
                        return "string";
                    case JsonValueKind.Array:
                        return "array";
                    default:
                        return "object";
                }
            }
        }

        /// <summary>UTF-8 bájtsorrend (a Python/Rust implementációval azonos rendezéshez).</summary>
        private sealed class Utf8ByteOrder : IComparer<string>
        {
            public static readonly Utf8ByteOrder Instance = new Utf8ByteOrder();

            public int Compare(string x, string y)
            {
                byte[] a = Encoding.UTF8.GetBytes(x);
                byte[] b = Encoding.UTF8.GetBytes(y);
                for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                {
                    if (a[i] != b[i])
                    {
                        return a[i].CompareTo(b[i]);
                    }
                }
                return a.Length.CompareTo(b.Length);
            }
        }
    }
}
