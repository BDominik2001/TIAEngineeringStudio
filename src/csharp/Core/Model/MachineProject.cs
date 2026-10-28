using System.Collections.Generic;

namespace TiaStudio.Domain.Model
{
    /// <summary>
    /// Verziófüggetlen gépmodell (schema_version 1.1). A TIA-célverzió csak exportcél;
    /// ugyanabból a modellből V18/V19/V20 forrás állítható elő (spec 6.1).
    /// </summary>
    public sealed class MachineProject
    {
        public const string CurrentSchemaVersion = "1.1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string ProjectName { get; set; }

        public string Description { get; set; }

        public MachineTarget Target { get; set; } = new MachineTarget();

        public List<Device> Devices { get; set; } = new List<Device>();
    }

    public sealed class MachineTarget
    {
        public string TiaVersion { get; set; }

        public string PlcName { get; set; }

        public string CpuFamily { get; set; }
    }

    public abstract class Device
    {
        public string Id { get; set; }

        public string Description { get; set; }

        public abstract string Kind { get; }

        /// <summary>A generált példány-DB neve (DB_&lt;Function&gt;_&lt;Instance&gt;).</summary>
        public abstract string InstanceBlockName { get; }
    }

    public sealed class CylinderDevice : Device
    {
        public const int DefaultMonitorTimeMs = 2000;

        public override string Kind => DeviceKinds.Cylinder;

        public override string InstanceBlockName => "DB_Cylinder_" + Id;

        public string RetractedInput { get; set; }

        public string ExtendedInput { get; set; }

        /// <summary>null = alapérték (2000 ms); a JSON-ban ilyenkor a parameters objektum elmarad.</summary>
        public int? MonitorTimeMs { get; set; }
    }

    public static class DeviceKinds
    {
        public const string Cylinder = "cylinder";

        public static readonly IReadOnlyList<string> Supported = new[] { Cylinder };
    }

    public static class CpuFamilies
    {
        public const string S71200 = "S7-1200";
        public const string S71500 = "S7-1500";

        public static readonly IReadOnlyList<string> Supported = new[] { S71200, S71500 };

        /// <summary>A bemeneti folyamatkép legnagyobb bájtcíme, vagy null ismeretlen családnál.</summary>
        public static int? MaxInputByte(string cpuFamily)
        {
            switch (cpuFamily)
            {
                case S71200:
                    return 1023;
                case S71500:
                    return 32767;
                default:
                    return null;
            }
        }
    }
}
