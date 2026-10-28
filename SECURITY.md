# Biztonsági szabályzat

## Sebezhetőség jelzése

Sebezhetőséget **ne** nyilvános Issue-ban jelezz. Használd a GitHub privát jelzését (Security →
Report a vulnerability), vagy írj közvetlenül a repó tulajdonosának. Kérjük, add meg az érintett
verziót (`build-info.json` / a kezdőlapon látható verzió és commit), a TIA-célverziót, a reprodukálás
lépéseit és a lehetséges hatást. Visszajelzés 5 munkanapon belül; a javítás új PATCH kiadásként jelenik meg.

## Támogatott verziók

Kiadott verzió még nincs (lásd [CHANGELOG.md](CHANGELOG.md)). A `0.x` sorozatban mindig csak a legutóbbi
kiadás kap biztonsági javítást.

## Titokkezelés

- Jelszó, API-kulcs, token, tanúsítvány privát kulcsa, licencfájl és Siemens telepítő/DLL nem kerülhet a
  repóba; a CI minden pushnál és PR-nál titokkeresést futtat (gitleaks, `.gitleaks.toml`).
- Ha mégis bekerült: azonnal érvénytelenítsd (rotáld) a titkot, majd távolítsd el; az előzményből való
  törlés önmagában nem elég.
- A műveleti napló (JSONL) jelszót, tokent vagy kulcsot nem rögzít.
- A TIA-integrációs és kiadási runner licencelt, elkülönített gép: nem megbízható Pull Request nem
  érheti el, a jobok környezeti jóváhagyással futnak (docs/RELEASE_PROCESS.md).

## Biztonsági alapelvek

A program nem tölt le PLC-re, nem kapcsolódik online, nem forszol, és nem generál/módosít safety
(F-) logikát; TIA-projektet csak sandbox-másolatban módosít, jóváhagyás után. Részletek és a
megvalósítás: [docs/SECURITY_RULES.md](docs/SECURITY_RULES.md).
