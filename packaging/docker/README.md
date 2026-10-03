# Docker

Build the Debian package first, then build the local Docker image:

```sh
bash packaging/debian/build-deb.sh
bash packaging/docker/build-docker.sh
```

The image installs the `.deb` and its dependencies, including MKVToolNix, then starts the GUI in a TigerVNC desktop session. The container runs as a non-root user. The current user needs access to the Docker daemon; Docker group membership grants root-equivalent privileges.

## Publish to GitHub Container Registry

The repository workflow downloads the `.deb` from the matching GitHub release, builds the image, and pushes versioned and `latest` tags. After pushing workflow changes to `main`, trigger it with GitHub CLI:

```sh
gh workflow run publish-docker.yml \
  --repo SMAP-git/gmkvextractgui \
  -f version=1.0
```

The published image is `ghcr.io/smap-git/gmkvextractgui:1.0`. Pull it with:

```sh
docker pull ghcr.io/smap-git/gmkvextractgui:1.0
```

GitHub may initially mark the container package private. If public downloads are needed, change its visibility to public in the package settings on GitHub.

## Unraid

Use the [Unraid container template](unraid-template.xml), or enter the same values in Unraid's Docker form:

| Setting | Value |
| --- | --- |
| Repository | `ghcr.io/smap-git/gmkvextractgui:1.0` |
| Network type | `bridge` |
| Extra parameters | `--user=99:100` (`nobody:users`) |
| VNC port | Host `5901` to container `5901/tcp` |
| VNC password | 6-8 characters |
| Container `HOME` | `/config` |
| App config path | `/mnt/user/appdata/gMKVExtractGUI` to `/config` |
| Media path | `/mnt/user/Media` to `/media` (read-only) |
| Extraction path | `/mnt/user/Media/Extracted` to `/output` (read/write) |

Start the container and connect a VNC client to `<unraid-ip>:5901` using the configured password. Make sure the host output directory is writable by UID `99`, GID `100` (`nobody:users`).

Create a VNC password, input directory, and extraction directory, then start the container with its VNC port bound to localhost:

```sh
mkdir -p "$HOME/Videos/gmkvextract-output"
export VNC_PASSWORD="$(openssl rand -hex 4)"
docker run --rm -d --name gmkvextractgui-vnc \
  --user "$(id -u):$(id -g)" \
  --env HOME=/tmp \
  --env VNC_PASSWORD \
  --publish 127.0.0.1:5901:5901 \
  --volume "$HOME/Videos:/media:ro" \
  --volume "$HOME/Videos/gmkvextract-output:/output:rw" \
  ghcr.io/smap-git/gmkvextractgui:1.0
```

Connect a VNC client to `127.0.0.1:5901` and enter the value of `$VNC_PASSWORD`. The VNC password must be 6-8 characters because TigerVNC's VncAuth protocol uses at most 8. Select input files under `/media` and use `/output` as the extraction folder. Stop the container with `docker stop gmkvextractgui-vnc`.