# Architektúra

```text
┌──────────────────────────────────────────────────────────────────────┐
│ TiaStudio.Desktop (WPF, net48 x64) – MainViewModel                   │
│   csak Application-portokon át dolgozik; kompozíciós gyökér: App.xaml.cs │
└───────────────┬──────────────────────────────────────────────────────┘
                │ EngineeringWorkflow (Application)
   ┌────────────┼──────────────┬──────────────────┬───────────────────┐
   ▼            ▼              ▼                  ▼                   ▼
IModelValidator IStaticValidator ISourceGenerator  ITiaWorkerClient    ISandboxService / IAuditLog
C# (System.Text RustStaticValidator PythonSource-  TiaWorkerProcess-   SandboxService / JsonlAuditLog
.Json, szigorú) P/Invoke →      Generator →       Client → worker EXE
                tes_validator   python -m         --command/--result JSON
                .dll            generator.main         │
                                                        ├─ TiaStudio.TiaAdapter.V18.exe → V18 Siemens.Engineering.dll
                                                        ├─ TiaStudio.TiaAdapter.V19.exe → V19 Siemens.Engineering.dll
                                                        └─ TiaStudio.TiaAdapter.V20.exe → V20 Siemens.Engineering.dll
                                                                    │
                                                         sandbox\V1x\<projekt>_<időbélyeg>\*.ap1x
```

## Rétegek és függőségi szabályok (spec 4.1)

| Projekt | Cél | Hivatkozhat | Nem hivatkozhat |
|---|---|---|---|
| `TiaStudio.Domain` | gépmodell, szabály-primitívek (`NamingRules`, `IoAddress`), `WorkflowState` | – | Siemens, WPF, JSON, Python, Rust |
| `TiaStudio.Contracts` | DTO-k, hibakódok, `PackageVerifier`, `SandboxRules`, `ApprovalDigest` | Newtonsoft.Json | Siemens, WPF |
| `TiaStudio.Application` | `EngineeringWorkflow`, port interfészek | Domain, Contracts | Infrastructure, WPF, Siemens |
| `TiaStudio.Infrastructure` | portok implementációi | Application | WPF, Siemens |
| `TiaStudio.TiaAdapter.Common` | worker-végrehajtás, `IOpennessBackend` | Contracts | WPF, Siemens |
| `TiaStudio.TiaAdapter.V18/V19/V20` | worker EXE | Common + saját `Siemens.Engineering.dll` | WPF, más verzió DLL-je |
| `TiaStudio.Desktop` | UI | Infrastructure (csak a kompozíciós gyökérben) | Siemens, workerek |

A `Contracts` a protokollt definiáló tiszta függvényeket is tartalmazza (manifest-, sandbox- és
jóváhagyás-ellenőrzés), mert ezeket **azonos módon** kell kiszámolnia a főalkalmazásnak és a workernek.

## Miért külön worker-folyamat verziónként? (spec 1.1, 4.2)

- Egy folyamatban soha nem töltődik be két különböző `Siemens.Engineering.dll`.
- A worker a saját telepítésének DLL-jét `app.config` `codeBase`-szel tölti be (strong name ellenőrzés
  megmarad); a DLL nem kerül a kimenetbe és Git-be.
- Elakadt Openness-hívás nem fagyasztja a felületet: a főalkalmazás időkorláttal leállítja a workert,
  és a sandbox állapotát `Unknown`-nak jelzi.
- A közös forrás (`OpennessShared`) a V18 API közös minimumát használja; V19/V20-specifikus API csak az
  adott worker projektjébe kerülhet, `capabilities` jelző mögé.

## Worker végrehajtási sorrend (`WorkerHost`, spec 8.4)

1. Parancs beolvasása (szigorú séma), protokollverzió, `job_id`, művelet, worker-verzió egyezése.
2. Környezet: betöltött `Siemens.Engineering` verziója/kulcsa, `Siemens TIA Openness` csoporttagság.
3. Kötelező mezők, `overwrite_policy=Deny`, `SandboxRules` (kiterjesztés = verzió, sandbox gyökér alatt, marker).
4. `PackageVerifier`: manifest-szerkezet, útvonal-kiszökés, symlink, SHA-256 + bájthossz; manifest célverziója és PLC-je; ASCII-forrás.
5. Valódi importnál `ApprovalDigest` újraszámolása a worker által mért hash-ekből.
6. Projekt megnyitása **frissítés nélkül** (`Projects.Open`), PLC keresése (többértelműség = nem található), offline állapot.
7. Rekurzív ütközésvizsgálat: összes blokk és PLC-adattípus minden felhasználói és rendszer-alcsoportban, valamint külső forrásfájl-nevek (kis/nagybetű-független). Dry run itt `ValidationOnly`-val megáll.
8. Import importálási sorrendben (FB → instance DB-k → FC), minden fájl előtt hash-újraellenőrzés.
9. Offline fordítás (`ICompilable`), rekurzív üzenetfa lapítva; hiba esetén nincs mentés.
10. Mentés csak teljes siker után; minden esetben eredményfájl és a TIA-példány felszabadítása.

## Blokk-könyvtár

`IBlockLibraryStore` (Infrastructure: `FileBlockLibraryStore`) tárolja a verziófüggetlen könyvtárakat;
`BlockLibraryService` (Application) futtatja a csak olvasó `ListBlocks`/`ExportBlocks` worker-műveleteket;
az import az `EngineeringWorkflow` könyvtár-módja (`SetLibrarySelection`): VALIDATED = elemszabályok és
integritás, CHECKED = hash-újramérés, GENERATED = manifest 1.1 importcsomag, utána a megszokott lánc.
Az export/import közös szabályai (`LibraryRules`) a Contracts-ban vannak, mert a worker is alkalmazza őket.
Részletek: [BLOCK_LIBRARY.md](BLOCK_LIBRARY.md).

## Openness-hivatkozások

Az `OpennessShared/OpennessBackend.cs` a TIA Portal Openness V18 kézikönyv (12/2022) fejezeteire
hivatkozik a kódkommentekben; a kézikönyvvel való tételes összevetés és a futásidőben még igazolandó
pontok: [COMPATIBILITY_MATRIX.md](COMPATIBILITY_MATRIX.md#openness-api--összevetés-a-v18-kézikönyvvel).

## Generált PLC-modulstratégia (spec 7.2)

- `FB_Cylinder` – egyetlen közös, paraméterezett FB (végállás-kiértékelés, mindkét érzékelő aktív hiba,
  mozgási időtúllépés, nyugtázás, státuszkód). Kimenetet nem ír; nem biztonsági funkció.
- `DB_Cylinder_<id>` – példány-DB eszközönként (`.db` külső forrás, Openness kézikönyv 5.11.3.17).
- `FC_Cylinders` – hívó FC; a felhasználó illeszti be egy ciklikus OB-ba (OB1-et nem módosítjuk).
- Bemenetek abszolút címzéssel (`%I0.0`) a gépmodellből; tag-tábla generálása későbbi kiadás.
