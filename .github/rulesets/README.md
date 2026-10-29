# Repository rulesets

A `main` ág és a `v*` kiadási tagek GitHub-oldali védelme (docs/BRANCHING.md, docs/RELEASE_PROCESS.md).
A Gitben leírt szabály önmagában nem akadályozza meg a közvetlen pusht; ezeket a GitHubon kell aktiválni:

Settings → Rules → Rulesets → **New ruleset → Import a ruleset**, majd fájlonként:

| Fájl | Hatás |
|---|---|
| `main.json` | `main`: csak Pull Requesttel, kötelező CI-ellenőrzések (`lint, format, schema, determinism, security`, `linux`, `windows`, naprakész ág), lezárt beszélgetések, csak squash merge, force push és törlés tiltva |
| `release-tags-immutable.json` | `v*` tag: módosítás és törlés **mindenkinek** tiltva (kiadott tag nem írható felül) |
| `release-tags-creation.json` | `v*` tag: létrehozás csak a repó adminjának (kiadáskezelő) |

Megjegyzések:

- `required_approving_review_count` = 0, mert egyszemélyes repóban a szerző a saját PR-ját nem hagyhatja
  jóvá. Ha többen fejlesztenek: 1 jóváhagyás és `require_code_owner_review: true` (CODEOWNERS – adapterek, import).
- Az `integration_id` 15368 a GitHub Actions; az ellenőrzések neve a `.github/workflows/ci.yml` jobneve.
  Ha a jobnév változik, a rulesetet is frissíteni kell.
- A rulesetek privát repón GitHub Pro/Team csomagot igényelnek; publikus repón ingyenesek.
- A `ui-screenshots` technikai publikációs ág (a CI force-pusholja); nem fejlesztési ág, a `main` ruleset nem érinti.
