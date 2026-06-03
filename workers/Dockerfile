FROM node:22-bookworm-slim

# Install git explicitly — not present in slim variant
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*

# Disable telemetry and auto-update
ENV CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
ENV DISABLE_AUTOUPDATER=1

# Pin exact version to avoid unexpected CLI behaviour changes
RUN npm install -g @anthropic-ai/claude-code@2.1.138

# Pre-create ~/.claude owned by the built-in node user (UID 1000)
RUN mkdir -p /home/node/.claude && chown node:node /home/node/.claude

# Conditional .NET SDK install
ARG INSTALL_DOTNET=false
RUN if [ "$INSTALL_DOTNET" = "true" ]; then \
        apt-get update && \
        apt-get install -y --no-install-recommends wget ca-certificates && \
        wget -q https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
        dpkg -i packages-microsoft-prod.deb && \
        rm packages-microsoft-prod.deb && \
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
