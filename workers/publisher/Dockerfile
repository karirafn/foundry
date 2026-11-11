# Publisher image (ADR 0100): the only container that holds a run's provider token. It starts from the
# official alpine image pinned by digest — the single place the pin lives — and adds git and nothing
# else: no claude CLI and no repository toolchain sit beside the token. busybox supplies the shell
# tooling publisher.sh needs. Foundry builds it from the workers/ context (docker build -f publisher/Dockerfile workers/).
FROM alpine:3.24@sha256:f59abe656b692d901e184be3445e084786602108599d2a51d229c0f5a48c71bf

RUN apk add --no-cache git

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
