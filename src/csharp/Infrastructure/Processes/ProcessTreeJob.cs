using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace TiaStudio.Infrastructure.Processes
{
    /// <summary>
    /// Windows Job Object a worker és az általa indított gyerekfolyamatok (pl. a TIA Portal-példány)
    /// együttes kezeléséhez (audit A-07). A job a gyerekekre öröklődik, így időtúllépésnél vagy
    /// megszakításnál a teljes folyamatfa leállítható, és ellenőrizhető, maradt-e futó folyamat.
    /// A job NEM zárás-kor-kilövő: ha a főalkalmazás leáll, a worker befejezheti a munkáját.
    /// Nem Windows rendszeren (és ha a hozzárendelés nem sikerül) null, a hívó egyetlen folyamatot állít le.
    /// </summary>
    internal sealed class ProcessTreeJob : IDisposable
    {
        private const int JobObjectBasicAccountingInformation = 1;
        private IntPtr handle;

        private ProcessTreeJob(IntPtr handle)
        {
            this.handle = handle;
        }

        public static ProcessTreeJob TryAttach(Process process)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                if (AssignProcessToJobObject(job, process.Handle))
                {
                    return new ProcessTreeJob(job);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
            {
            }
            CloseHandle(job);
            return null;
        }

        /// <summary>A jobban még futó folyamatok száma, vagy -1, ha nem kérdezhető le.</summary>
        public int ActiveProcesses()
        {
            JobObjectBasicAccounting info;
            return QueryInformationJobObject(handle, JobObjectBasicAccountingInformation, out info, Marshal.SizeOf(typeof(JobObjectBasicAccounting)), IntPtr.Zero)
                ? (int)info.ActiveProcesses
                : -1;
        }

        /// <summary>A teljes folyamatfa leállítása; visszaadja a várakozás után is futó folyamatok számát.</summary>
        public int TerminateTree(TimeSpan wait)
        {
            TerminateJobObject(handle, 1);
            return WaitForEmpty(wait);
        }

        /// <summary>Várakozás, amíg a jobban nem marad futó folyamat (pl. a kilépő konzol-gazdafolyamat); a maradék száma.</summary>
        public int WaitForEmpty(TimeSpan wait)
        {
            DateTime deadline = DateTime.UtcNow + wait;
            int active = ActiveProcesses();
            while (active > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
                active = ActiveProcesses();
            }
            return active;
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicAccounting
        {
            public long TotalUserTime;
            public long TotalKernelTime;
            public long ThisPeriodTotalUserTime;
            public long ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount;
            public uint TotalProcesses;
            public uint ActiveProcesses;
            public uint TotalTerminatedProcesses;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out JobObjectBasicAccounting info, int length, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
