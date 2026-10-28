# TIA Engineering Studio — projektleírás és fejlesztési specifikáció

**Célrendszerek:** Siemens TIA Portal **V18, V19 és V20**  
**Dokumentumverzió:** 1.0 • 2026-10-28  
**Tervezett alkalmazás:** C# / WPF + Python + Rust + TIA Portal Openness  
**Állapot:** megvalósítási terv, műszaki és fejlesztési követelményrendszer; **nem** annak igazolása, hogy a V18/V19/V20 adapterek már lefordultak vagy integrációs teszten megfeleltek.

> **Vezérelv:** egyetlen verziófüggetlen gépmodellből három különböző TIA-verzióra lehessen előállítani, ellenőrizni és *külön-külön* tesztprojektbe importálni a PLC-forrásokat. A program nem tölthet le PLC-re, nem vezérelhet berendezést és nem módosíthat fail-safe logikát az első kiadásban.

---

## 1. Rövid vezetői összefoglaló

A **TIA Engineering Studio** Windows-alapú mérnöki munkaeszköz. Célja a visszatérő PLC-programozási feladatok automatizálása, különösen célgépek és gyártócellák tervezésénél. Kezeli a gépek eszközlistáját és I/O-kiosztását, szabályok alapján programforrásokat generál, ellenőrzi a bemenetet és az eredményt, majd a megfelelő TIA Portal Openness-adapterrel *offline, másolati projektben* importál és fordít.

A tervezett technológiai feladatmegosztás:

| Rész | Technológia | Fő felelősség |
|---|---|---|
| Desktop | C# + WPF, .NET Framework 4.8 | Kezelőfelület, folyamatvezérlés, jóváhagyás, naplózás |
| Domain / Core | C# | Verziófüggetlen gépmodell, üzleti szabályok, interfészek |
| Generátor | Python 3 | Jól tesztelt SCL-sablonok, forráscsomag és manifest előállítása |
| Validator | Rust, natív x64 DLL | Determinisztikus, nagy adathalmazra is skálázható validáció |
| TIA-integráció | C# .NET Framework 4.8, külön worker folyamat | Openness-projektnyitás, import, fordítás, offline mentés |
| Konfiguráció | JSON + JSON Schema | Gépdefiníció, célverzió, cél PLC, eszközök és I/O |
| Napló és riport | JSONL / JSON / Markdown | Visszakereshető műveletek, fordítási eredmények, SHA-256 |
| Verziókezelés | Git | Forráskód, sablonok és konfigurációk auditálható története |

### 1.1 Kiemelt kompatibilitási döntés

A **TIA Portal V18 Openness API** a három célverzió **közös minimuma**. A Siemens V19 és V20 dokumentációja szerint a korábbi V18-as Openness DLL-re épülő alkalmazások ezekkel a verziókkal is tovább használhatók, mivel a megfelelő hosszú távon támogatott DLL-eket szállítják.

Ennek ellenére a termékhez **külön futtatható worker készüljön V18, V19 és V20 változatban**, közös, Siemens-típusoktól független parancsprotokollal. Elsődlegesen V18-as API-ra épülő *közös funkciókészletet* használunk; későbbi V19/V20-specifikus API-funkciót csak az adott workerben szabad bevezetni.

**Miért jobb a külön worker?** Azonos folyamatban nem kell több eltérő Siemens assembly-verziót kezelni. A verziók eltérő telepítési helyei, konfigurációi, hibái és függőségei elkülöníthetők; egy V19-es hiba nem kényszeríti a V18/V20-ág módosítását.

> **Fontos különbség:** az *Openness API bináris kompatibilitása* nem jelenti azt, hogy egy V20-as `.ap20` TIA-projektet meg lehet nyitni V18-ban, vagy hogy egy V20-ban exportált SimaticML automatikusan importálható V18-ba. Az adattartalom és a projektformátum kompatibilitását külön kell vizsgálni.

---

## 2. Funkcionális hatókör

### 2.1 MVP (első használható kiadás)

1. Gép-projekt létrehozása és mentése JSON-ban.
2. Célverzió megválasztása: **V18 / V19 / V20**.
3. PLC-típus és PLC-programnév megadása (kezdetben S7-1200, S7-1500).
4. Munkahengerekhez és további később hozzáadott eszközosztályokhoz konfiguráció.
5. I/O-címek és eszközazonosítók ellenőrzése; ütközések részletes jelentése.
6. Determinisztikus, sablon alapú `.scl` forrásgenerálás.
7. Minden generált fájlhoz SHA-256 hash és géppel olvasható manifest.
8. Kód-előnézet és manuális jóváhagyás.
9. Telepített TIA-verziók felderítése és célverzió szerinti worker kiválasztása.
10. Openness-kapcsolat a megfelelő TIA Portal példányhoz.
11. Forrásfájlok importálása **csak teljes értékű offline tesztprojekt-másolatba**.
12. PLC-szoftver fordítása, hibák és figyelmeztetések kigyűjtése.
13. Fordítási és importálási eredmény naplózása; sikertelen művelet után tiltott az automatikus továbblépés.

### 2.2 Későbbi kiadások

- Paraméterezett `FB_Cylinder`, `FB_Motor`, `FB_Valve`, `FB_Axis`, diagnosztikai FB-k és hozzájuk tartozó UDT-k/DB-k.
- Automatikus tag-táblák és biztonságos I/O-mapping.
- Globális PLC-sablonkönyvtár kezelése, verziózott master copy-k.
- HMI / Unified HMI integráció verziónként **külön capability flag** mellett.
- XML/SimaticML/AML export-import ott, ahol a célverzió és a funkció bizonyítottan támogatja.
- Git-alapú változáskövetés és generált projektek összevetése.
- AI-alapú *javaslatadó* asszisztens, kizárólag szabályellenőrzés és emberi felülvizsgálat után használható eredménnyel.

### 2.3 Kifejezetten kizárt funkciók az MVP-ben

- PLC-re letöltés, online csatlakozás a fizikai PLC-hez, RUN/STOP vezérlés, forcing.
- Safety/F-program generálása, módosítása vagy automatikus jóváhagyása.
- Éles TIA-projekt felülírása vagy engedély nélküli migrálása.
- Közvetlen Python → Siemens API és Rust → Siemens API kapcsolat.
- AI-eredmény ellenőrizetlen automatikus importja.
- Nem létező modulok vagy hardverazonosítók kitalálása.

