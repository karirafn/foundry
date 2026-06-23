FROM node:22-bookworm-slim

# Install git explicitly — not present in slim variant
RUN apt-get update && apt-get install -y --no-install-recommends git ca-certificates && rm -rf /var/lib/apt/lists/*

# Disable telemetry and auto-update
ENV CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
ENV DISABLE_AUTOUPDATER=1

# Pin exact version to avoid unexpected CLI behaviour changes
RUN npm install -g @anthropic-ai/claude-code@2.1.138

# Pre-create ~/.claude owned by the built-in node user (UID 1000)
RUN mkdir -p /home/node/.claude && chown node:node /home/node/.claude

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

# Conditional glab (GitLab CLI) install — downloads the canonical .deb from the latest GitLab
# release and verifies integrity against the release's published checksums.txt before installing.
# GitLab does not publish an official apt repository for glab, so apt-repo install is not viable.
# Integrity is verified against checksums.txt fetched from the same canonical GitLab release over
# HTTPS — trust is anchored to gitlab.com TLS + release integrity, not an independent pinned hash
# (deliberate trade-off to stay current without manual version bumps).
ARG INSTALL_GLAB=false
RUN if [ "$INSTALL_GLAB" = "true" ]; then \
        apt-get update && \
        apt-get install -y --no-install-recommends curl && \
        GLAB_VERSION=$(curl -fsSL "https://gitlab.com/api/v4/projects/gitlab-org%2Fcli/releases/permalink/latest" \
            | grep -o '"tag_name":"v[^"]*"' \
            | sed 's/.*"v//;s/"//') && \
        echo "$GLAB_VERSION" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$' || { echo "Unexpected glab version: '$GLAB_VERSION'"; exit 1; } && \
        GLAB_TMPDIR=$(mktemp -d) && \
        curl -fsSL -o "${GLAB_TMPDIR}/glab_${GLAB_VERSION}_linux_amd64.deb" \
            "https://gitlab.com/gitlab-org/cli/-/releases/v${GLAB_VERSION}/downloads/glab_${GLAB_VERSION}_linux_amd64.deb" && \
        curl -fsSL -o "${GLAB_TMPDIR}/checksums.txt" \
            "https://gitlab.com/gitlab-org/cli/-/releases/v${GLAB_VERSION}/downloads/checksums.txt" && \
        cd "${GLAB_TMPDIR}" && \
        grep -E "^[0-9a-f]{64}  glab_${GLAB_VERSION}_linux_amd64\.deb$" checksums.txt | sha256sum -c - && \
        apt-get install -y --no-install-recommends "${GLAB_TMPDIR}/glab_${GLAB_VERSION}_linux_amd64.deb" && \
        rm -rf "${GLAB_TMPDIR}" /var/lib/apt/lists/*; \
    fi

# Conditional gh (GitHub CLI) install — the keyring is fetched over HTTPS from cli.github.com
# (trust anchored to GitHub TLS); apt then cryptographically verifies every gh package against it
# via signed-by, so each installed package is GPG-verified. The keyring itself is intentionally
# not hash-pinned because GitHub rotates it periodically — pinning would break builds on rotation.
# The keyring is already in dearmored format and written directly (no gpg --dearmor step needed),
# matching https://github.com/cli/cli/blob/trunk/docs/install_linux.md#debian-ubuntu-linux-raspberry-pi-os-apt.
ARG INSTALL_GH=false
RUN if [ "$INSTALL_GH" = "true" ]; then \
        apt-get update && \
        apt-get install -y --no-install-recommends curl && \
        curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
            -o /usr/share/keyrings/githubcli-archive-keyring.gpg && \
        chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg && \
        echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
            > /etc/apt/sources.list.d/github-cli.list && \
        apt-get update && \
        apt-get install -y --no-install-recommends gh && \
        rm -rf /var/lib/apt/lists/*; \
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
# The bookworm codename is hardcoded to match the base image — Docker's repo errors on mismatch.
# This installs binaries only; rootless dockerd startup is handled by a separate entrypoint concern.
ARG INSTALL_DOCKER=false
RUN if [ "$INSTALL_DOCKER" = "true" ]; then \
        apt-get update && \
        apt-get install -y --no-install-recommends ca-certificates curl && \
        curl -fsSL https://download.docker.com/linux/debian/gpg \
            -o /usr/share/keyrings/docker-archive-keyring.gpg && \
        chmod go+r /usr/share/keyrings/docker-archive-keyring.gpg && \
        echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/debian bookworm stable" \
            > /etc/apt/sources.list.d/docker.list && \
        apt-get update && \
        apt-get install -y --no-install-recommends \
            docker-ce \
            docker-ce-cli \
            containerd.io \
            docker-buildx-plugin \
            docker-compose-plugin && \
        rm -rf /var/lib/apt/lists/*; \
    fi

COPY entrypoint.sh /entrypoint.sh
RUN chmod 555 /entrypoint.sh

WORKDIR /workspace
RUN chown node:node /workspace

USER node

CMD ["echo", "foundry-worker image ready"]
