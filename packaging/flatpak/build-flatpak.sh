#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FLATPAK_DIR="$ROOT_DIR/packaging/flatpak"
APP_ID="io.github.smapgit.gmkvextractgui"
MANIFEST="$FLATPAK_DIR/io.github.smap-git.gmkvextractgui.yml"
STAGE_DIR="$FLATPAK_DIR/.build-input"
OUTPUT_DIR="$ROOT_DIR/artifacts/flatpak"
OUTPUT_FILE="$OUTPUT_DIR/$APP_ID.flatpak"
BUILD_DIR="$(mktemp -d "$FLATPAK_DIR/.build.XXXXXX")"

if [[ "$(uname -m)" != "x86_64" ]]; then
    printf 'Flatpak build currently targets x86_64 Linux.\n' >&2
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    printf '.NET SDK is required to publish the application.\n' >&2
    exit 1
fi

if ! command -v flatpak >/dev/null 2>&1 || ! flatpak run org.flatpak.Builder --version >/dev/null 2>&1; then
    printf 'Install Flatpak Builder from Flathub (org.flatpak.Builder) first.\n' >&2
    exit 1
fi

if [[ -e "$STAGE_DIR" ]]; then
    printf 'Flatpak staging directory already exists: %s\n' "$STAGE_DIR" >&2
    exit 1
fi

mkdir -p "$STAGE_DIR" "$OUTPUT_DIR"
trap 'rm -rf "$STAGE_DIR" "$BUILD_DIR"' EXIT
mkdir -p "$STAGE_DIR/publish"
cp -a "$FLATPAK_DIR/assets/." "$STAGE_DIR/"
cp "$ROOT_DIR/packaging/docker/unraid-icon.png" "$STAGE_DIR/icon.png"

dotnet publish "$ROOT_DIR/src/gMKVExtractGUI.Linux/gMKVExtractGUI.Linux.csproj" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output "$STAGE_DIR/publish"

flatpak run org.flatpak.Builder \
    --user \
    --force-clean \
    --disable-rofiles-fuse \
    --repo="$BUILD_DIR/repo" \
    --state-dir="$BUILD_DIR/state" \
    "$BUILD_DIR/build" \
    "$MANIFEST"

flatpak build-bundle "$BUILD_DIR/repo" "$OUTPUT_FILE" "$APP_ID" stable
printf 'Created %s\n' "$OUTPUT_FILE"