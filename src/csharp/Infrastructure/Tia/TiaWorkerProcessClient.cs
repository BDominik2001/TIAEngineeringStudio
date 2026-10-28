using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;
using TiaStudio.Infrastructure.Processes;

namespace TiaStudio.Infrastructure.Tia
{
    /// <summary>
    /// A verziónkénti worker EXE indítása (TiaStudio.TiaAdapter.V1x.exe --command ... --result ...).
    /// A parancs és az eredmény UTF-8 JSON fájl a jobs mappában. Időtúllépésnél vagy összeomlásnál
    /// a sandbox állapota UNKNOWN: nem állítjuk, hogy a Siemens-oldali művelet visszaállt (spec 8.4).
    /// </summary>
    public sealed class TiaWorkerProcessClient : ITiaWorkerClient
    {
        private readonly ITiaInstallationDiscovery discovery;
        private readonly Func<string, string> workerPathResolver;
        private readonly string jobDirectory;
        private readonly TimeSpan capabilitiesTimeout;

        public TiaWorkerProcessClient(ITiaInstallationDiscovery discovery, Func<string, string> workerPathResolver, string jobDirectory, TimeSpan capabilitiesTimeout)
        {
            this.discovery = discovery;
            this.workerPathResolver = workerPathResolver;
            this.jobDirectory = jobDirectory;
            this.capabilitiesTimeout = capabilitiesTimeout;
        }

        public IReadOnlyList<TiaInstallInfo> DiscoverInstalledVersions()
        {
            return discovery.DiscoverInstalledVersions();
        }

        public Task<TiaResult> GetCapabilitiesAsync(string tiaVersion, CancellationToken cancellationToken)
        {
            TiaCommand command = new TiaCommand
            {
                JobId = Guid.NewGuid().ToString(),
                Operation = TiaOperations.GetCapabilities,
                TiaVersion = tiaVersion,
            };
            return RunAsync(new TiaJobRequest(command, capabilitiesTimeout), cancellationToken);
        }

        public Task<TiaResult> ValidateJobAsync(TiaJobRequest request, CancellationToken cancellationToken)
        {
            if (request.Command.Operation != TiaOperations.ValidateJob || !request.Command.DryRun)
            {
                throw new ArgumentException("ValidateJob requires operation ValidateJob with dry_run=true");
            }
            return RunAsync(request, cancellationToken);
        }

        public Task<TiaResult> ImportAndCompileAsync(TiaJobRequest request, CancellationToken cancellationToken)
        {
            if (request.Command.Operation != TiaOperations.ImportAndCompile)
            {
                throw new ArgumentException("ImportAndCompile requires operation ImportAndCompile");
            }
            return RunAsync(request, cancellationToken);
        }

        public Task<TiaResult> ListBlocksAsync(TiaJobRequest request, CancellationToken cancellationToken)
        {
            if (request.Command.Operation != TiaOperations.ListBlocks)
            {
                throw new ArgumentException("ListBlocks requires operation ListBlocks");
            }
            return RunAsync(request, cancellationToken);
        }

        public Task<TiaResult> ExportBlocksAsync(TiaJobRequest request, CancellationToken cancellationToken)
        {
            if (request.Command.Operation != TiaOperations.ExportBlocks)
            {
                throw new ArgumentException("ExportBlocks requires operation ExportBlocks");
            }
            return RunAsync(request, cancellationToken);
        }

        private async Task<TiaResult> RunAsync(TiaJobRequest request, CancellationToken cancellationToken)
        {
            TiaCommand command = request.Command;
            string workerPath = workerPathResolver(command.TiaVersion);
            if (string.IsNullOrEmpty(workerPath) || !File.Exists(workerPath))
            {
                return Synthesized(command, TiaJobStatus.Blocked, ErrorCodes.TiaNotInstalled, "Worker for " + command.TiaVersion + " not found: " + workerPath, SandboxStates.NotTouched);
            }
            Directory.CreateDirectory(jobDirectory);
            string commandPath = Path.Combine(jobDirectory, command.JobId + ".command.json");
            string resultPath = Path.Combine(jobDirectory, command.JobId + ".result.json");
            ProtocolJson.WriteAtomic(commandPath, command);

            ProcessRunResult run = await ProcessRunner.RunAsync(
                workerPath,
                new[] { "--command", commandPath, "--result", resultPath },
                Path.GetDirectoryName(workerPath),
                null,
                request.Timeout,
                cancellationToken).ConfigureAwait(false);

            bool mayHaveModified = command.Operation == TiaOperations.ImportAndCompile && !command.DryRun;
            string unknownState = mayHaveModified ? SandboxStates.Unknown : SandboxStates.Unchanged;
            if (run.StartFailed)
            {
                return Synthesized(command, TiaJobStatus.Failed, ErrorCodes.WorkerCrashed, "Worker could not be started: " + run.StartError, SandboxStates.NotTouched);
            }
            if (run.Cancelled)
            {
                return Synthesized(command, TiaJobStatus.Cancelled, ErrorCodes.Cancelled, "Cancelled; worker process was terminated. Sandbox state must be checked manually.", unknownState);
            }
            if (run.TimedOut)
            {
                return Synthesized(command, TiaJobStatus.Failed, ErrorCodes.Timeout, "Worker timed out after " + request.Timeout + " and was terminated. Sandbox state is UNKNOWN; restore it from the known-good copy.", unknownState);
            }
            if (!File.Exists(resultPath))
            {
                return Synthesized(command, TiaJobStatus.Failed, ErrorCodes.WorkerCrashed, "Worker exited with code " + run.ExitCode + " without a result file. " + Tail(run.StandardError), unknownState);
            }
            TiaResult result;
            try
            {
                result = ProtocolJson.DeserializeLenient<TiaResult>(ProtocolJson.ReadAllText(resultPath));
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is IOException)
            {
                return Synthesized(command, TiaJobStatus.Failed, ErrorCodes.WorkerCrashed, "Worker result is unreadable: " + ex.Message, unknownState);
            }
            if (result == null || result.JobId != command.JobId || result.ActualVersion != command.TiaVersion || result.ProtocolVersion != ProtocolVersions.Current)
            {
                return Synthesized(command, TiaJobStatus.Failed, ErrorCodes.WorkerVersionMismatch, "Worker result does not match the job (job id, protocol or TIA version)", unknownState);
            }
            return result;
        }

        private static string Tail(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.Length <= 2000 ? text : text.Substring(text.Length - 2000);
        }

        internal static TiaResult Synthesized(TiaCommand command, string status, string code, string text, string sandboxState)
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            return new TiaResult
            {
                JobId = command.JobId,
                RequestedVersion = command.TiaVersion,
                ActualVersion = command.TiaVersion,
                Operation = command.Operation,
                Status = status,
                ErrorCode = code,
                ErrorCount = 1,
                Messages = new List<TiaMessage> { new TiaMessage(MessageSeverity.Error, code, text) },
                SavedProject = false,
                SandboxState = sandboxState,
                StartedAtUtc = now,
                FinishedAtUtc = now,
            };
        }
    }
}
