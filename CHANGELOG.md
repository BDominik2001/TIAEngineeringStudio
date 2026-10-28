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

- Openness-integráció a V18 kézikönyv (12/2022) szerint: blokkcsoportok, SimaticML-korlátok,
  offline export, hibakódok (`TIA_PORTAL_TERMINATED`, `LICENSE_MISSING`, `PROJECT_OPEN_FAILED`). *TIA: all*
- Könyvtárszerkezet a szabályzat szerint (`src/csharp`, `src/python`, `src/rust`, `src/tia-adapters`,
  `templates/scl`, `tests/{unit,contracts,integration,tia,regression}`); a sablonkészlet a generátoron kívül.

### Ismert korlátozások

- Valódi TIA Portal V18/V19/V20 integrációs teszt még egyik verzióra sem futott le
  (állapot: *Supported, integration test pending*, lásd [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md)).
