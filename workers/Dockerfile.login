ARG BASE_IMAGE=foundry-claude-base:local
FROM ${BASE_IMAGE}

# Login image — runs 'claude auth login --claudeai' driven entirely by the
# Foundry Docker API. Operators do not run 'docker run' manually; Foundry
# starts and controls this container from the dashboard login flow.
#
# Runtime contract (managed by Foundry via the Docker API):
#   - The credential volume is mounted at /home/node/.claude (CLAUDE_CONFIG_DIR).
#   - The container entrypoint bootstraps a FIFO at /tmp/ci, writes the FIFO
#     writer's sleep-holder PID to /tmp/ci.pid, and invokes
#     'claude auth login --claudeai' (wrapped in 'timeout -k 10 <session-timeout>')
#     with the FIFO on stdin.
#   - Foundry streams stdout/stderr to extract the authorization URL.
#   - Foundry delivers the operator's code via 'docker exec' into the FIFO,
#     then kills the sleep-holder so the CLI receives EOF and proceeds.
#   - On success, Foundry captures account identity by running
#     'claude auth status --json' in a separate short-lived helper container
#     that mounts the same credential volume (see ADR 0027).
#   - The container is created with AutoRemove and the 'timeout' bound, so it
#     self-terminates and Docker removes it even if Foundry is not alive to
#     tear it down; a 'foundry.transient=true' label lets the reapers reclaim
#     any orphan (see ADR 0031).
#
# No ENTRYPOINT is set here — the entrypoint command is supplied by Foundry
# via the Docker API container-create spec (LoginContainerSpec).

USER node