---

## 3. TIA V18–V20 kompatibilitási követelmények

### 3.1 Célverziók és hivatkozások

| Kiválasztott TIA-verzió | Projekt kiterjesztése | Natív Openness DLL-hivatkozás | .NET cél | Elvárt integráció |
|---|---|---|---|---|
| V18 | `.ap18` | `Portal V18\PublicAPI\V18\Siemens.Engineering.dll` | .NET Framework 4.8 | V18 worker, saját V18 tesztprojekt |
| V19 | `.ap19` | `Portal V19\PublicAPI\V19\Siemens.Engineering.dll` | .NET Framework 4.8 | V19 worker, saját V19 tesztprojekt |
| V20 | `.ap20` | `Portal V20\PublicAPI\V20\Siemens.Engineering.dll` | .NET Framework 4.8 | V20 worker, saját V20 tesztprojekt |

Ezek **tipikus telepítési útvonalak**, nem beégetett feltételezések. A tényleges DLL-útvonalat az adott gép **Openness registry**-bejegyzéséből kell felismerni és ellenőrizni. Ha a megfelelő DLL vagy TIA-installáció hiányzik, a program felhasználóbarát hibát jelez és *nem* választ önkényesen másik verziót.

**A V21 API nem használható helyettesítésként.** A V21-ben a Siemens átalakította az assembly-felosztást; a korábbi MVP `Siemens.Engineering.Base.dll` / `Siemens.Engineering.Step7.dll` referenciái V18–V20 alatt nem megfelelőek.

### 3.2 Két kompatibilitási szint

**A. Közös V18 API-szint (MVP):** csak olyan szolgáltatásokat használunk, amelyeket a V18 dokumentációja ismertet, és amelyeket V19, V20 alatt is külön tesztelünk. Példák: projektmegnyitás, PLC-szoftver keresése, SCL-forrás importja, blokk generálása, offline fordítás, projektmentés.

**B. Verzióspecifikus funkciók (későbbi):** ha V19/V20 különleges képessége kell, a natív V19/V20 worker saját implementációt tartalmaz. A közös szerződés jelezze a `capabilities` mezőben, hogy az adott szolgáltatás elérhető-e. Hiányzó képesség esetén legyen **explicit „nem támogatott” eredmény**, ne csendes helyettesítés.

### 3.3 A projektfájlok kezelése

- A `.ap18` **V18-as TIA-projekt**, a `.ap19` **V19-es**, a `.ap20` **V20-as** projekt.
- Egy újabb verzió bizonyos régebbi projekteket *migrációval* megnyithat, de az eredményt az eredeti régi verzió általában már nem tudja megnyitni.
- A **projektverzió-frissítés külön, jóváhagyott, mentett másolaton végzett folyamat**; soha nem legyen az importálás mellékhatása.
- Az Openness `Projects.OpenWithUpgrade` funkciót csak külön `UpgradeProject` műveletben szabad hívni, **nem** a normál `ImportSources` során.
- Az importáló worker a TIA-verziót és a projekt `.apXX` kiterjesztését egyezteti; eltéréskor leáll.
- Azonos gépdefinícióból V18/V19/V20 esetén **három külön verziójú célprojekt** szükséges.
- A V19/V20 által exportált SimaticML nem minősül automatikusan V18-kompatibilisnek: V18-ba irányuló importhoz V18-ban előállított/ellenőrzött forrás vagy igazoltan támogatott régebbi SimaticML kell.
- A projekt eredeti TIA-verzióját a `manifest.json` és a napló is tartalmazza.

### 3.4 Telepített verziók felderítése

A Siemens dokumentációja szerint az Openness az alábbi registry-ágban teszi elérhetővé a telepített verziók és DLL-ek adatait:

```text
HKEY_LOCAL_MACHINE\SOFTWARE\Siemens\Automation\Openness\<telepített_verzió>\PublicAPI\...
```

A bejegyzések többek között megadhatják a `Siemens.Engineering` fájl teljes elérési útját, az assembly verzióját és a public key tokent. A **verziófelderítés** teendői:

1. A megfelelő registry-ágak bejárása olvasási jogosultsággal.
2. V18/V19/V20 telepítések azonosítása (a tényleges kulcs- és értékszerkezet figyelembevételével).
3. Openness DLL elérhetőségének és verziójának ellenőrzése.
4. A tényleges TIA telepítés és előfeltételek vizsgálata.
5. Az eredmények megjelenítése a GUI-ban: `Installed / Not found / Unavailable / Not tested`.
6. Kiválasztott verzióhoz csak a hozzá tartozó worker és projektútvonal használható.

**Ne** keverjük a `Siemens.Engineering.dll` különböző verzióit egyazon folyamatban. A worker saját `app.config`/assembly-feloldási mechanizmust kap; javasolt a Siemens által leírt, **strong name ellenőrzést megőrző `app.config`** használata. A `codeBase` hivatkozásokat és verziókat az adott telepítés alapján kell konfigurálni.

### 3.5 Licenc és jogosultság

- Telepített STEP 7/TIA Portal és Openness-összetevő szükséges.
- A futtató Windows-felhasználónak a helyi **`Siemens TIA Openness`** csoport megfelelő tagjának kell lennie.
- Csoporttagság változtatása után új bejelentkezés lehet szükséges.
- A Siemens Openness hozzáférési párbeszédét a felhasználó kezeli; jóváhagyást nem szabad megkerülni.
- V18-tól a Siemens dokumentációja explicit licencellenőrzést ír le; hiányzó licenc esetén a worker az adott hibát hibakóddal és érthető szöveggel jelzi.
- A TIA által megkövetelt technológiai csomagokat, hardvertámogatásokat, opciókat és HSP-ket az adott projektverzióhoz telepíteni kell.
- UMAC-védett projekteknél a szerepkör és a `Modify project via Openness API` jog ellenőrzendő.

---

## 4. Logikai architektúra

