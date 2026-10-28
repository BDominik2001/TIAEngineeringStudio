using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TiaStudio.Application.Library;
using TiaStudio.Application.Ports;
using TiaStudio.Application.Workflow;
using TiaStudio.Contracts;
using TiaStudio.Desktop.Services;

namespace TiaStudio.Desktop.ViewModels
{
    public sealed class LibraryItemRow : ObservableObject
    {
        private bool isSelected;

        public LibraryItemRow(BlockLibraryItem item, bool isLatest)
        {
            Item = item;
            IsLatest = isLatest;
        }

        public BlockLibraryItem Item { get; }

        public bool IsLatest { get; }

        public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }

        public string BlockName => Item.BlockName;

        public string BlockType => Item.BlockType + (Item.DbKind == null ? string.Empty : " (" + Item.DbKind + ")");

        public string Revision => "r" + Item.Revision + (IsLatest ? string.Empty : " (régi)");

        public string Format => Item.Format == BlockFormats.SimaticMl ? "SimaticML" : "SCL forrás";

        public string SourceTiaVersion => Item.SourceTiaVersion;

        public string Language => Item.ProgrammingLanguage;

        public string Description => Item.Description;

        public string Added => Item.AddedAtUtc + " " + Item.AddedBy;

        public string Origin => Item.Origin == null ? null : Item.Origin.ProjectFile + " / " + Item.Origin.PlcName;

