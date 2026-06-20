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
        rm -rf /var/lib/apt/lists/*; \
    fi

# Conditional Angular CLI install
ARG INSTALL_ANGULAR=false
RUN if [ "$INSTALL_ANGULAR" = "true" ]; then \
        npm install -g @angular/cli@21; \
    fi

# Conditional glab (GitLab CLI) install — downloads the pre-built binary tarball from the official release
# SHA256 checksum sourced from https://gitlab.com/gitlab-org/cli/-/releases/v1.52.0
ARG INSTALL_GLAB=false
RUN if [ "$INSTALL_GLAB" = "true" ]; then \
        apt-get update && \
        apt-get install -y --no-install-recommends curl && \
        GLAB_VERSION="1.52.0" && \
        GLAB_SHA256="b65bf65766e498cb725b80f0a93fcfc7ae615f69ca03118991be7ecc11d6567e" && \
        curl -fsSL "https://gitlab.com/gitlab-org/cli/-/releases/v${GLAB_VERSION}/downloads/glab_${GLAB_VERSION}_linux_amd64.tar.gz" \
            -o /tmp/glab.tar.gz && \
        echo "${GLAB_SHA256}  /tmp/glab.tar.gz" | sha256sum -c - && \
        tar -xzf /tmp/glab.tar.gz -C /tmp && \
        mv /tmp/bin/glab /usr/local/bin/glab && \
        chmod 755 /usr/local/bin/glab && \
        rm -rf /tmp/glab.tar.gz /tmp/bin && \
        rm -rf /var/lib/apt/lists/*; \
    fi

COPY entrypoint.sh /entrypoint.sh
RUN chmod 555 /entrypoint.sh

WORKDIR /workspace
RUN chown node:node /workspace

USER node

CMD ["echo", "foundry-worker image ready"]
