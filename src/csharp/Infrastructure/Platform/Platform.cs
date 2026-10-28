using System;
using System.IO;
using TiaStudio.Application.Ports;

namespace TiaStudio.Infrastructure.Platform
{
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public sealed class EnvironmentUserContext : IUserContext
    {
        public string UserName => Environment.UserDomainName + "\\" + Environment.UserName;
    }

    /// <summary>generated/&lt;run_id&gt; mappák; meglévő mappát soha nem használ újra.</summary>
    public sealed class FileWorkspace : IWorkspace
    {
        private readonly string outputRoot;

        public FileWorkspace(string outputRoot)
        {
            this.outputRoot = Path.GetFullPath(outputRoot);
        }

        public string CreateRunDirectory(string runId)
        {
            string path = Path.Combine(outputRoot, runId);
            if (Directory.Exists(path))
            {
                throw new IOException("Run directory already exists: " + path);
            }
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
