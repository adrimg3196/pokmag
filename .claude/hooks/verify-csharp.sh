#!/usr/bin/env bash
# PostToolUse hook (ECC rules/csharp/hooks.md pattern, adapted to Unity):
# after editing a .cs file, compile every Unity script against the UnityEngine
# reference assemblies and, for domain changes, re-run the rules tests.
# Exit code 2 feeds the compiler/test output back to Claude so it fixes it.
set -uo pipefail

input="$(cat)"
file="$(printf '%s' "$input" | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n1)"

case "$file" in
  *.cs) ;;
  *) exit 0 ;;
esac

if ! command -v dotnet >/dev/null 2>&1; then
  echo "verify-csharp: dotnet SDK not found; skipping compile check." >&2
  exit 0
fi

root="$(git -C "$(dirname "$file")" rev-parse --show-toplevel 2>/dev/null || pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if ! out="$(dotnet build "$root/Tools/UnityCompileCheck" --nologo -v q 2>&1)"; then
  echo "Unity compile check failed after editing $file:" >&2
  printf '%s\n' "$out" | grep -E "error|warning" | sed "s#$root/##" | sort -u | head -n 20 >&2
  exit 2
fi

case "$file" in
  */Scripts/Domain/*|*/Tests/HoloTable.Domain.Tests/*)
    if ! out="$(dotnet test "$root/Tests/HoloTable.Domain.Tests" --nologo -v q 2>&1)"; then
      echo "Domain tests failed after editing $file:" >&2
      printf '%s\n' "$out" | grep -E "Failed|error|Assert" | head -n 20 >&2
      exit 2
    fi
    ;;
esac

exit 0
