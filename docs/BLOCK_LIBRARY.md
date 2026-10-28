# Blokk-könyvtárak

A TIA Engineering Studio saját, **verziófüggetlen** blokk-könyvtárakat kezel. Egy könyvtárba
blokkokat lehet exportálni egy TIA-projektből, és a könyvtárból blokkokat lehet importálni egy
(akár másik verziójú) TIA-projektbe – mindkettő ugyanazon biztonsági szabályok mellett, mint a
generált SCL-források importja.

## Miért nem a TIA natív globális könyvtára?

A TIA Portal globális könyvtára (`.al18`, `.al19`, `.al20`) verzióhoz kötött fájl: egy V20-as
könyvtár V18-ban nem nyitható meg, és frissítés után a régebbi verzió sem tudja olvasni. A projekt
alapelve (spec 1.) egy semleges forrásból három célverzió kiszolgálása, ezért a könyvtár itt egy
egyszerű, Git-be tehető mappaszerkezet SHA-256 integritással. A natív globális könyvtárak
(master copy-k, típusok) kezelése későbbi, külön capability flag mögötti bővítés lehet.

## Tárolás

```text
<library_root>\<KönyvtárNév>\
  library.json                                   index (contracts/block-library.schema.json)
  items\FB_Motor\r1\FB_Motor.scl                 1. revízió (SCL külső forrás)
  items\FB_Motor\r2\FB_Motor.xml                 2. revízió (SimaticML)
  items\UDT_Motor\r1\UDT_Motor.udt
  items\DB_Recipe\r1\DB_Recipe.db
```

- `library_root` az `appsettings.json`-ban (alapértelmezés: `%USERPROFILE%\TIA_Engineering_Studio\libraries`).
- Elemfájl **soha nem íródik felül**: ugyanannak a blokknak minden új exportja új revízió (`r1`, `r2`, …).
- Minden elemhez rögzítve: blokktípus, DB-fajta, programnyelv, formátum, forrás TIA-verzió, SHA-256,
  bájthossz, leírás, ki és mikor vette fel, melyik projektből/PLC-ből/worker-jobból származik.
- Az „Integritás" gomb (és minden import) újraszámolja a hash-eket, és ellenőrzi az útvonalakat.

## Formátumok

| Formátum | Mire jó | Import célverziója |
|---|---|---|
| **SCL forrás** (`external_source`: `.scl` / `.db` / `.udt`) | SCL-nyelvű FB/FC, adatbázisok, PLC-adattípusok | bármely V18/V19/V20 – a célverzió offline fordítása dönti el a kompatibilitást (figyelmeztetés jelzi, ha más verzióból származik) |
| **SimaticML** (`simaticml`: `.xml`) | FB, FC (bármely nyelv: LAD, FBD, GRAPH, SCL, STL), **globális** DB, PLC-adattípus | **csak** az a TIA-verzió, amelyből exportálták (`LIBRARY_VERSION_MISMATCH`) – spec 3.3 |

A V18 Openness kézikönyv (6.4.1.12/6.4.1.13) a blokkok XML-exportját/-importját FB, FC, OB és
**globális** DB esetén dokumentálja, ezért példány- és tömb-DB csak SCL-forrásként (`.db`) kerülhet a
könyvtárba. A forrásgenerálás (5.11.3.18) csak SCL/STL kódblokkra, DB-re és UDT-re működik; az STL-t
(`.awl`) a könyvtár nem kezeli.

Exportálásnál a felület blokkonként csak az engedett formátumokat kínálja (LAD/FBD blokkhoz csak SimaticML-t).

## Mi nem kerülhet könyvtárba

| Eset | Ok | Hibakód |
|---|---|---|
| Szervezőblokk (OB) | megváltoztatná a PLC végrehajtási modelljét | `LIBRARY_ITEM_NOT_ALLOWED` |
| Safety / F-blokk (`F_LAD`, `F_FBD`, `F_DB`, …, vagy F-tartalmú SimaticML) | spec 2.3: safety-logika nem módosítható | `LIBRARY_ITEM_NOT_ALLOWED` |
| Know-how védett blokk | nem exportálható | `LIBRARY_ITEM_NOT_ALLOWED` |
| Inkonzisztens (nem lefordított) blokk | előbb fordítani kell | `LIBRARY_ITEM_NOT_ALLOWED` |
| Név nem `^[A-Za-z][A-Za-z0-9_]{0,124}$` | fájl- és külsőforrás-névként is biztonságos legyen | `LIBRARY_ITEM_NOT_ALLOWED` |
| Nem ASCII SCL-forrás (pl. ékezetes komment) | a V18 külső forrás ASCII; SimaticML-ként exportálható | `LIBRARY_ITEM_NOT_ALLOWED` |

