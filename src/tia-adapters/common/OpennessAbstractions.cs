using System;
using System.Collections.Generic;
using TiaStudio.Contracts;

namespace TiaStudio.TiaAdapter.Common
{
    /// <summary>
    /// A Siemens Openness elérésének Siemens-típusmentes absztrakciója. A V18/V19/V20 workerek
    /// a közös V18 API-szintű implementációt (OpennessShared) adják; a tesztek hamis backendet.
    /// </summary>
    public interface IOpennessBackend
    {
        /// <summary>A worker és a ténylegesen betöltött Siemens.Engineering assembly adatai.</summary>
        WorkerInfo Describe();

        /// <summary>Verzió, DLL, jogosultság és licenc előellenőrzése (spec 8.4/1). null = rendben.</summary>
        OpennessProblem ProbeEnvironment();

        TiaCapabilities GetCapabilities();

        /// <summary>A projekt megnyitása frissítés NÉLKÜL. Projects.OpenWithUpgrade tilos itt.</summary>
        IOpennessSession OpenProject(string projectPath);
    }

    /// <summary>Egy megnyitott projekt; Dispose bezárja mentés nélkül és felszabadítja a TIA-példányt.</summary>
    public interface IOpennessSession : IDisposable
    {
        /// <summary>A megadott nevű PLC-szoftver, vagy null.</summary>
        IPlcTarget FindPlc(string plcName);

        void Save();
    }

    public sealed class ExistingObject
    {
        public ExistingObject(string name, string location)
        {
            Name = name;
            Location = location;
        }

        public string Name { get; }

        /// <summary>Emberi olvasásra szánt hely, pl. "Program blocks/Station1/Motion".</summary>
        public string Location { get; }
    }

    public interface IPlcTarget
    {
        string Name { get; }

        /// <summary>
        /// A CPU hardverazonosítója (DeviceItem.TypeIdentifier, pl. "OrderNumber:6ES7 516-3AN02-0AB0/V2.9"),
        /// vagy null, ha nem olvasható. A CPU-családot ebből a <see cref="PlcHardware"/> határozza meg.
        /// </summary>
        string CpuTypeIdentifier { get; }

        /// <summary>
        /// Az online állapot lekérdezése (a worker soha nem kapcsol online-ba). Hiányzó OnlineProvider vagy
        /// nem egyértelmű állapot = <see cref="PlcOnlineState.Unknown"/>; írni/exportálni csak Offline mellett szabad.
        /// </summary>
        PlcOnlineState GetOnlineState();

        /// <summary>Minden blokk és típus (UDT) rekurzívan, az összes felhasználói és rendszer-alcsoporttal.</summary>
        IReadOnlyList<ExistingObject> GetAllBlocksAndTypes();

        IReadOnlyList<ExistingObject> GetExternalSources();

        /// <summary>Külső forrás létrehozása fájlból és blokkgenerálás belőle (PlcExternalSource.GenerateBlocksFromSource).</summary>
        void ImportExternalSource(string externalSourceName, string filePath);

        /// <summary>Offline fordítás (ICompilable); a rekurzív üzenetfa lapítva.</summary>
        CompileReport CompileOffline();

        /// <summary>Felhasználói blokkok és PLC-adattípusok (rendszerblokkok nélkül) metaadatai.</summary>
        IReadOnlyList<ProjectBlockInfo> DescribeBlocks();

        /// <summary>
        /// Egy blokk/adattípus exportja fájlba, a projekt módosítása nélkül:
        /// external_source = PlcExternalSourceSystemGroup.GenerateSource, simaticml = PlcBlock/PlcType.Export.
        /// </summary>
        void ExportBlock(ProjectBlockInfo block, string format, string filePath);

        /// <summary>SimaticML import a gyökércsoportba felülírás nélkül (ImportOptions.None).</summary>
        void ImportSimaticMl(string blockType, string filePath);
    }

    public enum PlcOnlineState
    {
        /// <summary>Kifejezetten offline (OnlineState.Offline): az egyetlen engedélyező állapot.</summary>
        Offline,

        /// <summary>Online, kapcsolódó vagy bontó kapcsolat.</summary>
        Online,

        /// <summary>Nem lekérdezhető vagy nem egyértelmű (nincs OnlineProvider, Incompatible, NotReachable, Protected).</summary>
        Unknown,
    }

    /// <summary>A CPU-család meghatározása a TIA-projekt hardverazonosítójából (Siemens-típusok nélkül).</summary>
    public static class PlcHardware
    {
        public const string S71200 = "S7-1200";
        public const string S71500 = "S7-1500";

