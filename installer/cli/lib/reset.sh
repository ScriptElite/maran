#!/usr/bin/env bash
# reset.sh: `maran admin-reset-password` — issues a one-hour password-reset link for an active
# administrator, for the operator who has lost access to the panel.
#
# This file only validates what the operator typed and hands it over. Every decision — whether the
# account exists, is an administrator, is active; spending older links; clearing the lockout;
# revoking sessions; the audit row — is the panel's own code running as the service account.
#
# The link is a credential. The panel binary prints it on standard output and this file never sees
# it: no command substitution, no tee, no pipe sits between that output and the terminal.

# cli_reset: the verb. Arguments: <email-or-username> [--clear-two-factor] [--allow-non-terminal].
cli_reset() {
  local identifier="" clear_two_factor=0 allow_non_terminal=0 arg
  for arg in "$@"; do
    case "$arg" in
      --clear-two-factor) clear_two_factor=1 ;;
      --allow-non-terminal) allow_non_terminal=1 ;;
      -*) cli_fail "unknown option '${arg}'. Usage: maran admin-reset-password <email|username> [--clear-two-factor] [--allow-non-terminal]" ;;
      *)
        [ -z "$identifier" ] || cli_fail "give exactly one email or username"
        identifier="$arg"
        ;;
    esac
  done
  [ -n "$identifier" ] || cli_fail "Usage: maran admin-reset-password <email|username> [--clear-two-factor] [--allow-non-terminal]"

  # Conservative on purpose: letters, digits and the punctuation real emails and usernames use.
  # Anything else is refused here, so the panel never receives it, whatever it would have done.
  [[ "$identifier" =~ ^[A-Za-z0-9][A-Za-z0-9._@+-]{0,253}$ ]] \
    || cli_fail "the email or username contains characters this command does not accept"

  # A link printed into a pipe or a file ends up in places a terminal does not: shell history of a
  # wrapper script, a CI log, a logfile on disk. Refused unless the operator says it is deliberate.
  if [ "$allow_non_terminal" -eq 0 ] && [ ! -t 1 ]; then
    cli_fail "refusing to print a reset link to a non-terminal; pass --allow-non-terminal if that is deliberate"
  fi

  # Each shape is a fixed argv; the operator's text is only ever the last word, after `--`.
  local status=0
  if [ "$clear_two_factor" -eq 1 ] && [ "$allow_non_terminal" -eq 1 ]; then
    cli_dotnet admin-reset-password --clear-two-factor --allow-non-terminal -- "$identifier" || status=$?
  elif [ "$clear_two_factor" -eq 1 ]; then
    cli_dotnet admin-reset-password --clear-two-factor -- "$identifier" || status=$?
  elif [ "$allow_non_terminal" -eq 1 ]; then
    cli_dotnet admin-reset-password --allow-non-terminal -- "$identifier" || status=$?
  else
    cli_dotnet admin-reset-password -- "$identifier" || status=$?
  fi

  if [ "$status" -eq 0 ]; then
    cli_audit_log admin-reset-password issued
  else
    cli_audit_log admin-reset-password "refused-or-failed-${status}"
  fi
  return "$status"
}
