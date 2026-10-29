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
                Operations = new List<string>
                {
                    TiaOperations.GetCapabilities, TiaOperations.ValidateJob, TiaOperations.ImportAndCompile,
                    TiaOperations.ListBlocks, TiaOperations.ExportBlocks,
                },
                Features = new Dictionary<string, bool>
                {
                    { CapabilityFeatures.SclExternalSourceImport, true },
                    { CapabilityFeatures.OfflineCompile, true },
                    { CapabilityFeatures.RecursiveBlockCollisionCheck, true },
                    { CapabilityFeatures.ProjectUpgrade, false },
                    { CapabilityFeatures.Hmi, false },
                    { CapabilityFeatures.SimaticMlImport, false },
                    { CapabilityFeatures.PlcDownload, false },
                    { CapabilityFeatures.BlockExport, true },
                    { CapabilityFeatures.SimaticMlBlockImport, true },
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

    /// <summary>
    /// PLC-szoftver elérése a TIA Portal Openness V18 kézikönyv (12/2022) szerint. Hivatkozások:
    /// blokkcsoportok 5.11.3.1-5.11.3.4, külső források 5.11.3.17-5.11.3.19, blokkexport/-import
    /// 6.4.1.12/6.4.1.13, UDT-export/-import 6.4.1.27/6.4.1.28, fordítás 5.3 (ICompilable),
    /// online állapot 5.11.2.5 (OnlineProvider), attribútumok 5.2.23.
    /// </summary>
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

        /// <summary>5.9 / 5.11.15.3: DeviceItem.TypeIdentifier, pl. "OrderNumber:6ES7 516-3AN02-0AB0/V2.9".</summary>
        public string CpuTypeIdentifier => cpu.TypeIdentifier;

        public PlcOnlineState GetOnlineState()
        {
            // 5.11.2.5: csak lekérdezés; a worker soha nem hív GoOnline-t. Fail-closed: hiányzó szolgáltatás
            // vagy nem egyértelmű állapot (Incompatible, NotReachable, Protected) nem számít offline-nak.
            OnlineProvider online = cpu.GetService<OnlineProvider>();
            if (online == null)
            {
                return PlcOnlineState.Unknown;
            }
            switch (online.State)
            {
                case OnlineState.Offline:
                    return PlcOnlineState.Offline;
                case OnlineState.Online:
                case OnlineState.Connecting:
                case OnlineState.Disconnecting:
                    return PlcOnlineState.Online;
                default:
                    return PlcOnlineState.Unknown;
            }
        }

        public IReadOnlyList<ExistingObject> GetAllBlocksAndTypes()
        {
            List<ExistingObject> result = new List<ExistingObject>();
            foreach (Tuple<PlcBlock, string> entry in UserBlocks())
            {
                result.Add(new ExistingObject(entry.Item1.Name, entry.Item2));
            }
            // 5.11.3.2/5.11.3.3: rendszerblokk-csoportok (PlcSystemBlockGroup.Groups rekurzívan).
            foreach (PlcSystemBlockGroup systemGroup in software.BlockGroup.SystemBlockGroups)
            {
                CollectSystemBlocks(systemGroup, "Program blocks/System blocks/" + systemGroup.Name, result);
            }
            foreach (Tuple<PlcType, string> entry in UserTypes())
            {
                result.Add(new ExistingObject(entry.Item1.Name, entry.Item2));
            }
            return result;
        }

        public IReadOnlyList<ExistingObject> GetExternalSources()
        {
            // 5.11.3.17: az "External source files" mappa csoportjai Opennessből nem érhetők el.
            List<ExistingObject> result = new List<ExistingObject>();
            foreach (PlcExternalSource source in software.ExternalSourceGroup.ExternalSources)
            {
                result.Add(new ExistingObject(source.Name, "External source files"));
            }
            return result;
        }

        public void ImportExternalSource(string externalSourceName, string filePath)
        {
            // 5.11.3.17: csak .scl/.db/.udt/.awl; 5.11.3.19: csak ASCII forrás, meglévő blokkot FELÜLÍR –
            // ezért fut előtte a teljes rekurzív ütközésvizsgálat, és csak jóváhagyott sandboxban hívható.
            // Hiba esetén a projekt a hívás előtti állapotba áll vissza.
            PlcExternalSource source = software.ExternalSourceGroup.ExternalSources.CreateFromFile(externalSourceName, filePath);
            source.GenerateBlocksFromSource();
        }

        public CompileReport CompileOffline()
        {
            // 5.3: minden eszköznek offline-nak kell lennie; jelszóval védett safety program fordítása bejelentkezés nélkül kivételt ad.
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

        public IReadOnlyList<ProjectBlockInfo> DescribeBlocks()
        {
            List<ProjectBlockInfo> result = new List<ProjectBlockInfo>();
            foreach (Tuple<PlcBlock, string> entry in UserBlocks())
            {
                PlcBlock block = entry.Item1;
                string language = block.ProgrammingLanguage.ToString();
                result.Add(new ProjectBlockInfo
                {
                    Name = block.Name,
                    BlockType = block is FB ? BlockTypes.FB : block is FC ? BlockTypes.FC : block is OB ? BlockTypes.OB
                        : block is GlobalDB || block is InstanceDB || block is ArrayDB ? BlockTypes.DB : "Unknown",
                    DbKind = block is InstanceDB ? DbKinds.Instance : block is ArrayDB ? DbKinds.Array : block is GlobalDB ? DbKinds.Global : null,
                    Number = block.Number,
                    ProgrammingLanguage = language,
                    Location = entry.Item2,
                    IsKnowHowProtected = BoolAttribute(block, "IsKnowHowProtected"),
                    IsConsistent = block.IsConsistent,
                    IsSafety = LibraryRules.IsSafetyLanguage(language),
                });
            }
            foreach (Tuple<PlcType, string> entry in UserTypes())
            {
                // 5.2.23: UDT-n nincs ProgrammingLanguage; IsConsistent és IsKnowHowProtected olvasható attribútum.
                result.Add(new ProjectBlockInfo
                {
                    Name = entry.Item1.Name,
                    BlockType = BlockTypes.UDT,
                    Location = entry.Item2,
                    IsKnowHowProtected = BoolAttribute(entry.Item1, "IsKnowHowProtected"),
                    IsConsistent = BoolAttribute(entry.Item1, "IsConsistent"),
                });
            }
            return result;
        }

        public void ExportBlock(ProjectBlockInfo block, string format, string filePath)
        {
            // Mindkét út csak fájlt ír; a projektet nem módosítja és nem menti. A célfájl nem létezhet.
            FileInfo target = new FileInfo(filePath);
            if (block.BlockType == BlockTypes.UDT)
            {
                PlcType type = UserTypes().Select(t => t.Item1).FirstOrDefault(t => t.Name == block.Name)
                    ?? throw new OpennessException(ErrorCodes.BlockNotFound, "PLC data type " + block.Name + " not found");
                if (format == BlockFormats.SimaticMl)
                {
                    type.Export(target, ExportOptions.WithDefaults);
                }
                else
                {
                    // 5.11.3.18: UDT-forrás csak *.udt kiterjesztéssel; GenerateOptions.None = csak a megadott objektum.
                    software.ExternalSourceGroup.GenerateSource(new IGenerateSource[] { type }, target, GenerateOptions.None);
                }
                return;
            }
            PlcBlock plcBlock = UserBlocks().Select(b => b.Item1).FirstOrDefault(b => b.Name == block.Name)
                ?? throw new OpennessException(ErrorCodes.BlockNotFound, "Block " + block.Name + " not found");
            if (format == BlockFormats.SimaticMl)
            {
                // 6.4.1.13: csak konzisztens blokk exportálható.
                plcBlock.Export(target, ExportOptions.WithDefaults);
            }
            else
            {
                // 5.11.3.18: csak STL/SCL blokk, *.scl / *.db kiterjesztés.
                software.ExternalSourceGroup.GenerateSource(new IGenerateSource[] { plcBlock }, target, GenerateOptions.None);
            }
        }

        public void ImportSimaticMl(string blockType, string filePath)
        {
            // 6.1.6: ImportOptions.None – meglévő objektumnál kivétel, nincs felülírás (az ütközésvizsgálat előtte lefutott).
            // 6.4.1.12: hiányzó példány-DB nem jön létre automatikusan; hivatkozott UDT/FB hiánya hibát ad
            // (ezért az importsorrend UDT → FC → FB → DB).
            if (blockType == BlockTypes.UDT)
            {
                software.TypeGroup.Types.Import(new FileInfo(filePath), ImportOptions.None);
            }
            else
            {
                software.BlockGroup.Blocks.Import(new FileInfo(filePath), ImportOptions.None);
            }
        }

        /// <summary>A "Program blocks" gyökér és minden felhasználói alcsoport blokkjai (5.11.3.4).</summary>
        private IEnumerable<Tuple<PlcBlock, string>> UserBlocks()
        {
            foreach (PlcBlock block in software.BlockGroup.Blocks)
            {
                yield return Tuple.Create(block, "Program blocks");
            }
            Stack<Tuple<PlcBlockUserGroup, string>> pending = new Stack<Tuple<PlcBlockUserGroup, string>>();
            foreach (PlcBlockUserGroup group in software.BlockGroup.Groups)
            {
                pending.Push(Tuple.Create(group, "Program blocks/" + group.Name));
            }
            while (pending.Count > 0)
            {
                Tuple<PlcBlockUserGroup, string> current = pending.Pop();
                foreach (PlcBlock block in current.Item1.Blocks)
                {
                    yield return Tuple.Create(block, current.Item2);
                }
                foreach (PlcBlockUserGroup child in current.Item1.Groups)
                {
                    pending.Push(Tuple.Create(child, current.Item2 + "/" + child.Name));
                }
            }
        }

        /// <summary>A "PLC data types" gyökér és minden felhasználói alcsoport típusai.</summary>
        private IEnumerable<Tuple<PlcType, string>> UserTypes()
        {
            foreach (PlcType type in software.TypeGroup.Types)
            {
                yield return Tuple.Create(type, "PLC data types");
            }
            Stack<Tuple<PlcTypeUserGroup, string>> pending = new Stack<Tuple<PlcTypeUserGroup, string>>();
            foreach (PlcTypeUserGroup group in software.TypeGroup.Groups)
            {
                pending.Push(Tuple.Create(group, "PLC data types/" + group.Name));
            }
            while (pending.Count > 0)
            {
                Tuple<PlcTypeUserGroup, string> current = pending.Pop();
                foreach (PlcType type in current.Item1.Types)
                {
                    yield return Tuple.Create(type, current.Item2);
                }
                foreach (PlcTypeUserGroup child in current.Item1.Groups)
                {
                    pending.Push(Tuple.Create(child, current.Item2 + "/" + child.Name));
                }
            }
        }

        private static void CollectSystemBlocks(PlcSystemBlockGroup group, string location, List<ExistingObject> output)
        {
            foreach (PlcBlock block in group.Blocks)
            {
                output.Add(new ExistingObject(block.Name, location));
            }
            foreach (PlcSystemBlockGroup child in group.Groups)
            {
                CollectSystemBlocks(child, location + "/" + child.Name, output);
            }
        }

        /// <summary>5.2.23: logikai attribútum olvasása GetAttribute-tal (blokkra és UDT-re is dokumentált).</summary>
        private static bool BoolAttribute(IEngineeringObject engineeringObject, string name)
        {
            object value = engineeringObject.GetAttribute(name);
            return value is bool flag && flag;
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
    }
}
#endif
