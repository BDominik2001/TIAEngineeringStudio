# Kiadási folyamat

Minden tényleges kiadást egy SemVer Git-tag és egy GitHub Release képvisel. Kiadott tag és Release-asset
**nem írható felül**; hibajavításból új PATCH verzió készül.

## Csatornák

| Csatorna | Tag | GitHub Release |
|---|---|---|
| Alpha | `v0.4.0-alpha.1` | pre-release |
| Beta | `v0.4.0-beta.1` | pre-release |
| Release Candidate | `v0.4.0-rc.1` | pre-release |
| Stable | `v0.4.0` | release |

A tag alapverziója (az utótag nélkül, pl. `0.4.0`) egyezzen a `Directory.Build.props` `<Version>`
értékével; az előzetes-kiadás utótag csak `-alpha.N`, `-beta.N` vagy `-rc.N` lehet (N ≥ 1). A csomagnevek,
a `release-manifest.json` és az alkalmazás InformationalVersion-je a **teljes** tag-verziót hordozza
(pl. `0.4.0-beta.1`). Ellenőrzés: `python scripts/tools/release_manifest.py --check-tag v0.4.0-beta.1`.

## Kiadási állományok

| Fájl | Tartalom |
|---|---|
| `TIAStudio_<ver>_TIA18_win-x64.zip` | Desktop + V18 worker (a telepített V18 Openness-szel fordítva) + `build-info.json` |
| `TIAStudio_<ver>_TIA19_win-x64.zip` | ugyanez V19-re |
| `TIAStudio_<ver>_TIA20_win-x64.zip` | ugyanez V20-ra |
| `checksums.sha256` | a csomagok SHA-256-ja |
| `release-manifest.json` | tag, commit, séma-/protokoll-/sablonverzió, csomagonként a kompatibilitási állapot és a TIA-teszt futásának linkje |

A Siemens DLL-ek egyik csomagban sincsenek.

## Kiadási kapu: TIA-integrációs teszt

| Kiadás | V18/V19/V20 TIA-integrációs teszt | Csomag állapota |
|---|---|---|
| **Stable** (`vX.Y.Z`) | **kötelező mindhárom verzióra**; hiányzó `TES_TIA_KNOWN_GOOD_PROJECT_Vxx` változó, ki nem futott teszt vagy hiányzó csomag = a kiadás megáll | „Supported & tested” |
| Előzetes (`-alpha/-beta/-rc`) | ajánlott; teszt nélkül is készülhet | tesztelt: „Supported & tested”; nem tesztelt: **„Untested development build”** – nem nevezhető Openness-kompatibilisnek, a release notes figyelmeztetéssel kezdődik |
| bármely | sikertelen teszt | a kiadás megáll |

A sikeres fordítás önmagában soha nem „tested”. Lefutott teszt esetén a `release-manifest.json`
csomagonként rögzíti a tesztkörnyezetet (`testEnvironment`): Windows-verzió, a használt
`Siemens.Engineering.dll` útvonala, fájl- és termékverziója (TIA Update), a mintaprojekt neve és SHA-256-ja,
a tesztelt commit és a runner neve; a release notes is felsorolja. Tesztelés kizárólag engedélyezett,
elkülönített, licencelt Windows gépen, demó/sandbox projekttel.

## Runnerek és környezetek (egyszeri beállítás)

A kiadási és TIA-integrációs jobok **elkülönített, licencelt TIA-t futtató Windows self-hosted
runneren** futnak, soha nem Pull Requestből:

1. Runner-címkék: `self-hosted`, `windows`, `x64`, és a telepített verzió szerint `tia-V18`, `tia-V19`, `tia-V20`
   (egy gépen több is lehet). A runner felhasználója tagja a „Siemens TIA Openness” csoportnak.
2. Settings → Environments: `release` és `tia-integration`, mindkettő **Required reviewers** védelemmel,
   a `release` csak `v*` tagekre engedélyezve.
3. Settings → Variables: `TES_TIA_KNOWN_GOOD_PROJECT_V18|V19|V20` – ismert jó, üres PLC-t tartalmazó
   demó/sandbox projekt útvonala a runneren (`.ap18/.ap19/.ap20`). Az eredeti nem módosul, a teszt
   friss sandbox-másolaton fut. Ügyfélprojekt nem használható.
