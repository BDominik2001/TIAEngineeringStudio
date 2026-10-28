using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaStudio.Application.Ports;
using TiaStudio.Application.Workflow;
using TiaStudio.Contracts;

namespace TiaStudio.Application.Library
{
    public sealed class OperationResult<T>
    {
        public OperationResult(StepResult step, T value)
        {
            Step = step;
            Value = value;
        }

        public StepResult Step { get; }

        public T Value { get; }

        public bool Ok => Step.Ok;
    }

    /// <summary>
    /// Könyvtár-use case-ek: létrehozás, elemtörlés és blokkexport egy sandbox-projektből.
    /// Az export csak olvasó worker-művelet (a projekt nem módosul és nem mentődik); részleges
    /// export esetén a könyvtárba semmi nem kerül. A könyvtárból való import a
    /// EngineeringWorkflow.SetLibrarySelection útján, a szokásos jóváhagyási láncon fut.
    /// </summary>
    public sealed class BlockLibraryService
    {
        private readonly IBlockLibraryStore store;
        private readonly ITiaWorkerClient worker;
        private readonly ISandboxService sandbox;
        private readonly IWorkspace workspace;
        private readonly IAuditLog audit;
        private readonly IClock clock;
        private readonly IUserContext user;
        private readonly TimeSpan workerTimeout;

        public BlockLibraryService(
            IBlockLibraryStore store,
            ITiaWorkerClient worker,
            ISandboxService sandbox,
            IWorkspace workspace,
            IAuditLog audit,
            IClock clock,
            IUserContext user,
            TimeSpan workerTimeout)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.worker = worker ?? throw new ArgumentNullException(nameof(worker));
            this.sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
            this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.user = user ?? throw new ArgumentNullException(nameof(user));
            this.workerTimeout = workerTimeout;
        }

        public IBlockLibraryStore Store => store;

        public OperationResult<BlockLibraryIndex> CreateLibrary(string name, string description)
        {
            try
            {
                BlockLibraryIndex index = store.Create(name, description);
                Log("LibraryCreated", name, null, "library " + name + " created", null);
                return new OperationResult<BlockLibraryIndex>(StepResult.Success("Library " + name + " created"), index);
            }
            catch (BlockLibraryException ex)
            {
                return Error<BlockLibraryIndex>(ex.ErrorCode, ex.Message);
            }
        }

        public OperationResult<bool> RemoveItem(string libraryName, BlockLibraryItem item)
        {
            try
            {
                store.RemoveItem(libraryName, item.ItemId);
                Log("LibraryItemRemoved", libraryName, null, item.BlockName + " r" + item.Revision + " removed", null,
                    new Dictionary<string, object> { { "item_id", item.ItemId }, { "sha256", item.Sha256 } });
                return new OperationResult<bool>(StepResult.Success(item.BlockName + " r" + item.Revision + " removed"), true);
            }
            catch (BlockLibraryException ex)
            {
                return Error<bool>(ex.ErrorCode, ex.Message);
            }
        }

        public OperationResult<IReadOnlyList<TiaMessage>> Verify(string libraryName)
        {
            try
            {
                IReadOnlyList<TiaMessage> problems = store.Verify(libraryName);
                Log("LibraryVerified", libraryName, problems.Count == 0 ? null : problems[0].Code, problems.Count + " problem(s)", null);
                return new OperationResult<IReadOnlyList<TiaMessage>>(
                    problems.Count == 0 ? StepResult.Success("Library integrity OK") : StepResult.Error(problems[0].Code, problems.Count + " integrity problem(s)", problems.Select(p => p.ToString()).ToList()),
                    problems);
            }
            catch (BlockLibraryException ex)
            {
                return Error<IReadOnlyList<TiaMessage>>(ex.ErrorCode, ex.Message);
            }
        }

        public async Task<OperationResult<IReadOnlyList<ProjectBlockInfo>>> ListProjectBlocksAsync(string tiaVersion, string projectPath, string plcName, CancellationToken cancellationToken)
        {
            Tuple<string, string> problem = sandbox.Verify(projectPath, tiaVersion);
            if (problem != null)
            {
                return Error<IReadOnlyList<ProjectBlockInfo>>(problem.Item1, problem.Item2);
            }
            TiaCommand command = BaseCommand(TiaOperations.ListBlocks, tiaVersion, projectPath, plcName);
            TiaResult result = await worker.ListBlocksAsync(new TiaJobRequest(command, workerTimeout), cancellationToken).ConfigureAwait(false);
            if (result.Status != TiaJobStatus.Success)
            {
                return Error<IReadOnlyList<ProjectBlockInfo>>(result.ErrorCode ?? ErrorCodes.InternalError, "Listing blocks failed: " + result.Status, result.Messages);
            }
            IReadOnlyList<ProjectBlockInfo> blocks = result.Blocks ?? new List<ProjectBlockInfo>();
            return new OperationResult<IReadOnlyList<ProjectBlockInfo>>(StepResult.Success(blocks.Count + " block(s)/type(s) in " + plcName), blocks);
        }

