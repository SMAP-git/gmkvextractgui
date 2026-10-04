#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FLATPAK_DIR="$ROOT_DIR/packaging/flatpak"
APP_ID="io.github.smapgit.gmkvextractgui"
MANIFEST="$FLATPAK_DIR/io.github.smap-git.gmkvextractgui.yml"
MKVTOOLNIX_VERSION="102.0"
MKVTOOLNIX_SHA256="c66345b30d6d5fd640ea982ab5e202a99b9f541a20e42aa19edd90f3ddd5dc9b"
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

if ! command -v curl >/dev/null 2>&1 || ! command -v sha256sum >/dev/null 2>&1; then
    printf 'curl and sha256sum are required to bundle MKVToolNix.\n' >&2
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

MKVTOOLNIX_APPIMAGE="$BUILD_DIR/MKVToolNix_GUI-${MKVTOOLNIX_VERSION}-x86_64.AppImage"
MKVTOOLNIX_EXTRACT_DIR="$BUILD_DIR/mkvtoolnix-extract"
curl --fail --location --retry 3 \
    "https://mkvtoolnix.download/appimage/MKVToolNix_GUI-${MKVTOOLNIX_VERSION}-x86_64.AppImage" \
    --output "$MKVTOOLNIX_APPIMAGE"
printf '%s  %s\n' "$MKVTOOLNIX_SHA256" "$MKVTOOLNIX_APPIMAGE" | sha256sum --check --status
chmod 0755 "$MKVTOOLNIX_APPIMAGE"
mkdir -p "$MKVTOOLNIX_EXTRACT_DIR"
(cd "$MKVTOOLNIX_EXTRACT_DIR" && APPIMAGE_EXTRACT_AND_RUN=1 "$MKVTOOLNIX_APPIMAGE" --appimage-extract >/dev/null)
cp -a "$MKVTOOLNIX_EXTRACT_DIR/squashfs-root" "$STAGE_DIR/mkvtoolnix"

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