using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;
using TiaStudio.Infrastructure.Processes;

namespace TiaStudio.Infrastructure.Generator
{
    /// <summary>
    /// A src/python/generator csomag futtatása külön folyamatban (python -m generator.main).
    /// A standard output egyetlen JSON-sor; a napló a standard errorra kerül.
    /// </summary>
    public sealed class PythonSourceGenerator : ISourceGenerator
    {
        private readonly string pythonExecutable;
        private readonly string generatorRoot;
        private readonly TimeSpan timeout;

        /// <param name="pythonExecutable">python.exe / python3 teljes útvonala vagy neve.</param>
        /// <param name="generatorRoot">A 'generator' csomagot tartalmazó mappa (repo: src/python/).</param>
        public PythonSourceGenerator(string pythonExecutable, string generatorRoot, TimeSpan timeout)
        {
            this.pythonExecutable = pythonExecutable;
            this.generatorRoot = generatorRoot;
            this.timeout = timeout;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
        {
            if (!File.Exists(Path.Combine(generatorRoot, "generator", "main.py")))
            {
                return Failed(ErrorCodes.GeneratorFailed, "Python generator not found under " + generatorRoot, null);
            }
            List<string> arguments = new List<string>
            {
                "-E", "-s", "-B",
                "-m", "generator.main",
                "--input", request.InputPath,
                "--output", request.OutputDirectory,
            };
            if (!string.IsNullOrEmpty(request.RunId))
            {
                arguments.Add("--run-id");
                arguments.Add(request.RunId);
            }
            Dictionary<string, string> environment = new Dictionary<string, string>
            {
                { "PYTHONIOENCODING", "utf-8" },
                { "PYTHONDONTWRITEBYTECODE", "1" },
            };
            ProcessRunResult run = await ProcessRunner.RunAsync(pythonExecutable, arguments, generatorRoot, environment, timeout, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (run.StartFailed)
            {
                return Failed(ErrorCodes.GeneratorFailed, "Cannot start Python (" + pythonExecutable + "): " + run.StartError, null);
            }
            if (run.TimedOut)
            {
                return Failed(ErrorCodes.Timeout, "Python generator timed out after " + timeout, run.StandardError);
            }
            return Parse(run, request);
        }

        internal static GenerationResult Parse(ProcessRunResult run, GenerationRequest request)
        {
            string line = (run.StandardOutput ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            JObject json;
            try
            {
                json = line == null ? null : JObject.Parse(line);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                json = null;
            }
            if (json == null)
            {
                return Failed(ErrorCodes.GeneratorFailed, "Generator produced no JSON result (exit code " + run.ExitCode + ")", run.StandardError);
            }
            GenerationResult result = new GenerationResult
            {
                Success = run.ExitCode == 0 && (string)json["status"] == "Success",
                ErrorCode = (string)json["code"],
                Message = (string)json["message"] ?? (string)json["status"],
                OutputDirectory = (string)json["output_directory"],
                ManifestPath = (string)json["manifest_path"],
                ManifestSha256 = (string)json["manifest_sha256"],
                Validation = json["validation"]?.ToObject<ValidationReport>(),
                Diagnostics = run.StandardError,
            };
            if (!result.Success && result.ErrorCode == null)
            {
                result.ErrorCode = ErrorCodes.GeneratorFailed;
            }
            if (result.Success && !SamePath(result.OutputDirectory, request.OutputDirectory))
            {
                return Failed(ErrorCodes.GeneratorFailed, "Generator wrote to an unexpected directory: " + result.OutputDirectory, run.StandardError);
            }
            return result;
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), comparison);
        }

        private static GenerationResult Failed(string code, string message, string diagnostics)
        {
            return new GenerationResult { Success = false, ErrorCode = code, Message = message, Diagnostics = diagnostics };
        }
    }
}
