# Publisher image (ADR 0100): the only container that holds a run's provider token. It starts from the
# official alpine image pinned by digest — the single place the pin lives — and adds git and the Diff gate's
# secret scanner and nothing else: no claude CLI and no repository toolchain sit beside the token. busybox
# supplies the shell tooling publisher.sh needs. Foundry builds it from the workers/ context
# (docker build -f publisher/Dockerfile workers/).
FROM alpine:3.24@sha256:f59abe656b692d901e184be3445e084786602108599d2a51d229c0f5a48c71bf

RUN apk add --no-cache git

# The Diff gate's secret scanner (#788): gitleaks, pinned by version and by the SHA-256 of each architecture's
# release archive as published in the release's checksums file. A bump changes all three values together.
ARG TARGETARCH
ARG GITLEAKS_VERSION=8.30.1
ARG GITLEAKS_SHA256_AMD64=551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb
ARG GITLEAKS_SHA256_ARM64=e4a487ee7ccd7d3a7f7ec08657610aa3606637dab924210b3aee62570fb4b080
RUN set -eu; \
    arch="${TARGETARCH:-$(apk --print-arch)}"; \
    case "$arch" in \
        amd64|x86_64) asset=x64; sha256="$GITLEAKS_SHA256_AMD64" ;; \
        arm64|aarch64) asset=arm64; sha256="$GITLEAKS_SHA256_ARM64" ;; \
        *) echo "gitleaks: unsupported architecture $arch" >&2; exit 1 ;; \
    esac; \
    archive="/tmp/gitleaks.tar.gz"; \
    wget -q -O "$archive" "https://github.com/gitleaks/gitleaks/releases/download/v${GITLEAKS_VERSION}/gitleaks_${GITLEAKS_VERSION}_linux_${asset}.tar.gz"; \
    echo "$sha256  $archive" | sha256sum -c -; \
    tar -xzf "$archive" -C /usr/local/bin gitleaks; \
    rm -f "$archive"; \
    chmod 555 /usr/local/bin/gitleaks; \
    [ "$(gitleaks version)" = "$GITLEAKS_VERSION" ]

# Foundry's own scanner configuration: publisher.sh always passes it, so no repository file configures the scan.
COPY publisher/gitleaks.toml /etc/foundry/gitleaks.toml
RUN chmod 444 /etc/foundry/gitleaks.toml

# An unprivileged user: Foundry starts the container with every capability dropped and a read-only
# root filesystem, so nothing here needs root. /work (the scratch repository) and /handoff (the
# per-run Hand-off volume) are created owned by it, and the volumes mounted there inherit that
# ownership on first mount.
RUN addgroup -S -g 10001 publisher \
    && adduser -S -D -H -u 10001 -G publisher -h /work -s /sbin/nologin publisher \
    && mkdir -p /work /handoff \
    && chown publisher:publisher /work /handoff

COPY publisher/publisher.sh /usr/local/bin/publisher.sh
RUN chmod 555 /usr/local/bin/publisher.sh

USER publisher
WORKDIR /work

ENTRYPOINT ["/usr/local/bin/publisher.sh"]
