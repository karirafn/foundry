ARG BASE_IMAGE=foundry-claude-base:local
FROM ${BASE_IMAGE}

# Login image — minimal image for running 'claude /login' interactively.
# Mount the credential volume at /home/node/.claude at runtime so the OAuth
# credentials written by 'claude /login' persist on the host volume.
#
# Example:
#   docker run -it --rm \
#     -v foundry-claude-credentials:/home/node/.claude \
#     foundry-claude-login:local

USER node

ENTRYPOINT ["claude", "/login"]
