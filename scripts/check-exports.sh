#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-}"
FILTER="${2:-}"

if [[ -z "${ROOT}" || ! -d "${ROOT}" ]]; then
  echo "Usage: $0 /path/to/Plugins [symbol_filter]" >&2
  exit 1
fi

NDK_BIN=""
if [[ -n "${ANDROID_HOME:-}" && -d "$ANDROID_HOME/ndk" ]]; then
  while IFS= read -r -d '' cand; do
    if [[ -x "$cand/toolchains/llvm/prebuilt/darwin-x86_64/bin/llvm-readelf" ]]; then
      NDK_BIN="$cand/toolchains/llvm/prebuilt/darwin-x86_64/bin"
      break
    fi
  done < <(find "$ANDROID_HOME/ndk" -mindepth 1 -maxdepth 1 -type d -print0)
fi

FILE_BIN="$(command -v file || true)"
LLVM_NM="$(command -v llvm-nm || true)"
NM_BIN="$(command -v nm || true)"
if command -v xcrun >/dev/null 2>&1; then
  LLVM_NM="$(xcrun -f llvm-nm 2>/dev/null || echo "${LLVM_NM}")"
fi

rule(){ printf '\n'; printf '%*s\n' 80 '' | tr ' ' -; }
title(){ rule; echo "$1"; rule; }

kind_of(){
  if [[ -n "$FILE_BIN" ]]; then "$FILE_BIN" -b "$1" 2>/dev/null || true; else echo "unknown"; fi
}

pipe_filter(){
  if [[ -n "$FILTER" ]]; then grep -E "$FILTER" || true; else cat; fi
}

inspect_macho(){
  local f="$1"
  echo "[Mach-O] Exported (external, defined) symbols:"
  if [[ -n "$LLVM_NM" ]]; then "$LLVM_NM" -gU "$f" 2>/dev/null | pipe_filter
  elif [[ -n "$NM_BIN" ]]; then "$NM_BIN" -gU "$f" 2>/dev/null | pipe_filter
  else echo "  (no nm found)"; fi
}

inspect_elf(){
  local f="$1"
  if [[ -n "$NDK_BIN" && -x "$NDK_BIN/llvm-readelf" ]]; then
    echo "[ELF] Dynamic symbol table:"
    "$NDK_BIN/llvm-readelf" --dyn-syms "$f" 2>/dev/null | pipe_filter
    echo
    echo "[ELF] Defined dynamic exports (nm -D --defined-only):"
    "$NDK_BIN/llvm-nm" -D --defined-only "$f" 2>/dev/null | pipe_filter
  else
    echo "[ELF] (NDK llvm-readelf not found; trying llvm-nm fallback)"
    if [[ -n "$LLVM_NM" ]]; then "$LLVM_NM" -D --defined-only "$f" 2>/dev/null | pipe_filter
    else echo "  (no suitable tools found for ELF)"; fi
  fi
}

inspect_archive(){
  local f="$1"
  echo "[Static Archive] Global symbols:"
  if [[ -n "$LLVM_NM" ]]; then "$LLVM_NM" -gU "$f" 2>/dev/null | pipe_filter
  elif [[ -n "$NM_BIN" ]]; then "$NM_BIN" -gU "$f" 2>/dev/null | pipe_filter
  else echo "  (no nm found)"; fi
}

inspect_bitcode(){
  local f="$1"
  echo "[LLVM Bitcode] Defined symbols:"
  if [[ -n "$LLVM_NM" ]]; then "$LLVM_NM" --defined-only "$f" 2>/dev/null | pipe_filter
  else echo "  (llvm-nm not found)"; fi
}

inspect_pe(){
  local f="$1"
  echo "[PE/COFF] Windows DLL exports (best-effort):"
  if [[ -n "$LLVM_NM" ]]; then
    if ! "$LLVM_NM" --extern-only "$f" 2>/dev/null | pipe_filter; then
      echo "  (llvm-nm couldn't parse this DLL here; use dumpbin /exports on Windows)"
    fi
  else
    echo "  (llvm-nm not found)"
  fi
}

inspect_file(){
  local f="$1"
  local kind; kind="$(kind_of "$f" | tr '[:upper:]' '[:lower:]')"
  echo "File: $f"
  echo "Type: $kind"
  case "$kind" in
    *mach-o*) inspect_macho "$f" ;;
    *elf*|*shared\ object*) inspect_elf "$f" ;;
    *current\ ar\ archive*|*static\ library*) inspect_archive "$f" ;;
    *llvm\ bitcode*) inspect_bitcode "$f" ;;
    *pe32*|*pe32+*|*coff*) inspect_pe "$f" ;;
    *) echo "  (unsupported/unknown on macOS)" ;;
  esac
  echo
}

title "Export check in: $ROOT"
[[ -n "$FILTER" ]] && { echo "Symbol filter: /$FILTER/"; rule; }

FILES=()
while IFS= read -r -d '' f; do FILES+=("$f"); done < <(
  find "$ROOT" -type f \( -name "*.dylib" -o -name "*.so" -o -name "*.a" -o -name "*.bc" -o -name "*.dll" \) -print0
)

if [[ ${#FILES[@]} -eq 0 ]]; then
  echo "No candidate libraries found under: $ROOT"
  exit 0
fi

for f in "${FILES[@]}"; do inspect_file "$f"; done
echo "Done."
