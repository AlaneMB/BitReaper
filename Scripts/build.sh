#!/bin/bash
# Builds BitReaper.app into ./dist (Release, universal, ad-hoc signed).
set -euo pipefail
cd "$(dirname "$0")/.."
CONFIG="${1:-Release}"
xcodegen generate --quiet
# Universal binary: runs natively on Apple Silicon and Intel Macs.
xcodebuild -project BitReaper.xcodeproj -scheme BitReaper -configuration "$CONFIG" \
  -derivedDataPath build -destination 'generic/platform=macOS' \
  ARCHS="arm64 x86_64" ONLY_ACTIVE_ARCH=NO build -quiet
mkdir -p dist
rm -rf dist/BitReaper.app
cp -R "build/Build/Products/$CONFIG/BitReaper.app" dist/
codesign --force --deep --sign - dist/BitReaper.app >/dev/null 2>&1 || true
echo "Built dist/BitReaper.app"
