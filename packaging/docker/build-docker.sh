#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${VERSION:-1.0}"
DEB_PACKAGE="${DEB_PACKAGE:-$ROOT_DIR/artifacts/debian/gmkvextractgui_${VERSION}_amd64.deb}"
IMAGE_NAME="${IMAGE_NAME:-gmkvextractgui:${VERSION}}"
BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT

if [[ "$(uname -m)" != "x86_64" ]]; then
    printf 'Docker image build currently targets x86_64 Linux.\n' >&2
    exit 1
fi

if [[ ! -f "$DEB_PACKAGE" ]]; then
    printf 'Debian package not found: %s\nBuild it first with packaging/debian/build-deb.sh.\n' "$DEB_PACKAGE" >&2
    exit 1
fi

if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    printf 'Docker is not installed or the current user cannot access the Docker daemon.\n' >&2
    exit 1
fi

cp "$ROOT_DIR/packaging/docker/Dockerfile" "$BUILD_DIR/Dockerfile"
cp "$DEB_PACKAGE" "$BUILD_DIR/gmkvextractgui.deb"
docker build --tag "$IMAGE_NAME" "$BUILD_DIR"
printf 'Created Docker image %s\n' "$IMAGE_NAME"