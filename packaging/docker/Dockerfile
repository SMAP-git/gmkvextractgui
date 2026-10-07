FROM ubuntu:24.04

ARG DEBIAN_FRONTEND=noninteractive

COPY gmkvextractgui.deb /tmp/gmkvextractgui.deb

RUN sed -i 's/^Components: main$/Components: main universe/' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates dbus dbus-x11 libnss-wrapper novnc openbox python3-xdg tigervnc-standalone-server \
        tigervnc-tools websockify xdg-desktop-portal xdg-desktop-portal-gtk \
        adwaita-icon-theme shared-mime-info \
        /tmp/gmkvextractgui.deb \
    && rm -f /tmp/gmkvextractgui.deb \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --create-home --shell /usr/sbin/nologin app \
    && mkdir -p /config /media /output /tmp/.X11-unix \
    && chown app:app /config /media /output \
    && chmod 1777 /tmp/.X11-unix

COPY portals.conf /etc/xdg-desktop-portal/portals.conf
COPY openbox-rc.xml /etc/xdg/openbox/rc.xml
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod 0755 /usr/local/bin/docker-entrypoint.sh

ENV HOME=/config \
    VNC_GEOMETRY=1280x800 \
    VNC_PORT=5901 \
    NOVNC_PORT=6080 \
    GMKVEXTRACTGUI_DEFAULT_INPUT_PATH=/media \
    GMKVEXTRACTGUI_DEFAULT_OUTPUT_PATH=/output

EXPOSE 5901/tcp 6080/tcp
USER 1000:1000
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]