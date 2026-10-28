# TIA Portal V20

| Tétel | Érték |
|---|---|
| Projektkiterjesztés | `.ap20` (más kiterjesztés: `PROJECT_VERSION_MISMATCH`) |
| Worker | `TiaStudio.TiaAdapter.V20.exe` (net48, x64) – `src/TiaStudio.TiaAdapter.V20` |
| Tipikus DLL | `C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll` (csak alapérték; a valódi helyet a registry adja) |
| Registry | `HKLM\SOFTWARE\Siemens\Automation\Openness\20.x\PublicAPI\20.x.x.x` |
| Assembly-verzió | 20.0.0.0, public key token `d29ec89bac048f84` |
| API-szint | V18 közös funkciókészlet (`api_level = V18-common`) |
| Integrációs teszt állapota | **nem futott** – lásd [COMPATIBILITY_MATRIX.md](COMPATIBILITY_MATRIX.md) |

A „What's new in TIA Portal Openness V20" (01/2025) szerint a V20 a V17/V18/V19 DLL-eket is szállítja. A felderítés ezért **csak a natív `20.x` PublicAPI-kulcsot** fogadja el V20-nak; egy V20 telepítés V18-as kompatibilitási API-ja nem jelent telepített V18-at (`OlderApiInsideNewerInstallIsNotTreatedAsOlderTia`).

A V21-ben a Siemens átalakította az assembly-felosztást (`Siemens.Engineering.Base.dll`, `Siemens.Engineering.Step7.dll`); ezek a V18–V20 workerekben **nem** használhatók, és a forrásban sem maradhat rájuk hivatkozás (`NoV21LeftoversTests`).

## Build és beállítás

```powershell
.\scripts\build-tia-workers.ps1 -Versions V20 -RequireOpenness
# vagy kézzel, nem szabványos telepítési hellyel:
dotnet build .\src\TiaStudio.TiaAdapter.V20\TiaStudio.TiaAdapter.V20.csproj -c Release -p:TiaApiPath="D:\Siemens\Portal V20\PublicAPI\V20"
```

A build után a kimeneti `TiaStudio.TiaAdapter.V20.exe.config` `codeBase`-e a fenti DLL-re mutat; más gépen
a `scripts\configure-workers.ps1` állítja be a helyi telepítés alapján.

## Integrációs teszt

```powershell
.\scripts\test-tia-integration.ps1 -Version V20 -KnownGoodProject D:\TIA\KnownGood\Empty\Empty.ap20 -PlcName PLC_1
```

Előfeltételek: TIA Portal V20 + Openness, érvényes licenc, a szükséges HSP-k/opciók, a felhasználó a
`Siemens TIA Openness` csoport tagja (tagság után új bejelentkezés), az ismert jó projekt egy üres,
offline S7-1200/S7-1500 PLC-t tartalmaz a megadott néven, és be van zárva a TIA Portalban.
