using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
            StartupOptions options = StartupOptions.Parse(e.Args);
            if (options.ScreenshotDirectory != null)
            {
                // Felügyelet nélküli futás: modális ablak helyett hibafájl és kilépési kód.
                DispatcherUnhandledException += (s, args) =>
                {
                    ScreenshotMode.Fail(this, options, args.Exception);
                    args.Handled = true;
                };
                try
                {
                    ScreenshotMode.Run(this, options);
                }
                catch (Exception ex)
                {
                    ScreenshotMode.Fail(this, options, ex);
                }
                return;
            }

            DispatcherUnhandledException += (s, args) =>
            {
                ReportFatal("TIA Engineering Studio – váratlan hiba", args.Exception.ToString());
                args.Handled = true;
            };

            if (options.NoSplash)
            {
                (MainWindow window, MainViewModel _) = BuildMainWindow(null);
                if (window == null)
                {
                    Shutdown(1);
                    return;
                }
                ShowMain(window);
                return;
            }
            RunWithSplash();
        }

        /// <summary>
        /// Nyitóképernyő: amíg az intro fut, a főablak ApplicationIdle prioritással felépül; ha
        /// mindkettő kész, az outro elhalványítja a nyitóképet, és a főablak beúszik.
        /// </summary>
        private void RunWithSplash()
        {
            SplashWindow splash = new SplashWindow();
            splash.Show();
            MainWindow main = null;
            bool introDone = false;
            bool mainBuilt = false;

            void TryFinish()
            {
                if (!introDone || !mainBuilt)
                {
                    return;
                }
                if (main == null)
                {
                    splash.Close();
                    Shutdown(1);
                    return;
                }
                splash.PlayOutro(() =>
                {
                    ShowMain(main);
                    splash.Close();
                });
            }

            splash.PlayIntro(() =>
            {
                introDone = true;
                TryFinish();
            });
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                main = BuildMainWindow(splash).Window;
                mainBuilt = true;
                TryFinish();
            }));
        }

        private void ShowMain(MainWindow window)
        {
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Opacity = 0;
            window.Show();
            window.Activate();
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 320 : 1))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            window.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private static void ReportFatal(string title, string message)
        {
            try
            {
                ThemedDialog.Show(Current?.MainWindow, title, message, ThemedDialog.Kind.Error, "Rendben", null);
            }
            catch (Exception)
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>A kompozíciós gyökér; hibás beállítás esetén (null, null).</summary>
        internal static (MainWindow Window, MainViewModel ViewModel) BuildMainWindow(Window owner, Action<StudioSettings> overrideSettings = null)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            StudioSettings settings;
            try
            {
                settings = StudioSettings.Load(Path.Combine(baseDirectory, "appsettings.json"));
            }
            catch (Exception ex) when (ex is IOException || ex is Newtonsoft.Json.JsonException)
            {
                if (owner != null)
                {
                    owner.Hide();
                }
                ReportFatal("TIA Engineering Studio", "appsettings.json hibás: " + ex.Message);
                return (null, null);
            }
            overrideSettings?.Invoke(settings);

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
            return (new MainWindow { DataContext = viewModel }, viewModel);
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
