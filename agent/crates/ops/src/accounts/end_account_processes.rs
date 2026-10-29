//! Ending every process of a hosting account's uid, and proving none is left,
//! before the account's user is deleted.

use std::time::Duration;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::pkill_answer::PkillAnswer;
use maran_agent_core::utils::pkill_uid_arguments::pkill_uid_arguments;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_distro::DistroAdapter;

use crate::accounts::{AccountError, SystemHost};

/// How many rounds of kill-then-check the cull makes before it gives up.
///
/// Several rounds and not one because a single pass has a gap: a process that
/// forked a moment before `pkill` read `/proc` is not in that snapshot and
/// survives it, and its child is signalled only by the NEXT pass. Bounded
/// because the loop runs while the account's lock is held, and a process that
/// cannot be killed (uninterruptible sleep) must end in a refusal rather than in
/// an operation that never returns.
const ROUNDS: u32 = 5;

/// How long the cull waits between a check that found survivors and the next
/// round.
///
/// Long enough for a killed process to be reaped and leave `/proc`; short enough
/// that all rounds together (four pauses) hold the lock for under a second of
/// waiting. Spent through [`SystemHost::pause`], so no test waits it.
const PAUSE: Duration = Duration::from_millis(200);

/// The signal that ends a process and cannot be caught or ignored.
///
/// `KILL` and not `TERM`: the account is being deleted, there is nothing to shut
/// down gracefully, and a process that ignores `TERM` is exactly the survivor
/// this operation exists to prevent.
const KILL_SIGNAL: &str = "KILL";

/// The signal that is never delivered and only reports whether a process of the
/// uid exists.
const PROBE_SIGNAL: &str = "0";

/// Kills every process running as `account`'s uid and verifies that none is
/// left, so that `userdel` frees a uid nothing is still using.
///
/// # Why this is not `userdel`'s job
///
/// `userdel` refuses a user with live processes (exit 8) only by statting
/// `/proc/<pid>/root`, which the kernel allows for a foreign uid's process only
/// to a caller with `CAP_SYS_PTRACE`. This agent's bounding set lacks that
/// capability on purpose, so `userdel` exits 0, the user is gone, and the
/// process survives owned by a uid the next `useradd` hands to a new tenant
/// (measured on both families; issue #103).
///
/// # How
///
/// Up to five rounds of: `pkill --signal KILL --count --uid <uid>`, then
/// `pkill --signal 0 --count --uid <uid>` as the check. The check uses the same
/// matcher as the kill, so the two cannot disagree about which processes count.
/// A round that finds survivors waits through [`SystemHost::pause`] and kills
/// again; the kill is repeated rather than only the check, because a process
/// forked just before the previous pass is only visible to the next one.
///
/// # How the uid is confined
///
/// The uid comes from exactly one place, the [`ManagedAccount`] the caller holds,
/// and the type refuses uid 0, any id below the distro's floor and any home other
/// than the one this agent gives a hosting account — so a system user's processes
/// cannot be named here (the same argument as `logins::end_account_sessions`).
/// The program is `distro.pkill_binary()`, every other argument is a constant or
/// the uid rendered with `Display`, and the host spawns an argv array: no shell,
/// nothing of the request in the command line (rules/security.md items 3 and 12).
///
/// # The lock
///
/// Does not take a lock and must be called with the account's lock held, which is
/// what lets it assume no `fork_as_account` child of this agent runs as the uid.
/// That lock is process-local.
///
/// # What it cannot see
///
/// `pkill` reads `/proc`; on a host mounted with `hidepid` a process of another
/// uid is invisible to it, so both the kill and the check are blind in the same
/// direction. Zombies count as survivors until reaped, and a process in
/// uninterruptible sleep cannot be killed: both end in
/// [`AccountError::ProcessesRemain`], the safe direction.
///
/// # Errors
///
/// - [`AccountError::CommandUnavailable`] when `pkill` could not be started.
/// - [`AccountError::Bound`] when a `pkill` outran its deadline.
/// - [`AccountError::CommandFailed`] when a `pkill` exited with anything but
///   "matched" (0) or "nothing matched" (1). Whether anything was signalled is
///   then unknown, so it is never read as "no processes".
/// - [`AccountError::ProcessesRemain`] when the last round's check still found a
///   process. Nothing has been deleted at that point.
pub(crate) fn end_account_processes(
    host: &dyn SystemHost,
    distro: &dyn DistroAdapter,
    account: &ManagedAccount,
) -> Result<(), AccountError> {
    let pkill = distro.pkill_binary();
    let kill = pkill_uid_arguments(KILL_SIGNAL, account.uid());
    let kill: Vec<&str> = kill.iter().map(String::as_str).collect();
    let probe = pkill_uid_arguments(PROBE_SIGNAL, account.uid());
    let probe: Vec<&str> = probe.iter().map(String::as_str).collect();

    for round in 1..=ROUNDS {
        let killed = host.run(&pkill, &kill, CallClass::Mutation)?;
        if let PkillAnswer::Refused(_) = PkillAnswer::from_status(killed.status) {
            return Err(AccountError::command_failed(pkill.as_str(), &killed));
        }

        let checked = host.run(&pkill, &probe, CallClass::Mutation)?;
        match PkillAnswer::from_status(checked.status) {
            PkillAnswer::NothingMatched => return Ok(()),
            PkillAnswer::Refused(_) => {
                return Err(AccountError::command_failed(pkill.as_str(), &checked));
            }
            PkillAnswer::Matched => {
                if round < ROUNDS {
                    host.pause(PAUSE);
                }
            }
        }
    }

    tracing::warn!(
        username = account.name().as_str(),
        uid = account.uid(),
        rounds = ROUNDS,
        "processes of the account survived every kill round; the deletion was stopped before userdel"
    );
    Err(AccountError::ProcessesRemain {
        username: account.name().as_str().to_owned(),
    })
}

#[cfg(test)]
#[path = "../tests/accounts/end_account_processes_tests.rs"]
mod tests;
