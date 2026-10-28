using System;
using TiaStudio.TiaAdapter.Openness;

namespace TiaStudio.TiaAdapter.V18
{
    internal static class Program
    {
        internal const string TiaVersion = "V18";

        [STAThread]
        private static int Main(string[] args)
        {
            return WorkerProgram.Run(args, TiaVersion);
        }
    }
}
