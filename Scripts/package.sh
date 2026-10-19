#!/bin/bash
# Builds the universal Release app and zips it for a GitHub release: dist/BitReaper-macOS.zip
set -euo pipefail
cd "$(dirname "$0")/.."
Scripts/build.sh Release
rm -f dist/BitReaper-macOS.zip
ditto -c -k --sequesterRsrc --keepParent dist/BitReaper.app dist/BitReaper-macOS.zip
shasum -a 256 dist/BitReaper-macOS.zip
