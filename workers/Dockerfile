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
        apt-get install -y --no-install-recommends curl && \
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

COPY entrypoint.sh /entrypoint.sh
RUN chmod 555 /entrypoint.sh

WORKDIR /workspace
RUN chown node:node /workspace

USER node

CMD ["echo", "foundry-worker image ready"]
