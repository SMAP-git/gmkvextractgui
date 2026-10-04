# Docker

Build the Debian package first, then build the local Docker image:

```sh
bash packaging/debian/build-deb.sh
bash packaging/docker/build-docker.sh
```

The image installs the `.deb` and its dependencies, including MKVToolNix, then starts the GUI in a TigerVNC desktop session. It serves noVNC/Websockify on port `6080` for browser access and raw VNC on port `5901` for native clients. The container runs as a non-root user. The current user needs access to the Docker daemon; Docker group membership grants root-equivalent privileges.

## Publish to GitHub Container Registry

The repository workflow downloads the `.deb` from the matching GitHub release, builds the image, and pushes versioned and `latest` tags. After pushing workflow changes to `main`, trigger it with GitHub CLI:

```sh
gh workflow run publish-docker.yml \
  --repo SMAP-git/gmkvextractgui \
  -f version=1.0.1
```

The published image is `ghcr.io/smap-git/gmkvextractgui:1.0.1`. Pull it with:

```sh
docker pull ghcr.io/smap-git/gmkvextractgui:1.0.1
```

GitHub may initially mark the container package private. If public downloads are needed, change its visibility to public in the package settings on GitHub.

## Unraid

Use the [Unraid container template](unraid-template.xml), or enter the same values in Unraid's Docker form:

| Setting | Value |
| --- | --- |
| Repository | `ghcr.io/smap-git/gmkvextractgui:1.0.1` |
| Network type | `bridge` |
| Extra parameters | `--user=99:100` (`nobody:users`) |
| VNC WebUI port | Host `6080` to container `6080/tcp`; the WebUI opens noVNC |
| Native VNC port | Host `5901` to container `5901/tcp` |
| VNC password | Optional; 6-8 characters when set. Blank disables authentication. |
| Container `HOME` | `/config` |
| App config path | `/mnt/user/appdata/gMKVExtractGUI` to `/config` |
| Media path | `/mnt/user/Media` to `/media` (read/write for input-folder mode) |
| Extraction path | `/mnt/user/Media/Extracted` to `/output` (read/write) |

 Click **WebUI** or browse to `http://<unraid-ip>:6080/vnc.html?autoconnect=true&resize=scale` for browser access; native VNC clients can connect to `<unraid-ip>:5901`. Set a 6-8 character password for VNC authentication, or leave it blank to disable authentication. No-password access is unsafe on an untrusted network. The input file picker opens in `/media`, the **Use input folder** option is enabled by default, and extraction otherwise uses `/output`. Input-folder mode writes to `/media`, so its mount must be read/write. Ensure both writable directories are writable by UID `99`, GID `100` (`nobody:users`).

Create a VNC password, input directory, and extraction directory, then start the container with its VNC port bound to localhost:

```sh
mkdir -p "$HOME/Videos/gmkvextract-output"
read -rsp 'VNC password (6-8 chars; Enter disables authentication): ' VNC_PASSWORD
printf '\n'
docker run --rm -d --name gmkvextractgui-vnc \
  --user "$(id -u):$(id -g)" \
  --env HOME=/tmp \
  --env "VNC_PASSWORD=$VNC_PASSWORD" \
  --publish 127.0.0.1:6080:6080 \
  --publish 127.0.0.1:5901:5901 \
  --volume "$HOME/Videos:/media:ro" \
  --volume "$HOME/Videos/gmkvextract-output:/output:rw" \
  ghcr.io/smap-git/gmkvextractgui:1.0.1
```

 Open `http://127.0.0.1:6080/vnc.html?autoconnect=true&resize=scale` in a browser or connect a native VNC client to `127.0.0.1:5901`. Enter the password if set. TigerVNC VncAuth passwords must be 6-8 characters. Without a password, anyone who can reach the port can control the desktop; bind it to localhost or use a trusted network. The picker opens in `/media`; input-folder mode is enabled by default and requires the `/media` mount to be `:rw`. Stop the container with `docker stop gmkvextractgui-vnc`.