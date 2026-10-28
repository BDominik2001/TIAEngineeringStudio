#!/usr/bin/env bash
# CI A szint: a generált SCL-csomag determinisztikus (azonos bemenet + sablonkészlet -> azonos bájtok).
# Mindhárom példamodellt kétszer generálja azonos run-id-val, és bájtra összeveti a teljes kimenetet.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
for version in v18 v19 v20; do
  for run in a b; do
    (cd "$root/src/python" && SOURCE_DATE_EPOCH=0 python3 -m generator.main \
      --input "$root/examples/machine-$version.json" --output "$work/$run-$version" --run-id determinism-check >/dev/null)
  done
  diff -r "$work/a-$version" "$work/b-$version"
  echo "$version: deterministic"
done
