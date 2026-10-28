# Ütemterv és döntési napló

## Mérföldkövek (spec 11.1)

| Fázis | Feladat | Állapot |
|---|---|---|
| 0 | V21-es MVP átvilágítása | A V21-es MVP ZIP nem került a repóba; a kód közvetlenül a V18–V20 célstruktúrában készült. V21-hivatkozás nincs (`NoV21LeftoversTests`). |
| 1 | JSON-séma v1.1 | kész – séma, 63 közös teszteset, C#/Python/Rust validátor |
| 2 | Projektstrukturálás | kész – Domain, Application, Contracts, Infrastructure, Common, 3 worker, Desktop |
| 3 | Registry-verziófelderítés | kész, hamis registryvel tesztelve; valódi gépen igazolandó |
| 4 | Python/Rust integráció | kész – TIA nélküli validáció és generálás, folyamatközi tesztekkel |
| 5 | V18 worker | kód kész; **következő feladat:** V18 sandbox-import + offline fordítás valódi TIA-val (`test-tia-integration.ps1 -Version V18`) |
| 6 | V19 worker | kód kész; integrációs teszt V18 után |
| 7 | V20 worker | kód kész; integrációs teszt V18 után |
| 8 | Verzióváltó GUI + jóváhagyás | kész (WPF); Windows-buildet a CI ellenőrzi |
| 9 | Automatizált teszt és release | CI (Linux + Windows), `build-desktop.ps1`; release csak a 11.4 feltételek teljesülése után |

## Rögzített döntések

- Célverziók: TIA Portal V18 / V19 / V20; .NET Framework 4.8, WPF, Windows x64.
- C# orchestrator, Python generátor, Rust natív validátor; Python és Rust nem érheti el a Siemens API-t.
- Külön Openness-worker verziónként, közös JSON-protokollal; V18 API a közös minimum.
- Offline sandbox import; nincs PLC-letöltés, online kapcsolat, forcing, safety-módosítás.
- **A `schema_version` 1.0 nem támogatott** (`UNSUPPORTED_SCHEMA_VERSION`): a repóban nem volt 1.0-s
  gépmodell, így migrációs útvonal nem szükséges; ha mégis előkerül, külön migrációs eszköz és teszt kell.
- A könyvtárak `netstandard2.0` célúak (net48-ból használhatók, és Linux CI-ben net8.0 tesztekkel futtathatók);
  a Desktop és a workerek `net48` x64 EXE-k.
- A C# modellvalidátor `System.Text.Json`-t használ (szigorú RFC 8259), a DTO-k Newtonsoft.Json-t.
- A generátor egyetlen paraméterezett `FB_Cylinder`-t, eszközönként példány-DB-t és egy hívó FC-t állít elő;
  kimenetet nem ír, csak diagnosztikát ad (a mozgásvezérlés későbbi, külön jóváhagyott sablonkönyvtár feladata).
- A worker a dry runt (`ValidateJob`) és a valódi importot külön `job_id`-val futtatja; a jóváhagyás csak a valódi importra szól.
- `UpgradeProject` az MVP-ben `Unsupported`; frissítés kizárólag kézzel, külön másolaton.

## Későbbi kiadások (spec 2.2)

- `FB_Motor`, `FB_Valve`, `FB_Axis`, diagnosztikai FB-k, UDT-k; új `kind` értékek a sémában (sémaverzió-növeléssel).
- Automatikus PLC tag-táblák, biztonságos I/O-mapping (alias csak explicit szabályként).
- ~~Verziózott blokk-könyvtár~~ – elkészült verziófüggetlen formában ([BLOCK_LIBRARY.md](BLOCK_LIBRARY.md));
  hátra van: TIA natív globális könyvtárak (`.al1x`, master copy-k, típusok), csoportstruktúra megőrzése,
  függőségek automatikus felvétele.
- HMI / Unified HMI verziónként külön capability flaggel.
- SimaticML/AML export-import, ahol a célverzió bizonyítottan támogatja.
- Git-alapú változáskövetés, generált projektek összevetése.
- AI-javaslatadó, kizárólag szabályellenőrzés és emberi jóváhagyás után.
- `UpgradeProject` külön jóváhagyott folyamatként.

## Nyitott kérdések (spec 14.)

Lásd még a [COMPATIBILITY.md](COMPATIBILITY.md) „Valódi Openness API-n igazolandó pontok" listáját.

- Telepített TIA Update/szervizcsomagok, támogatott Windows-verziók.
- Használt CPU-k és firmware-ek támogatása V18/V19/V20 alatt.
- Több telepített TIA-verzió és nyitott példány együttes kezelése.
- A projektarchiválás és visszaállítás hitelesíthető, operátori módja.
