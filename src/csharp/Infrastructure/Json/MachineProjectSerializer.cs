using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TiaStudio.Application.Ports;
using TiaStudio.Domain.Model;

namespace TiaStudio.Infrastructure.Json
{
    /// <summary>
    /// Kanonikus, determinisztikus JSON (schema 1.1): rögzített mezősorrend, 2 szóközös behúzás,
    /// LF sorvég, BOM nélkül, záró sortöréssel. Az alapértelmezett opcionális mezők elmaradnak.
    /// </summary>
    public sealed class MachineProjectSerializer : IMachineProjectSerializer
    {
        public byte[] Serialize(MachineProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteString("schema_version", project.SchemaVersion);
                    writer.WriteString("project_name", project.ProjectName);
                    if (!string.IsNullOrEmpty(project.Description))
                    {
                        writer.WriteString("description", project.Description);
                    }
                    writer.WriteStartObject("target");
                    writer.WriteString("tia_version", project.Target?.TiaVersion);
                    writer.WriteString("plc_name", project.Target?.PlcName);
                    writer.WriteString("cpu_family", project.Target?.CpuFamily);
                    writer.WriteEndObject();
                    writer.WriteStartArray("devices");
                    foreach (Device device in project.Devices ?? new List<Device>())
                    {
                        WriteDevice(writer, device);
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                stream.WriteByte((byte)'\n');
                return stream.ToArray();
            }
        }

        private static void WriteDevice(Utf8JsonWriter writer, Device device)
        {
            writer.WriteStartObject();
            writer.WriteString("id", device.Id);
            writer.WriteString("kind", device.Kind);
            if (!string.IsNullOrEmpty(device.Description))
            {
                writer.WriteString("description", device.Description);
            }
            if (device is CylinderDevice cylinder)
            {
                writer.WriteStartObject("inputs");
                writer.WriteString("retracted", cylinder.RetractedInput);
                writer.WriteString("extended", cylinder.ExtendedInput);
                writer.WriteEndObject();
                if (cylinder.MonitorTimeMs.HasValue)
                {
                    writer.WriteStartObject("parameters");
                    writer.WriteNumber("monitor_time_ms", cylinder.MonitorTimeMs.Value);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndObject();
        }

        public MachineProject Deserialize(byte[] json)
        {
            int offset = json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF ? 3 : 0;
            using (JsonDocument document = JsonDocument.Parse(new ReadOnlyMemory<byte>(json, offset, json.Length - offset)))
            {
                JsonElement root = document.RootElement;
                JsonElement target = root.GetProperty("target");
                MachineProject project = new MachineProject
                {
                    SchemaVersion = root.GetProperty("schema_version").GetString(),
                    ProjectName = root.GetProperty("project_name").GetString(),
                    Description = OptionalString(root, "description"),
                    Target = new MachineTarget
                    {
                        TiaVersion = target.GetProperty("tia_version").GetString(),
                        PlcName = target.GetProperty("plc_name").GetString(),
                        CpuFamily = target.GetProperty("cpu_family").GetString(),
                    },
                };
                foreach (JsonElement device in root.GetProperty("devices").EnumerateArray())
                {
                    string kind = device.GetProperty("kind").GetString();
                    if (kind != DeviceKinds.Cylinder)
                    {
                        throw new InvalidDataException("Unsupported device kind: " + kind);
                    }
                    JsonElement inputs = device.GetProperty("inputs");
                    int? monitor = null;
                    if (device.TryGetProperty("parameters", out JsonElement parameters) && parameters.TryGetProperty("monitor_time_ms", out JsonElement value))
                    {
                        monitor = value.GetInt32();
                    }
                    project.Devices.Add(new CylinderDevice
                    {
                        Id = device.GetProperty("id").GetString(),
                        Description = OptionalString(device, "description"),
                        RetractedInput = inputs.GetProperty("retracted").GetString(),
                        ExtendedInput = inputs.GetProperty("extended").GetString(),
                        MonitorTimeMs = monitor,
                    });
                }
                return project;
            }
        }

        private static string OptionalString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
    }
}
