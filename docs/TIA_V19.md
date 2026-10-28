# TIA Portal V19

| Tétel | Érték |
|---|---|
| Projektkiterjesztés | `.ap19` (más kiterjesztés: `PROJECT_VERSION_MISMATCH`) |
| Worker | `TiaStudio.TiaAdapter.V19.exe` (net48, x64) – `src/tia-adapters/v19` |
| Tipikus DLL | `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll` (csak alapérték; a valódi helyet a registry adja) |
| Registry | `HKLM\SOFTWARE\Siemens\Automation\Openness\19.x\PublicAPI\19.x.x.x` |
| Assembly-verzió | 19.0.0.0, public key token `d29ec89bac048f84` |
| API-szint | V18 közös funkciókészlet (`api_level = V18-common`) |
| Integrációs teszt állapota | **nem futott** – lásd [COMPATIBILITY.md](COMPATIBILITY.md) |

A Siemens V19 dokumentációja (11/2023) szerint a V18-as Openness DLL-re épülő alkalmazások V19 alatt is futtathatók. Ennek ellenére a V19 worker **saját V19 `Siemens.Engineering.dll`-re** hivatkozik, hogy egy folyamatban csak egy verzió töltődjön be, és a V19 telepítés hibái elkülönüljenek.

V19-specifikus API-t csak ebben a projektben (`src/tia-adapters/v19`) szabad bevezetni, `TiaCapabilities.Features` jelző mögött; hiányzó képesség explicit `Unsupported`.

V19-ben/V20-ban exportált SimaticML **nem** minősül V18-kompatibilisnek; a SimaticML import/export (`simaticml_import`) az MVP-ben ki van kapcsolva.

## Build és beállítás

```powershell
.\scripts\build-tia-workers.ps1 -Versions V19 -RequireOpenness
# vagy kézzel, nem szabványos telepítési hellyel:
dotnet build .\src\tia-adapters\v19\TiaStudio.TiaAdapter.V19.csproj -c Release -p:TiaApiPath="D:\Siemens\Portal V19\PublicAPI\V19"
```

A build után a kimeneti `TiaStudio.TiaAdapter.V19.exe.config` `codeBase`-e a fenti DLL-re mutat; más gépen
a `scripts\configure-workers.ps1` állítja be a helyi telepítés alapján.

## Integrációs teszt

```powershell
.\scripts\test-tia-integration.ps1 -Version V19 -KnownGoodProject D:\TIA\KnownGood\Empty\Empty.ap19 -PlcName PLC_1
```

Előfeltételek: TIA Portal V19 + Openness, érvényes licenc, a szükséges HSP-k/opciók, a felhasználó a
`Siemens TIA Openness` csoport tagja (tagság után új bejelentkezés), az ismert jó projekt egy üres,
offline S7-1200/S7-1500 PLC-t tartalmaz a megadott néven, és be van zárva a TIA Portalban.
