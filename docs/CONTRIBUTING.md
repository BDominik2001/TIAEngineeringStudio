# Közreműködés

## Előfeltételek

Windows x64: Visual Studio 2022 vagy .NET SDK 8 + .NET Framework 4.8 Developer Pack, Python 3.9+,
Rust stable (`x86_64-pc-windows-msvc`). TIA-művelethez a célverzió TIA Portal + Openness. Linuxon/macOS-en
a WPF projekt kivételével minden fordítható és tesztelhető. Gyors indulás: `.\scripts\bootstrap.ps1`.

## Fejlesztői munkafolyamat

1. Issue a feladathoz (sablon: `.github/ISSUE_TEMPLATE`), címkékkel és milestone-nal.
2. Rövid életű ág a friss `main`-ből ([BRANCHING.md](BRANCHING.md)): `feature/…`, `fix/…`, `refactor/…`.
3. Kis, logikus commitok Conventional Commits formátumban.
4. Helyi ellenőrzés commit előtt:

   ```bash
   bash scripts/test-offline.sh        # Python, Rust (fmt nélkül), .NET portable, contracts, golden SCL
   ruff check . && ruff format --check .
   (cd src/rust/validator && cargo fmt --check)
   bash scripts/ci/check-scl-determinism.sh
   ```

   Windows: `.\scripts\test-offline.ps1` (a WPF Desktopot is fordítja).
5. Pull Request a `main`-re, a PR-sablon minden pontjával. CI zöld, beszélgetések lezárva, review után squash merge.

## Függőségfrissítések (Dependabot)

A Dependabot hetente nyit PR-t (NuGet, Cargo, GitHub Actions). Ezeket egyenként, a CI zöldje után kell
beolvasztani, nem vakon:

- főverzió-váltásnál (pl. System.Text.Json 8 → 10) a `windows` job net48 Desktop buildje, a netstandard2.0
  könyvtárak és a C# tesztek is legyenek zöldek, és nézd át a csomag kiadási jegyzetét (törő változások,
  új tranzitív függőségek, binding redirectek);
- a régebbi alapra épülő Dependabot PR-t a `@dependabot rebase` megjegyzéssel kell frissíteni, ha a `main`
  CI-je időközben változott;
- Dependabot-változás soha nem fut a licencelt self-hosted TIA-runneren: a `tia-integration` és a `release`
  workflow nem indul Pull Requestre.

## Szabályok a kódhoz

- A közös logika (`src/csharp/Core`, `Application`, `Ipc`, `Infrastructure`, `Desktop`) **nem hivatkozhat**
  Siemens Engineering típusokra. Verzióspecifikus Openness-kód csak a `src/tia-adapters/v18|v19|v20`
  adapterbe kerülhet, `TiaCapabilities.Features` jelző mögé; a közös Openness-kód az `openness-shared`-ben
  csak a V18 kézikönyvben dokumentált API-t használhatja ([COMPATIBILITY.md](COMPATIBILITY.md)).
- Új TIA-verzió támogatása ne igényelje a meglévő adapterek vagy a közös üzleti logika átírását.
- Validációs szabály változása: a C#, a Python és a Rust egyszerre, a `tests/contracts/validation-cases.json`
  bővítésével, és a `RulesVersion` emelésével.
- Sablonváltozás: `templates/scl`, majd `python -m generator.template_hashes --write` (a `src/python`
  mappából), a `template-set.json` verziójának emelése és a `tests/regression/golden-scl` tudatos újragenerálása.
- Séma- vagy protokollváltozás: [VERSIONING.md](VERSIONING.md); törő változásnál migrációs terv a PR-ban.
- A generált PLC-programok módosítása csak a jóváhagyási láncon (validálás → ellenőrzés → generálás →
  átnézés → jóváhagyás → sandbox-import) történhet; PLC-letöltés, online kapcsolat, forcing és safety
  (F-) logika módosítása tilos ([SECURITY_RULES.md](SECURITY_RULES.md)).

## Mi kerülhet Gitbe

Igen: saját forráskód (`.cs`, `.py`, `.rs`, XAML), SCL/UDT/DB-sablonok, JSON-sémák, nem bizalmas
mintakonfigurációk (`examples/`), dokumentáció, workflow-k, lock fájlok (`Cargo.lock`).

Nem: build-kimenet (`bin/`, `obj/`, `target/`, `dist/`, `__pycache__/`, `.venv/`), jelszavak, API-kulcsok,
tokenek, privát kulcsok, Siemens telepítők, licencfájlok és DLL-ek (`Siemens.Engineering*.dll`),
ügyfél TIA-projektek (`*.ap1x`, `*.ap2x`, `*.zap*`), lokális gépspecifikus fájlok, ideiglenes exportok.
A `.gitignore` ezeket kizárja; bináris TIA-projekt helyett exportált SCL/SimaticML kerüljön a repóba.
Nagy, jogszerűen megosztható bináris mintaprojekthez csak indokolt esetben Git LFS.

## Repó-felépítés

Lásd a [README](../README.md#szerkezet) „Szerkezet” részét és az [ARCHITECTURE.md](ARCHITECTURE.md)-t.
