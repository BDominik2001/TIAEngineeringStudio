using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TiaStudio.Application.Library;
using TiaStudio.Application.Ports;
using TiaStudio.Application.Workflow;
using TiaStudio.Contracts;
using TiaStudio.Desktop.Services;
using TiaStudio.Domain.Model;
using TiaStudio.Domain.Workflow;

namespace TiaStudio.Desktop.ViewModels
{
    public sealed class DeviceRowViewModel : ObservableObject
    {
        private string id;
        private string description;
        private string retractedInput;
        private string extendedInput;
        private int? monitorTimeMs;

        public string Kind => DeviceKinds.Cylinder;

        public string Id { get => id; set => Set(ref id, value?.Trim()); }

        public string Description { get => description; set => Set(ref description, value); }

        public string RetractedInput { get => retractedInput; set => Set(ref retractedInput, value?.Trim()); }

        public string ExtendedInput { get => extendedInput; set => Set(ref extendedInput, value?.Trim()); }

        /// <summary>Üres = alapérték (2000 ms).</summary>
        public int? MonitorTimeMs { get => monitorTimeMs; set => Set(ref monitorTimeMs, value); }

        public Device ToDevice()
        {
            return new CylinderDevice
            {
                Id = Id,
                Description = string.IsNullOrEmpty(Description) ? null : Description,
                RetractedInput = RetractedInput,
                ExtendedInput = ExtendedInput,
                MonitorTimeMs = MonitorTimeMs,
            };
        }

        public static DeviceRowViewModel From(Device device)
        {
            CylinderDevice cylinder = (CylinderDevice)device;
            return new DeviceRowViewModel
            {
                Id = cylinder.Id,
                Description = cylinder.Description,
                RetractedInput = cylinder.RetractedInput,
                ExtendedInput = cylinder.ExtendedInput,
                MonitorTimeMs = cylinder.MonitorTimeMs,
            };
        }
    }

    public sealed class GeneratedFileViewModel : ObservableObject
    {
        private bool viewed;

        public int ImportOrder { get; set; }

        public string BlockName { get; set; }

        public string BlockType { get; set; }

        public string RelativePath { get; set; }

        public string Sha256 { get; set; }

        public long ByteLength { get; set; }

        public string FullPath { get; set; }

        public bool Viewed { get => viewed; set => Set(ref viewed, value); }
    }

    /// <summary>
    /// A főablak állapota. Minden TIA-művelet az EngineeringWorkflow-n keresztül fut, amely kikényszeríti
    /// a DRAFT → … → SAVED sorrendet; a felület csak az aktuális állapotban megengedett lépést kínálja.
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private readonly EngineeringWorkflow workflow;
        private readonly ITiaWorkerClient workerClient;
        private readonly ISandboxService sandbox;
        private readonly IMachineProjectSerializer serializer;
        private readonly IModelValidator modelValidator;
        private readonly IDialogService dialogs;
        private readonly SynchronizationContext ui;
        private CancellationTokenSource cancellation;
        private bool modelDirty = true;
        private bool loading;
        private string projectName;
        private string projectDescription;
        private string tiaVersion = TiaVersions.V18;
        private string plcName;
        private string cpuFamily = CpuFamilies.S71500;
        private string projectFilePath;
        private string statusMessage = "Kész.";
        private bool isBusy;
        private DeviceRowViewModel selectedDevice;
        private GeneratedFileViewModel selectedFile;
        private string previewText;
        private string sandboxProjectPath;
        private string knownGoodProjectPath;

