# TIA Portal V18

| Tétel | Érték |
|---|---|
| Projektkiterjesztés | `.ap18` (más kiterjesztés: `PROJECT_VERSION_MISMATCH`) |
| Worker | `TiaStudio.TiaAdapter.V18.exe` (net48, x64) – `src/TiaStudio.TiaAdapter.V18` |
| Tipikus DLL | `C:\Program Files\Siemens\Automation\Portal V18\PublicAPI\V18\Siemens.Engineering.dll` (csak alapérték; a valódi helyet a registry adja) |
| Registry | `HKLM\SOFTWARE\Siemens\Automation\Openness\18.x\PublicAPI\18.x.x.x` |
| Assembly-verzió | 18.0.0.0, public key token `d29ec89bac048f84` |
| API-szint | V18 közös funkciókészlet (`api_level = V18-common`) |
| Integrációs teszt állapota | **nem futott** – lásd [COMPATIBILITY_MATRIX.md](COMPATIBILITY_MATRIX.md) |

A V18 a három célverzió **közös API-minimuma**. A közös forrás (`src/TiaStudio.TiaAdapter.OpennessShared`) kizárólag a V18 kézikönyvben (TIA Portal Openness, 12/2022) ismertetett szolgáltatásokat használja:

- kapcsolódás és projektnyitás: `TiaPortal(TiaPortalMode.WithoutUserInterface)`, `Projects.Open(FileInfo)` (kézikönyv 78–83. o.)
- telepített verziók: `HKLM\SOFTWARE\Siemens\Automation\Openness\18.0\PublicAPI\18.0.0.0` (68–69. o.)
- offline fordítás: `ICompilable.Compile()`, `CompilerResult` rekurzív üzenetei (136–138. o.)
- külső forrás: `PlcExternalSourceComposition.CreateFromFile`, `PlcExternalSource.GenerateBlocksFromSource()` – **felülírhat meglévő blokkot**, ezért előtte teljes rekurzív ütközésvizsgálat fut (453–456. o.); a külső forrás ASCII.
- V18-tól explicit licencellenőrzés: hiánynál `LICENSE_MISSING`.

**Elsőként a V18 workerrel** kell eljutni egy sikeres, naplózott sandbox-import + offline fordítás tesztig (spec 16.); a V19/V20 regresszió csak ezután tekinthető teljesnek.

## Build és beállítás

```powershell
.\scripts\build-tia-workers.ps1 -Versions V18 -RequireOpenness
# vagy kézzel, nem szabványos telepítési hellyel:
dotnet build .\src\TiaStudio.TiaAdapter.V18\TiaStudio.TiaAdapter.V18.csproj -c Release -p:TiaApiPath="D:\Siemens\Portal V18\PublicAPI\V18"
```

A build után a kimeneti `TiaStudio.TiaAdapter.V18.exe.config` `codeBase`-e a fenti DLL-re mutat; más gépen
a `scripts\configure-workers.ps1` állítja be a helyi telepítés alapján.

## Integrációs teszt

```powershell
.\scripts\test-tia-integration.ps1 -Version V18 -KnownGoodProject D:\TIA\KnownGood\Empty\Empty.ap18 -PlcName PLC_1
```

Előfeltételek: TIA Portal V18 + Openness, érvényes licenc, a szükséges HSP-k/opciók, a felhasználó a
`Siemens TIA Openness` csoport tagja (tagság után új bejelentkezés), az ismert jó projekt egy üres,
offline S7-1200/S7-1500 PLC-t tartalmaz a megadott néven, és be van zárva a TIA Portalban.
