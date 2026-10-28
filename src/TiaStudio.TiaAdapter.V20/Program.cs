using System;
using TiaStudio.TiaAdapter.Openness;

namespace TiaStudio.TiaAdapter.V20
{
    internal static class Program
    {
        internal const string TiaVersion = "V20";

        [STAThread]
        private static int Main(string[] args)
        {
            return WorkerProgram.Run(args, TiaVersion);
        }
    }
}