        public async Task<OperationResult<IReadOnlyList<BlockLibraryItem>>> ExportToLibraryAsync(
            string libraryName,
            string tiaVersion,
            string projectPath,
            string plcName,
            IReadOnlyList<ExportRequestItem> requests,
            string description,
            CancellationToken cancellationToken)
        {
            if (requests == null || requests.Count == 0)
            {
                return Error<IReadOnlyList<BlockLibraryItem>>(ErrorCodes.LibraryItemNotFound, "Select at least one block to export");
            }
            Tuple<string, string> problem = sandbox.Verify(projectPath, tiaVersion);
            if (problem != null)
            {
                return Error<IReadOnlyList<BlockLibraryItem>>(problem.Item1, problem.Item2);
            }
            try
            {
                store.Load(libraryName);
            }
            catch (BlockLibraryException ex)
            {
                return Error<IReadOnlyList<BlockLibraryItem>>(ex.ErrorCode, ex.Message);
            }

            string runId = "export-" + clock.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string exportDirectory = Path.Combine(workspace.CreateRunDirectory(runId), "blocks");
            Directory.CreateDirectory(exportDirectory);

            TiaCommand command = BaseCommand(TiaOperations.ExportBlocks, tiaVersion, projectPath, plcName);
            command.ExportDirectory = exportDirectory;
            command.ExportBlocks = requests.ToList();
            TiaResult result = await worker.ExportBlocksAsync(new TiaJobRequest(command, workerTimeout), cancellationToken).ConfigureAwait(false);
            if (result.Status != TiaJobStatus.Success)
            {
                Log("LibraryExportFailed", libraryName, result.ErrorCode, "export job " + command.JobId + ": " + result.Status, command.JobId);
                return Error<IReadOnlyList<BlockLibraryItem>>(result.ErrorCode ?? ErrorCodes.ExportFailed, "Export failed: " + result.Status + "; nothing was added to the library", result.Messages);
            }
            List<ExportedBlockFile> files = result.ExportedFiles ?? new List<ExportedBlockFile>();
            bool complete = files.Count == requests.Count
                && requests.All(r => files.Any(f => string.Equals(f.BlockName, r.BlockName, StringComparison.OrdinalIgnoreCase) && f.Format == r.Format));
            if (!complete)
            {
                return Error<IReadOnlyList<BlockLibraryItem>>(ErrorCodes.ExportFailed, "Worker export result does not match the request; nothing was added to the library");
            }

            try
            {
                IReadOnlyList<BlockLibraryItem> items = store.AddExportedBlocks(
                    libraryName,
                    exportDirectory,
                    files,
                    tiaVersion,
                    new BlockLibraryOrigin { ProjectFile = Path.GetFileName(projectPath), PlcName = plcName, JobId = command.JobId },
                    description);
                Log("LibraryExported", libraryName, null, items.Count + " item(s) added from " + Path.GetFileName(projectPath), command.JobId,
                    new Dictionary<string, object> { { "items", items.Select(i => i.BlockName + " r" + i.Revision + " " + i.Format + " " + i.Sha256).ToList() } });
                return new OperationResult<IReadOnlyList<BlockLibraryItem>>(StepResult.Success(items.Count + " block(s) added to library " + libraryName), items);
            }
            catch (BlockLibraryException ex)
            {
                return Error<IReadOnlyList<BlockLibraryItem>>(ex.ErrorCode, ex.Message);
            }
        }

        private TiaCommand BaseCommand(string operation, string tiaVersion, string projectPath, string plcName)
        {
            return new TiaCommand
            {
                JobId = Guid.NewGuid().ToString(),
                Operation = operation,
                TiaVersion = tiaVersion,
                ProjectPath = Path.GetFullPath(projectPath),
                SandboxRoot = sandbox.SandboxRoot,
                PlcName = plcName,
                DryRun = true,
            };
        }

        private void Log(string eventName, string libraryName, string errorCode, string message, string jobId, IDictionary<string, object> details = null)
        {
            Dictionary<string, object> all = new Dictionary<string, object> { { "library", libraryName }, { "user", user.UserName } };
            if (details != null)
            {
                foreach (KeyValuePair<string, object> pair in details)
                {
                    all[pair.Key] = pair.Value;
                }
            }
            audit.Write(new AuditEvent { Event = eventName, JobId = jobId, ErrorCode = errorCode, Message = message, Details = all });
        }

        private static OperationResult<T> Error<T>(string code, string message, IEnumerable<TiaMessage> details = null)
        {
            return new OperationResult<T>(StepResult.Error(code, message, details?.Select(d => d.ToString()).ToList()), default(T));
        }
    }
}
