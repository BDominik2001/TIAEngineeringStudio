#!/usr/bin/env bash
# TIA nélküli tesztek Linuxon/CI-ben (a WPF Desktop projekt kivételével).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

echo "==> Python"
(cd "$root/python" && python3 -m unittest discover -s tests -v && python3 -m generator.template_hashes)

echo "==> Rust"
(
  cd "$root/rust/tes_validator"
  cargo test
  if cargo clippy --version >/dev/null 2>&1; then cargo clippy --all-targets -- -D warnings; fi
  cargo build --release
)

echo "==> .NET (portable solution filter)"
dotnet test "$root/TIA-Engineering-Studio.Portable.slnf"
