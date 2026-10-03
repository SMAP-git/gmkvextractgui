FROM ubuntu:24.04

ARG DEBIAN_FRONTEND=noninteractive

COPY gmkvextractgui.deb /tmp/gmkvextractgui.deb

RUN sed -i 's/^Components: main$/Components: main universe/' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates openbox python3-xdg tigervnc-standalone-server tigervnc-tools \
        /tmp/gmkvextractgui.deb \
    && rm -f /tmp/gmkvextractgui.deb \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --create-home --shell /usr/sbin/nologin app \
    && mkdir -p /tmp/.X11-unix \
    && chmod 1777 /tmp/.X11-unix

COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod 0755 /usr/local/bin/docker-entrypoint.sh

ENV HOME=/home/app \
    VNC_GEOMETRY=1280x800 \
    VNC_PORT=5901 \
    GMKVEXTRACTGUI_DEFAULT_OUTPUT_PATH=/output

EXPOSE 5901/tcp
USER 1000:1000
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]