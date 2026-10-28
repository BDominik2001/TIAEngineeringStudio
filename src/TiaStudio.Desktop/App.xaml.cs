using System;
using System.IO;
using System.Windows;
using TiaStudio.Application.Library;
using TiaStudio.Application.Workflow;
using TiaStudio.Desktop.Services;
using TiaStudio.Desktop.ViewModels;
using TiaStudio.Infrastructure.Configuration;
using TiaStudio.Infrastructure.Generator;
using TiaStudio.Infrastructure.Json;
using TiaStudio.Infrastructure.Library;
using TiaStudio.Infrastructure.Logging;
using TiaStudio.Infrastructure.Native;
using TiaStudio.Infrastructure.Platform;
using TiaStudio.Infrastructure.Sandbox;
using TiaStudio.Infrastructure.Tia;

namespace TiaStudio.Desktop
{
    /// <summary>Kompozíciós gyökér: itt (és csak itt) kapcsolódnak az Infrastructure-implementációk.</summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(args.Exception.ToString(), "TIA Engineering Studio – váratlan hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            StudioSettings settings;
            try
            {
                settings = StudioSettings.Load(Path.Combine(baseDirectory, "appsettings.json"));
            }
            catch (Exception ex) when (ex is IOException || ex is Newtonsoft.Json.JsonException)
            {
                MessageBox.Show("appsettings.json hibás: " + ex.Message, "TIA Engineering Studio", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            SystemClock clock = new SystemClock();
            EnvironmentUserContext user = new EnvironmentUserContext();
            JsonlAuditLog audit = new JsonlAuditLog(settings.Resolve(baseDirectory, settings.LogDirectory), clock);
            Func<string, string> workerPath = version => settings.WorkerPath(baseDirectory, version);
            RegistryTiaInstallationDiscovery discovery = new RegistryTiaInstallationDiscovery(
                new WindowsRegistryReader(), new FileAssemblyInspector(), workerPath, settings.IntegrationTestedVersions);
            TiaWorkerProcessClient workerClient = new TiaWorkerProcessClient(
                discovery, workerPath, settings.Resolve(baseDirectory, settings.JobDirectory), TimeSpan.FromSeconds(settings.CapabilitiesTimeoutSeconds));
            SandboxService sandbox = new SandboxService(settings.Resolve(baseDirectory, settings.SandboxRoot), clock, user);
            MachineProjectSerializer serializer = new MachineProjectSerializer();
            MachineProjectJsonValidator validator = new MachineProjectJsonValidator();
            FileWorkspace workspace = new FileWorkspace(settings.Resolve(baseDirectory, settings.OutputRoot));
            FileBlockLibraryStore libraries = new FileBlockLibraryStore(settings.Resolve(baseDirectory, settings.LibraryRoot), clock, user);

            EngineeringWorkflow workflow = new EngineeringWorkflow(
                serializer,
                validator,
                new RustStaticValidator(),
                new PythonSourceGenerator(
                    settings.PythonExecutable,
                    ResolveGeneratorRoot(settings, baseDirectory),
                    TimeSpan.FromSeconds(settings.GeneratorTimeoutSeconds)),
                workerClient,
                sandbox,
                audit,
                clock,
                user,
                workspace,
                new WorkflowOptions
                {
                    DryRunTimeout = TimeSpan.FromSeconds(settings.DryRunTimeoutSeconds),
                    ImportTimeout = TimeSpan.FromSeconds(settings.ImportTimeoutSeconds),
                },
                libraries);
            BlockLibraryService libraryService = new BlockLibraryService(
                libraries, workerClient, sandbox, workspace, audit, clock, user, TimeSpan.FromSeconds(settings.LibraryWorkerTimeoutSeconds));

            MainViewModel viewModel = new MainViewModel(
                workflow,
                workerClient,
                sandbox,
                serializer,
                validator,
                new WpfDialogService(),
                handler => audit.LineWritten += (sender, line) => handler(line),
                libraryService);
            MainWindow window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            window.Show();
        }

        /// <summary>Kiadási elrendezésben a python\ mappa az EXE mellett van; fejlesztéskor a repó python\ mappája.</summary>
        private static string ResolveGeneratorRoot(StudioSettings settings, string baseDirectory)
        {
            string configured = settings.Resolve(baseDirectory, settings.GeneratorRoot);
            if (File.Exists(Path.Combine(configured, "generator", "main.py")))
            {
                return configured;
            }
            for (DirectoryInfo current = new DirectoryInfo(baseDirectory); current != null; current = current.Parent)
            {
                string candidate = Path.Combine(current.FullName, "python");
                if (File.Exists(Path.Combine(candidate, "generator", "main.py")))
                {
                    return candidate;
                }
            }
            return configured;
        }
    }
}
