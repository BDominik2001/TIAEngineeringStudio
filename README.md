<img src="docs/assets/brand/logo-app.png" alt="TIA Engineering Studio logó" width="140" align="right" />

# TIA Engineering Studio

Windows-alapú mérnöki munkaeszköz Siemens **TIA Portal V18, V19 és V20** célrendszerekhez.
Egyetlen verziófüggetlen gépmodellből (JSON) ellenőrzött, determinisztikus SCL-forrásokat generál,
majd a célverzióhoz tartozó, külön folyamatban futó Openness-workerrel **offline, teljes
sandbox-másolatba** importálja és lefordítja őket.

> A program **nem** tölt le PLC-re, nem kapcsolódik online, nem vezérel berendezést, nem forszol, és
> nem generál/módosít safety (F-) logikát. A generált kódot mérnöknek kell ellenőriznie a gép
> dokumentációja és kockázatértékelése alapján.

A teljes követelményrendszer: [`TIA_Engineering_Studio_TIA_V18_V19_V20_Projektleiras_HU.md`](TIA_Engineering_Studio_TIA_V18_V19_V20_Projektleiras_HU.md).

## Állapot

| Rész | Állapot |
|---|---|
| JSON-séma 1.1, közös hibakódok, 63 közös validációs teszteset | kész, tesztelt |
| Python SCL-generátor (determinisztikus, golden-fájlos tesztek V18/V19/V20-ra) | kész, tesztelt |
| Rust validátor (C ABI, P/Invoke) | kész, tesztelt (a C#-validátorral esetenként egyező kimenet) |
| C# Domain / Contracts / Application (állapotgép, jóváhagyás) / Infrastructure | kész, unit- és integrációs tesztekkel |
| Worker-logika (TiaAdapter.Common): előfeltételek, sandbox, hash, jóváhagyás, rekurzív ütközésvizsgálat | kész, hamis Openness-backenddel tesztelve |
| V18/V19/V20 worker + V18 API-szintű Openness-implementáció | megírva; **valódi TIA Portallal még nincs igazolva** |
| Blokk-könyvtárak: export projektből (SCL-forrás / SimaticML), import a jóváhagyási láncon | kész, hamis Openness-backenddel tesztelve; valódi TIA-val igazolandó |
| WPF Desktop | megírva; a XAML csak Windows alatt fordul (CI: `windows-latest`) |

**Fontos:** a V18/V19/V20 Openness-integráció a spec 11.4 szerint csak akkor tekinthető
kompatibilisnek, ha a `scripts/test-tia-integration.ps1` mindhárom verzióra dokumentáltan lefutott
(lásd [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md)). Jelenleg egyik verzióra sincs
ilyen eredmény; a felület ezért a telepített verziókat `Not tested` állapottal mutatja.

## Szerkezet

```text
src/
  csharp/
    Desktop/          WPF felület (net48 x64)                      – TiaStudio.Desktop
    Core/             Siemens-független gépmodell, szabályok, állapotgép – TiaStudio.Domain
    Application/      munkafolyamat (DRAFT → … → SAVED), port interfészek – TiaStudio.Application
    Ipc/              worker-protokoll DTO-k, hibakódok, manifest/jóváhagyás-integritás – TiaStudio.Contracts
    Infrastructure/   C#-validátor, Rust P/Invoke, Python-folyamat, registry, worker IPC, napló
  python/
    generator/        SCL-generátor (Python 3.9+, külső függőség nélkül)
    ai/               későbbi AI-asszisztens (még nincs implementálva)
  rust/
    validator/        determinisztikus validátor, natív x64 DLL (C ABI) – tes_validator crate
  tia-adapters/
    common/           worker végrehajtási sorrend, Siemens-típusmentes absztrakció
    openness-shared/  V18 API-szintű Openness-kód (a workerekbe fordul)
    v18/ v19/ v20/    net48 x64 worker EXE, saját Siemens.Engineering.dll-lel
contracts/            JSON-sémák (gépmodell, worker-parancs/-eredmény, manifest, blokk-könyvtár), error-codes.json
templates/scl/        verziózott PLC-sablonok (template-set.json: azonosító, verzió, SHA-256)
tests/
  unit/               C# (TiaStudio.UnitTests) és Python unit tesztek – TIA nélkül
  contracts/          validation-cases.json: közös esetek, C#, Python és Rust is ezt futtatja
  integration/        folyamatközi tesztek: Rust DLL, Python-folyamat, teljes munkafolyamat
  tia/                valódi TIA Portal Openness tesztek (csak beállított TIA-gépen)
  regression/golden-scl/  elvárt SCL-bájtok V18/V19/V20-ra
examples/             machine-v18.json, machine-v19.json, machine-v20.json
scripts/              bootstrap, build-*, configure-workers, check-environment, test-*
docs/                 architektúra, verziózás, branchelés, kompatibilitás, kiadás, biztonság, üzemeltetés
```

## Felület

A kezelőfelület a TIA Portal portál nézetének színvilágát követi: fekete fejléc türkiz kiemeléssel,
sötét palaszürke bal oldali navigációs csempék (a kiválasztott világos, narancs jelölővel), kékesszürke
műveleti oszlop fogaskerék-vízjellel és pont-hivatkozásokkal, világosszürke munkaterület kártyákkal.
Minden vezérlő saját sablont kap (`src/csharp/Desktop/Themes/PortalTheme.xaml`): egyedi ablakkeret
(WindowChrome) saját címsor-gombokkal, gombok, beviteli mezők, legördülő listák, táblázatok,
görgetősávok, folyamatjelző és a megerősítő/hibaablak (`ThemedDialog`) is. A felső sáv a munkafolyamat
lépéseit mutatja (kész: türkiz, aktuális: narancs, hibás: piros).

Indításkor a nyitóképen előbb a logó ikonja ugrik be és felragyog, majd felcsúszik és átúszik a
teljes logóba, amelynek felirata (TIA / ENGINEERING / STUDIO) fentről lefelé tárul fel; ezután a
nyitókép finoman elhalványul, és megjelenik a kezdőlap. Ha a Windows animációi ki vannak kapcsolva, a
sorozat gyorsítva fut; `--no-splash` kapcsolóval kihagyható.

Képernyőképek: `TiaStudio.Desktop.exe --screenshot <mappa> [--demo examples\machine-v18.json]`
ideiglenes mappákkal minden szekciót PNG-be ment (TIA Portalt nem indít). A CI Windows-jobja ezt
minden pushnál lefuttatja; a képek a `ui-screenshots` ágon és a `ui-screenshots` artifactban
érhetők el. A logóváltozatok (keretes, körvonalas és keret nélküli
ikon, teljes logó, app-ikon) a `docs/assets/brand` mappában vannak; az alkalmazásikont (`app.ico`) és a
felület képeit a `scripts/tools/build_brand_assets.py` állítja elő belőlük.

## Gyors kezdés (Windows)

Előfeltételek: Windows x64, Visual Studio 2022 vagy .NET SDK 8 + **.NET Framework 4.8 Developer Pack**,
Python 3.9+, Rust stable (`x86_64-pc-windows-msvc`). TIA-művelethez: a célverzió TIA Portal + Openness,
licenc, és a felhasználó a `Siemens TIA Openness` helyi csoport tagja.

```powershell
.\scripts\bootstrap.ps1
.\scripts\check-environment.ps1
.\scripts\test-offline.ps1
.\scripts\build-desktop.ps1          # -> dist\TiaEngineeringStudio
.\dist\TiaEngineeringStudio\TiaStudio.Desktop.exe
```

A workerek a build-gépen a registryből megtalált `Siemens.Engineering.dll`-re hivatkoznak (a DLL-t
nem másoljuk). Ha egy verzió nincs telepítve, az adott worker „unlinked" módban épül, és minden
TIA-műveletet `WORKER_BUILT_WITHOUT_OPENNESS` hibával utasít el. Másik gépre telepítés után:

```powershell
.\scripts\configure-workers.ps1 -WorkersRoot .\dist\TiaEngineeringStudio\workers
```

## Parancssor (TIA nélkül)

```powershell
$env:PYTHONPATH = "$PWD\src\python"
python -m unittest discover -s tests\unit\python -t tests\unit\python -v
python -m generator.main --input examples\machine-v18.json --output generated\v18-demo

cd src\rust\validator
cargo test
cargo build --release
```

Linuxon/CI-ben: `bash scripts/test-offline.sh` (a WPF projekt kivételével mindent fordít és tesztel).

## Munkafolyamat

`DRAFT → VALIDATED (C#) → CHECKED (Rust) → GENERATED (Python + SHA-256 manifest) → REVIEWED (minden
fájl megnyitva az előnézetben) → APPROVED (konkrét sandbox + hash-ekhez kötött jóváhagyás) →
READY_TO_IMPORT (verzió-felderítés + száraz futás, rekurzív ütközésvizsgálat) → IMPORTED → COMPILED → SAVED`.
Bármely hiba `FAILED`; onnan csak újrakezdés (DRAFT) vezet tovább. Részletek:
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/OPERATIONS.md`](docs/OPERATIONS.md).

## Dokumentáció

- [Architektúra](docs/ARCHITECTURE.md) · [API-szerződések](docs/API_CONTRACTS.md) · [Validációs szabályok](docs/VALIDATION_RULES.md)
- [Biztonsági szabályok](docs/SECURITY_RULES.md) · [Kompatibilitási mátrix](docs/COMPATIBILITY.md)
- [TIA V18](docs/TIA_V18.md) · [TIA V19](docs/TIA_V19.md) · [TIA V20](docs/TIA_V20.md)
- [Blokk-könyvtárak](docs/BLOCK_LIBRARY.md) · [Üzemeltetés](docs/OPERATIONS.md) · [Ütemterv és döntési napló](docs/ROADMAP.md)
