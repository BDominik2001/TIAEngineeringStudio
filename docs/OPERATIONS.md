# Üzemeltetés és kezelési útmutató

## Telepítési elrendezés (`scripts\build-desktop.ps1` kimenete)

```text
TiaEngineeringStudio\
  TiaStudio.Desktop.exe, *.dll, appsettings.json
  tes_validator.dll                 Rust validátor (x64)
  python\generator\                 SCL-generátor + sablonok (template-set.json hash-ekkel)
  workers\V18\TiaStudio.TiaAdapter.V18.exe(.config)
  workers\V19\...
  workers\V20\...
```

`appsettings.json` (relatív útvonal az EXE mappájához; `%VAR%` környezeti változó kibontva):

| Kulcs | Jelentés |
|---|---|
| `python_executable` | Python 3.9+ (pl. `python` vagy teljes út) |
| `generator_root` | a `generator` csomagot tartalmazó mappa |
| `workers_directory` | `<dir>\V1x\TiaStudio.TiaAdapter.V1x.exe` |
| `sandbox_root` | minden sandbox-projekt e mappa alatt (`<root>\V1x\<név>_<UTC időbélyeg>\`) |
| `output_root` | generált csomagok (`<root>\<run_id>\input`, `\package`) és exportok (`export-…\blocks`) |
| `library_root` | blokk-könyvtárak (`<root>\<név>\library.json`) – Git-be tehető |
| `library_worker_timeout_seconds` | blokklista és -export időkorlátja |
| `log_directory` | `audit-YYYYMMDD.jsonl` műveleti napló |
| `job_directory` | worker-parancs és -eredmény fájlok |
| `*_timeout_seconds` | generátor, száraz futás, import, képesség-lekérdezés időkorlátja |
| `integration_tested_versions` | csak dokumentált integrációs teszt után bővíthető |

## Kezelés lépésről lépésre

1. **Gépmodell** fül: projektnév, célverzió (V18/V19/V20), PLC-név (a TIA-projektben lévő PLC-vel egyező), CPU-család, munkahengerek és bemeneti címek. Mentés JSON-ba.
2. **Ellenőrzés és generálás**: (1) séma-/modellellenőrzés → (2) Rust statikus ellenőrzés → (3) SCL-generálás. Minden hiba útvonallal és hibakóddal jelenik meg.
3. **Előnézet és jóváhagyás**: minden generált fájlt meg kell nyitni az előnézetben, ezután (4) „Átnéztem". Sandbox: vagy meglévő sandbox-projekt tallózása (csak a célverzió kiterjesztése választható, marker kötelező), vagy „Sandbox-másolat készítése" egy **ismert jó**, a TIA Portalban bezárt projektből. (5) Jóváhagyás.
4. **TIA Portal**: telepített verziók (registry), worker-képességek, (6) száraz futás (projektnyitás, PLC, offline állapot, rekurzív ütközésvizsgálat – a projekt nem módosul), (7) import + offline fordítás + mentés.
5. **Blokk-könyvtárak**: könyvtár létrehozása, export egy sandbox-projektből, könyvtárelemek importjának előkészítése (utána 2–4. fül) – részletek: [BLOCK_LIBRARY.md](BLOCK_LIBRARY.md).
6. **Napló**: az összes állapotátmenet és könyvtárművelet JSONL-ben.

A modell bármely módosítása visszaállítja a munkafolyamatot DRAFT-ba, és érvényteleníti a generált
csomagot és a jóváhagyást. Bármely hiba után (`FAILED`) csak „Újrakezdés" vezet tovább.

## Hibakezelés és visszaállítás

| Helyzet | Teendő |
|---|---|
| `sandbox_state = Unchanged` | a sandbox használható tovább |
| `ModifiedNotSaved` (import-/fordítási hiba) | a TIA nem mentett, de a sandboxot **dobd el**, és készíts újat az ismert jó projektből |
| `Unknown` (időtúllépés, összeomlás, megszakítás, mentési hiba) | ellenőrizd, fut-e még `Siemens.Automation.Portal.exe` a worker nevében; a sandboxot dobd el; az ismert jó projekt érintetlen |
| `TIA_NOT_INSTALLED` / `API_DLL_MISMATCH` | `scripts\check-environment.ps1`, majd `scripts\configure-workers.ps1` |
| `WORKER_BUILT_WITHOUT_OPENNESS` | a workert a TIA-t tartalmazó gépen újra kell építeni (`build-tia-workers.ps1 -RequireOpenness`) |
| `OPENNESS_ACCESS_DENIED` | felhasználó hozzáadása a `Siemens TIA Openness` csoporthoz, ki-/bejelentkezés |
| `LICENSE_MISSING` | TIA/Openness licenc ellenőrzése az Automation License Managerben |
| `PROJECT_UPGRADE_REQUIRED` | a projekt más verziójú; frissítést csak kézzel, külön jóváhagyott másolaton szabad végezni |
| `BLOCK_COLLISION` | a sandbox már tartalmazza a neveket; új sandbox vagy más eszközazonosítók |

A program nem állítja, hogy a Siemens-műveletek tranzakciósan visszafordíthatók: a visszaállítás
módja mindig az ismert jó projektmásolat újbóli másolása.

## Napló

`log_directory\audit-YYYYMMDD.jsonl`: soronként egy JSON-objektum (`timestamp_utc`, `event`,
`session_id`, `run_id`, `job_id`, `state`, `tia_version`, `error_code`, `message`, `details`).
A `details` jelszó/token/kulcs jellegű kulcsai kitakarva. A generált csomag `manifest.json`-ja és a
worker `*.result.json` fájljai a futás teljes, visszakereshető bizonyítékai (SHA-256).