        public MainViewModel(
            EngineeringWorkflow workflow,
            ITiaWorkerClient workerClient,
            ISandboxService sandbox,
            IMachineProjectSerializer serializer,
            IModelValidator modelValidator,
            IDialogService dialogs,
            Action<Action<string>> subscribeAuditLines,
            BlockLibraryService libraryService = null)
        {
            this.workflow = workflow;
            this.workerClient = workerClient;
            this.sandbox = sandbox;
            this.serializer = serializer;
            this.modelValidator = modelValidator;
            this.dialogs = dialogs;
            ui = SynchronizationContext.Current ?? new SynchronizationContext();

            Devices.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (DeviceRowViewModel row in e.NewItems)
                    {
                        row.PropertyChanged += OnDevicePropertyChanged;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (DeviceRowViewModel row in e.OldItems)
                    {
                        row.PropertyChanged -= OnDevicePropertyChanged;
                    }
                }
                ModelEdited();
            };
            workflow.StateChanged += (s, e) => OnUi(RefreshFromWorkflow);
            subscribeAuditLines?.Invoke(line => OnUi(() =>
            {
                AuditLines.Add(line);
                while (AuditLines.Count > 2000)
                {
                    AuditLines.RemoveAt(0);
                }
            }));

            NewProjectCommand = new RelayCommand(NewProject, () => !IsBusy);
            OpenProjectCommand = new RelayCommand(OpenProject, () => !IsBusy);
            SaveProjectCommand = new RelayCommand(SaveProject, () => !IsBusy);
            AddCylinderCommand = new RelayCommand(AddCylinder, () => !IsBusy);
            RemoveDeviceCommand = new RelayCommand(() => Devices.Remove(SelectedDevice), () => !IsBusy && SelectedDevice != null);
            ValidateCommand = new RelayCommand(Validate, () => !IsBusy && (State == WorkflowState.Draft || modelDirty));
            CheckCommand = new RelayCommand(() => Show(workflow.Check()), () => !IsBusy && State == WorkflowState.Validated);
            GenerateCommand = Async(ct => workflow.GenerateAsync(ct), () => State == WorkflowState.Checked);
            MarkReviewedCommand = new RelayCommand(() => Show(workflow.MarkReviewed()), () => !IsBusy && State == WorkflowState.Generated && GeneratedFiles.Count > 0 && GeneratedFiles.All(f => f.Viewed));
            BrowseSandboxProjectCommand = new RelayCommand(BrowseSandboxProject, () => !IsBusy);
            BrowseKnownGoodProjectCommand = new RelayCommand(BrowseKnownGoodProject, () => !IsBusy);
            CreateSandboxCopyCommand = new RelayCommand(CreateSandboxCopy, () => !IsBusy && !string.IsNullOrEmpty(KnownGoodProjectPath));
            ApproveCommand = new RelayCommand(Approve, () => !IsBusy && State == WorkflowState.Reviewed && !string.IsNullOrEmpty(SandboxProjectPath));
            RefreshInstallationsCommand = new RelayCommand(RefreshInstallations, () => !IsBusy);
            QueryCapabilitiesCommand = new AsyncCommand(QueryCapabilitiesAsync, () => !IsBusy, OnUnhandled);
            DryRunCommand = Async(ct => workflow.PrepareImportAsync(ct), () => State == WorkflowState.Approved);
            ImportCommand = new AsyncCommand(ImportAsync, () => !IsBusy && State == WorkflowState.ReadyToImport, OnUnhandled);
            ResetCommand = new RelayCommand(() => { workflow.Reset(); modelDirty = true; }, () => !IsBusy && State != WorkflowState.Draft);
            CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => IsBusy && cancellation != null);

            NewProject();
            RefreshInstallations();
            if (libraryService != null)
            {
                Library = new LibraryViewModel(libraryService, workflow, sandbox, dialogs, RunStepAsync, Show, () => IsBusy, OnUnhandled);
            }
        }

        /// <summary>Blokk-könyvtárak fül (null, ha nincs könyvtártár konfigurálva).</summary>
        public LibraryViewModel Library { get; }

        /// <summary>A munkafolyamat célverziója: könyvtár-módban a választásé, egyébként a gépmodellé.</summary>
        public string EffectiveTiaVersion => workflow.InputKind == WorkflowInputKind.Library ? workflow.TiaVersion : TiaVersion;

        public string EffectivePlcName => workflow.InputKind == WorkflowInputKind.Library ? workflow.PlcName : PlcName;

        public string InputText => workflow.InputKind == WorkflowInputKind.Library && workflow.LibrarySelection != null
            ? "könyvtár " + workflow.LibrarySelection.LibraryName + " (" + workflow.LibrarySelection.ItemIds.Count + " elem)"
            : "gépmodell";

        public string ValidateLabel => workflow.InputKind == WorkflowInputKind.Library ? "1) Könyvtárválasztás ellenőrzése" : "1) Séma- és modellellenőrzés";

        public string CheckLabel => workflow.InputKind == WorkflowInputKind.Library ? "2) Elem-hashek újraellenőrzése" : "2) Statikus ellenőrzés (Rust)";

        public string GenerateLabel => workflow.InputKind == WorkflowInputKind.Library ? "3) Importcsomag összeállítása" : "3) SCL generálás (Python)";

        public IReadOnlyList<string> TiaVersionOptions => TiaVersions.Supported;

        public IReadOnlyList<string> CpuFamilyOptions => CpuFamilies.Supported;

        public ObservableCollection<DeviceRowViewModel> Devices { get; } = new ObservableCollection<DeviceRowViewModel>();

        public ObservableCollection<string> Issues { get; } = new ObservableCollection<string>();

        public ObservableCollection<GeneratedFileViewModel> GeneratedFiles { get; } = new ObservableCollection<GeneratedFileViewModel>();

        public ObservableCollection<TiaInstallInfo> Installations { get; } = new ObservableCollection<TiaInstallInfo>();

        public ObservableCollection<string> WorkerMessages { get; } = new ObservableCollection<string>();

        public ObservableCollection<ImportPlanItem> ImportPlan { get; } = new ObservableCollection<ImportPlanItem>();

        public ObservableCollection<CompileMessage> CompileMessages { get; } = new ObservableCollection<CompileMessage>();

        public ObservableCollection<string> AuditLines { get; } = new ObservableCollection<string>();

        public ICommand NewProjectCommand { get; }

        public ICommand OpenProjectCommand { get; }

        public ICommand SaveProjectCommand { get; }

        public ICommand AddCylinderCommand { get; }

        public ICommand RemoveDeviceCommand { get; }

        public ICommand ValidateCommand { get; }

        public ICommand CheckCommand { get; }

        public ICommand GenerateCommand { get; }

        public ICommand MarkReviewedCommand { get; }

        public ICommand BrowseSandboxProjectCommand { get; }

        public ICommand BrowseKnownGoodProjectCommand { get; }

        public ICommand CreateSandboxCopyCommand { get; }

        public ICommand ApproveCommand { get; }

        public ICommand RefreshInstallationsCommand { get; }

        public ICommand QueryCapabilitiesCommand { get; }

        public ICommand DryRunCommand { get; }

        public ICommand ImportCommand { get; }

        public ICommand ResetCommand { get; }

        public ICommand CancelCommand { get; }

        public string ProjectName { get => projectName; set { if (Set(ref projectName, value)) { ModelEdited(); } } }

        public string ProjectDescription { get => projectDescription; set { if (Set(ref projectDescription, value)) { ModelEdited(); } } }

        public string TiaVersion
        {
            get => tiaVersion;
            set
            {
                if (Set(ref tiaVersion, value))
                {
                    // Más célverzióhoz más projektkiterjesztés tartozik: a kiválasztott sandbox érvénytelen.
                    SandboxProjectPath = null;
                    OnPropertyChanged(nameof(ProjectExtension));
                    ModelEdited();
                }
            }
        }

        public string ProjectExtension => TiaVersions.ProjectExtension(EffectiveTiaVersion);

        public string PlcName { get => plcName; set { if (Set(ref plcName, value?.Trim())) { ModelEdited(); } } }

        public string CpuFamily { get => cpuFamily; set { if (Set(ref cpuFamily, value)) { ModelEdited(); } } }

        public string ProjectFilePath { get => projectFilePath; private set => Set(ref projectFilePath, value); }

        public DeviceRowViewModel SelectedDevice { get => selectedDevice; set => Set(ref selectedDevice, value); }

        public GeneratedFileViewModel SelectedFile
        {
            get => selectedFile;
            set
            {
                if (Set(ref selectedFile, value))
                {
                    PreviewText = value == null ? null : ReadPreview(value.FullPath);
                    if (value != null)
                    {
                        value.Viewed = true;
                    }
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string PreviewText { get => previewText; private set => Set(ref previewText, value); }

        public string SandboxProjectPath { get => sandboxProjectPath; set => Set(ref sandboxProjectPath, value); }

        public string KnownGoodProjectPath { get => knownGoodProjectPath; set => Set(ref knownGoodProjectPath, value); }

        public string SandboxRoot => sandbox.SandboxRoot;

        public WorkflowState State => workflow.State;

        public string StateText => WorkflowStateMachine.DisplayName(workflow.State) + (modelDirty && workflow.State == WorkflowState.Draft ? " (módosítva)" : string.Empty);

        public string StatusMessage { get => statusMessage; private set => Set(ref statusMessage, value); }

        public bool IsBusy
        {
            get => isBusy;
            private set
            {
                if (Set(ref isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private ICommand Async(Func<CancellationToken, Task<StepResult>> step, Func<bool> canRun)
        {
            return new AsyncCommand(() => RunStepAsync(step), () => !IsBusy && canRun(), OnUnhandled);
        }

        private async Task RunStepAsync(Func<CancellationToken, Task<StepResult>> step)
        {
            IsBusy = true;
            cancellation = new CancellationTokenSource();
            StatusMessage = "Folyamatban…";
            try
            {
                Show(await step(cancellation.Token));
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                IsBusy = false;
            }
        }

        private void NewProject()
        {
            loading = true;
            ProjectFilePath = null;
            ProjectName = "New_Machine";
            ProjectDescription = null;
            TiaVersion = TiaVersions.V18;
            PlcName = "PLC_1";
            CpuFamily = CpuFamilies.S71500;
            Devices.Clear();
            loading = false;
            AddCylinder();
        }

        private void AddCylinder()
        {
            int next = Devices.Count + 1;
            while (Devices.Any(d => d.Id == "CYL_" + next.ToString("00")))
            {
                next++;
            }
            Devices.Add(new DeviceRowViewModel { Id = "CYL_" + next.ToString("00"), RetractedInput = string.Empty, ExtendedInput = string.Empty });
        }

        private void OpenProject()
        {
            string path = dialogs.OpenFile("Gépmodell megnyitása", "Gépmodell (*.json)|*.json");
            if (path == null)
            {
                return;
            }
            byte[] bytes = File.ReadAllBytes(path);
            ValidationReport report = modelValidator.Validate(bytes);
            if (!report.Ok)
            {
                ReplaceAll(Issues, report.Issues.Select(i => i.ToString()));
                StatusMessage = "A fájl nem érvényes 1.1-es gépmodell; nem töltöttük be.";
                return;
            }
            MachineProject project = serializer.Deserialize(bytes);
            loading = true;
            ProjectName = project.ProjectName;
            ProjectDescription = project.Description;
            TiaVersion = project.Target.TiaVersion;
            PlcName = project.Target.PlcName;
            CpuFamily = project.Target.CpuFamily;
            Devices.Clear();
            foreach (Device device in project.Devices)
            {
                Devices.Add(DeviceRowViewModel.From(device));
            }
            loading = false;
            ProjectFilePath = path;
            ModelEdited();
            StatusMessage = "Betöltve: " + path;
        }

        private void SaveProject()
        {
            string path = dialogs.SaveFile("Gépmodell mentése", "Gépmodell (*.json)|*.json", (ProjectName ?? "machine") + ".json");
            if (path == null)
            {
                return;
            }
            File.WriteAllBytes(path, serializer.Serialize(BuildProject()));
            ProjectFilePath = path;
            StatusMessage = "Mentve: " + path;
        }

        private MachineProject BuildProject()
        {
            return new MachineProject
            {
                ProjectName = ProjectName,
                Description = string.IsNullOrEmpty(ProjectDescription) ? null : ProjectDescription,
                Target = new MachineTarget { TiaVersion = TiaVersion, PlcName = PlcName, CpuFamily = CpuFamily },
                Devices = Devices.Select(d => d.ToDevice()).ToList(),
            };
        }

        private void OnDevicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ModelEdited();
        }

        /// <summary>Bármely modellmódosítás: jóváhagyás és generált csomag érvénytelen, vissza DRAFT-ba.</summary>
        private void ModelEdited()
        {
            if (loading)
            {
                return;
            }
            modelDirty = true;
            // Könyvtár-módból a gépmodell bármely szerkesztése visszavált gépmodell-bemenetre.
            if (workflow.InputKind == WorkflowInputKind.Library || (workflow.Project != null && workflow.State != WorkflowState.Draft))
            {
                workflow.SetProject(BuildProject());
                modelDirty = false;
            }
            OnPropertyChanged(nameof(StateText));
        }

        private void Validate()
        {
            if (workflow.InputKind != WorkflowInputKind.Library && (modelDirty || workflow.Project == null))
            {
                workflow.SetProject(BuildProject());
                modelDirty = false;
            }
            Show(workflow.Validate());
        }

        private void BrowseSandboxProject()
        {
            string version = EffectiveTiaVersion;
            string extension = TiaVersions.ProjectExtension(version);
            string path = dialogs.OpenFile("Sandbox TIA-projekt (" + version + ")", "TIA Portal " + version + " projekt (*" + extension + ")|*" + extension);
            if (path != null)
            {
                Tuple<string, string> problem = sandbox.Verify(path, version);
                SandboxProjectPath = problem == null ? path : null;
                StatusMessage = problem == null ? "Sandbox projekt kiválasztva." : problem.Item1 + ": " + problem.Item2;
            }
        }

        private void BrowseKnownGoodProject()
        {
            string version = EffectiveTiaVersion;
            string extension = TiaVersions.ProjectExtension(version);
            string path = dialogs.OpenFile("Ismert jó (forrás) TIA-projekt – nem módosul", "TIA Portal " + version + " projekt (*" + extension + ")|*" + extension);
            if (path != null)
            {
                KnownGoodProjectPath = path;
            }
        }

        private void CreateSandboxCopy()
        {
            if (!dialogs.Confirm("Sandbox-másolat", "A teljes projektmappát lemásoljuk ide:\n" + SandboxRoot + "\n\nA forrásprojektet előtte zárd be a TIA Portalban. Az eredeti projekt nem módosul. Folytatod?"))
            {
                return;
            }
            try
            {
                SandboxCopy copy = sandbox.CreateSandboxCopy(KnownGoodProjectPath, TiaVersions.FromProjectPath(KnownGoodProjectPath) ?? EffectiveTiaVersion);
                SandboxProjectPath = copy.ProjectPath;
                StatusMessage = "Sandbox létrehozva: " + copy.ProjectPath;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Infrastructure.Sandbox.SandboxException)
            {
                dialogs.ShowError("Sandbox-másolat", ex.Message);
            }
        }

        private void Approve()
        {
            string message = "Jóváhagyod a(z) " + GeneratedFiles.Count + " forrásfájl importját?\n\n" +
                             "Forrás: " + InputText + "\nCélverzió: TIA Portal " + EffectiveTiaVersion + "\nPLC: " + EffectivePlcName + "\nSandbox: " + SandboxProjectPath + "\n\n" +
                             "A jóváhagyás ehhez a projektútvonalhoz és a forrásfájlok mostani SHA-256 hash-éhez kötött. " +
                             "A generált kódot a gép dokumentációja és kockázatértékelése alapján mérnöknek kell ellenőriznie.";
            if (dialogs.Confirm("Jóváhagyás", message))
            {
                Show(workflow.Approve(SandboxProjectPath));
            }
        }

        private async Task ImportAsync()
        {
            if (!dialogs.Confirm("Import és offline fordítás", "Valódi import a sandboxba:\n" + SandboxProjectPath + "\n\nSikeres fordítás után a projekt mentésre kerül. Folytatod?"))
            {
                return;
            }
            await RunStepAsync(ct => workflow.ImportAndCompileAsync(ct));
        }

        private void RefreshInstallations()
        {
            ReplaceAll(Installations, workerClient.DiscoverInstalledVersions());
        }

        private async Task QueryCapabilitiesAsync()
        {
            IsBusy = true;
            try
            {
                TiaResult result = await workerClient.GetCapabilitiesAsync(EffectiveTiaVersion, CancellationToken.None);
                List<string> lines = new List<string> { "Worker " + EffectiveTiaVersion + ": " + result.Status + (result.ErrorCode == null ? string.Empty : " (" + result.ErrorCode + ")") };
                if (result.Worker != null)
                {
                    lines.Add("Openness linked: " + result.Worker.OpennessLinked + ", Siemens.Engineering " + result.Worker.EngineeringAssemblyVersion + " " + result.Worker.EngineeringAssemblyPath);
                }
                if (result.Capabilities != null)
                {
                    lines.AddRange(result.Capabilities.Features.Select(f => "  " + f.Key + " = " + f.Value));
                }
                lines.AddRange(result.Messages.Select(m => m.ToString()));
                ReplaceAll(WorkerMessages, lines);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Show(StepResult result)
        {
            StatusMessage = result.Ok ? result.Message : result.ErrorCode + ": " + result.Message;
            ReplaceAll(Issues, result.Details);
            RefreshFromWorkflow();
        }

        private void RefreshFromWorkflow()
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(EffectiveTiaVersion));
            OnPropertyChanged(nameof(EffectivePlcName));
            OnPropertyChanged(nameof(ProjectExtension));
            OnPropertyChanged(nameof(InputText));
            OnPropertyChanged(nameof(ValidateLabel));
            OnPropertyChanged(nameof(CheckLabel));
            OnPropertyChanged(nameof(GenerateLabel));
            if (workflow.Package?.Manifest != null && workflow.Generation != null)
            {
                if (GeneratedFiles.Count == 0 || GeneratedFiles[0].FullPath == null || !GeneratedFiles[0].FullPath.StartsWith(workflow.Generation.OutputDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    ReplaceAll(GeneratedFiles, workflow.Package.Manifest.Files.OrderBy(f => f.ImportOrder).Select(f => new GeneratedFileViewModel
                    {
                        ImportOrder = f.ImportOrder,
                        BlockName = f.BlockName,
                        BlockType = f.BlockType,
                        RelativePath = f.RelativePath,
                        Sha256 = f.Sha256,
                        ByteLength = f.ByteLength,
                        FullPath = Path.Combine(workflow.Generation.OutputDirectory, f.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    }));
                }
            }
            else
            {
                GeneratedFiles.Clear();
                SelectedFile = null;
            }
            TiaResult worker = workflow.ImportResult ?? workflow.DryRunResult;
            ReplaceAll(WorkerMessages, worker == null ? Enumerable.Empty<string>() : worker.Messages.Select(m => m.ToString()).Concat(new[] { "sandbox_state = " + worker.SandboxState }));
            ReplaceAll(ImportPlan, worker?.ImportPlan ?? Enumerable.Empty<ImportPlanItem>());
            ReplaceAll(CompileMessages, worker?.Compile?.Messages ?? Enumerable.Empty<CompileMessage>());
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnUnhandled(Exception ex)
        {
            StatusMessage = "Váratlan hiba: " + ex.Message;
            dialogs.ShowError("TIA Engineering Studio", ex.ToString());
        }

        private void OnUi(Action action)
        {
            if (SynchronizationContext.Current == ui)
            {
                action();
            }
            else
            {
                ui.Post(_ => action(), null);
            }
        }

        private static string ReadPreview(string path)
        {
            try
            {
                return File.ReadAllText(path, new UTF8Encoding(false, false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "// A fájl nem olvasható: " + ex.Message;
            }
        }

        private static void ReplaceAll<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            target.Clear();
            foreach (T item in items ?? Enumerable.Empty<T>())
            {
                target.Add(item);
            }
        }
    }
}
