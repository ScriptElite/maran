# Threat note: the break-glass `maran` command-line tool (issue #130)

`rules/security.md` requires a threat note, written first, for a change to authentication, token
handling and the installer's privileged steps, plus a second reviewer. This change gives the server's
root user a command that can mint an administrator password-reset link (and optionally clear that
administrator's two-factor enrolment) and a command that gathers diagnostics. Both are privileged
surfaces, so this note argues why they are safe and says what is not proven.

**Second review: OUTSTANDING — must not merge to main.** The work was carried out by an agent session,
which cannot obtain a second reviewer. This branch MUST NOT merge to `main` until a human reviewer has
read it. The owner is told at hand-off that this surface carries an outstanding review.

## The problem being solved

An administrator who has lost the password (or the authenticator) of the only admin account has no way
back in: the panel cannot mail a reset when SMTP is unconfigured, and the setup window is closed once
an administrator exists. The operator holds root on the server, so the recovery path is a command run
as root on that server, not a new network surface.

## Shape

`maran` is an installed, root-only bash wrapper (`/usr/local/sbin/maran`, 0700 root:root, libraries in
`/usr/local/maran/cli/`). It checks euid (exit 77 otherwise), selects a verb from a `case` allow-list,
and runs `Maran.Host --cli <verb>` as the unprivileged `maran` user via `runuser`. All database access
therefore happens as `maran`, through the same module code the API uses. Root never touches PostgreSQL.
No new RPC, daemon, port or outbound call is introduced (security.md item 10).

Verbs: `status`, `admin-reset-password <email|username> [--clear-two-factor] [--allow-non-terminal]`,
`collect-diagnostics [--out DIR] [--no-logs]`.

Owner decisions (2026-10-29): `--clear-two-factor` is in scope; the installer writes `PasswordReset__PanelUrl`
so the CLI prints a full link; CLI operator messages are English literals; edits to the rules maps are
approved.

## What root can now do

- Mint a password-reset link for an active administrator account, which lets root choose that
  administrator's password. Optionally clear that administrator's two-factor enrolment.
- Reading a diagnostics bundle of the allow-listed content described below.

Root could already do all of this and more: root can read `panel.env`, edit the database through
`runuser -u postgres`, and replace the binaries. The tool does not widen what root can do; it makes the
operation supported, bounded to administrators, audited, and removes the incentive to hand-edit the
database (which leaves no audit row). Stated plainly: anyone who has root has the panel's
administrators; this is not a new trust boundary and the note does not claim it is a reduction.

## What a process running as `maran` gains: nothing new

The wrapper is root-only and the `--cli` entry point is reachable by `maran` already (it is the same
binary the service runs). A compromised API process (uid `maran`) can already execute that binary with
`--cli` and could already do, through the database connection it holds, anything the CLI does. The CLI
adds no privilege to that uid: it does not install a setuid file, a sudoers entry, a new socket or a
group membership. The root wrapper is not callable by `maran` (0700 root:root, euid check exits 77).
A reviewer should confirm this by listing the files the installer step creates and their modes, not by
trusting this paragraph.

## Link exposure

The reset link is a credential. It leaves the system in exactly one place:

- **Terminal stdout only.** The CLI refuses to print the link when stdout is not a terminal unless the
  operator passes `--allow-non-terminal`, so a pipe into a log file, a CI capture or `tee` is an
  explicit act, not an accident.
- **Never in a log, the audit row or the journal.** The audit row and the `authpriv` journal line name
  the operator, the account and the action, and carry no token and no link. The plaintext is held in
  the existing `SensitiveString` type until the moment it is printed.
- **The web server does not retain it.** The nginx access log strips the query string, so a link that is
  later opened in a browser does not leave its token in `access.log`.
- **One hour, single use.** The token lives 1 h, only its digest is stored, and using it spends it.
  Outstanding reset tokens for the account are spent when a new one is minted, so there is never more
  than one live link.
- **Host.** The absolute link is built from the configured `PasswordReset__PanelUrl` (written by the
  installer), never from anything the operator or a request supplies. When no address is configured the
  CLI prints a root-relative path and says so.

A side effect that is intended: minting the link also clears the account's lockout and revokes its
sessions, so a stolen session does not survive a recovery.

## Wrapper injection surface

The wrapper is bash running as root, so it is the part most worth attacking.

- Verb selection is a `case` allow-list; an unknown verb exits non-zero before any other work.
- The `Maran.Host` command line is a fixed argv array. User input is appended only after `--`, so it is
  never parsed as an option by the .NET host.
- User input (email or username, `--out DIR`) is matched against an explicit regex before use, and a
  mismatch is a refusal, not a sanitising step.
- No `eval`, no `sh -c`, no string-built commands, no unquoted expansion of user input (security.md
  item 3). Environment is loaded the same way the installer's schema step loads it.
- The environment of the invoking shell is not trusted: the wrapper uses absolute paths for the
  binaries it calls.

**Stated limit:** shell quoting correctness was reviewed by reading and by the polygon tests, not by a
formal analysis. A reviewer should read the wrapper line by line with an input such as
`a b; touch /x`, `$(id)`, a leading `-`, and an embedded newline in mind.

## `/usr/local/sbin` and the RHEL `secure_path`

The wrapper lives in `/usr/local/sbin` so that it is on root's `PATH` on Debian-family systems. On
RHEL-family systems `sudo` resets `PATH` to `secure_path`, which lists `/usr/local/sbin` on current
releases but is configurable; the installer therefore also tells the operator the absolute path, and
the documentation uses `sudo /usr/local/sbin/maran`. A directory on that path that a non-root user can
write to would be a way to shadow the command: `/usr/local/sbin` is root-owned 0755 on a default
system, and the installer step verifies the mode of the file it writes, not of the directory.

## Diagnostics: what can leak, and the residual

`collect-diagnostics` produces a 0600 tarball (created O_EXCL from a root-owned 0700 staging directory) from an
allow-list only. Included: a manifest; `status.txt`, which is the output of `maran status` and so carries
the service states and the panel's own report (database reachability, agent handshake, pending
migrations, API version, administrator count, setup-window state) - there is no separate database
collection; `/etc/os-release`; `systemctl cat` of the panel units; `systemctl list-units` for the Maran
services, PostgreSQL and nginx; the two Maran nginx configuration files; environment variable NAMES
(never values); the panel certificate's subject, issuer and validity dates (never the key); the nft
ruleset; and, unless `--no-logs`, the journal for 7 days (size capped) of the API, agent and nginx units
and the tail of the top-level `/var/log/maran/*.log` and `*.log.1` files. Excluded by construction:
`/var/log/maran/sites/` and `/var/log/maran/panel/`, `/home`, `/var/lib/maran*`, backups, keys and
dumps, and the values of `panel.env`.

