<!-- PR címe Conventional Commits formátumban, pl. "feat(generator): add cylinder FB template" (docs/BRANCHING.md) -->

## 1. Kapcsolódó Issue

Closes #

## 2. A változás célja

## 3. Érintett modulok és TIA-verziók

- Modulok: <!-- core / desktop / python / rust / openness / generator / templates / contracts / docs / ci -->
- TIA: <!-- V18 / V19 / V20 / all / nem érint -->

## 4. Futtatott tesztek és eredményük

- [ ] `bash scripts/test-offline.sh` / `.\scripts\test-offline.ps1`
- [ ] `ruff check . && ruff format --check .`, `cargo fmt --check`
- [ ] TIA-integrációs teszt (`tia-integration` workflow vagy `scripts/test-tia-integration.ps1`): <!-- verzió, eredmény, link; "nem futott" is érvényes válasz, de nem "Passed" -->

## 5. Érint-e PLC-blokkot, DB-t, hardverkonfigurációt vagy projektadatot?

<!-- pl. új/változott SCL-sablon, generált blokknevek, import-viselkedés, sandbox-kezelés -->

## 6. Visszafelé nem kompatibilis változás?

- [ ] Nem
- [ ] Igen – leírás:

## 7. Változik-e a projektadatséma vagy a sablonverzió?

- Gépmodell-séma: <!-- nem / 1.1 -> … -->
- Sablonkészlet: <!-- nem / tes-scl-core 1.0.0 -> … -->
- IPC-protokoll / könyvtárformátum: <!-- nem / … -->

## 8. Szükséges-e migráció vagy felhasználói jóváhagyás?

## 9. Kockázatok és visszaállítás

---

- [ ] `CHANGELOG.md` `[Unreleased]` frissítve (felhasználót érintő változásnál)
- [ ] Dokumentáció frissítve (`docs/`), ha viselkedés vagy szerződés változott
- [ ] Siemens-specifikus kód csak a `src/tia-adapters/*` alatt; titok, licenc, ügyfélprojekt nincs a diffben
