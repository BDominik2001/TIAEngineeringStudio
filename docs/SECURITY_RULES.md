# Biztonsági szabályok és megvalósításuk

| # | Szabály (spec 10.1) | Megvalósítás | Teszt |
|---|---|---|---|
| 1 | Éles projekt nem módosítható, csak teljes sandbox-másolat | `SandboxService` teljes mappát másol `.tes-sandbox.json` markerrel (egyedi `sandbox_id`); linkelt fájlt/mappát nem másol; `SandboxRules` a főalkalmazásban és a workerben is, a sandbox útvonalán és a projektmappában junction/symlink tiltva | `SandboxRuleTests`, `SandboxCopyIsCompleteAndMarked`, `ProjectOutsideSandboxIsRejected`, `LinksInTheSandboxPathOrProjectFolderAreRejected`, `SandboxCopyRejectsLinkedFiles` |
| 2 | `.apXX` = TIA-verzió | `SandboxRules`, worker-verzió ellenőrzés, manifest célverzió | `WrongExtensionIsVersionMismatch`, `ProjectExtensionMismatchIsRejected`, `PackageForOtherVersionIsRejected` |
| 3 | Projektfrissítés csak külön `UpgradeProject` | `Projects.Open` (soha nem `OpenWithUpgrade`); `UpgradeProject` = `Unsupported` | `UpgradeProjectIsUnsupported`, `UpgradeRequiredOnOpenIsClassified` |
| 4 | Import előtt teljes rekurzív ütközésvizsgálat, felülírás tiltva | `WorkerHost.BuildPlan` + `OpennessPlcTarget.GetAllBlocksAndTypes` (user + system csoportok, típusok, külső források) | `NestedGroupCollisionBlocksImport`, `ExternalSourceNameCollisionBlocksImport` |
| 5 | PLC offline | `IPlcTarget.GetOnlineState` (csak lekérdezés, GoOnline nincs); fail-closed: csak a kifejezett `OnlineState.Offline` engedélyez, hiányzó `OnlineProvider` vagy nem egyértelmű állapot `PLC_ONLINE_STATE_UNKNOWN` | `OnlinePlcIsRejected`, `UndeterminableOnlineStateIsRejected` |
| 5a | A célhardver egyezik a gépmodellel | a manifest `cpu_family`-je és a CPU `TypeIdentifier`-éből (rendelési szám) meghatározott család egyezik; eltérő vagy fel nem ismerhető CPU = `CPU_FAMILY_MISMATCH` | `CpuFamilyMustMatchTheManifest`, `CpuFamilyIsDerivedFromTheOrderNumber`, `WrongCpuFamilyIsRejected` |
| 6 | Backup és visszaállítás | ismert jó projekt → új sandbox minden futáshoz; hiba után új másolat (OPERATIONS.md) | – (eljárás) |
| 7 | Hash-ellenőrzés jóváhagyáskor és import előtt | `EngineeringWorkflow.VerifyIntegrity`, worker `PackageVerifier` + fájlonkénti újramérés; a jóváhagyás a sandbox-másolat azonosítójához is kötött (ApprovalDigest v2) | `SourceChangedAfterApprovalFails`, `SourceChangedAfterApprovalIsHashMismatch`, `SandboxRecreatedAfterApprovalNeedsNewApproval` |
| 8 | Nincs download/online/force/safety | a protokoll nem ismeri; `download_allowed=false`, `safety_scope=non-safety` kötelező a manifestben; a generált kód nem ír kimenetet | `ProtocolHasNoDownloadOnlineOrForceOperation`, `DownloadOperationDoesNotExist`, `DownloadAllowedTrueIsRejected`, `test_generated_code_writes_no_outputs` |
| 9 | Részleges siker nem siker | `TiaResult.IsSuccessful`; workflow csak `Success`+`Saved` esetén SAVED | `PartialSuccessIsNotSuccess`, `CompileErrorFailsWithoutSaving` |
| 10 | Napló nem tartalmaz titkot | `JsonlAuditLog.Redact` | `AuditLogRedactsSecretsAndAppendsJsonl` |
| 11 | Siemens DLL nem kerül Git-be/csomagba | `.gitignore`, `Private=False` referencia, `codeBase` | – |
| 12 | Minden képességhez automatizált teszt | Python/Rust/C# + közös esetek | `scripts/test-offline.*` |
| 13 | Generált kódot ember ellenőriz | REVIEWED csak minden fájl sikeres, a manifest-hash-sel egyező megjelenítése után (`SourcePreview`); jóváhagyó párbeszéd; fejléc-figyelmeztetés minden SCL-ben | `OnlyReadableContentMatchingTheManifestIsShown` |
| 14 | TIA-verziót nem találgat | registry-felderítés csak natív API-kulcsra; hiánynál megáll; TIA nélkül fordított worker `Unavailable` (`WORKER_BUILT_WITHOUT_OPENNESS`) | `MissingTiaStopsWithoutGuessing`, `OlderApiInsideNewerInstallIsNotTreatedAsOlderTia`, `UnlinkedWorkerIsUnavailableEvenWithTiaInstalled` |
| 14a | Megszakítás/időtúllépés nem hagy futó TIA-példányt | Windows Job Object a worker folyamatfáján: leállításkor a TIA Portal is leáll, a maradék folyamat figyelmeztetés | `TimeoutTerminatesTheWholeProcessTreeOnWindows` |
| 15 | Közös hibatipológia | `contracts/error-codes.json`; C#/Python/Rust teszt ellenőrzi | `CSharpConstantsMatchSharedCatalogue`, `test_emitted_codes_are_in_shared_catalogue`, Rust `error_codes` teszt |

