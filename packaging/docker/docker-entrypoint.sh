#!/usr/bin/env bash
set -euo pipefail

RUNTIME_DIR="$(mktemp -d /tmp/gmkvextractgui-vnc.XXXXXX)"
PASSWORD_FILE="$RUNTIME_DIR/passwd"
VNC_PID=""
OPENBOX_PID=""
APP_PID=""
VNC_SECURITY_ARGS=()

cleanup() {
    for pid in "$APP_PID" "$OPENBOX_PID" "$VNC_PID"; do
        if [[ -n "$pid" ]]; then
            kill "$pid" 2>/dev/null || true
        fi
    done
    rm -rf "$RUNTIME_DIR"
}

trap cleanup EXIT
trap 'exit 0' INT TERM

if [[ -n "${VNC_PASSWORD:-}" ]]; then
    if [[ ${#VNC_PASSWORD} -lt 6 || ${#VNC_PASSWORD} -gt 8 ]]; then
        printf 'VNC_PASSWORD must contain 6-8 characters (TigerVNC VncAuth limit).\n' >&2
        exit 1
    fi
    printf '%s\n' "$VNC_PASSWORD" | tigervncpasswd -f > "$PASSWORD_FILE"
    chmod 0600 "$PASSWORD_FILE"
    VNC_SECURITY_ARGS=(-SecurityTypes VncAuth -rfbauth "$PASSWORD_FILE")
    unset VNC_PASSWORD
else
    printf 'WARNING: VNC authentication is disabled; only expose port 5901 on a trusted network.\n' >&2
    VNC_SECURITY_ARGS=(-SecurityTypes None)
fi

/usr/bin/Xtigervnc :1 \
    -localhost no \
    -rfbport "${VNC_PORT:-5901}" \
    "${VNC_SECURITY_ARGS[@]}" \
    -geometry "${VNC_GEOMETRY:-1280x800}" \
    -depth 24 &
VNC_PID=$!

export DISPLAY=:1
for attempt in {1..50}; do
    if [[ -S /tmp/.X11-unix/X1 ]]; then
        break
    fi
    if ! kill -0 "$VNC_PID" 2>/dev/null; then
        wait "$VNC_PID"
        exit 1
    fi
    sleep 0.1
done

if [[ ! -S /tmp/.X11-unix/X1 ]]; then
    printf 'TigerVNC did not create the X11 display socket.\n' >&2
    exit 1
fi

openbox-session &
OPENBOX_PID=$!
/usr/bin/gMKVExtractGUI &
APP_PID=$!
wait -n "$VNC_PID" "$OPENBOX_PID" "$APP_PID"