Leak classes considered:

1. **Secret values in configuration** (database password, signing keys, the setup token). Env values are
   never collected; the live configuration's secret literals are also fed to the redactor as exact
   strings.
2. **Secrets in log lines** (connection strings, bearer tokens, reset-link query parameters, password
   fields echoed in an exception message).
3. **Customer data** (domains, mailbox names, file paths under `/home`). Paths and domains in logs are
   not secret to the operator but are customer-identifying; the bundle is for the operator to send
   deliberately, and the documentation says to review it first.
4. **Link/token fragments** from a recovery run on the same host.

Redaction has three stages: live configuration literals, a pattern set shared with the agent's error
translator (extracted so there is one copy), then extract-and-scan of the finished bundle with a
second-look pass that fails closed (the bundle is deleted and the command exits non-zero if the scan
finds a hit).

**Residual, stated honestly:** redaction is pattern- and literal-based. It is not provably complete. A
secret that is neither a known literal nor shaped like a pattern (a free-text password in a log line, a
token in a format nobody listed) will pass. The second-look scan reduces this risk; it cannot remove it.
The allow-list is the primary control and redaction is the net under it.

## The operator name is unverified

The audit row records the operator as the value of `SUDO_USER` (or, failing that, `root`). That
variable is set by the caller's environment: a root shell can set it to anything. It is recorded as a
**claimed** operator, labelled as such in the row, and is useful for accountability among honest
operators, not as proof. The unforgeable part of the record is the `authpriv` journal line written by
`logger -t maran-cli -p authpriv.notice` from the root process, together with the audit row's system
actor and timestamp.

## Audit and journal

- Every reset (granted or refused) writes an audit row through `IdentityAuditJournal`
  (`RecordConsoleRecoveryAsync`, system actor, claimed operator, target account, whether two-factor was
  cleared, outcome) and an `authpriv.notice` syslog line.
- `status` and `collect-diagnostics` are not journalled as audit events (read-only), but the diagnostics
  run is logged to `authpriv`.
- The audit row never carries the token or the link.
- Refusals are typed: unknown account, account not an administrator, account not active. When no user
  exists at all the CLI points at `/setup` and `Setup__Token` and never prints the token.

Clearing two-factor is the sharpest option: an attacker with the link and the password reset would still
need the second factor; clearing it removes that. It is therefore a separate flag, never implied, and
recorded in the audit row.

## Reviewer checklist

1. The wrapper: `case` allow-list, euid check (77), regexes, argv-only invocation, nothing after `--`
   interpreted, no `eval`/`sh -c`, absolute binary paths, file mode 0700 root:root.
2. The installer step creates nothing else (no sudoers entry, no setuid, no group change) and the
   directory modes are as described.
3. The link is printed in exactly one place and only to a terminal unless `--allow-non-terminal`;
   `grep -rn "Reveal()"` over the new Identity code lists only that door (compare with the
   invitation-link note's check).
4. The audit row and the `authpriv` line contain no token or link.
5. The reset token lifetime is 1 h, single use, digest only, and previous tokens are spent.
6. Only Admin + Active accounts are accepted, and each refusal is typed.
7. The nginx configuration strips the query string from the access log.
8. Redaction: the pattern set is the one the agent uses plus the diagnostics superset; the fail-closed
   second-look scan deletes the bundle; env values are not collected.
9. The diagnostics allow-list contains none of `/home`, `/var/lib/maran*`, backups, keys, dumps, and reads only the top-level `/var/log/maran/*.log` and `*.log.1` (not `sites/` or `panel/`).
10. `--migrate` still works as an alias and the CLI logging goes to stderr at Warning, never to a file.
11. The documentation tells the operator that the diagnostics bundle should be reviewed before sending.

## What the author could not verify

- **No second reviewer.** This is an agent session.
- **Completeness of redaction.** No test can show the absence of an unlisted secret shape.
- **Real-host behaviour on RHEL-family systems** (`secure_path`, SELinux contexts on the wrapper and
  `/usr/local/maran/cli`) was reasoned about, not exercised, unless the polygon run records otherwise.
- **That root cannot be tricked into running a wrapper someone replaced:** that is the integrity of the
  host, outside this change.
- **The claimed operator name** cannot be verified by design (above).
- **Behaviour under a locale or shell environment hostile to the wrapper** beyond what the polygon
  exercises.

**Second review: OUTSTANDING — must not merge to main.**