## Export: projekt → könyvtár

1. A projektről sandbox-másolat kell (3. fül: *Sandbox-másolat készítése*). Exportálni is csak
   sandboxból lehet – így a worker soha nem nyit meg éles projektet.
2. 5. fül: TIA-verzió, PLC-név, *Tallózás…* (a sandbox `.apXX` fájlja), *Blokkok listázása*.
   A lista a PLC összes felhasználói blokkját és PLC-adattípusát mutatja, alcsoportokkal, az
   exportálhatósággal és az okkal, ha nem exportálható.
3. Könyvtár kiválasztása (vagy létrehozása), blokkok kipipálása, formátum, leírás → *Kipipáltak exportja a könyvtárba*.

A worker `ExportBlocks` művelete csak olvas, és csak **offline** PLC-vel fut (kézikönyv 5.11.3.18, 6.4.1.13): `PlcExternalSourceSystemGroup.GenerateSource` vagy
`PlcBlock/PlcType.Export` egy új, üres mappába, a projektet nem menti (`sandbox_state = Unchanged`).
A főalkalmazás minden exportált fájl hash-ét újraméri, és csak **teljes siker** esetén veszi fel az
elemeket (részleges export: semmi nem kerül a könyvtárba).

## Import: könyvtár → projekt

1. 5. fül: könyvtár, célverzió, PLC-név, elemek kipipálása → *Kipipáltak importjának előkészítése*.
   Ez a munkafolyamatot könyvtár-módba váltja, és lefuttatja az ellenőrzést:
   - könyvtár-integritás, elemek létezése, szabályok (lásd fent), SimaticML verzió-egyezés,
   - ugyanaz a blokk nem szerepelhet két revízióval (`LIBRARY_DUPLICATE_BLOCK`),
   - SCL-forrás ASCII; utána az elem-hash-ek újramérése (CHECKED).
2. 2. fül: *Importcsomag összeállítása* – manifest 1.1 (`package_kind = library_import`), az elemek
   importsorrendben: UDT → FC → FB → globális/tömb DB → példány-DB.
3. 3. fül: minden fájl átnézése az előnézetben, sandbox kiválasztása, jóváhagyás (ugyanaz az
   `ApprovalDigest`, mint a generált forrásoknál).
4. 4. fül: száraz futás (rekurzív ütközésvizsgálat – meglévő azonos nevű blokk esetén `BLOCK_COLLISION`,
   felülírás nincs), majd import + offline fordítás + mentés. SCL-forrás: `CreateFromFile` +
   `GenerateBlocksFromSource`; SimaticML: `Blocks.Import` / `Types.Import` `ImportOptions.None`-nal.

A gépmodell bármely szerkesztése visszaváltja a munkafolyamatot gépmodell-módba.

## Korlátok (MVP)

- Az import a PLC „Program blocks" / „PLC data types" gyökerébe kerül (csoportstruktúra nem öröklődik).
- Függőségek (pl. FB által hívott FC, használt UDT) nem kerülnek automatikusan a választásba. SimaticML
  importnál a hiányzó hivatkozott objektum már az importot megállítja (`IMPORT_FAILED`, kézikönyv 6.4.1.14);
  SCL-forrásnál a célverzió fordítása jelzi (`COMPILE_ERROR`). A projekt egyik esetben sem mentődik.
- XML-importnál a példányspecifikus attribútumok (pl. kezdőértékek) bizonyos esetekben elveszhetnek (kézikönyv 2.1).
- A blokkok a gyökércsoportba kerülnek. A kézikönyv (5.11.3.27) leírja a felhasználói csoportba generálást
  (`GenerateBlocksFromSource(PlcBlockUserGroup, GenerateBlockOption)`); ez későbbi bővítés lehet.
- PLC-tag-táblák nem részei a könyvtárnak.
- A valódi Openness-hívásokat (`GenerateSource`, `Export`, `Import`) még nem igazolta integrációs
  teszt; lásd [COMPATIBILITY.md](COMPATIBILITY.md) és
  `TiaOpennessIntegrationTests.LibraryRoundTripForConfiguredVersion`.