```text
┌────────────────────────────────────────────────────────────────────┐
│                     TIA Engineering Studio Desktop                 │
│                      C# WPF / .NET Framework 4.8                   │
│       Gépmodell • I/O táblák • Előnézet • Jóváhagyás • Napló        │
└──────────────────────────┬─────────────────────────────────────────┘
                           │ DTO / Application Core
           ┌───────────────┼─────────────────────┐
           │               │                     │
           ▼               ▼                     ▼
    Python generator  Rust validator      TIA Adapter Manager (C#)
    külön folyamat    natív DLL           verzió-kiválasztás, IPC
    JSON → SCL        P/Invoke            │
                                          ├─ TiaWorker.V18.exe → V18 Openness
                                          ├─ TiaWorker.V19.exe → V19 Openness
                                          └─ TiaWorker.V20.exe → V20 Openness
                                                     │
                                          Offline sandbox .ap18/.ap19/.ap20
```

### 4.1 Rétegek és függőségi szabályok

1. **Domain**: tiszta C# modellek, nincs Siemens-, WPF-, Python- vagy Rust-függősége.
2. **Application**: munkafolyamat-állapotgép, jogosultságok, fájlszabályok, port interfészek.
3. **Infrastructure**: Python-folyamat, Rust DLL, állománytárolás, registry-verziófelderítés, IPC.
4. **Desktop**: felhasználói felület; csak Application interfészeken keresztül dolgozik.
5. **Openness Workers**: Siemens API-kat ismerő külön futtatható folyamatok, **nem hivatkozhatnak a WPF projektre**.
6. **Shared Contracts**: csak általános JSON/DTO adatok; ne legyen benne `Siemens.Engineering.*` típus.

### 4.2 Miért külön folyamatban fut az Openness?

- A Windows-folyamatonként elkülönülnek az assembly-terhelések.
- Könnyebb a megfelelő TIA-telepítés kiválasztása és a folyamat leállításának kezelése.
- Meghibásodott vagy elakadt Openness-folyamat nem fagyaszthatja végleg a felületet.
- A worker által látott fájlrendszer-jogosultságok és paraméterek korlátozhatók.
- A bővítések a közös JSON szerződés megtartásával külön fejleszthetők.

---

## 5. Ajánlott projektszerkezet

```text
TIA-Engineering-Studio/
│
├─ TIA-Engineering-Studio.sln
├─ README.md
├─ TIA_Engineering_Studio_TIA_V18_V19_V20_Projektleiras_HU.md
├─ .gitignore
│
├─ src/
│  ├─ TiaStudio.Domain/                  # Siemens-független modellek
│  ├─ TiaStudio.Application/             # Use case-ek, workflow, interfészek
│  ├─ TiaStudio.Contracts/               # JSON DTO-k, hibakódok, manifest
│  ├─ TiaStudio.Infrastructure/          # Python/Rust/registry/IPC kapcsolatok
│  ├─ TiaStudio.Desktop/                 # WPF GUI (net48, x64)
│  ├─ TiaStudio.TiaAdapter.Common/       # Common worker logika, Siemens típustól mentes
│  ├─ TiaStudio.TiaAdapter.V18/          # net48 + V18 Openness
│  ├─ TiaStudio.TiaAdapter.V19/          # net48 + V19 Openness
│  └─ TiaStudio.TiaAdapter.V20/          # net48 + V20 Openness
│
├─ contracts/
│  ├─ machine-project.schema.json       # verziózott gépmodell
│  ├─ tia-command.schema.json           # worker parancs
│  ├─ tia-result.schema.json            # worker eredmény
│  └─ generation-manifest.schema.json
│
├─ python/
│  ├─ generator/
│  │  ├─ main.py
│  │  ├─ models.py
│  │  ├─ validation.py
│  │  ├─ render.py
│  │  └─ templates/                    # SCL sablonok, V18-kompatibilis alapok
│  └─ tests/
│
├─ rust/
│  └─ tes_validator/
│     ├─ Cargo.toml
│     └─ src/lib.rs
│
├─ examples/
│  ├─ machine-v18.json
│  ├─ machine-v19.json
│  └─ machine-v20.json
│
├─ tests/
│  ├─ Unit/
│  ├─ Integration/
│  ├─ Contracts/
│  └─ GoldenScl/
│
├─ scripts/
│  ├─ bootstrap.ps1
│  ├─ build-desktop.ps1
│  ├─ build-rust.ps1
│  ├─ build-tia-workers.ps1
│  ├─ test-offline.ps1
│  └─ test-tia-integration.ps1
│
├─ docs/
│  ├─ ARCHITECTURE.md
│  ├─ API_CONTRACTS.md
│  ├─ SECURITY_RULES.md
│  ├─ COMPATIBILITY_MATRIX.md
│  ├─ TIA_V18.md
│  ├─ TIA_V19.md
│  ├─ TIA_V20.md
│  ├─ OPERATIONS.md
│  └─ ROADMAP.md
│
├─ generated/                             # futásidőben keletkezik, nem commitoljuk
└─ sandbox/                               # kizárólag teljes, másolati tesztprojektek
```

**Megjegyzés:** ez a **célstruktúra**. A korábbi V21-es MVP ZIP jelenleg ennél egyszerűbb; a fenti fájlok nem mind találhatók meg benne. A 12. fejezet részletezi az átalakítást.

---

## 6. Adatmodell és szerződések

### 6.1 Verziófüggetlen forrásadat

A gépkonfiguráció legyen önálló mérnöki adat, ne Siemens-projektből származó, verziófüggő XML. Célszerű különválasztani az **eszközdefiníciót** és az **exportcélt**. Az MVP-ben a korábbi `target.tia_version` megőrizhető, de csak `V18`, `V19`, `V20` érték engedélyezett.

**Példa `examples/machine-v18.json`:**

```json
{
  "schema_version": "1.1",
  "project_name": "Demo_Assembly_Cell",
  "target": {
    "tia_version": "V18",
    "plc_name": "PLC_1",
    "cpu_family": "S7-1500"
  },
  "devices": [
    {
      "id": "CYL_01",
      "kind": "cylinder",
      "inputs": { "retracted": "I0.0", "extended": "I0.1" }
    },
    {
      "id": "CYL_02",
      "kind": "cylinder",
      "inputs": { "retracted": "I0.2", "extended": "I0.3" }
    }
  ]
}
```

A `schema_version: 1.1` **javasolt új szerződés** a többverziós kiterjesztéshez; a jelenlegi ZIP-ben szereplő **1.0-s séma ezt még nem támogatja**. Módosításkor migrációs teszt szükséges, és a régi JSON-formátum támogatásáról tudatos döntés kell.

### 6.2 Sémaszabályok

