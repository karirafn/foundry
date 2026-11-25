ARG BASE_IMAGE=foundry-claude-base:local
FROM ${BASE_IMAGE}
LABEL dev.foundry.image=worker

# Every toolchain is installed unconditionally (ADR 0109); /opt/foundry/toolchain.sh reports what
# the image has.

# .NET SDKs — every channel Microsoft's release index lists as active or maintenance, selected by
# install-dotnet-sdks.sh (ADR 0109); no SDK version is named here. The index is ADDed rather than
# fetched inside the RUN because Docker re-fetches an ADD URL on every build and invalidates the cache
# only when its content changes, so the SDK layer rebuilds exactly when a release changes the index.
# Packages come from Microsoft's apt feed, verified against its GPG signing key.
# csharp-ls (the C# language server registered by the baked csharp-lsp plugin) is pinned in
# .config/dotnet-tools.json, the single source of its version, which Dependabot tracks.
COPY .config/dotnet-tools.json /tmp/dotnet-tools.json
COPY install-dotnet-sdks.sh /tmp/install-dotnet-sdks.sh
ADD https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json /tmp/releases-index.json
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl gnupg && \
    curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
        | gpg --dearmor -o /usr/share/keyrings/microsoft-archive-keyring.gpg && \
    echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft-archive-keyring.gpg] https://packages.microsoft.com/repos/microsoft-debian-bookworm-prod bookworm main" \
        > /etc/apt/sources.list.d/microsoft.list && \
    apt-get update && \
    bash /tmp/install-dotnet-sdks.sh /tmp/releases-index.json && \
    dotnet tool install --tool-path /usr/local/bin dotnet-ef && \
    csharp_ls_version=$(jq -er '.tools["csharp-ls"].version' /tmp/dotnet-tools.json) && \
    dotnet tool install --tool-path /usr/local/bin csharp-ls --version "$csharp_ls_version" && \
    rm -rf /var/lib/apt/lists/* && \
    rm -f /tmp/dotnet-tools.json /tmp/install-dotnet-sdks.sh /tmp/releases-index.json

# Claude Code plugin registering csharp-ls as the C# LSP server. The entrypoint loads it when
# csharp-ls is on PATH.
COPY claude-plugins/csharp-lsp /opt/claude-plugins/csharp-lsp

# Worker roles (ADR 0095): each role's Claude Code plugin and prompts, plus the shared plugin loaded
# for every role. The entrypoint loads the run's role and shared with --plugin-dir. Read-only to
# node so a run cannot rewrite the instructions it or a later run is given.
COPY roles /opt/foundry/roles
RUN chmod -R a-w,a+rX /opt/foundry/roles

# Toolchain report (ADR 0109): Foundry runs it as the entrypoint of a probe container against each new
# image and gates toolchain-dependent prompt sections on what it prints. Read-only to node, like the
# roles, so a run cannot change what a later probe reports.
COPY toolchain.sh /opt/foundry/toolchain.sh
RUN chmod 555 /opt/foundry/toolchain.sh

# Angular CLI
RUN npm install -g @angular/cli@21

# Chromium — chromium-headless-shell is the smallest headless footprint on Bookworm (no GUI
# dependencies). CHROME_BIN points Angular test runners at it.
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        chromium-headless-shell \
        libglib2.0-0 \
        libnss3 \
        libatk1.0-0 \
        libatk-bridge2.0-0 \
        libcups2 \
        libdrm2 \
        libxkbcommon0 \
        libxcomposite1 \
        libxdamage1 \
        libxfixes3 \
        libxrandr2 \
        libgbm1 \
        libasound2 && \
    rm -rf /var/lib/apt/lists/*
# Path must match the chromium-headless-shell package's installed binary location on Bookworm
ENV CHROME_BIN=/usr/lib/chromium/chromium-headless-shell

# Docker engine + CLI — keyring is fetched over HTTPS from Docker's CDN
# (trust anchored to download.docker.com TLS); apt verifies every package against it via signed-by.
# Docker's /gpg endpoint serves an ASCII-armored key; stored as .asc so apt reads it natively
# without a gpg --dearmor step. The bookworm codename is hardcoded to match the base image —
# Docker's repo errors on mismatch.
# This installs binaries only; the entrypoint starts rootless dockerd only for full-capability runs
# the operator has enabled nested Docker for (ADR 0110).
# Storage driver: the data-root is mounted on a named volume at runtime, giving the nested daemon
# a real ext4/overlayfs filesystem — fuse-overlayfs and /dev/fuse are not needed. The data-root
# path (/home/node/.local/share/docker) is pre-created owned by node so the per-run named volume
# inherits node ownership on first mount; otherwise Docker initialises the fresh volume as root
# and rootless dockerd (running as node) cannot chmod its own data-root and exits at startup.
# Security: all setuid binaries except newuidmap and newgidmap are stripped of the setuid bit;
# newuidmap/newgidmap (from uidmap) retain it because rootless dockerd needs them to write
# uid/gid maps for user-namespace setup.
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates curl && \
    curl -fsSL https://download.docker.com/linux/debian/gpg \
        -o /usr/share/keyrings/docker-archive-keyring.asc && \
    chmod go+r /usr/share/keyrings/docker-archive-keyring.asc && \
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker-archive-keyring.asc] https://download.docker.com/linux/debian bookworm stable" \
        > /etc/apt/sources.list.d/docker.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends \
        docker-ce \
        docker-ce-cli \
        containerd.io \
        docker-buildx-plugin \
        docker-compose-plugin \
        docker-ce-rootless-extras \
        uidmap \
        slirp4netns \
        iproute2 \
        procps && \
    touch /etc/subuid /etc/subgid && \
    sed -i '/^node:/d' /etc/subuid && echo "node:100000:65536" >> /etc/subuid && \
    sed -i '/^node:/d' /etc/subgid && echo "node:100000:65536" >> /etc/subgid && \
    mkdir -p /home/node/.local/share/docker && chown -R node:node /home/node/.local && \
    find / -xdev -perm -4000 -type f ! -name newuidmap ! -name newgidmap -exec chmod u-s {} + && \
    rm -rf /var/lib/apt/lists/*

COPY entrypoint.sh /entrypoint.sh
RUN chmod 555 /entrypoint.sh

WORKDIR /workspace
RUN chown node:node /workspace

# No USER: the entrypoint starts as root only to render the root-owned
# /etc/claude-code/managed-settings.json, then drops to node via setpriv before any
# other work (ADR 0086). Foundry runs the container hardened (ADR 0088): capabilities dropped
# to the SETUID/SETGID that drop needs, no-new-privileges, and a read-only root filesystem with
# tmpfs at /tmp, /var/tmp and /etc/claude-code and anonymous volumes at /workspace and /home/node.
# Anything the image needs writable at runtime must live under one of those paths.

CMD ["echo", "foundry-worker image ready"]
