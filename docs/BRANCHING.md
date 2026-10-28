# Branchelés, commitok, Pull Requestek

Trunk-based fejlesztés rövid életű branchekkel. A `main` az állandó, ellenőrzött integrációs ág;
közvetlen push, force push és törlés tiltott (lásd [Main védelme](#main-védelme)).

## Ágak

| Ág | Mire | Élettartam |
|---|---|---|
| `main` | ellenőrzött integráció | állandó |
| `feature/<leiras>` | új funkció | ideiglenes |
| `fix/<leiras>` | hibajavítás | ideiglenes |
| `refactor/<leiras>` | belső átalakítás | ideiglenes |
| `docs/<leiras>` | dokumentáció | ideiglenes |
| `test/<leiras>` | tesztelés | ideiglenes |
| `release/<major.minor>` | kiadásstabilizálás, csak ha tényleg kell | a kiadásig |
| `hotfix/<leiras>` | sürgős javítás kiadott verzióhoz | ideiglenes |
| `support/<major.minor>` | hosszabban támogatott régi kiadás, csak indokoltan | indokolt ideig |

Példák: `feature/scl-generator`, `feature/tia-v19-adapter`, `fix/io-overlap-detection`,
`refactor/openness-abstraction`, `docs/versioning-rules`.

**Tilos** tartós `tia-v18`, `tia-v19`, `tia-v20` branchekkel megoldani a kompatibilitást: a közös
kódbázis és a `src/tia-adapters/v18|v19|v20` adapterek a megoldás ([ARCHITECTURE.md](ARCHITECTURE.md)).

Az AI-asszisztens munkamenetei (`claude/<azonosító>` ágak) ugyanígy rövid életűek, és PR-on át kerülnek
a `main`-be.

## Munkafolyamat

1. Frissítsd a `main` ágat.
2. Hozz létre rövid nevű, egyetlen feladatra koncentráló branchet.
3. Készíts kis, logikus commitokat (Conventional Commits).
4. Futtasd a helyi teszteket: `bash scripts/test-offline.sh` (Linux) vagy `.\scripts\test-offline.ps1` (Windows).
5. Nyiss Pull Requestet a `main`-re a PR-sablon kitöltésével.
6. Ellenőrizd a CI-t és a reviewer visszajelzését; minden beszélgetést zárj le.
7. Sikeres ellenőrzés után **squash merge**.
8. Töröld a lezárt branchet.

## Commitüzenetek – Conventional Commits

```text
<type>(<scope>): <rövid leírás>

<törzs: mit és miért; magyarul is lehet>

BREAKING CHANGE: <leírás és migráció, ha van>
```

Típusok: `feat`, `fix`, `refactor`, `test`, `docs`, `build`, `ci`, `chore`, `perf`, `revert`, `style`.

Ajánlott scope-ok: `core`, `desktop`, `generator`, `rust`, `openness`, `tia-v18`, `tia-v19`, `tia-v20`,
`library`, `contracts`, `templates`, `repo`, `ci`, `release`, `docs`.

Példák: `feat(generator): add cylinder FB template` · `fix(tia-v20): handle compilation errors` ·
`test(rust): add overlapping address tests` · `docs(compatibility): record V19 integration run`.

Squash merge esetén a PR címe lesz a commitüzenet, ezért a PR címe is Conventional Commits formátumú.

## Pull Request

A [PR-sablon](../.github/PULL_REQUEST_TEMPLATE.md) kötelező pontjai:

1. kapcsolódó Issue;
2. a változás célja;
3. érintett modulok és TIA-verziók;
4. futtatott tesztek és eredményük;
5. érint-e PLC-blokkot, DB-t, hardverkonfigurációt vagy projektadatot;
6. visszafelé nem kompatibilis változás;
7. változik-e a projektadatséma vagy a sablonverzió;
8. kell-e migráció vagy felhasználói jóváhagyás;
9. kockázatok és visszaállítás.

Egy PR lehetőleg egy Issue-t old meg. Adapter- vagy importművelet-módosításhoz szakmai review kell
(`.github/CODEOWNERS`).

## Issue-k, címkék, milestone-ok

Milestone = melyik kiadásra tervezett (pl. `v0.2.0 – Project Validation`). Címkék:

| Csoport | Címkék |
|---|---|
| Modul | `area:core`, `area:desktop`, `area:python`, `area:rust`, `area:openness`, `area:generator`, `area:hmi`, `area:docs` |
| TIA | `tia:v18`, `tia:v19`, `tia:v20`, `tia:all` |
| Típus | `type:feature`, `type:bug`, `type:refactor`, `type:documentation`, `type:security`, `type:test` |
| Prioritás | `priority:critical`, `priority:high`, `priority:normal`, `priority:low` |

A Projects tábla oszlopai: Backlog, Ready, In progress, Review, Done.

## Main védelme

GitHub Settings → Rules → Rulesets (a `main`-re):

- csak Pull Requesttel lehet változást a `main`-be tenni;
- kötelező státuszellenőrzések: `ci / lint, format, schema, determinism, security`, `ci / linux`, `ci / windows`;
- minden beszélgetés lezárása kötelező;
- force push és törlés tiltva;
- legalább egy jóváhagyás, ha többen fejlesztenek; `CODEOWNERS` review az adapterekre és az importra;
- squash merge az alapértelmezett, a lezárt ágak automatikus törlése bekapcsolva;
- a `v*` tagekre külön ruleset: létrehozás csak kiadáskezelőnek, módosítás és törlés tiltva.

A beállítások elérhetősége a repó láthatóságától és a GitHub-csomagtól függ.