- `target.tia_version ∈ {"V18", "V19", "V20"}`.
- `target.plc_name` ne legyen üres, azonosítása egyértelmű legyen a TIA-projektben.
- Minden `device.id` egyedi (kis- és nagybetű kezelésére egységes szabály).
- Csatornacím ne legyen véletlenül újra felhasználva. Ha alias funkció szükséges, azt külön engedélyezett szabályként kell modellezni.
- I/O-címtartomány és CPU-hardware feltételei validálandók, nem elég a `I0.0` szintaktikai ellenőrzése.
- A generált azonosítók megfeleljenek a kiválasztott PLC és TIA közös minimumának.
- Forrásadatok és sablonok a futás végéig változatlanok legyenek; hash változásakor új ellenőrzés és jóváhagyás szükséges.
- `null`, hiányzó mező, ismeretlen kulcs, nem támogatott eszközfajta, nulla eszköz és mérettúllépés hibával térjen vissza.

### 6.3 Worker-parancs protokoll

**Példa:** `contracts/tia-command.schema.json` által leírt, UTF-8-ban tárolt parancs:

```json
{
  "protocol_version": "1.0",
  "job_id": "b9f4a91e-8d8e-4f55-9f99-27af49000528",
  "operation": "ImportAndCompile",
  "tia_version": "V19",
  "project_path": "D:\\TIA_Sandbox\\V19\\Demo\\Demo.ap19",
  "sources_directory": "D:\\TIA_Output\\run-001\\sources",
  "manifest_path": "D:\\TIA_Output\\run-001\\manifest.json",
  "dry_run": true,
  "overwrite_policy": "Deny",
  "approved_by_user": false
}
```

**Kötelező korlátok:** a `dry_run=true` nem módosíthat Siemens-projektet; az `ImportAndCompile` ténylegesen módosító ága csak külön, ember által jóváhagyott műveletben futhat. Az `approved_by_user` mező önmagában **nem biztonsági jogosultság**: a főalkalmazásnak a konkrét projektútvonalat, az összes forráshasht és a futtatási kérelmet is a jóváhagyáshoz kell kötnie.

### 6.4 Worker-válasz protokoll

```json
{
  "protocol_version": "1.0",
  "job_id": "b9f4a91e-8d8e-4f55-9f99-27af49000528",
  "requested_version": "V19",
  "actual_version": "V19",
  "operation": "ImportAndCompile",
  "status": "ValidationOnly",
  "error_count": 0,
  "warning_count": 0,
  "messages": [],
  "saved_project": false
}
```

Állapotok: `ValidationOnly`, `Success`, `Blocked`, `Failed`, `Cancelled`, `Unsupported`. Ezen felül stabil hibakódok legyenek (például `TIA_NOT_INSTALLED`, `API_DLL_MISMATCH`, `PROJECT_VERSION_MISMATCH`, `PROJECT_NOT_SANDBOX`, `HASH_MISMATCH`, `BLOCK_COLLISION`, `COMPILE_ERROR`, `LICENSE_MISSING`).

### 6.5 Manifest

A generálási manifest minimum tartalmazza:

```text
manifest_version, generator_version, schema_version,
target_tia_version, plc_name, template_set_version,
run_id, generated_at_utc, source_config_sha256,
files: [relative_path, sha256, byte_length],
validation_result, safety_scope, download_allowed=false
```

A manifest-fájlok relatív elérési útjai ne szökjenek ki a generált csomag gyökeréből (`..`, abszolút útvonal, symlink/útvonaltrükkök tiltása). Import előtt minden SHA-256-ot újra kell számolni.

---

## 7. Generálási és ellenőrzési munkafolyamat

```text
DRAFT
  ↓ JSON-séma és adatmodell ellenőrzés
VALIDATED
  ↓ Rust statikus/ütközésvalidáció
CHECKED
  ↓ Python, verziófüggetlen vagy explicit célverziós sablon
GENERATED
  ↓ SCL-források + SHA-256 manifest, előnézet
REVIEWED
  ↓ felhasználói jóváhagyás
APPROVED
  ↓ célverzió ellenőrzése + offline sandbox validáció
READY_TO_IMPORT
  ↓ verzióspecifikus Openness worker: import
IMPORTED
  ↓ offline fordítás + diagnosztika kiértékelése
COMPILED
  ↓ kizárólag sikeres és jóváhagyott folyamatban mentés
SAVED
```

Bármely validációs, import- vagy fordítási hiba → `FAILED` állapot, az automatikus továbblépés **tiltott**. Hibás vagy részlegesen módosított sandbox esetén a kiinduló, ismert jó teljes projektmásolatot újra elő kell állítani; a rendszer nem állíthatja, hogy minden Siemens-művelet tranzakciósan visszafordítható.

### 7.1 Az SCL-generálás követelményei

1. **Determinista:** azonos konfiguráció + sablonverzió → azonos forrásbyteok.
2. **Célverzió:** a közös SCL nyelvi részhalmaz V18-ban is forduljon.
3. **Névütközés:** a generált FB/FC/DB/UDT ne írjon felül semmit külön jóváhagyott diff nélkül.
4. **Kódkomment:** mérnöki jelentés, egység és esetleges veszélyforrás dokumentált.
5. **Sablon-szabály:** a mozgás-/biztonságkritikus logika csak ellenőrzött, verziókezelt könyvtárból származzon.
6. **AI:** kizárólag javasolhat; sablon és statikus szabályok nem kerülhetők meg.
7. **Fordítás:** SCL-forrás sikeres létrehozása **nem egyenlő** sikeres TIA PLC-fordítással.

### 7.2 PLC-modulstratégia

Több azonos munkahengerhez **elsősorban egy közös, paraméterezett FB + külön példányadat** célszerű; nem 50 független, kézzel nehezen karbantartható FB-kódot kell legyártani. Az egyedi beállításokat konfiguráció és DB/instance adatok hordozzák. A feladatkörök különválasztandók:

- diagnosztika (például két végállás egyszerre aktív),
- működtetés, módválasztás, reteszek,
- magasabb szintű állapot-/szekvenciavezérlés,
- biztonsági funkciók (az MVP-ben **nem generálható**).

### 7.3 Generált források TIA-importja

