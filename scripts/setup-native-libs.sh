#!/usr/bin/env bash
#
# Copies Homebrew-installed Tesseract & Leptonica native libraries into the
# .NET build output so that the Tesseract NuGet InteropDotNet loader can find
# arm64 libraries on Apple Silicon.
#
# Usage:  ./scripts/setup-native-libs.sh
#
# Prerequisites:  brew install tesseract
#
set -euo pipefail

ARCH=$(uname -m)
if [[ "$ARCH" != "arm64" ]]; then
    echo "[INFO] Not Apple Silicon ($ARCH) -- skipping native lib copy."
    exit 0
fi

BIN_BASE="OcrAgent/bin/Debug"
TFM=$(ls -d "$BIN_BASE"/net* 2>/dev/null | head -1 | xargs basename 2>/dev/null || echo "net9.0")
X64_DIR="$BIN_BASE/$TFM/x64"

BREW_PREFIX=$(brew --prefix 2>/dev/null || echo "/opt/homebrew")
TESS_LIB="$BREW_PREFIX/lib/libtesseract.dylib"
LEPT_LIB="$BREW_PREFIX/lib/libleptonica.dylib"

if [[ ! -f "$TESS_LIB" ]]; then
    echo "[ERROR] libtesseract.dylib not found at $TESS_LIB"
    echo "        Install via:  brew install tesseract"
    exit 1
fi

if [[ ! -f "$LEPT_LIB" ]]; then
    echo "[ERROR] libleptonica.dylib not found at $LEPT_LIB"
    echo "        Install via:  brew install leptonica"
    exit 1
fi

mkdir -p "$X64_DIR"

# InteropDotNet searches x64/ for these versioned names on macOS
cp -f "$LEPT_LIB" "$X64_DIR/libleptonica-1.82.0.dylib"
cp -f "$TESS_LIB" "$X64_DIR/libtesseract50.dylib"

echo "[OK] Copied arm64 native libraries to $X64_DIR/"
echo "     libleptonica-1.82.0.dylib  (from $LEPT_LIB)"
echo "     libtesseract50.dylib       (from $TESS_LIB)"
echo ""
echo "You can now run: dotnet run --project OcrAgent"
