#!/usr/bin/env bash
# common.sh: the constants and helpers every verb of the `maran` break-glass command shares.
#
# Sourced by /usr/local/sbin/maran AFTER its root check and before any verb runs; never executed
# on its own. It carries no `set` option of its own — the wrapper has already set -euo pipefail.
#
# The one rule that shapes everything here: the wrapper is root, the panel is not, and root must
# never touch the panel's data directly. Every question about panel state is therefore put to the
# panel's own binary (`Maran.Host --cli <verb>`) run as the service account, with the same
# environment the service itself starts with. Root contributes only what only root can read:
# systemd facts, journals, the firewall ruleset.

readonly MARAN_CLI_BINARY="/usr/local/maran/api/Maran.Host"
readonly MARAN_CLI_ENV_FILE="/etc/maran/panel.env"
readonly MARAN_CLI_SERVICE_USER="maran"

# cli_fail: prints one message on stderr and ends the command with exit 1.
cli_fail() {
  echo "maran: $*" >&2
  exit 1
}

# cli_audit_log: writes one fixed-shape line to the authentication log. It takes only verbs and
# outcomes — never an argument the operator typed and never a secret — and passes them as separate
# argv words, so no operator text is ever interpreted by anything.
cli_audit_log() {
  local verb="$1" outcome="$2" operator="${SUDO_USER:-root}"
  # SUDO_USER is set by sudo and is advisory; anything outside the account-name alphabet is logged
  # as a placeholder rather than passed on.
  [[ "$operator" =~ ^[A-Za-z0-9._-]{1,64}$ ]] || operator="unknown"
  logger -t maran-cli -p authpriv.notice -- "verb=${verb} outcome=${outcome} operator=${operator}" \
    2>/dev/null || true
}

# cli_dotnet: runs the panel's own binary in CLI mode as the service account, with the panel's
# environment, and returns its exit status. Standard input and output pass straight through, so a
# caller never has to capture what the binary prints — which matters for the one verb that prints a
# credential.
#
# The environment is read the way systemd reads it and the way 65-schema.sh reads it, inside a
# subshell so the secrets never reach the wrapper's own environment or any other command it runs.
# The working directory is the panel's own install directory, which the service account can enter
# wherever the operator happens to be standing.
cli_dotnet() {
  [ -x "$MARAN_CLI_BINARY" ] || cli_fail "${MARAN_CLI_BINARY} is missing; is Maran installed?"
  [ -r "$MARAN_CLI_ENV_FILE" ] || cli_fail "${MARAN_CLI_ENV_FILE} is missing; is Maran installed?"
  (
    set -a
    # shellcheck disable=SC1090
    . "$MARAN_CLI_ENV_FILE"
    set +a
    cd "$(dirname "$MARAN_CLI_BINARY")" || exit 1
    exec runuser -u "$MARAN_CLI_SERVICE_USER" -- "$MARAN_CLI_BINARY" --cli "$@"
  )
}