## Blokk-könyvtár (docs/BLOCK_LIBRARY.md)

| Szabály | Megvalósítás | Teszt |
|---|---|---|
| Export is csak sandboxból, csak olvasva | `BlockLibraryService` + worker `SandboxRules`; `ExportBlocks` nem ment | `ExportFromNonSandboxIsRefused`, `ExportWritesHashedFilesWithoutSaving` |
| OB, safety (F-), know-how védett, inkonzisztens blokk kizárva | `LibraryRules.ExportProblem/ImportProblem`, SimaticML F-tartalom szűrése | `ExportableFormats`, `ExportOfOrganizationBlockIsRefusedBeforeAnyExport`, `SafetyContentInExportIsRefused` |
| SimaticML csak azonos TIA-verzióba | `LibraryRules.ImportProblem` | `SimaticMlFromOtherVersionFailsValidation` |
| Elemfájl nem íródik felül, minden export új revízió | `FileBlockLibraryStore` (`FileMode.CreateNew`) | `ExportsBecomeNewRevisionsAndFilesAreNeverOverwritten` |
| Részleges export nem kerül a könyvtárba | teljes siker + hash-újramérés után atomikus indexírás | `FailedExportAddsNothing`, `ExportFileChangedAfterExportAddsNothing` |
| Könyvtár-manipuláció észlelése | `Verify`: útvonalminta, SHA-256, bájthossz; import előtt kétszer (VALIDATED, CHECKED) | `TamperedItemIsDetected`, `ManipulatedIndexPathIsRejected`, `ItemChangedAfterValidationFailsCheck` |
| Könyvtárimport ugyanazon jóváhagyási láncon | `EngineeringWorkflow` könyvtár-mód | `LibraryImportRunsThroughApprovalChainToSaved` |

## Jóváhagyás

Az `approved_by_user` önmagában **nem** jogosultság. A jóváhagyás `ApprovalDigest`-je a konkrét
`job_id`-hoz, művelethez, verzióhoz, projektútvonalhoz, PLC-hez, felülírási szabályhoz, manifest-hash-hez
és minden forrás hash-éhez kötött; a worker újraszámolja a saját méréseiből (`ApprovalFlagWithoutValidDigestIsRejected`).
A digest integritásvédelem, nem hitelesítés: helyi támadó ellen az operációs rendszer jogosultságai védenek.

## Openness-hozzáférés

A Siemens Openness hozzáférési párbeszédét a felhasználó kezeli; a program nem kerüli meg. UMAC-védett
projektnél a `Modify project via Openness API` jog hiánya `UMAC_PERMISSION_DENIED`.
