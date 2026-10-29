using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TiaStudio.Infrastructure.Processes
{
    public sealed class ProcessRunResult
    {
        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }

        public bool TimedOut { get; set; }

        public bool Cancelled { get; set; }

        public bool StartFailed { get; set; }

        public string StartError { get; set; }

        /// <summary>
        /// A folyamatfa állapota a futás végén (Windows Job Object): null, ha nincs teendő, különben
        /// operátornak szóló megjegyzés (pl. a leállítás után is futó TIA Portal-folyamat).
        /// </summary>
        public string ProcessTreeNote { get; set; }

        /// <summary>A futás végén a folyamatfában még futó folyamatok száma (-1 = nem ismert).</summary>
        public int RemainingProcesses { get; set; } = -1;
    }

    /// <summary>
    /// Külső folyamat futtatása időkorláttal és megszakítással. Időtúllépésnél/megszakításnál
    /// Windows alatt a teljes folyamatfát (a worker által indított TIA Portalt is) leállítja, máshol
    /// a folyamatot; a hívónak ilyenkor a mellékhatások állapotát ismeretlennek kell tekintenie.
    /// </summary>
    public static class ProcessRunner
    {
        public static async Task<ProcessRunResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            IDictionary<string, string> environment,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = JoinArguments(arguments),
                WorkingDirectory = workingDirectory ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment)
                {
                    info.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            using (Process process = new Process { StartInfo = info, EnableRaisingEvents = true })
            {
                TaskCompletionSource<bool> exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<bool> outputClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<bool> errorClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null)
                    {
                        outputClosed.TrySetResult(true);
                    }
                    else
                    {
                        lock (stdout)
                        {
                            stdout.AppendLine(e.Data);
                        }
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null)
                    {
                        errorClosed.TrySetResult(true);
                    }
                    else
                    {
                        lock (stderr)
                        {
                            stderr.AppendLine(e.Data);
                        }
                    }
                };
                process.Exited += (s, e) => exited.TrySetResult(true);

                try
                {
                    process.Start();
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
                {
                    return new ProcessRunResult { StartFailed = true, StartError = ex.Message, ExitCode = -1 };
                }
                using (ProcessTreeJob job = ProcessTreeJob.TryAttach(process))
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    Task delay = Task.Delay(timeout, cancellationToken);
                    Task finished = await Task.WhenAny(exited.Task, delay).ConfigureAwait(false);
                    ProcessRunResult result = new ProcessRunResult();
                    if (finished != exited.Task)
                    {
                        result.Cancelled = cancellationToken.IsCancellationRequested;
                        result.TimedOut = !result.Cancelled;
                        if (job != null)
                        {
                            result.RemainingProcesses = job.TerminateTree(TimeSpan.FromSeconds(15));
                            result.ProcessTreeNote = result.RemainingProcesses == 0
                                ? "The worker process tree (including any TIA Portal instance it started) was terminated."
                                : "WARNING: " + result.RemainingProcesses + " process(es) of the worker tree are still running after termination; close the TIA Portal instance (Task Manager) before reusing the sandbox.";
                        }
                        else
                        {
                            TryKill(process);
                            result.ProcessTreeNote = "Only the worker process was terminated; check for a remaining TIA Portal instance before reusing the sandbox.";
                        }
                        await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                    }
                    else if (job != null)
                    {
                        result.RemainingProcesses = job.WaitForEmpty(TimeSpan.FromSeconds(5));
                        if (result.RemainingProcesses > 0)
                        {
                            result.ProcessTreeNote = "WARNING: " + result.RemainingProcesses + " child process(es) started by the worker (e.g. TIA Portal) are still running after it exited.";
                        }
                    }
                    return await Collect(process, result, stdout, stderr, outputClosed, errorClosed).ConfigureAwait(false);
                }
            }
        }

        private static async Task<ProcessRunResult> Collect(
            Process process, ProcessRunResult result, StringBuilder stdout, StringBuilder stderr,
            TaskCompletionSource<bool> outputClosed, TaskCompletionSource<bool> errorClosed)
        {
            await Task.WhenAny(Task.WhenAll(outputClosed.Task, errorClosed.Task), Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            result.ExitCode = process.HasExited ? process.ExitCode : -1;
            lock (stdout)
            {
                result.StandardOutput = stdout.ToString();
            }
            lock (stderr)
            {
                result.StandardError = stderr.ToString();
            }
            return result;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        /// <summary>Windows parancssori idézés (CommandLineToArgvW szabályai szerint).</summary>
        public static string JoinArguments(IEnumerable<string> arguments)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(Quote(argument ?? string.Empty));
            }
            return builder.ToString();
        }

        private static string Quote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"', '\n', '\v' }) < 0)
            {
                return argument;
            }
            StringBuilder quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    quoted.Append('\\', (backslashes * 2) + 1).Append('"');
                }
                else
                {
                    quoted.Append('\\', backslashes).Append(c);
                }
                backslashes = 0;
            }
            quoted.Append('\\', backslashes * 2).Append('"');
            return quoted.ToString();
        }
    }
}
