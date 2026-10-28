# Kompatibilitási mátrix

> **A V18/V19/V20 kompatibilitás jelenleg NINCS igazolva.** Az alábbi táblázat a követelményt és a
> mért állapotot rögzíti. Egy verzió csak akkor kerülhet az `appsettings.json`
> `integration_tested_versions` listájába (és a GUI-ban `Installed` állapotba `Not tested` helyett),
> ha a `scripts/test-tia-integration.ps1` az adott verzióra sikeresen lefutott, az eredményt itt
> rögzítették, és a sandboxban létrejött PLC-objektumokat kézzel is ellenőrizték (spec 11.4).

## Tesztmátrix (spec 11.2)

Jelmagyarázat: ✅ igazolt (dátum, gép, TIA Update a naplóban) · ⏳ nem futott · ❌ hibás · 🧪 csak TIA nélküli teszt (hamis backend) igazolja.

| Teszt | V18 | V19 | V20 | Automatizált teszt |
|---|---|---|---|---|
| Telepítés / registry-felderítés | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `RegistryDiscoveryTests`, `MatrixForConfiguredVersion` |
| API DLL helyes betöltése | ⏳ | ⏳ | ⏳ | `MatrixForConfiguredVersion` (GetCapabilities) |
| Üres sandbox projekt megnyitása | ⏳ | ⏳ | ⏳ | `MatrixForConfiguredVersion` (dry run) |
| Választott PLC felismerése | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `UnknownPlcIsRejected`, dry run |
| SCL külső forrás importálása | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `SuccessfulImportCompilesAndSaves`, `MatrixForConfiguredVersion` |
| Generált blokk fordítása | ⏳ | ⏳ | ⏳ | `MatrixForConfiguredVersion` |
| Projektnév/blokkütközés elutasítása | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `NestedGroupCollisionBlocksImport`, `MatrixForConfiguredVersion` |
| Hibás forráshash elutasítása | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `SourceChangedAfterApprovalIsHashMismatch`, `MatrixForConfiguredVersion` |
| Verzióhibás `.apXX` elutasítása | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `ProjectExtensionMismatchIsRejected`, `WrongProjectVersionIsRejected` |
| PLC-letöltés hiánya | ✅ (protokoll) | ✅ (protokoll) | ✅ (protokoll) | `ProtocolHasNoDownloadOnlineOrForceOperation`, `DownloadOperationDoesNotExist` |
| Hibából napló + kontrollált leállás | ⏳ 🧪 | ⏳ 🧪 | ⏳ 🧪 | `CompileErrorFailsWithoutSaving`, `ImportFailureStopsAndDoesNotSave` |

## Futtatási napló

| Dátum | Verzió | TIA Update | Windows | CPU / firmware | Eredmény | Ellenőrizte |
|---|---|---|---|---|---|---|
| – | – | – | – | – | még nem futott | – |

## Valódi Openness API-n igazolandó pontok

Az `OpennessShared/OpennessBackend.cs` a V18 kézikönyv alapján készült, és egy helyi API-csonk ellen
fordításilag ellenőrizve van, de a tényleges Siemens assembly-kkel még nem. Az első V18 buildnél és
futásnál ellenőrizendő:

1. `PlcBlockSystemGroup.SystemBlockGroups` elemei `PlcBlockGroup`-ként bejárhatók-e (`PlcSystemBlockGroup`).
2. `PlcTypeSystemGroup.SystemTypeGroups` és a `PlcSystemTypeGroup` típusnév/öröklés.
3. `PlcExternalSourceGroup` közös ősosztály megléte (`ExternalSources`, `Groups`).
4. `Project.UngroupedDevicesGroup.Devices` és `Project.Devices` viszonya (duplikáció-szűrés megvan).
5. `PlcSoftware.Name` vs. CPU `DeviceItem.Name` – melyik egyezik a gépmodell `plc_name`-jével.
6. `OnlineProvider` szolgáltatás elérhetősége a CPU `DeviceItem`-en offline projektben.
7. A generált SCL (`TON_TIME` multi-instance, abszolút `%I` címzés optimalizált FC-ben, `REGION`) fordul-e S7-1200 és S7-1500 CPU-n; létrehoz-e a TIA automatikus tag-bejegyzéseket az abszolút címekhez.
8. A licenc-, UMAC- és frissítési hibák tényleges kivételtípusai/üzenetei (`OpennessErrorClassifier`).
9. `[STAThread]` szükségessége `WithoutUserInterface` módban.

Minden eltérést ide kell rögzíteni, verziónként külön sorban.

## API-eltérések nyilvántartása

| Verzió | API-elem | Eltérés | Kezelés |
|---|---|---|---|
| – | – | még nincs ismert eltérés | – |