A V18 Siemens-kézikönyv a `PlcExternalSource.GenerateBlocksFromSource()` műveletet ismerteti. **Az import meglévő blokkokat felülírhat.** Ezért előbb a teljes PLC blokkstruktúrában (alcsoportokkal együtt) kell ütközéseket keresni, majd csak jóváhagyott sandboxban lehet importálni. A külső források kezelésénél az adott API-ban megadott kódolási/formátumkorlátokat követni kell (V18 dokumentáció: ASCII external source). A változatfüggetlen „közös” SCL-t a V18/V19/V20 fordítási tesztekkel kell bizonyítani.

**Offline fordítás:** a V18 dokumentáció szerint a PLC-nek offline állapotban kell lennie; a `ICompilable` szolgáltatás eredményét és a rekurzív diagnosztikai üzeneteket naplózni kell. A `CompilerResult` üzeneteinek sorrendjére vagy mélységére nem szabad stabil logikát építeni.

---

## 8. C# és Openness implementációs útmutató

### 8.1 Fejlesztői környezet

- Windows x64, a telepített TIA-verzió(k) által támogatott Windows kiadás.
- Visual Studio 2022, **.NET desktop development** és **.NET Framework 4.8 Developer Pack**.
- A TIA Portal V18/V19/V20 közül legalább a tesztelt célverzió telepítve.
- Openness-összetevő és a funkcióhoz szükséges Siemens licencek.
- Python 3 és Rust stable toolchain (`x86_64-pc-windows-msvc`).

### 8.2 V18 worker `.csproj` minta

**A következő projektfájl egy javasolt indítókonfiguráció, nem készre tesztelt adapter.** A telepítési útvonal a tényleges Windows-környezethez állítandó.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <LangVersion>latest</LangVersion>
    <AssemblyName>TiaStudio.TiaAdapter.V18</AssemblyName>
    <TiaApiPath Condition="'$(TiaApiPath)' == ''">C:\Program Files\Siemens\Automation\Portal V18\PublicAPI\V18</TiaApiPath>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Siemens.Engineering">
      <HintPath>$(TiaApiPath)\Siemens.Engineering.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