        public string Sha256 => Item.Sha256;
    }

    public sealed class ProjectBlockRow : ObservableObject
    {
        private bool isSelected;
        private string selectedFormat;

        public ProjectBlockRow(ProjectBlockInfo info)
        {
            Info = info;
            selectedFormat = info.ExportableFormats.FirstOrDefault();
        }

        public ProjectBlockInfo Info { get; }

        public bool CanExport => Info.ExportableFormats.Count > 0;

        public bool IsSelected { get => isSelected; set => Set(ref isSelected, value && CanExport); }

        public IReadOnlyList<string> Formats => Info.ExportableFormats;

        public string SelectedFormat { get => selectedFormat; set => Set(ref selectedFormat, value); }

        public string Name => Info.Name;

        public string BlockType => Info.BlockType + (Info.DbKind == null ? string.Empty : " (" + Info.DbKind + ")");

        public string Language => Info.ProgrammingLanguage;

        public string Location => Info.Location;

        public string Note => Info.NotExportableReason;
    }

    /// <summary>
    /// Blokk-könyvtárak fül: könyvtár létrehozása, elemek listája/ellenőrzése/törlése, blokkexport egy
    /// sandbox-projektből a könyvtárba, és könyvtárelemek importjának előkészítése (a munkafolyamat
    /// könyvtár-módba vált; az importot a 3–4. fül jóváhagyási lánca végzi).
    /// </summary>
    public sealed class LibraryViewModel : ObservableObject
    {
        private readonly BlockLibraryService service;
        private readonly EngineeringWorkflow workflow;
        private readonly ISandboxService sandbox;
        private readonly IDialogService dialogs;
        private readonly Func<Func<CancellationToken, Task<StepResult>>, Task> runAsync;
        private readonly Action<StepResult> show;
        private readonly Func<bool> isBusy;
        private BlockLibrarySummary selectedLibrary;
        private LibraryItemRow selectedItem;
        private string newLibraryName;
        private string newLibraryDescription;
        private string exportDescription;
        private bool showAllRevisions;
        private string tiaVersion = TiaVersions.V18;
        private string plcName = "PLC_1";
        private string sourceProjectPath;

        public LibraryViewModel(
            BlockLibraryService service,
            EngineeringWorkflow workflow,
            ISandboxService sandbox,
            IDialogService dialogs,
            Func<Func<CancellationToken, Task<StepResult>>, Task> runAsync,
            Action<StepResult> show,
            Func<bool> isBusy,
            Action<Exception> onError)
        {
            this.service = service;
            this.workflow = workflow;
            this.sandbox = sandbox;
            this.dialogs = dialogs;
            this.runAsync = runAsync;
            this.show = show;
            this.isBusy = isBusy;

            RefreshLibrariesCommand = new RelayCommand(RefreshLibraries, () => !isBusy());
            CreateLibraryCommand = new RelayCommand(CreateLibrary, () => !isBusy() && !string.IsNullOrWhiteSpace(NewLibraryName));
            VerifyLibraryCommand = new RelayCommand(() => show(service.Verify(SelectedLibrary.Name).Step), () => !isBusy() && SelectedLibrary != null);
            RemoveItemCommand = new RelayCommand(RemoveItem, () => !isBusy() && SelectedLibrary != null && SelectedItem != null);
            BrowseSourceProjectCommand = new RelayCommand(BrowseSourceProject, () => !isBusy());
            ListBlocksCommand = new AsyncCommand(() => runAsync(ListBlocksAsync), () => !isBusy() && !string.IsNullOrEmpty(SourceProjectPath), onError);
            ExportCommand = new AsyncCommand(() => runAsync(ExportAsync),
                () => !isBusy() && SelectedLibrary != null && ProjectBlocks.Any(b => b.IsSelected), onError);
            PrepareImportCommand = new RelayCommand(PrepareImport, () => !isBusy() && SelectedLibrary != null && Items.Any(i => i.IsSelected));

            RefreshLibraries();
        }

        public IReadOnlyList<string> TiaVersionOptions => TiaVersions.Supported;

        public string LibraryRoot => service.Store.LibraryRoot;

        public ObservableCollection<BlockLibrarySummary> Libraries { get; } = new ObservableCollection<BlockLibrarySummary>();

        public ObservableCollection<LibraryItemRow> Items { get; } = new ObservableCollection<LibraryItemRow>();

        public ObservableCollection<ProjectBlockRow> ProjectBlocks { get; } = new ObservableCollection<ProjectBlockRow>();

        public ICommand RefreshLibrariesCommand { get; }

        public ICommand CreateLibraryCommand { get; }

        public ICommand VerifyLibraryCommand { get; }

        public ICommand RemoveItemCommand { get; }

        public ICommand BrowseSourceProjectCommand { get; }

        public ICommand ListBlocksCommand { get; }

        public ICommand ExportCommand { get; }

        public ICommand PrepareImportCommand { get; }

        public BlockLibrarySummary SelectedLibrary
        {
            get => selectedLibrary;
            set
            {
                if (Set(ref selectedLibrary, value))
                {
                    LoadItems();
                }
            }
        }

        public LibraryItemRow SelectedItem { get => selectedItem; set => Set(ref selectedItem, value); }

        public string NewLibraryName { get => newLibraryName; set => Set(ref newLibraryName, value?.Trim()); }

        public string NewLibraryDescription { get => newLibraryDescription; set => Set(ref newLibraryDescription, value); }

        public string ExportDescription { get => exportDescription; set => Set(ref exportDescription, value); }

        public bool ShowAllRevisions
        {
            get => showAllRevisions;
            set
            {
                if (Set(ref showAllRevisions, value))
                {
                    LoadItems();
                }
            }
        }

        /// <summary>Export forrásverziója és import célverziója.</summary>
        public string TiaVersion
        {
            get => tiaVersion;
            set
            {
                if (Set(ref tiaVersion, value))
                {
                    SourceProjectPath = null;
                    ProjectBlocks.Clear();
                }
            }
        }

        public string PlcName { get => plcName; set => Set(ref plcName, value?.Trim()); }

        public string SourceProjectPath { get => sourceProjectPath; private set => Set(ref sourceProjectPath, value); }

        private void RefreshLibraries()
        {
            string current = SelectedLibrary?.Name;
            Libraries.Clear();
            foreach (BlockLibrarySummary summary in service.Store.ListLibraries())
            {
                Libraries.Add(summary);
            }
            SelectedLibrary = Libraries.FirstOrDefault(l => l.Name == current) ?? Libraries.FirstOrDefault();
            LoadItems();
        }

        private void LoadItems()
        {
            Items.Clear();
            if (SelectedLibrary == null || SelectedLibrary.Problem != null)
            {
                if (SelectedLibrary?.Problem != null)
                {
                    show(StepResult.Error(ErrorCodes.LibraryInvalid, SelectedLibrary.Problem));
                }
                return;
            }
            try
            {
                BlockLibraryIndex index = service.Store.Load(SelectedLibrary.Name);
                HashSet<string> latest = new HashSet<string>(index.LatestItems().Select(i => i.ItemId));
                IEnumerable<BlockLibraryItem> items = ShowAllRevisions ? index.Items : index.LatestItems();
                foreach (BlockLibraryItem item in items.OrderBy(i => i.BlockName, StringComparer.OrdinalIgnoreCase).ThenByDescending(i => i.Revision))
                {
                    Items.Add(new LibraryItemRow(item, latest.Contains(item.ItemId)));
                }
            }
            catch (BlockLibraryException ex)
            {
                show(StepResult.Error(ex.ErrorCode, ex.Message));
            }
        }

        private void CreateLibrary()
        {
            OperationResult<BlockLibraryIndex> result = service.CreateLibrary(NewLibraryName, string.IsNullOrWhiteSpace(NewLibraryDescription) ? null : NewLibraryDescription.Trim());
            show(result.Step);
            if (result.Ok)
            {
                NewLibraryName = null;
                NewLibraryDescription = null;
                RefreshLibraries();
                SelectedLibrary = Libraries.FirstOrDefault(l => l.Name == result.Value.Name);
            }
        }

        private void RemoveItem()
        {
            BlockLibraryItem item = SelectedItem.Item;
            if (!dialogs.Confirm("Elem törlése", "Végleg törlöd a könyvtárból: " + item.BlockName + " r" + item.Revision + " (" + item.Format + ")?\nA művelet naplózásra kerül."))
            {
                return;
            }
            show(service.RemoveItem(SelectedLibrary.Name, item).Step);
            RefreshLibraries();
        }

        private void BrowseSourceProject()
        {
            string extension = TiaVersions.ProjectExtension(TiaVersion);
            string path = dialogs.OpenFile("Forrás sandbox-projekt (" + TiaVersion + ") – csak olvasva", "TIA Portal " + TiaVersion + " projekt (*" + extension + ")|*" + extension);
            if (path == null)
            {
                return;
            }
            Tuple<string, string> problem = sandbox.Verify(path, TiaVersion);
            if (problem != null)
            {
                show(StepResult.Error(problem.Item1, problem.Item2 + " – exportálni is csak sandbox-másolatból lehet (3. fül: Sandbox-másolat készítése)."));
                return;
            }
            SourceProjectPath = path;
            ProjectBlocks.Clear();
        }

        private async Task<StepResult> ListBlocksAsync(CancellationToken cancellationToken)
        {
            OperationResult<IReadOnlyList<ProjectBlockInfo>> result = await service.ListProjectBlocksAsync(TiaVersion, SourceProjectPath, PlcName, cancellationToken);
            ProjectBlocks.Clear();
            if (result.Ok)
            {
                foreach (ProjectBlockInfo block in result.Value.OrderBy(b => b.Location, StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                {
                    ProjectBlocks.Add(new ProjectBlockRow(block));
                }
            }
            return result.Step;
        }

        private async Task<StepResult> ExportAsync(CancellationToken cancellationToken)
        {
            List<ExportRequestItem> requests = ProjectBlocks.Where(b => b.IsSelected && b.SelectedFormat != null)
                .Select(b => new ExportRequestItem { BlockName = b.Name, Format = b.SelectedFormat })
                .ToList();
            OperationResult<IReadOnlyList<BlockLibraryItem>> result = await service.ExportToLibraryAsync(
                SelectedLibrary.Name, TiaVersion, SourceProjectPath, PlcName, requests,
                string.IsNullOrWhiteSpace(ExportDescription) ? null : ExportDescription.Trim(), cancellationToken);
            if (result.Ok)
            {
                foreach (ProjectBlockRow row in ProjectBlocks)
                {
                    row.IsSelected = false;
                }
                RefreshLibraries();
            }
            return result.Step;
        }

        private void PrepareImport()
        {
            List<string> ids = Items.Where(i => i.IsSelected).Select(i => i.Item.ItemId).ToList();
            workflow.SetLibrarySelection(new LibraryImportSelection
            {
                LibraryName = SelectedLibrary.Name,
                ItemIds = ids,
                TargetTiaVersion = TiaVersion,
                PlcName = PlcName,
            });
            StepResult validated = workflow.Validate();
            if (validated.Ok)
            {
                StepResult checkedResult = workflow.Check();
                show(checkedResult.Ok
                    ? StepResult.Success(ids.Count + " elem ellenőrizve (" + TiaVersion + ", " + PlcName + "). Következő: 2. fül → csomag összeállítása, majd 3–4. fül.", validated.Details)
                    : checkedResult);
            }
            else
            {
                show(validated);
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
