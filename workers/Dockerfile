ARG BASE_IMAGE=foundry-claude-base:local
FROM ${BASE_IMAGE}

# Conditional .NET SDK install — uses apt GPG signing key for authenticity verification
ARG INSTALL_DOTNET=false
RUN if [ "$INSTALL_DOTNET" = "true" ]; then \
        apt-get update && \
        apt-get install -y --no-install-recommends curl gnupg && \
        curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
            | gpg --dearmor -o /usr/share/keyrings/microsoft-archive-keyring.gpg && \
        echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft-archive-keyring.gpg] https://packages.microsoft.com/repos/microsoft-debian-bookworm-prod bookworm main" \
            > /etc/apt/sources.list.d/microsoft.list && \
        apt-get update && \
        apt-get install -y --no-install-recommends dotnet-sdk-10.0 && \
        dotnet tool install --tool-path /usr/local/bin dotnet-ef && \
        dotnet tool install --tool-path /usr/local/bin rq-cli && \
        rm -rf /var/lib/apt/lists/*; \
    fi

# Conditional Angular CLI install
ARG INSTALL_ANGULAR=false
RUN if [ "$INSTALL_ANGULAR" = "true" ]; then \
        npm install -g @angular/cli@21; \
    fi

# Conditional Chromium install — chromium-headless-shell is the smallest headless footprint on
# Bookworm (no GUI dependencies). CHROME_BIN is set unconditionally so Angular test runners pick
# it up when the binary is present; it is harmless when INSTALL_CHROMIUM is false.
ARG INSTALL_CHROMIUM=false
RUN if [ "$INSTALL_CHROMIUM" = "true" ]; then \
        apt-get update && \
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
        rm -rf /var/lib/apt/lists/*; \
    fi
# Path must match the chromium-headless-shell package's installed binary location on Bookworm
ENV CHROME_BIN=/usr/lib/chromium/chromium-headless-shell

# Conditional Docker engine + CLI install — keyring is fetched over HTTPS from Docker's CDN
# (trust anchored to download.docker.com TLS); apt verifies every package against it via signed-by.
# Docker's /gpg endpoint serves an ASCII-armored key; stored as .asc so apt reads it natively
# without a gpg --dearmor step (the slim base image has no gpg). The bookworm codename is
# hardcoded to match the base image — Docker's repo errors on mismatch.
# This installs binaries only; rootless dockerd startup is handled by a separate entrypoint concern.
ARG INSTALL_DOCKER=false
RUN if [ "$INSTALL_DOCKER" = "true" ]; then \
        apt-get update && \
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
            fuse-overlayfs && \
        touch /etc/subuid /etc/subgid && \
        sed -i '/^node:/d' /etc/subuid && echo "node:100000:65536" >> /etc/subuid && \
        sed -i '/^node:/d' /etc/subgid && echo "node:100000:65536" >> /etc/subgid && \
        rm -rf /var/lib/apt/lists/*; \
    fi

COPY entrypoint.sh /entrypoint.sh
RUN chmod 555 /entrypoint.sh

WORKDIR /workspace
RUN chown node:node /workspace

USER node

CMD ["echo", "foundry-worker image ready"]
