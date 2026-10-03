# gMKVExtractGUI Linux Version

This directory is a standalone Linux-only edition of gMKVExtractGUI. It contains the Avalonia desktop application, the cross-platform `gMKVToolNix` library it uses, and Linux package definitions. It does not contain the Windows Forms application or Windows build scripts.

The application bundles the .NET runtime in its packages. MKVToolNix and the host's Avalonia/X11 system libraries are installed as package dependencies.

## Build packages

Build each package on its target Linux distribution. All builds target x86_64 Linux.

| Package | Build command | Local output path | Download |
| --- | --- | --- | --- |
| Debian/Ubuntu (.deb; install `dpkg-deb` and .NET 8 SDK) | `bash packaging/debian/build-deb.sh` | `artifacts/debian/gmkvextractgui_1.0_amd64.deb` | [Download .deb](https://github.com/smap-git/gMKVExtractGUI/releases/latest/download/gmkvextractgui_1.0_amd64.deb) |
| Fedora (.rpm; install `rpm-build` and .NET 8 SDK) | `bash packaging/fedora/build-rpm.sh` | `artifacts/fedora/gmkvextractgui-1.0-1.x86_64.rpm` | [Download .rpm](https://github.com/smap-git/gMKVExtractGUI/releases/latest/download/gmkvextractgui-1.0-1.x86_64.rpm) |
| Arch Linux (.pkg.tar.gz; install `base-devel` and `dotnet-sdk`) | `bash packaging/arch/build-arch.sh` | `artifacts/arch/gmkvextractgui-1.0-1-x86_64.pkg.tar.gz` | [Download Arch package](https://github.com/smap-git/gMKVExtractGUI/releases/latest/download/gmkvextractgui-1.0-1-x86_64.pkg.tar.gz) |
| AppImage (install `appimagetool` and .NET 8 SDK) | `bash packaging/appimage/build-appimage.sh` | `artifacts/appimage/gMKVExtractGUI-x86_64.AppImage` | [Download AppImage](https://github.com/smap-git/gMKVExtractGUI/releases/latest/download/gMKVExtractGUI-x86_64.AppImage) |
| Flatpak (install Flatpak Builder from Flathub and Freedesktop 26.08 SDK/runtime) | `bash packaging/flatpak/build-flatpak.sh` | `artifacts/flatpak/io.github.smapgit.gmkvextractgui.flatpak` | [Download Flatpak](https://github.com/smap-git/gMKVExtractGUI/releases/latest/download/io.github.smapgit.gmkvextractgui.flatpak) |

The build scripts write packages to the local paths shown above. The `artifacts/` directory is gitignored, so attach these generated files to a GitHub Release for the download links to work. Debian, Fedora, and Arch builds default to version `1.0`; set `VERSION` to override it. All package filenames in the Release assets must match the names shown above.

The Flatpak uses the host's MKVToolNix installation through `flatpak-spawn`; install MKVToolNix on the host to analyze and extract files.

## Docker

Build the Debian package, then run `bash packaging/docker/build-docker.sh` to create the local `gmkvextractgui:1.0` image with TigerVNC and a browser WebUI on port `6080`. VNC authentication is optional; no-password access is unsafe on untrusted networks. The **Use input folder** option is enabled by default and requires a read/write media mount. To publish the image to GitHub Container Registry, run `gh workflow run publish-docker.yml --repo SMAP-git/gmkvextractgui -f version=1.0`.

For Unraid, use the [container template](packaging/docker/unraid-template.xml) and see [the Docker guide](packaging/docker/README.md).

After the workflow succeeds, pull `ghcr.io/smap-git/gmkvextractgui:1.0`. See [packaging/docker/README.md](packaging/docker/README.md) for X11 display, file-mount, and container registry instructions.

## Build the application

From this directory, run:

```sh
dotnet publish src/gMKVExtractGUI.Linux/gMKVExtractGUI.Linux.csproj \
  --configuration Release --runtime linux-x64 --self-contained true
```
