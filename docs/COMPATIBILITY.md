# Kompatibilitási mátrix

> **A V18/V19/V20 kompatibilitás jelenleg NINCS igazolva.** Az alábbi táblázat a követelményt és a
> mért állapotot rögzíti. Egy verzió csak akkor kerülhet az `appsettings.json`
> `integration_tested_versions` listájába (és a GUI-ban `Installed` állapotba `Not tested` helyett),
> ha a `scripts/test-tia-integration.ps1` az adott verzióra sikeresen lefutott, az eredményt itt
> rögzítették, és a sandboxban létrejött PLC-objektumokat kézzel is ellenőrizték (spec 11.4).

## Kiadási állapot (verziókövetési szabályzat 11. pont)

Állapotok: **Supported & tested** (a `tia-integration` workflow vagy a `scripts/test-tia-integration.ps1`
az adott verzióra sikeresen lefutott, és az eredmény itt rögzítve) · **Supported, integration test pending**
(a worker megépül a saját Openness-szel, valódi TIA-teszt még nincs) · **Build only** (csak „unlinked”
worker, Openness nélkül) · **Unsupported**. A sikeres fordítás önmagában soha nem „tested”; a
`Skipped` / `Not tested` eredmény nem helyettesíthető `Passed`-del.

| TIA | Állapot | Adapter / build | Openness assembly | Siemens frissítési szint | Windows | CPU / HMI | Import | Compile | Export | Utolsó teszt | CI job / report | Ismert problémák |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| V18 | Supported, integration test pending | `TiaStudio.TiaAdapter.V18` 0.1.0 | 18.0.0.0 | – | – | S7-1200 / S7-1500 | ⏳ | ⏳ | ⏳ | – | – | lásd a tesztmátrixot |
| V19 | Supported, integration test pending | `TiaStudio.TiaAdapter.V19` 0.1.0 | 19.0.0.0 | – | – | S7-1200 / S7-1500 | ⏳ | ⏳ | ⏳ | – | – | lásd a tesztmátrixot |
| V20 | Supported, integration test pending | `TiaStudio.TiaAdapter.V20` 0.1.0 | 20.0.0.0 | – | – | S7-1200 / S7-1500 | ⏳ | ⏳ | ⏳ | – | – | lásd a tesztmátrixot |

Frissítés: minden `tia-integration` futás után ebbe a táblába és a lenti futtatási naplóba kerül a
dátum, a TIA Update, a Windows-verzió és a workflow-futás linkje (a `build-info-V<NN>.json` artifact
tartalmazza a commitot és a verziókat). Egy V20-ban elérhető művelet nem feltételezhető V18-on, és a
`.ap18/.ap19/.ap20` projektek visszafelé nem nyithatók meg automatikusan: a worker mindig csak a
saját verziójú sandbox-projektet nyitja meg (`PROJECT_VERSION_MISMATCH`).

## Tesztmátrix (spec 11.2)

Jelmagyarázat: ✅ igazolt (dátum, gép, TIA Update a naplóban) · ⏳ nem futott · ❌ hibás · 🧪 csak TIA nélküli teszt (hamis backend) igazolja.

| Teszt | V18 | V19 | V20 | Automatizált teszt |
|---|---|---|---|---|
| Telepítés / registry-felderítés | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `RegistryDiscoveryTests`, `MatrixForConfiguredVersion` |
| API DLL helyes betöltése | ⏳ | ⏳ | ⏳ | `MatrixForConfiguredVersion` (GetCapabilities) |
| Üres sandbox projekt megnyitása | ⏳ | ⏳ | ⏳ | `MatrixForConfiguredVersion` (dry run) |
| Választott PLC felismerése | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `UnknownPlcIsRejected`, dry run |
| SCL külső forrás importálása | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `SuccessfulImportCompilesAndSaves`, `MatrixForConfiguredVersion` |
| Generált blokk fordítása | ⏳ | ⏳ | ⏳ | `MatrixForConfiguredVersion` |
| Projektnév/blokkütközés elutasítása | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `NestedGroupCollisionBlocksImport`, `MatrixForConfiguredVersion` |
| Hibás forráshash elutasítása | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `SourceChangedAfterApprovalIsHashMismatch`, `MatrixForConfiguredVersion` |
| Verzióhibás `.apXX` elutasítása | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `ProjectExtensionMismatchIsRejected`, `WrongProjectVersionIsRejected` |
| PLC-letöltés hiánya | ✅ (protokoll) | ✅ (protokoll) | ✅ (protokoll) | `ProtocolHasNoDownloadOnlineOrForceOperation`, `DownloadOperationDoesNotExist` |
| Hibából napló + kontrollált leállás | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `CompileErrorFailsWithoutSaving`, `ImportFailureStopsAndDoesNotSave` |
| Blokklista (ListBlocks) | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `ListBlocksReportsExportability`, `LibraryRoundTripForConfiguredVersion` |
| Export SCL-forrás / SimaticML a könyvtárba | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `ExportWritesHashedFilesWithoutSaving`, `LibraryRoundTripForConfiguredVersion` |
| Import a könyvtárból (SCL + SimaticML) + fordítás | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `LibraryPackageImportUsesFormatPerFile`, `LibraryRoundTripForConfiguredVersion` |
| Más verzióból származó SCL-forrás importja | ⏳ | ⏳ | ⏳ | kézi: pl. V20-ból exportált SCL → V18 sandbox |

