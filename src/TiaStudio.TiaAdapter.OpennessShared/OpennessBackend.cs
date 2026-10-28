#if TES_OPENNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Online;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;
using TiaStudio.Contracts;
using TiaStudio.TiaAdapter.Common;

namespace TiaStudio.TiaAdapter.Openness
{
    /// <summary>
    /// Openness-implementáció a V18 API közös funkciókészletével (spec 3.2/A). Ugyanez a forrás
    /// fordul a V18, V19 és V20 workerbe, mindegyik a saját telepítésének Siemens.Engineering.dll-jével.
    /// Tiltott: Projects.OpenWithUpgrade, letöltés, online kapcsolat, safety-objektumok módosítása.
    /// </summary>
    internal sealed class OpennessBackend : IOpennessBackend
    {
        private const string SiemensPublicKeyToken = "d29ec89bac048f84";
        private const string OpennessGroup = "Siemens TIA Openness";
        private readonly string tiaVersion;

        public OpennessBackend(string tiaVersion)
        {
            this.tiaVersion = tiaVersion;
        }

        public WorkerInfo Describe()
        {
            WorkerInfo info = new WorkerInfo
            {
                Name = "TiaStudio.TiaAdapter." + tiaVersion,
                Version = typeof(OpennessBackend).Assembly.GetName().Version.ToString(3),
                OpennessLinked = true,
            };
            try
            {
                Assembly engineering = EngineeringAssembly();
                info.EngineeringAssemblyPath = engineering.Location;
                info.EngineeringAssemblyVersion = engineering.GetName().Version.ToString();
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is FileLoadException || ex is BadImageFormatException)
            {
                info.EngineeringAssemblyPath = null;
            }
            return info;
        }

