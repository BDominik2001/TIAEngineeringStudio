using System;
using TiaStudio.TiaAdapter.Common;

namespace TiaStudio.TiaAdapter.Openness
{
    /// <summary>A V18/V19/V20 worker közös belépési pontja (a verzió a hívó projekt állandója).</summary>
    internal static class WorkerProgram
    {
        internal static int Run(string[] args, string tiaVersion)
        {
#if TES_OPENNESS
            IOpennessBackend backend = new OpennessBackend(tiaVersion);
#else
            IOpennessBackend backend = new UnlinkedOpennessBackend(tiaVersion);
#endif
            WorkerHost host = new WorkerHost(tiaVersion, backend, Console.Error);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                host.RequestCancel();
            };
            return host.Run(args);
        }
    }
}