</Project>
```

A V19 és V20 projektek hasonlóak, de **saját telepítési útvonalat és saját Siemens DLL-referenciát** használnak. Ha HMI-funkció bevezetésre kerül, az adott verzió telepítéséből származó HMI assembly-ket is külön kell kezelni.

### 8.3 Alapvető Openness-használat, V18 API

Az alábbi kód *minimalista kapcsolatpróba*, nem teljes importáló alkalmazás:

```csharp
using System;
using Siemens.Engineering;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            using (var tia = new TiaPortal(TiaPortalMode.WithUserInterface))
            {
                Console.WriteLine("TIA Portal started through Openness.");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }
}
```

A futtatáshoz az assembly-ket a Siemens V18 dokumentációjában leírt `app.config`/`codeBase` módon kell feloldani (vagy gondosan megvalósított `AssemblyResolve` segítségével). **Ne másold és ne terjeszd** a Siemens DLL-eket az alkalmazás saját csomagjában. Az assembly-mismatch-et a program indulás előtt, lehetőleg telepítésellenőrzéssel jelezze.

### 8.4 Worker interfész és folyamatkezelés

**Javasolt végrehajtási minta:**

```powershell
TiaStudio.TiaAdapter.V18.exe --command D:\TIA_Output\job-v18.json
TiaStudio.TiaAdapter.V19.exe --command D:\TIA_Output\job-v19.json
TiaStudio.TiaAdapter.V20.exe --command D:\TIA_Output\job-v20.json
```

A C# főprogram `ProcessStartInfo` használatával indítja a megfelelő EXE-t, külön kezeli az `exit code`-ot, a strukturált JSON-eredményt és a naplót. Timeout esetén nem szabad úgy naplózni, hogy a Siemens oldali művelet biztosan visszaállt; a sandbox állapota `UNKNOWN` és további vizsgálat szükséges.

**Végrehajtási sorrend egy workerben:**

1. Verzió, DLL, licenc és jogosultság ellenőrzése.
2. Bemeneti JSON séma és relatív/abszolút elérési utak ellenőrzése.
3. Projektkiterjesztés és sandbox-útvonal ellenőrzése.
4. SHA-256 manifest és forrásfájlok egyeztetése.
5. Meglévő blokknevek ellenőrzése rekurzívan.
6. Openness-kapcsolat létrehozása; célszoftver keresése.
7. Szárazfutás / import terv megjelenítése.
8. Valódi import kizárólag jóváhagyás után.
9. Offline fordítás; diagnosztikai üzenetek rekurzív feldolgozása.
10. Siker esetén projektmentés; minden esetben részletes napló és erőforrás-felszabadítás.

### 8.5 Javasolt C# interfész

```csharp
public interface ITiaWorkerClient
{
    TiaInstallInfo[] DiscoverInstalledVersions();
    TiaCapabilities GetCapabilities(string tiaVersion);
    TiaJobResult ValidateJob(TiaJobRequest request);
    TiaJobResult ImportAndCompile(TiaJobRequest request);
}
```

A `TiaInstallInfo`, `TiaCapabilities`, `TiaJobRequest`, `TiaJobResult` saját DTO legyen; **tilos** Siemens API-típust ilyen interfészen átszivárogtatni.

---

## 9. Python és Rust modulok

### 9.1 Python-generátor

- Külön folyamat, nincs Siemens API-függősége.
- Bemenet: JSON gépkonfiguráció és sablonverzió.
- Kimenet: csak új, ellenőrzött munkamappába generált `.scl`, `manifest.json`, validációs riport.
- UTF-8 bemeneti JSON, a Siemens V18 külső forrás követelményeinek megfelelő, ellenőrzött ASCII SCL kimenet az MVP-ben.
- A standard output géppel feldolgozható legyen; logok és hibaüzenetek külön csatornára kerüljenek.
- Nincs automatikus fájlfelülírás; atomikus publikálás staging → célmappa.
- Külön tesztek: hibás/hiányzó kulcs, ütközés, non-ASCII név, fájlútvonal-kilépés, ismételhetőség, legrosszabb esetű eszközszám.

**Futtatási példa:**

```powershell
cd python
python -m unittest discover -s tests -v
python -m generator.main --input ..\examples\machine-v18.json --output ..\generated\v18-demo
```

### 9.2 Rust validátor

- `extern "C"` API, verziózott FFI szerződés.
- C# oldalról `DllImport`/P/Invoke.
- Szabályos pointer- és bufferkezelés; Rust-pánik nem léphet át FFI-határon.
- Funkciók: azonosító-ellenőrzés, duplikált I/O, egyszerű típustartományok, később erőforrás-korlátok és adatkonzisztencia.
- Rust DLL csak a validációra kap adatot; fájlrendszer/TIA-módosításra nem jogosult.

**Futtatási példa:**

```powershell
cd rust\tes_validator
cargo test
cargo build --release
```

**Telepítési feltétel:** a natív DLL és a WPF processz egyező architektúrájú (x64) legyen. A szükséges natív függőségeket és a Windows VC++ futtatókörnyezetet a kiadási csomag ellenőrizze.

---

## 10. Biztonsági, minőségi és kódolási szabályok

### 10.1 Kötelező szabályok

1. Az alkalmazás **nem módosíthat éles TIA-projektet**; csak teljes, önálló sandbox-másolatot.
2. `.ap18`, `.ap19`, `.ap20` kiterjesztés és tényleges TIA-példány verziója egyezzen.
3. Projektfrissítés külön `UpgradeProject` művelet: külön másolat + figyelmeztetés + megerősítés.
4. Import előtt teljes rekurzív blokkütközés-ellenőrzés, felülírás alapból tiltott.
5. PLC minden fordítás és forrásgenerálás során **offline** legyen.
6. Projektszerkesztés előtt ellenőrzött backup/archívum és visszaállítási eljárás szükséges.
7. A fájlhasheket jóváhagyáskor és import előtt ellenőrizni kell.
8. PLC-download, online vezérlés, forcing és safety-módosítás nem implementálható az MVP-ben.
9. Hiba esetén az eredmény és a sandbox állapota explicit legyen; részleges siker nem számít sikernek.
10. Naplóban bizalmas gyártási adat, hozzáférési token vagy jelszó nem szerepelhet.
11. A Siemens DLL-ek nem kerülnek Git-be és nem csomagolhatók továbbterjesztésre.
12. Minden hozzáadott képességhez automatizált teszt és verziónkénti integrációs teszt szükséges.
13. Generált kódot embernek kell ellenőriznie a gép dokumentációja és kockázatértékelése alapján.
14. A TIA-változatot soha ne találgassa a program; hiányzó telepítésnél megáll.
15. A Python, Rust, C# és a Siemens oldali hibakódok közös hibatipológiába kerüljenek.

### 10.2 Elnevezési és repository-szabályok

- Namespaces: `TiaStudio.<Layer>`.
- Projektek és osztályok: `PascalCase`, helyi változók: `camelCase`.
- PLC modulok: `FB_<Function>`, `FC_<Function>`, `DB_<Function>_<Instance>`, `UDT_<Function>`.
- Berendezésazonosítók: `CYL_01`, `MTR_01`, `AX_X01`.
- Fájlútvonalak Unicode-ok lehetnek, de a PLC-szimbólumok a közös szabályrendszert követik.
- Egy commit egy értelmes módosítás, célzott tesztekkel.
- A repository-ba nem kerülnek `bin/`, `obj/`, `target/`, `.vs/`, `generated/`, éles `.ap*` projektek, kulcsok vagy jelszavak.
- Nyilvános interfész megváltoztatása esetén protokoll/séma-verzió növelés és migrációs teszt kötelező.

### 10.3 Kódellenőrzési minimum

- C#: warningok ellenőrzése, `IDisposable` objektumok életciklusa, aszinkron UI-folyamatok kezelése.
- Python: unit tesztek, determinisztikus golden-file ellenőrzés, típus- és mezőellenőrzés.
- Rust: `cargo test`, `cargo clippy` (ha telepített), FFI boundary-tesztek.
- Siemens adapter: compile/report strukturált kiértékelése, ütközésvizsgálat, projektfájl-verzió ellenőrzés.
- Dokumentáció: minden API-eltérés legyen rögzítve a `COMPATIBILITY_MATRIX.md` fájlban.

---

## 11. Fejlesztési és tesztelési terv

### 11.1 Mérföldkövek

| Fázis | Feladat | Kimenet / elfogadási feltétel |
|---|---|---|
| 0 | V21-es MVP átvilágítása | A V21-hivatkozások és beégetett verziók teljes listája |
| 1 | JSON-séma v1.1 | V18/V19/V20 mezőellenőrzés, unit tesztekkel |
| 2 | Projektstrukturálás | Közös Domain, Application, Contracts, 3 worker projekt |
| 3 | Registry-verziófelderítés | Telepített verziók és hiányok megbízható felismerése |
| 4 | Python/Rust integráció | TIA nélküli validáció és generálás sikeres |
| 5 | V18 worker | V18 sandboxba SCL import + offline fordítás |
| 6 | V19 worker | V19 sandboxba ugyanez, regresszió nélkül |
| 7 | V20 worker | V20 sandboxba ugyanez, regresszió nélkül |
| 8 | Verzióváltó GUI + jóváhagyás | Hibás kombinációk tiltása, érthető visszajelzés |
| 9 | Automatizált teszt és release | Megismételhető építés, három verzióban igazolt működés |

### 11.2 Kompatibilitási tesztmátrix

| Teszt | V18 | V19 | V20 |
|---|---|---|---|
| Telepítés / registry-felderítés | Kötelező | Kötelező | Kötelező |
| API DLL helyes betöltése | Kötelező | Kötelező | Kötelező |
| Üres sandbox projekt megnyitása | Kötelező | Kötelező | Kötelező |
| Választott PLC felismerése | Kötelező | Kötelező | Kötelező |
| SCL külső forrás importálása | Kötelező | Kötelező | Kötelező |
| Generált blokk fordítása | Kötelező | Kötelező | Kötelező |
| Projektnév/blokkütközés elutasítása | Kötelező | Kötelező | Kötelező |
| Hibás forráshash elutasítása | Kötelező | Kötelező | Kötelező |
| Verzióhibás `.apXX` elutasítása | Kötelező | Kötelező | Kötelező |
| PLC-letöltés hiánya | Kötelező | Kötelező | Kötelező |
| Hibából napló + kontrollált leállás | Kötelező | Kötelező | Kötelező |

**Aktuális állapot a dokumentum készítésekor:** a korábbi MVP leírása szerint a Python-ág tesztelt; a V18/V19/V20 Openness munkafolyamatokhoz még **nincs helyi Siemens rendszerben igazolt integrációs teszteredmény**. A táblázat a **követelményt** tartalmazza, nem a már teljesített tesztek állapotát.

### 11.3 Tesztelési forgatókönyvek

1. *Normál*: V18 konfiguráció → SCL → V18 `.ap18` sandbox → `Success` és mentett projekt.
2. *Normál*: ugyanez V19 `.ap19` és V20 `.ap20` esetén.
3. *Negatív*: kiválasztott `V18`, de `.ap20` útvonal → `PROJECT_VERSION_MISMATCH`.
4. *Negatív*: TIA V19 nincs telepítve → `TIA_NOT_INSTALLED`.
5. *Negatív*: lejárt vagy hiányzó funkciólicenc → `LICENSE_MISSING`.
6. *Negatív*: blokknév már létezik nested group-ban → `BLOCK_COLLISION`.
7. *Negatív*: forrásfájl megváltozik jóváhagyás után → `HASH_MISMATCH`.
8. *Negatív*: újabb projekt régebbi verzióban → automatikus upgrade/degrade nélkül elutasítás.
9. *Hiba*: fordítás hibaüzenetet ad → `COMPILE_ERROR`, nincs automatikus projektmentés.
10. *Hiba*: megszakított Openness-folyamat → `UNKNOWN` sandbox státusz, kézi ellenőrzés.
11. *Biztonság*: nincs elérhető vagy meghívható PLC download/force parancs a GUI-ban és protokollban.
12. *Regresszió*: azonos input/sablon ugyanazt az SCL-hasht állítja elő minden megismételt futásban.

### 11.4 Release-feltételek

**A szoftver csak akkor jelölhető V18/V19/V20-kompatibilisnek, ha a fenti tesztek mindhárom verzióra dokumentáltan sikeresek,** a Windows/Visual Studio build reprodukálható, és az elkülönített sandboxokban kézzel is ellenőrizték a létrejött PLC-objektumokat.

---

## 12. A korábban elkészített V21 MVP átalakítása

Ez a dokumentum az előzőleg összeállított `TIA-Engineering-Studio-MVP.zip` csomagra is épít, de **nem állítja**, hogy a ZIP automatikusan kompatibilissé vált. A következő változtatásokat a forráskódban ténylegesen végre kell hajtani.

### 12.1 Kötelező módosítási lista

| Korábbi fájl / logika | Változtatás |
|---|---|
| `src/TiaAdapter.V21/` | Három új projekt: `TiaAdapter.V18`, `.V19`, `.V20`. A V21 munkaváltozat archiválható, de nem része a célrelease-nek. |
| `TiaAdapter.V21/*.csproj` | A `Siemens.Engineering.Base.dll`/`Step7.dll` referenciák helyett az adott V18/V19/V20 telepítés `Siemens.Engineering.dll` referenciája. |
| `TiaAdapter.V21/Program.cs` | V21 `AssemblyResolve`, `TIA_V21_API_PATH` és útvonalak helyett verziónkénti `app.config` és ellenőrzött verziófelderítés. |
| `TiaAdapter.V21/TiaImportJob.cs` | V21 típus/útvonal/kiterjesztés elhagyása; átvezetés közös jobprotokollra, majd V18/19/20 adapterlogikára; rekurzív ütközésvizsgálat. |
| `src/Desktop/MainWindow.xaml.cs` | `V21` ellenőrzés cseréje V18/V19/V20-ra, `.ap18/.ap19/.ap20` fájlválasztó, worker-diszpécser. |
| `src/Desktop/Models.cs` | `TiaVersion` elfogadási kör bővítése; új manifest/worker DTO-k.
| `contracts/machine-project.schema.json` | `"const": "V21"` helyett `"enum": ["V18", "V19", "V20"]`; séma verziózásának megtervezése. |
| `examples/sample-machine.json` | Három új minta V18/V19/V20 célértékkel. |
| `python/generator/main.py` | A V21-re vonatkozó ellenőrzés bővítése; generált artefaktumon a célverzió feltüntetése; közös kompatibilis SCL. |
| `docs/TIA_V21_INTEGRATION.md` | Leváltás három verzióspecifikus leírással és kompatibilitási mátrixszal. |
| `README.md`, `FEJLESZTESI_UTMUTATO_HU.md` | Frissítés: V18–V20 fejlesztés, fordítás, telepítés, tesztelés. |
| `scripts/*` | Verzióspecifikus build / teszt, DLL-felderítés, diagnosztika. |

### 12.2 Külön figyelmeztetések

- **Tilos** a V21-es `Siemens.Engineering.Base.dll` neveinek puszta kicserélése után azt feltételezni, hogy a régi kód lefordul. A V18–V20 API-szerkezetet a V18 kézikönyv és a helyi telepített assembly-k alapján kell újrailleszteni.
- A WPF-ben most **V21-be égetett** ellenőrzések vannak, ezért a verzióválasztó GUI nem puszta kozmetikai módosítás.
- A Python-generátor is visszautasíthatja a nem V21-es bemenetet: ezt a validációs lánc minden érintett pontján módosítani kell.
- A korábbi MVP rekurzív nested blokkütközés-ellenőrzése hiányos; ennek pótlása release-blokkoló követelmény.
- A cél nem egy meglévő V21 TIA-projekt visszaalakítása V18-ra: **a semleges gépmodellből új V18/V19/V20 forrásokat és verzióhelyes projekteket kell képezni**.

---

## 13. Gyakorlati indulási műveletek Windows alatt

### 13.1 Környezetvizsgálat

PowerShellben:

```powershell
python --version
cargo --version
rustc --version

# Telepített TIA Openness ágak (csak ellenőrzés):
Get-ChildItem 'HKLM:\SOFTWARE\Siemens\Automation\Openness' -ErrorAction SilentlyContinue |
  Select-Object PSChildName

# A három tipikus DLL útvonal ellenőrzése:
Test-Path 'C:\Program Files\Siemens\Automation\Portal V18\PublicAPI\V18\Siemens.Engineering.dll'
Test-Path 'C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll'
Test-Path 'C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll'
```

A fenti `Test-Path` csak gyors diagnosztika; a kész alkalmazás **registry-alapon** keresse meg a tényleges telepítést, ne ezekből a fix útvonalakból.

### 13.2 Ajánlott fejlesztési sorrend

1. A régi MVP ZIP kicsomagolása új Git-repository-ba.
2. Új branch: `feature/tia-v18-v20-compatibility`.
3. Sémák és minták módosítása, Python unit tesztek frissítése.
4. A WPF és Siemens-integráció leválasztása saját interfészre.
5. Egyetlen **V18** Openness worker életre keltése helyi Windows/TIA V18 telepítésen.
6. Ugyanazt a generált SCL-t V18 sandboxban fordítani és naplózni.
7. V19 és V20 worker létrehozása; csomagok/DLL-ek külön feloldása.
8. Választó GUI és automatikus verifikáció létrehozása.
9. Három verziós tesztmátrix végrehajtása.
10. Kiadási dokumentáció, visszaállítási eljárás és release-archívum készítése.

### 13.3 Build-terv

```powershell
# Példák a TiaStudio névre átalakított célprojekt esetére:
# (A fájlok és projektek létrehozása után használhatók.)

dotnet build .\src\TiaStudio.Desktop\TiaStudio.Desktop.csproj -c Release

dotnet build .\src\TiaStudio.TiaAdapter.V18\TiaStudio.TiaAdapter.V18.csproj -c Release

dotnet build .\src\TiaStudio.TiaAdapter.V19\TiaStudio.TiaAdapter.V19.csproj -c Release

dotnet build .\src\TiaStudio.TiaAdapter.V20\TiaStudio.TiaAdapter.V20.csproj -c Release
```

A `dotnet build` parancs a .NET Framework 4.8 fejlesztői célcsomaggal és megfelelő Visual Studio/MSBuild környezettel működik; ha a gépen ez nem elérhető, a megfelelő Visual Studio Developer PowerShell/`MSBuild.exe` használata szükséges.

---

## 14. Nyitott kérdések és döntési napló

### Már rögzített döntések

- Három támogatott célverzió: **TIA V18 / V19 / V20**.
- .NET Framework **4.8**, WPF és Windows x64.
- C# orchestrator, Python generátor, Rust natív validátor.
- **Külön Openness worker verziónként**, közös JSON protokollal.
- V18 API a közös funkcionális minimum.
- Offline sandbox import; nincs PLC download, nincs safety módosítás.

### Megvalósítás során ellenőrzendő kérdések

- A tényleges telepített TIA Update/szervizcsomagok és támogatott Windows-verziók.
- A felhasznált CPU-k és firmware-verziók tényleges támogatása V18, V19, V20 alatt.
- Project Software/Program blocks navigáció és importkapcsolatok konkrét API-eltérései.
- HMI/WinCC Unified, Startdrive és más opciócsomagok egyedi verziókövetelményei.
- Az SCL-sablonokból létrejövő összes blokk és instance DB precíz fordíthatósága mindhárom verzióban.
- Több telepített TIA-verzió és megnyitott példány kezelésének pontos ütközései.
- A projektarchiválás és visszaállítás hitelesíthető, operátori módja.

---

## 15. Források és ellenőrzési hivatkozások

### A felhasználó által feltöltött Siemens dokumentáció (elsődleges V18 forrás)

**`TIAPortalOpeness_enUS_en-US.pdf`**, Siemens, *TIA Portal Openness: API for automation of engineering workflows*, 12/2022, 1388 oldal.

- **22–27. oldal:** V18 változások, hosszú távú API-kompatibilitás, stabil kód szabályai és SimaticML verzióviszonyok.
- **31–33. oldal:** .NET Framework 4.8, telepítés, „Siemens TIA Openness” csoport.
- **68–69. oldal:** telepített API-verziók registry-bejegyzései.
- **78–83. oldal:** `app.config`, Siemens assembly-feloldás, Openness-kapcsolódás és engedélyezés.
- **136–138. oldal:** offline fordítás és `ICompilable`.
- **453–456. oldal:** külső forrásból blokkgenerálás, felülírási kockázat, hibaeredmények.

### A V19 és V20 kompatibilitás külső, hivatalos ellenőrzése

1. Siemens, **TIA Portal Openness V19** (11/2023), a V18-as DLL-re épülő alkalmazások V19 alatti továbbfuttathatóságáról:  
   <https://support.industry.siemens.com/cs/attachments/109826886/TIAPortalOpenness_enUS_en-US.pdf>
2. Siemens, **What's new in TIA Portal Openness V20** (01/2025), V17/V18/V19 DLL-ek támogatása V20 alatt:  
   <https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/what-s-new-in-tia-portal-openness>
3. Siemens, **Information about installed TIA Portal Openness versions V20**, registry és assembly-verziók:  
   <https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/general-functions/information-about-installed-tia-portal-openness-versions>
4. Siemens, **Compatibility between TIA Portal versions**, projektfrissítés és visszafelé nem kompatibilis formátumok:  
   <https://docs.tia.siemens.cloud/r/en-us/v20/editing-projects/compatibility-of-projects/compatibility-between-tia-portal-versions>

**Forráskezelési megjegyzés:** az 1.0 dokumentum V18 állításai a feltöltött V18 kézikönyvön, a V19/V20 bővítések az itt felsorolt Siemens dokumentumokon alapulnak. Az architektúra, repository-struktúra, DTO-k, hibakódok és tesztmódszertan **fejlesztési javaslatok**, nem Siemens által előírt egyetlen megoldás.

---

## 16. Elfogadási összegzés

A projekt akkor felel meg a követelménynek, ha egyetlen C# Windows alkalmazásból a felhasználó kiválaszthatja a **V18 / V19 / V20** TIA-verziót, a közös gépmodellből verzióhelyes SCL-forrásokat generálhat, ezeket ellenőrizheti, majd a megfelelő, külön Openness workerrel a **megegyező verziójú offline tesztprojektbe** importálhatja és lefordíthatja. Minden művelet naplózott, ellenőrizhető, hibára leáll, és nem végez online vagy safety beavatkozást.

**Jelenlegi következő konkrét fejlesztési feladat:** a régi, V21-re rögzített MVP-t a 12. fejezet szerint átalakítani, majd **először a V18 workerrel** eljutni egy sikeres, naplózott sandbox-import + offline fordítás tesztig. Csak ezután célszerű a V19/V20 regressziót teljesnek tekinteni.