## Futtatási napló

| Dátum | Verzió | TIA Update | Windows | CPU / firmware | Eredmény | Ellenőrizte |
|---|---|---|---|---|---|---|
| – | – | – | – | – | még nem futott | – |

## Openness API – összevetés a V18 kézikönyvvel

Forrás: *TIA Portal Openness: API for automation of engineering workflows*, System Manual, 12/2022
(V18, 1388 oldal; a felhasználó által csatolt PDF – Siemens-dokumentum, a repóba nem kerül).
Jelmagyarázat: 📘 a kézikönyv megerősíti · 🔧 a kézikönyv alapján javítva · ⏳ futásidőben (valódi TIA-val) igazolandó.

| # | Pont | Kézikönyv | Állapot |
|---|---|---|---|
| 1 | Telepített verziók: `HKLM\SOFTWARE\Siemens\Automation\Openness\xx.x\PublicAPI\xx.x.x.x` → `Siemens.Engineering`, `PublicKeyToken` (`d29ec89bac048f84`), `AssemblyVersion` | 5.2.2 (68–69. o.) | 📘 + 🔧 a registry `PublicKeyToken`/`AssemblyVersion` értékét is összevetjük a DLL-lel |
| 2 | `app.config` `codeBase` a strong name ellenőrzés megőrzésével (ajánlott), `AssemblyResolve` csak alternatíva | 5.2.8 (78–80. o.) | 📘 |
| 3 | `TiaPortal(TiaPortalMode.WithoutUserInterface)`; a tűzfal-prompt csak futó példányhoz csatlakozáskor jelenik meg | 5.2.8–5.2.9 (78–84. o.) | 📘 – whitelist-bejegyzést a program nem ír (nem kerüljük meg a jóváhagyást) |
| 4 | `Projects.Open(FileInfo)` csak aktuális verziójú projektet nyit, régebbinél kivétel; `OpenWithUpgrade` új, frissített projektet hoz létre | 5.3.1 (112–117. o.) | 📘 – a worker csak `Open`-t hív |
| 5 | A megnyitandó projekt nem lehet nyitva másik TIA-példányban; csak olvasható projekt nem érhető el | 5.3.1 (112. o.) | 🔧 új hibakód: `PROJECT_OPEN_FAILED` (eddig tévesen `PROJECT_NOT_FOUND`) |
| 6 | Eszközök: `Project.Devices`, `DeviceGroups` (rekurzív), `UngroupedDevicesGroup.Devices` | 5.9.7 (309. o.) | 📘 |
| 7 | `PlcSoftware.Name` vs. CPU `DeviceItem.Name` – melyik egyezik a gépmodell `plc_name`-jével | – | ⏳ (mindkettőt elfogadjuk; többértelműségnél `PLC_NOT_FOUND`) |
| 8 | Online állapot: `DeviceItem.GetService<OnlineProvider>().State`, `OnlineState.Offline` | 5.11.2.5 (384–385. o.) | 📘 |
| 9 | Fordítás: `GetService<ICompilable>().Compile()`, rekurzív `CompilerResult.Messages`; minden eszköz offline; jelszavas safety programnál bejelentkezés nélkül kivétel | 5.3 (137–138. o.) | 📘 – a hibaüzenet utal a safety-jelszóra |
| 10 | Blokkcsoportok: `BlockGroup.Blocks`, `BlockGroup.Groups` (`PlcBlockUserGroup`, rekurzív); rendszerblokkok: `SystemBlockGroups` → `PlcSystemBlockGroup.Groups` (szintén `PlcSystemBlockGroup`) | 5.11.3.1–5.11.3.4 (432–435. o.) | 🔧 a bejárás konkrét csoporttípusokra írva (a rendszercsoportok nem `PlcBlockUserGroup`-ok) |
| 11 | Adattípusok: `TypeGroup.Types`, `TypeGroup.Groups` (`PlcTypeUserGroup`) | 5.11.3, 6.4.1.27 | 🔧 a nem dokumentált `SystemTypeGroups` bejárása törölve |
| 12 | Külső forrás: `ExternalSources.CreateFromFile(név, út)`, csak `*.awl/*.scl/*.db/*.udt`; **az „External source files" mappa csoportjai nem érhetők el** | 5.11.3.17 (451. o.) | 🔧 a külső források csoport-bejárása törölve |
| 13 | `GenerateBlocksFromSource()`: **csak ASCII** forrás; **meglévő blokkot felülír**; hibánál a projekt a hívás előtti állapotba áll vissza | 5.11.3.19 (453–456. o.) | 📘 – ASCII-szabály és rekurzív ütközésvizsgálat import előtt |
| 14 | Generált DB-forrás kiterjesztése `*.db`, UDT `*.udt`, SCL-blokk `*.scl` | 5.11.3.17–5.11.3.18 | 🔧 a Python-generátor a példány-DB-ket `.db` fájlba írja (eddig `.scl`) |
| 15 | `GenerateSource(IEnumerable<IGenerateSource>, FileInfo, GenerateOptions)`: UTF-8, csak STL/SCL blokk, DB, UDT; `GenerateOptions.None` = csak a megadott objektum; létező célfájlnál kivétel; PLC offline | 5.11.3.18 (452–453. o.) | 📘 + 🔧 export előtt offline-ellenőrzés |
| 16 | Blokk XML-export/-import: FB, FC, OB, **globális** DB; LAD/FBD/GRAPH/SCL/STL; csak konzisztens blokk; hiányzó példány-DB nem jön létre; hiányzó hivatkozott objektumnál (UDT, FB) a normál import hibát ad | 6.4.1.12–6.4.1.14 (1044–1054. o.) | 🔧 SimaticML csak FB/FC/globális DB/UDT; példány- és tömb-DB csak SCL-forrásként (`.db`); importsorrend UDT → FC → FB → DB |
| 17 | UDT XML-export/-import: `PlcType.Export`, `PlcTypeComposition.Import` | 6.4.1.27–6.4.1.28 (1082–1083. o.) | 📘 |
| 18 | `ImportOptions.None`: meglévő objektumnál kivétel, nincs felülírás; `ExportOptions.WithDefaults` | 6.1.5–6.1.6 (932–934. o.) | 📘 |
| 19 | Blokkattribútumok: `PlcBlock.IsConsistent`, `.Number`, `.ProgrammingLanguage` típusos tulajdonság; `IsKnowHowProtected`, UDT `IsConsistent` attribútumként olvasható | 5.2.23 (110–111. o.), 5.11.3 | 🔧 `IsKnowHowProtected` és UDT-`IsConsistent` `GetAttribute`-tal |
| 20 | Kivételek: `EngineeringException` (visszaállítható: `EngineeringSecurityException`, `EngineeringTargetInvocationException`, …), `NonRecoverableException` (a TIA Portal bezárul) | 5.11.17.1 (734–735. o.) | 🔧 `NonRecoverableException` → `TIA_PORTAL_TERMINATED`, sandbox `Unknown` |
| 21 | V18-tól explicit licencellenőrzés, `LicenseNotFoundException` | 2.2 (22. o.) | 🔧 típusnév alapján is `LICENSE_MISSING` |
| 22 | `[STAThread]` csak a letöltési delegáltakhoz szükséges | 5.11.1.7 (353. o.) | 📘 – ártalmatlan, maradt |
| 23 | `GlobalDB` / `InstanceDB` / `ArrayDB` osztálynevek | 6.4.1.17, 6.4.1.13 | 📘 (`InstanceDB`, `ArrayDB` szerepel); `GlobalDB` osztálynév ⏳ |
| 24 | A generált SCL (`TON_TIME` multi-instance, abszolút `%I` címzés optimalizált FC-ben, `REGION`) fordul-e S7-1200/S7-1500-on | – | ⏳ |
| 25 | `GenerateSource` ír-e UTF-8 BOM-ot, és ékezetmentes forrásnál ASCII-kompatibilis-e (a BOM megengedett) | 5.11.3.18 („UTF-8", a példakód szerint „ASCII text") | ⏳ |

A ⏳ pontokat az első valódi V18 futás (`scripts/test-tia-integration.ps1 -Version V18`) igazolja; az eredményt ide és az alábbi táblázatba kell rögzíteni.

## API-eltérések nyilvántartása

| Verzió | API-elem | Eltérés | Kezelés |
|---|---|---|---|
| V18 (kézikönyv) | External source files csoportjai | Opennessből nem érhetők el | csak a gyökér `ExternalSources` |
| V18 (kézikönyv) | `PlcTypeSystemGroup.SystemTypeGroups` | nem dokumentált | nem használjuk |
| V18 (kézikönyv) | Példány-/tömb-DB SimaticML export | nem dokumentált (csak globális DB) | csak SCL-forrásként (`.db`) |
