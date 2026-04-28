#!/usr/bin/env bash
# Build a Release configuration and package the deployed module
# folder into a zip suitable for NexusMods upload.
#
# Output: ./dist/BannerKings.Redux-<version>.zip whose top-level
# folder is BannerKings.Redux/, drop-in into <Bannerlord>/Modules/.
#
# Usage:
#   BANNERLORD_GAME_DIR="/c/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord" \
#     scripts/package-release.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [[ -z "${BANNERLORD_GAME_DIR:-}" ]]; then
  echo "BANNERLORD_GAME_DIR not set." >&2
  exit 1
fi

# Pull version from SubModule.xml (e.g. v1.5.0.0).
VERSION="$(grep -oE 'Version value="[^"]+"' "$REPO_ROOT/BannerKings/_Module/SubModule.xml" | head -1 | sed -E 's/Version value="([^"]+)"/\1/')"
if [[ -z "$VERSION" ]]; then
  echo "Could not parse version from SubModule.xml" >&2
  exit 1
fi

MODULE_NAME="BannerKings.Redux"
MODULE_DIR="$BANNERLORD_GAME_DIR/Modules/$MODULE_NAME"
DIST_DIR="$REPO_ROOT/dist"
ZIP_NAME="$MODULE_NAME-$VERSION.zip"

echo "==> Building Release"
dotnet build "$REPO_ROOT/BannerKings/BannerKings.csproj" -c Release

if [[ ! -d "$MODULE_DIR" ]]; then
  echo "Expected deployed module at $MODULE_DIR but not found." >&2
  exit 1
fi

mkdir -p "$DIST_DIR"
rm -f "$DIST_DIR/$ZIP_NAME"

# Stage the module folder so the zip's top-level entry is BannerKings.Redux/.
STAGE_DIR="$(mktemp -d)"
trap 'rm -rf "$STAGE_DIR"' EXIT
cp -r "$MODULE_DIR" "$STAGE_DIR/$MODULE_NAME"

# Drop development-only files from the staged copy.
find "$STAGE_DIR/$MODULE_NAME" -name "*.pdb" -delete
find "$STAGE_DIR/$MODULE_NAME" -name "*.xml" -path "*/bin/*" -delete

# Embed the LICENSE alongside the module so it travels with the zip.
cp "$REPO_ROOT/LICENSE" "$STAGE_DIR/$MODULE_NAME/LICENSE.txt"

if command -v 7z >/dev/null 2>&1; then
  ( cd "$STAGE_DIR" && 7z a -tzip "$DIST_DIR/$ZIP_NAME" "$MODULE_NAME" >/dev/null )
elif command -v zip >/dev/null 2>&1; then
  ( cd "$STAGE_DIR" && zip -r "$DIST_DIR/$ZIP_NAME" "$MODULE_NAME" >/dev/null )
else
  # Windows fallback: use PowerShell's Compress-Archive.
  STAGE_WIN="$(cygpath -w "$STAGE_DIR/$MODULE_NAME" 2>/dev/null || echo "$STAGE_DIR/$MODULE_NAME")"
  ZIP_WIN="$(cygpath -w "$DIST_DIR/$ZIP_NAME" 2>/dev/null || echo "$DIST_DIR/$ZIP_NAME")"
  powershell.exe -NoProfile -Command "Compress-Archive -Force -Path '$STAGE_WIN' -DestinationPath '$ZIP_WIN'"
fi

echo "==> Packaged: $DIST_DIR/$ZIP_NAME"
echo "Contents top-level: $MODULE_NAME/"