        /// <summary>
        /// "OrderNumber:&lt;rendelési szám&gt;[/&lt;firmware&gt;]" (V18 kézikönyv 5.9, TypeIdentifier). S7-1200 CPU:
        /// 6ES7 21x-…, S7-1500 CPU (ET 200SP/pro CPU-k is): 6ES7 51x-…; SIPLUS-változat: 6AG1/6AG2 ugyanígy.
        /// Minden más (GSD/System azonosító, ismeretlen rendelési szám) = null, vagyis nem ellenőrizhető.
        /// </summary>
        public static string CpuFamilyOf(string typeIdentifier)
        {
            const string prefix = "OrderNumber:";
            if (string.IsNullOrEmpty(typeIdentifier) || !typeIdentifier.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }
            string orderNumber = typeIdentifier.Substring(prefix.Length);
            int slash = orderNumber.IndexOf('/');
            if (slash >= 0)
            {
                orderNumber = orderNumber.Substring(0, slash);
            }
            orderNumber = orderNumber.Replace(" ", string.Empty).ToUpperInvariant();
            if (orderNumber.Length < 8 || !(orderNumber.StartsWith("6ES7", StringComparison.Ordinal)
                || orderNumber.StartsWith("6AG1", StringComparison.Ordinal) || orderNumber.StartsWith("6AG2", StringComparison.Ordinal)))
            {
                return null;
            }
            string series = orderNumber.Substring(4, 2);
            bool digit = char.IsDigit(orderNumber[6]) && orderNumber[7] == '-';
            if (!digit)
            {
                return null;
            }
            return series == "21" ? S71200 : series == "51" ? S71500 : null;
        }
    }

    public sealed class OpennessProblem
    {
        public OpennessProblem(string errorCode, string message)
        {
            ErrorCode = errorCode;
            Message = message;
        }

        public string ErrorCode { get; }

        public string Message { get; }
    }

    /// <summary>Openness-hiba stabil hibakóddal.</summary>
    public sealed class OpennessException : Exception
    {
        public OpennessException(string errorCode, string message, Exception inner = null)
            : base(message, inner)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }

    /// <summary>
    /// Siemens-kivételek besorolása típusnév és üzenet alapján, hogy a közös kód ne függjön
    /// verziófüggő kivételtípusoktól. Ismeretlen eset: a megadott alapértelmezett kód.
    /// </summary>
    public static class OpennessErrorClassifier
    {
        public static string Classify(Exception exception, string fallbackCode)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is OpennessException known)
                {
                    return known.ErrorCode;
                }
                string type = current.GetType().FullName ?? string.Empty;
                string message = (current.Message ?? string.Empty).ToLowerInvariant();
                // V18 kézikönyv 5.11.17.1: NonRecoverableException bezárja a TIA Portalt.
                if (type.EndsWith("NonRecoverableException", StringComparison.Ordinal))
                {
                    return ErrorCodes.TiaPortalTerminated;
                }
                // V18 kézikönyv 2.2: V18-tól explicit licencellenőrzés, LicenseNotFoundException.
                if (type.EndsWith("LicenseNotFoundException", StringComparison.Ordinal))
                {
                    return ErrorCodes.LicenseMissing;
                }
                if (message.Contains("licen"))
                {
                    return ErrorCodes.LicenseMissing;
                }
                if (message.Contains("umac") || message.Contains("user management") || message.Contains("modify project via openness"))
                {
                    return ErrorCodes.UmacPermissionDenied;
                }
                if (type.EndsWith("EngineeringSecurityException", StringComparison.Ordinal) || message.Contains("siemens tia openness"))
                {
                    return ErrorCodes.OpennessAccessDenied;
                }
                if (message.Contains("upgrade") || message.Contains("migrat") || message.Contains("older version") || message.Contains("newer version"))
                {
                    return ErrorCodes.ProjectUpgradeRequired;
                }
                if (current is System.IO.FileNotFoundException || current is System.IO.FileLoadException)
                {
                    return message.Contains("siemens.engineering") ? ErrorCodes.ApiDllMismatch : fallbackCode;
                }
            }
            return fallbackCode;
        }
    }

    /// <summary>Openness-referencia nélkül fordított worker: minden TIA-művelet explicit elutasítva.</summary>
    public sealed class UnlinkedOpennessBackend : IOpennessBackend
    {
        private readonly string tiaVersion;

        public UnlinkedOpennessBackend(string tiaVersion)
        {
            this.tiaVersion = tiaVersion;
        }

        public WorkerInfo Describe()
        {
            return new WorkerInfo
            {
                Name = "TiaStudio.TiaAdapter." + tiaVersion,
                Version = typeof(UnlinkedOpennessBackend).Assembly.GetName().Version.ToString(3),
                OpennessLinked = false,
            };
        }

        public OpennessProblem ProbeEnvironment()
        {
            return new OpennessProblem(
                ErrorCodes.WorkerBuiltWithoutOpenness,
                "This worker was built without a Siemens.Engineering.dll reference (TiaApiPath not found at build time). Rebuild it on a machine with TIA Portal " + tiaVersion + " Openness installed.");
        }

        public TiaCapabilities GetCapabilities()
        {
            return new TiaCapabilities
            {
                TiaVersion = tiaVersion,
                Operations = new List<string> { TiaOperations.GetCapabilities },
                Features = new Dictionary<string, bool>
                {
                    { CapabilityFeatures.SclExternalSourceImport, false },
                    { CapabilityFeatures.OfflineCompile, false },
                    { CapabilityFeatures.RecursiveBlockCollisionCheck, false },
                    { CapabilityFeatures.ProjectUpgrade, false },
                    { CapabilityFeatures.Hmi, false },
                    { CapabilityFeatures.SimaticMlImport, false },
                    { CapabilityFeatures.PlcDownload, false },
                    { CapabilityFeatures.BlockExport, false },
                    { CapabilityFeatures.SimaticMlBlockImport, false },
                },
            };
        }

        public IOpennessSession OpenProject(string projectPath)
        {
            throw new OpennessException(ErrorCodes.WorkerBuiltWithoutOpenness, "Openness is not linked into this worker");
        }
    }
}
