#!/usr/bin/env bash
# TIA nélküli tesztek Linuxon/CI-ben (a WPF Desktop projekt kivételével).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

echo "==> Verziók (docs/VERSIONING.md)"
python3 "$root/scripts/tools/build_info.py" --check

echo "==> Python"
PYTHONPATH="$root/src/python" python3 -m unittest discover -s "$root/tests/unit/python" -t "$root/tests/unit/python" -v
(cd "$root/src/python" && python3 -m generator.template_hashes)

echo "==> Rust"
(
  cd "$root/src/rust/validator"
  cargo test
  if cargo clippy --version >/dev/null 2>&1; then cargo clippy --all-targets -- -D warnings; fi
  cargo build --release
)

echo "==> .NET (portable solution filter)"
dotnet test "$root/TIA-Engineering-Studio.Portable.slnf"
