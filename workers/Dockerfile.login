ARG BASE_IMAGE=foundry-claude-base:local
FROM ${BASE_IMAGE}

# Login image — minimal image for running 'claude /login' interactively.
# Mount the credential volume at /home/node/.claude at runtime so the OAuth
# credentials written by 'claude /login' persist on the host volume.
#
# No ENTRYPOINT is set here — the 'claude /login' subcommand is supplied by the
# operator on the 'docker run' command line (see GetLoginCommand), so it executes
# exactly once. Hardcoding ENTRYPOINT ["claude", "/login"] would cause Docker to
# prepend it to the CMD args and run 'claude /login claude /login' instead.
#
# Example:
#   docker run -it --rm \
#     -v foundry-claude-credentials:/home/node/.claude \
#     -e CLAUDE_CONFIG_DIR=/home/node/.claude \
#     foundry-claude-login:local \
#     claude /login

USER node