4. Settings → Actions → General: „Require approval for all outside collaborators”; self-hosted runner
   publikus repón nem használható.

A CI nem tölt le programot PLC-re, nem indít/állít meg PLC-t, és nem módosít éles gépprojektet.

## Lépések

1. A `main` CI-je zöld; a kiadandó változások PR-on át bekerültek.
2. Kiadás-előkészítő PR (`release/<major.minor>` vagy `docs/release-<ver>` ágon):
   - `Directory.Build.props` `<Version>`, `src/python/generator/__init__.py`, `src/rust/validator/Cargo.toml`
     (és `Cargo.lock`) azonos új verzióra;
   - `CHANGELOG.md`: az `[Unreleased]` szakasz átnevezése `[<ver>] – <dátum>`-ra (újdonságok, hibajavítások,
     séma-/sablonváltozás és migráció, ismert korlátozások), új üres `[Unreleased]`;
   - `docs/COMPATIBILITY.md` friss (a `tia-integration` workflow legutóbbi futásai alapján).
3. Merge után a `main` megfelelő commitjára tag: `git tag -a v<ver> -m "TIA Engineering Studio v<ver>"` és push.
4. A `release` workflow:
   - `verify`: tag-formátum, alapverzió = `Directory.Build.props`, a Release még nem létezik, van
     CHANGELOG-szakasz (előzetes kiadásnál `[X.Y.Z-ch.N]`, `[X.Y.Z]` vagy `[Unreleased]`), offline tesztek;
   - `package` (V18/V19/V20 párhuzamosan, jóváhagyás után): csomag `-RequireOpenness`-szel és a teljes
     tag-verzióval, TIA-integrációs teszt sandboxon a fenti kapu szerint, tesztkörnyezet, eredmény-artifact;
   - `publish`: a kapu újraellenőrzése, `checksums.sha256`, `release-manifest.json`, release notes,
     **piszkozat** Release (előzetes kiadásnál pre-release jelöléssel).
5. A kiadáskezelő a checklist alapján átnézi a piszkozatot, majd publikálja.

A release notes automatikusan tartalmazza: a CHANGELOG-szakaszt (újdonságok, hibajavítások, ismert
korlátozások, séma-/migrációs információ), a kompatibilitási táblát és a ténylegesen tesztelt
TIA-verziókat, a verziókat, a commit SHA-t, az előfeltételeket és az ellenőrzőösszegeket.

## Kiadási checklist

- [ ] Main ág CI-tesztje sikeres
- [ ] V18 integrációs teszt dokumentált (stable: kötelező, a workflow kikényszeríti)
- [ ] V19 integrációs teszt dokumentált (stable: kötelező, a workflow kikényszeríti)
- [ ] V20 integrációs teszt dokumentált (stable: kötelező, a workflow kikényszeríti)
- [ ] Előzetes kiadásnál a nem tesztelt csomagok „Untested development build” jelölése ellenőrizve
- [ ] Kompatibilitási mátrix friss (`docs/COMPATIBILITY.md`)
- [ ] `CHANGELOG.md` friss
- [ ] Sémaverzió és migráció dokumentált
- [ ] PLC-sablonok verziói rögzítettek (`templates/scl/template-set.json`)
- [ ] A build Git commitja rögzített (`release-manifest.json`, `build-info.json`)
- [ ] Titokellenőrzés és függőségvizsgálat sikeres (CI `checks` job)
- [ ] Release csomagok előállítva
- [ ] `checksums.sha256` elkészítve
- [ ] Release notes ellenőrizve
- [ ] Git-tag létrehozva
- [ ] Release publikálva
- [ ] Visszaállítási terv dokumentált (lásd lent)

## Visszaállítás

- Hibás kiadás: a Release-t „pre-release”-re állítjuk vagy a leírásában visszavontnak jelöljük; tag és
  asset nem törlődik és nem íródik felül. A javítás új PATCH verzióként jelenik meg.
- A felhasználó a korábbi verzió csomagjára áll vissza (a mappát lecseréli; a beállítások, a sandboxok
  és a blokk-könyvtárak a telepítési mappán kívül vannak, verziófüggetlenek).
- Séma- vagy könyvtárformátum-változásnál a release notes tartalmazza, hogy a régi verzió olvassa-e az
  új formátumot; ha nem, a visszaállítás előtt a régi formátumú másolatot kell használni.
