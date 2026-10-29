# Változástörténet

A formátum a [Keep a Changelog](https://keepachangelog.com/hu/1.1.0/) ajánlását, a verziószámozás a
[Semantic Versioning](https://semver.org/lang/hu/) szabályait követi (lásd [docs/VERSIONING.md](docs/VERSIONING.md)).
Az alkalmazás verziója független a TIA Portal célverzióitól; minden bejegyzés jelöli az érintett
TIA-verziókat és a séma-, protokoll- vagy sablonverzió változását.

Kiadott verzió még nincs. Az első tag (`v0.1.0`) csak akkor készül, ha a
[kiadási checklist](docs/RELEASE_PROCESS.md#kiadási-checklist) minden pontja teljesül.

## [Unreleased]

### Hozzáadva

- V18/V19/V20 többverziós alap: verziófüggetlen gépmodell (séma 1.1), C#/Rust/Python validáció,
  determinisztikus SCL-generálás (sablonkészlet `tes-scl-core@1.0.0`), jóváhagyási lánc, offline
  sandbox-import verziónkénti Openness-workerrel (IPC-protokoll 1.1). *TIA: all*
- Blokk-könyvtárak: blokkok exportja projektből, import a könyvtárból a jóváhagyási láncon. *TIA: all*
- Felület: TIA Portal portál-stílusú téma, saját vezérlősablonok, animált nyitókép, a megadott logó.
- Buildazonosító (`build-info.json`): alkalmazás-, séma-, protokoll-, sablon- és adapterverzió, git-commit.
- Verziókövetési szabályzat: monorepo-szerkezet, dokumentáció, PR/Issue-sablonok, CI A szint
  (lint, formázás, séma, determinizmus, titok- és függőségvizsgálat), kiadási és TIA-integrációs workflow.

### Módosítva

- Repó: importálható GitHub rulesetek a `main` ágra és a `v*` tagekre (`.github/rulesets`).
- Felület: magyar hibaüzenetek és teendők a közös hibakatalógusból (`contracts/error-codes.json`
  `message_hu`/`hint_hu`, katalógus 1.3); a validációs hibalista és az állapotsor is magyar.
- Felület: a Napló táblázatos (idő, esemény, állapot, TIA, hibakód, üzenet; a nyers JSON buboréksúgóban).
- Felület: „Mintamodell betöltése” (a kiadási csomag `examples\` mappájából), „Vissza a gépmodellhez”.
- Felület: a téma színei a logó palettájából (navy, acélkék, a logó kék íve, borostyán) a korábbi
  semleges palaszürke/türkiz helyett, hogy a felület ne üssön el a beágyazott logóktól.
- Openness-integráció a V18 kézikönyv (12/2022) szerint: blokkcsoportok, SimaticML-korlátok,
  offline export, hibakódok (`TIA_PORTAL_TERMINATED`, `LICENSE_MISSING`, `PROJECT_OPEN_FAILED`). *TIA: all*
- Könyvtárszerkezet a szabályzat szerint (`src/csharp`, `src/python`, `src/rust`, `src/tia-adapters`,
  `templates/scl`, `tests/{unit,contracts,integration,tia,regression}`); a sablonkészlet a generátoron kívül.

### Javítva

- Kódaudit (2026-10-29) javításai, *TIA: all*:
  - Az offline-ellenőrzés fail-closed: csak a kifejezett `OnlineState.Offline` engedi az importot/exportot;
    hiányzó `OnlineProvider` vagy nem egyértelmű állapot új `PLC_ONLINE_STATE_UNKNOWN` hiba (eddig offline-nak számított).
  - A worker a projektben talált CPU családját (`TypeIdentifier` rendelési száma) összeveti a manifest
    `cpu_family`-jével; eltérés vagy fel nem ismerhető CPU: új `CPU_FAMILY_MISMATCH` (hibakatalógus 1.4).
  - A TIA nélkül fordított („unlinked”) worker a felületen `Unavailable`; a `configure-workers.ps1` nem
    állítja át (config-írással sem lenne használható), a worker mellé `.openness.json` link-jelölő kerül.
  - A TIA-integrációs tesztszkript kihagyott teszteknél nem jelez sikert (TRX-ellenőrzés: `passed | failed | not-run`);
    új teszt a hibás CPU-családra; `-CpuFamily` / `TES_TIA_CPU_FAMILY_Vxx`.
  - A jóváhagyás a sandbox-másolat azonosítójához is kötött (ApprovalDigest v2, sandbox-marker 1.1 `sandbox_id`;
    a régi markerű sandboxot újra kell létrehozni).
  - A sandbox útvonalán és a projektmappában junction/symlink tiltva; linkelt fájlból nem készül sandbox.
  - Időtúllépés/megszakítás a worker teljes folyamatfáját (a TIA Portal-példányt is) leállítja (Windows Job Object),
    a maradék folyamatról figyelmeztetés szól.
  - Atomi JSON-írás (`File.Replace`, lemezre ürítve): a `library.json` csere közben sem hiányzik.
  - A forráskód-előnézet olvasási hibája vagy a manifesttől eltérő tartalma nem jelöli a fájlt átnézettnek.
  - A `simaticml_import` és `simaticml_block_import` képesség hatóköre dokumentálva.
- Kiadás: az előzetes-kiadás tagek (`v0.2.0-beta.1`) eddig elbuktak a verzióellenőrzésen; a tag alapverziója
  egyezik a `Directory.Build.props`-szal, a csomag és a build a teljes tag-verziót viseli.
- Kiadás: stabil kiadáshoz mindhárom TIA-verzió integrációs tesztje kötelező (eddig kihagyható volt);
  előzetes kiadás nem tesztelt csomagja „Untested development build”; a tesztkörnyezet a manifestbe kerül.
- Maximalizált ablaknál a fejléc teteje nagyobb DPI-skálázáson (pl. 150%) lelógott a képernyőről;
  az ablak most pontosan a monitor munkaterületét kapja.

### Ismert korlátozások

- Valódi TIA Portal V18/V19/V20 integrációs teszt még egyik verzióra sem futott le
  (állapot: *Supported, integration test pending*, lásd [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md)).