        public OpennessProblem ProbeEnvironment()
        {
            Assembly engineering;
            try
            {
                engineering = EngineeringAssembly();
            }
            catch (FileNotFoundException ex)
            {
                return new OpennessProblem(ErrorCodes.TiaNotInstalled,
                    "Siemens.Engineering.dll of TIA Portal " + tiaVersion + " cannot be loaded (" + ex.Message + "). Check the installation and run scripts/configure-workers.ps1.");
            }
            catch (Exception ex) when (ex is FileLoadException || ex is BadImageFormatException)
            {
                return new OpennessProblem(ErrorCodes.ApiDllMismatch, "Siemens.Engineering.dll cannot be loaded: " + ex.Message);
            }
            AssemblyName name = engineering.GetName();
            int expectedMajor = TiaVersions.Major(tiaVersion);
            if (name.Version.Major != expectedMajor)
            {
                return new OpennessProblem(ErrorCodes.ApiDllMismatch,
                    "Loaded Siemens.Engineering " + name.Version + " from " + engineering.Location + " does not belong to " + tiaVersion);
            }
            string token = string.Concat((name.GetPublicKeyToken() ?? new byte[0]).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            if (token != SiemensPublicKeyToken)
            {
                return new OpennessProblem(ErrorCodes.ApiDllMismatch, "Unexpected public key token " + token + " for Siemens.Engineering");
            }
            if (!IsMemberOfOpennessGroup())
            {
                return new OpennessProblem(ErrorCodes.OpennessAccessDenied,
                    "The current Windows user is not a member of the local group '" + OpennessGroup + "'. Add the user, then log off and on again.");
            }
            return null;
        }

        public TiaCapabilities GetCapabilities()
        {
            return new TiaCapabilities
            {
                TiaVersion = tiaVersion,
                ApiLevel = "V18-common",
                Operations = new List<string> { TiaOperations.GetCapabilities, TiaOperations.ValidateJob, TiaOperations.ImportAndCompile },
                Features = new Dictionary<string, bool>
                {
                    { CapabilityFeatures.SclExternalSourceImport, true },
                    { CapabilityFeatures.OfflineCompile, true },
                    { CapabilityFeatures.RecursiveBlockCollisionCheck, true },
                    { CapabilityFeatures.ProjectUpgrade, false },
                    { CapabilityFeatures.Hmi, false },
                    { CapabilityFeatures.SimaticMlImport, false },
                    { CapabilityFeatures.PlcDownload, false },
                },
            };
        }

        public IOpennessSession OpenProject(string projectPath)
        {
            return OpenProjectCore(projectPath);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Assembly EngineeringAssembly()
        {
            return typeof(TiaPortal).Assembly;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IOpennessSession OpenProjectCore(string projectPath)
        {
            TiaPortal portal = null;
            try
            {
                // A Siemens Openness hozzáférési párbeszédét a felhasználó kezeli; nem kerüljük meg.
                portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                // Szándékosan Open és NEM OpenWithUpgrade: frissítést igénylő projekt itt hibát ad.
                Project project = portal.Projects.Open(new FileInfo(projectPath));
                return new OpennessSession(portal, project);
            }
            catch
            {
                portal?.Dispose();
                throw;
            }
        }

        private static bool IsMemberOfOpennessGroup()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (identity.Groups == null)
                {
                    return false;
                }
                foreach (IdentityReference group in identity.Groups)
                {
                    try
                    {
                        string account = group.Translate(typeof(NTAccount)).Value;
                        if (account.Equals(OpennessGroup, StringComparison.OrdinalIgnoreCase)
                            || account.EndsWith("\\" + OpennessGroup, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch (IdentityNotMappedException)
                    {
                    }
                }
            }
            return false;
        }
    }

    internal sealed class OpennessSession : IOpennessSession
    {
        private readonly TiaPortal portal;
        private Project project;

        public OpennessSession(TiaPortal portal, Project project)
        {
            this.portal = portal;
            this.project = project;
        }

        public IPlcTarget FindPlc(string plcName)
        {
            List<OpennessPlcTarget> matches = new List<OpennessPlcTarget>();
            foreach (Device device in AllDevices())
            {
                foreach (DeviceItem item in AllItems(device.DeviceItems))
                {
                    SoftwareContainer container = item.GetService<SoftwareContainer>();
                    if (container?.Software is PlcSoftware software
                        && (string.Equals(software.Name, plcName, StringComparison.Ordinal) || string.Equals(item.Name, plcName, StringComparison.Ordinal)))
                    {
                        matches.Add(new OpennessPlcTarget(item, software));
                    }
                }
            }
            // Többértelmű egyezésnél nem választunk: PLC_NOT_FOUND.
            return matches.Count == 1 ? matches[0] : null;
        }

        public void Save()
        {
            project.Save();
        }

        public void Dispose()
        {
            try
            {
                // Mentés nélküli bezárás: a nem mentett módosítások elvesznek (a sandboxot ettől még újra kell előállítani).
                project?.Close();
            }
            finally
            {
                project = null;
                portal?.Dispose();
            }
        }

        private IEnumerable<Device> AllDevices()
        {
            HashSet<Device> seen = new HashSet<Device>();
            foreach (Device device in project.Devices)
            {
                if (seen.Add(device))
                {
                    yield return device;
                }
            }
            foreach (Device device in project.UngroupedDevicesGroup.Devices)
            {
                if (seen.Add(device))
                {
                    yield return device;
                }
            }
            Stack<DeviceUserGroup> groups = new Stack<DeviceUserGroup>(project.DeviceGroups);
            while (groups.Count > 0)
            {
                DeviceUserGroup group = groups.Pop();
                foreach (Device device in group.Devices)
                {
                    if (seen.Add(device))
                    {
                        yield return device;
                    }
                }
                foreach (DeviceUserGroup child in group.Groups)
                {
                    groups.Push(child);
                }
            }
        }

        private static IEnumerable<DeviceItem> AllItems(DeviceItemComposition items)
        {
            foreach (DeviceItem item in items)
            {
                yield return item;
                foreach (DeviceItem child in AllItems(item.DeviceItems))
                {
                    yield return child;
                }
            }
        }
    }

    internal sealed class OpennessPlcTarget : IPlcTarget
    {
        private const int MaxCompileMessages = 5000;
        private readonly DeviceItem cpu;
        private readonly PlcSoftware software;

        public OpennessPlcTarget(DeviceItem cpu, PlcSoftware software)
        {
            this.cpu = cpu;
            this.software = software;
        }

        public string Name => software.Name;

        public bool IsOffline()
        {
            // Csak lekérdezés; a worker soha nem hív GoOnline-t.
            OnlineProvider online = cpu.GetService<OnlineProvider>();
            return online == null || online.State == OnlineState.Offline;
        }

        public IReadOnlyList<ExistingObject> GetAllBlocksAndTypes()
        {
            List<ExistingObject> result = new List<ExistingObject>();
            CollectBlocks(software.BlockGroup, "Program blocks", result);
            foreach (PlcSystemBlockGroup systemGroup in software.BlockGroup.SystemBlockGroups)
            {
                CollectBlocks(systemGroup, "Program blocks/System blocks/" + systemGroup.Name, result);
            }
            CollectTypes(software.TypeGroup, "PLC data types", result);
            foreach (PlcSystemTypeGroup systemTypes in software.TypeGroup.SystemTypeGroups)
            {
                CollectTypes(systemTypes, "PLC data types/System data types/" + systemTypes.Name, result);
            }
            return result;
        }

        public IReadOnlyList<ExistingObject> GetExternalSources()
        {
            List<ExistingObject> result = new List<ExistingObject>();
            CollectSources(software.ExternalSourceGroup, "External source files", result);
            return result;
        }

        public void ImportExternalSource(string externalSourceName, string filePath)
        {
            // V18 kézikönyv: GenerateBlocksFromSource felülírhat meglévő blokkot – ezért fut előtte
            // a teljes rekurzív ütközésvizsgálat, és csak jóváhagyott sandboxban hívható.
            PlcExternalSource source = software.ExternalSourceGroup.ExternalSources.CreateFromFile(externalSourceName, filePath);
            source.GenerateBlocksFromSource();
        }

        public CompileReport CompileOffline()
        {
            ICompilable compilable = software.GetService<ICompilable>();
            if (compilable == null)
            {
                throw new OpennessException(ErrorCodes.CompileError, "ICompilable service is not available for PLC " + software.Name);
            }
            CompilerResult compiled = compilable.Compile();
            CompileReport report = new CompileReport
            {
                State = compiled.State.ToString(),
                ErrorCount = compiled.ErrorCount,
                WarningCount = compiled.WarningCount,
            };
            Flatten(compiled.Messages, 0, report.Messages);
            return report;
        }

        private static void Flatten(CompilerResultMessageComposition messages, int depth, List<CompileMessage> output)
        {
            foreach (CompilerResultMessage message in messages)
            {
                if (output.Count >= MaxCompileMessages)
                {
                    return;
                }
                output.Add(new CompileMessage
                {
                    Depth = depth,
                    State = message.State.ToString(),
                    Path = message.Path,
                    Description = message.Description,
                    ErrorCount = message.ErrorCount,
                    WarningCount = message.WarningCount,
                });
                Flatten(message.Messages, depth + 1, output);
            }
        }

        private static void CollectBlocks(PlcBlockGroup group, string location, List<ExistingObject> output)
        {
            foreach (PlcBlock block in group.Blocks)
            {
                output.Add(new ExistingObject(block.Name, location));
            }
            foreach (PlcBlockUserGroup child in group.Groups)
            {
                CollectBlocks(child, location + "/" + child.Name, output);
            }
        }

        private static void CollectTypes(PlcTypeGroup group, string location, List<ExistingObject> output)
        {
            foreach (PlcType type in group.Types)
            {
                output.Add(new ExistingObject(type.Name, location));
            }
            foreach (PlcTypeUserGroup child in group.Groups)
            {
                CollectTypes(child, location + "/" + child.Name, output);
            }
        }

        private static void CollectSources(PlcExternalSourceGroup group, string location, List<ExistingObject> output)
        {
            foreach (PlcExternalSource source in group.ExternalSources)
            {
                output.Add(new ExistingObject(source.Name, location));
            }
            foreach (PlcExternalSourceUserGroup child in group.Groups)
            {
                CollectSources(child, location + "/" + child.Name, output);
            }
        }
    }
}
#endif
