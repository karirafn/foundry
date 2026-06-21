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

COPY entrypoint.sh /entrypoint.sh
RUN chmod 555 /entrypoint.sh

WORKDIR /workspace
RUN chown node:node /workspace

USER node

CMD ["echo", "foundry-worker image ready"]
