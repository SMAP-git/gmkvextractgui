FROM ubuntu:24.04

ARG DEBIAN_FRONTEND=noninteractive

COPY gmkvextractgui.deb /tmp/gmkvextractgui.deb

RUN sed -i 's/^Components: main$/Components: main universe/' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates /tmp/gmkvextractgui.deb \
    && rm -f /tmp/gmkvextractgui.deb \
    && rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["/usr/bin/gMKVExtractGUI"]