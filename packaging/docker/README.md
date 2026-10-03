# Docker

Build the Debian package first, then build the local Docker image:

```sh
bash packaging/debian/build-deb.sh
bash packaging/docker/build-docker.sh
```

The image installs the `.deb` and its dependencies, including MKVToolNix. It runs the GUI through the host's X11 or XWayland display. The current user needs access to the Docker daemon; Docker group membership grants root-equivalent privileges.

For an X11 session, set up the host directories and launch the image:

```sh
mkdir -p "$HOME/Videos/gmkvextract-output"
XAUTHORITY_FILE="${XAUTHORITY:-$HOME/.Xauthority}"
docker run --rm -it \
  --user "$(id -u):$(id -g)" \
  --env DISPLAY="$DISPLAY" \
  --env HOME=/tmp \
  --env XAUTHORITY=/tmp/.Xauthority \
  --volume /tmp/.X11-unix:/tmp/.X11-unix:rw \
  --volume "$XAUTHORITY_FILE:/tmp/.Xauthority:ro" \
  --volume "$HOME/Videos:/media:ro" \
  --volume "$HOME/Videos/gmkvextract-output:/output:rw" \
  gmkvextractgui:1.0
```

Select inputs under `/media` and use `/output` as the extraction folder. If your X server does not authorize the container through the mounted Xauthority file, configure local X11 access on the host before starting it.