using System;
using TiaStudio.TiaAdapter.Openness;

namespace TiaStudio.TiaAdapter.V19
{
    internal static class Program
    {
        internal const string TiaVersion = "V19";

        [STAThread]
        private static int Main(string[] args)
        {
            return WorkerProgram.Run(args, TiaVersion);
        }
    }
}
