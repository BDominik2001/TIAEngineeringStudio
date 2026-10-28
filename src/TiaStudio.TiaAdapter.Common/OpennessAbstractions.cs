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

        /// <summary>true, ha a PLC offline (a worker soha nem kapcsol online-ba).</summary>
        bool IsOffline();

        /// <summary>Minden blokk és típus (UDT) rekurzívan, az összes felhasználói és rendszer-alcsoporttal.</summary>
        IReadOnlyList<ExistingObject> GetAllBlocksAndTypes();

        IReadOnlyList<ExistingObject> GetExternalSources();

        /// <summary>Külső forrás létrehozása fájlból és blokkgenerálás belőle (PlcExternalSource.GenerateBlocksFromSource).</summary>
        void ImportExternalSource(string externalSourceName, string filePath);

        /// <summary>Offline fordítás (ICompilable); a rekurzív üzenetfa lapítva.</summary>
        CompileReport CompileOffline();
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
                },
            };
        }

        public IOpennessSession OpenProject(string projectPath)
        {
            throw new OpennessException(ErrorCodes.WorkerBuiltWithoutOpenness, "Openness is not linked into this worker");
        }
    }
}
