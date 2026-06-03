FROM node:22-bookworm

# git is included in node:22-bookworm; install explicitly to make the dependency visible
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*

# Pin exact version to avoid unexpected CLI behaviour changes
RUN npm install -g @anthropic-ai/claude-code@2.1.138

# Create a non-root user and workspace directory with correct ownership
RUN useradd --create-home --shell /bin/bash claude && \
    mkdir -p /workspace && \
    chown claude:claude /workspace && \
    mkdir -p /home/claude/.claude && \
    chown claude:claude /home/claude/.claude

COPY entrypoint.sh /entrypoint.sh
RUN chmod 555 /entrypoint.sh

WORKDIR /workspace

USER claude

CMD ["echo", "foundry-worker image ready"]
