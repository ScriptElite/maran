//! The in-memory [`PhpHost`] the PHP tests decide against, and the inputs they
//! decide about.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host starts the install unit, writes into a root-owned pool directory and
//! restarts a live php-fpm: what a unit test can pin is which content an
//! operation chooses to write, which argv array it chooses to spawn, and when
//! it chooses to do neither. The write protocol has its own tests in
//! `safe_write`.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::php::model::php_override::PhpOverride;
use crate::php::model::pool_input::PoolInput;
use crate::php::{PhpHost, PhpOpError};
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, SafeWriteError};
use crate::test_support::recording_commands::RecordingCommands;

/// A [`PhpHost`] that keeps the pool directory, the installed versions and the
/// spawned commands in memory.
pub(crate) struct FakePhpHost {
    /// Directories that "exist" — the installed versions' pool directories,
    /// plus every directory the host has been asked to create.
    directories: Mutex<BTreeSet<PathBuf>>,
    /// The pool files, path to content.
    files: Mutex<BTreeMap<PathBuf, String>>,
    /// The spawned commands and the outcome they are answered with.
    recording: RecordingCommands,
    /// `php-fpm -t`'s answer, and what it says when it refuses.
    validation: Mutex<(i32, String)>,
    /// How many times a write actually reached the protocol — the number an
    /// idempotence test pins.
    writes: Mutex<usize>,
    /// How many times a removal actually reached the protocol, i.e. how many
    /// pools were really there to remove.
    removals: Mutex<usize>,
    /// Directories the host was asked to create as an account.
    created_as_account: Mutex<Vec<PathBuf>>,
    /// The mode the host was asked to give each directory it created.
    modes: Mutex<BTreeMap<PathBuf, u32>>,
    /// Per-subcommand answers that override the recording's single outcome,
    /// keyed by the first argument: status, stdout, stderr.
    answers: Mutex<BTreeMap<String, (i32, String, String)>>,
    /// Subcommands whose program "cannot be started".
    spawn_failures: Mutex<BTreeSet<String>>,
    /// The version whose pool directory appears once a `start` succeeds, which
    /// is what a real install unit leaves behind.
    installs_on_start: Mutex<Option<String>>,
}

impl FakePhpHost {
    /// A host with nothing installed and a validator that accepts everything.
    pub(crate) fn empty() -> Self {
        Self {
            directories: Mutex::new(BTreeSet::new()),
            files: Mutex::new(BTreeMap::new()),
            recording: RecordingCommands::new(),
            validation: Mutex::new((0, String::new())),
            writes: Mutex::new(0),
            removals: Mutex::new(0),
            created_as_account: Mutex::new(Vec::new()),
            modes: Mutex::new(BTreeMap::new()),
            answers: Mutex::new(BTreeMap::new()),
            spawn_failures: Mutex::new(BTreeSet::new()),
            installs_on_start: Mutex::new(None),
        }
    }

    /// A host with `versions` already installed, i.e. with their pool
    /// directories present — which is exactly what the operations read as
    /// "installed".
    pub(crate) fn with_installed(versions: &[&str]) -> Self {
        let host = Self::empty();
        {
            let mut directories = host.directories.lock().unwrap();
            for version in versions {
                directories.insert(PathBuf::from(distro().php_fpm_pool_directory(version)));
            }
        }
        host
    }

    /// Makes every spawned command fail, with `stderr` as the reason an
    /// operator would read in the log.
    pub(crate) fn reject_commands(&self, stderr: &str) {
        self.recording.set_next(1, "", stderr);
    }

    /// Answers every command whose first argument is `subcommand` with the
    /// given outcome, whatever the rest of the commands answer.
    pub(crate) fn answer(&self, subcommand: &str, status: i32, stdout: &str, stderr: &str) {
        self.answers.lock().unwrap().insert(
            subcommand.to_owned(),
            (status, stdout.to_owned(), stderr.to_owned()),
        );
    }

    /// Makes every command whose first argument is `subcommand` fail to spawn.
    pub(crate) fn fail_to_spawn(&self, subcommand: &str) {
        self.spawn_failures
            .lock()
            .unwrap()
            .insert(subcommand.to_owned());
    }

    /// Makes a successful `start` leave `version`'s pool directory behind, as
    /// the install unit's package manager does.
    pub(crate) fn installs_on_start(&self, version: &str) {
        *self.installs_on_start.lock().unwrap() = Some(version.to_owned());
    }

    /// Makes `php-fpm -t` refuse every pool, with `stderr` as the reason.
    pub(crate) fn reject_validation(&self, stderr: &str) {
        *self.validation.lock().unwrap() = (1, stderr.to_owned());
    }

    /// The content of a pool file, if the host holds one.
    pub(crate) fn config(&self, path: &Path) -> Option<String> {
        self.files.lock().unwrap().get(path).cloned()
    }

    /// Every command the host was asked to spawn.
    pub(crate) fn commands(&self) -> Vec<Vec<String>> {
        self.recording.calls()
    }

    /// The directories the host was asked to create as the account.
    pub(crate) fn created_as_account(&self) -> Vec<PathBuf> {
        self.created_as_account.lock().unwrap().clone()
    }

    /// Every directory the host was asked to create with `create_directory`,
    /// whatever its path.
    pub(crate) fn created_directories(&self) -> Vec<PathBuf> {
        self.modes.lock().unwrap().keys().cloned().collect()
    }

    /// The mode the host was asked to give `path`, if it created it.
    pub(crate) fn mode(&self, path: &Path) -> Option<u32> {
        self.modes.lock().unwrap().get(path).copied()
    }

    /// How many removals reached the protocol.
    pub(crate) fn removals(&self) -> usize {
        *self.removals.lock().unwrap()
    }

    /// How many writes reached the protocol.
    pub(crate) fn writes(&self) -> usize {
        *self.writes.lock().unwrap()
    }
}

impl ConfigHost for FakePhpHost {
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        let mut outcome = self.recording.record(program, arguments);
        let subcommand = arguments.first().copied().unwrap_or_default();

        if self.spawn_failures.lock().unwrap().contains(subcommand) {
            return Err(SafeWriteError::SpawnFailed {
                program: program.to_owned(),
                reason: "no such file or directory".to_owned(),
            });
        }
        if let Some((status, stdout, stderr)) = self.answers.lock().unwrap().get(subcommand) {
            outcome = CommandOutcome {
                status: *status,
                stdout: stdout.clone(),
                stderr: stderr.clone(),
            };
        }
        let installed = self.installs_on_start.lock().unwrap().clone();
        if let Some(version) = installed.filter(|_| subcommand == "start" && outcome.status == 0) {
            // Whichever family the operation was given, the unit leaves ITS
            // pool directory behind, so both spellings appear.
            let mut directories = self.directories.lock().unwrap();
            for family in [DistroFamily::Debian, DistroFamily::Rhel] {
                directories.insert(PathBuf::from(
                    adapter_for(family).php_fpm_pool_directory(&version),
                ));
            }
        }
        Ok(outcome)
    }
}

impl PhpHost for FakePhpHost {
    fn directory_exists(&self, path: &Path) -> bool {
        self.directories.lock().unwrap().contains(path)
    }

    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), PhpOpError> {
        self.directories.lock().unwrap().insert(path.to_path_buf());
        self.modes.lock().unwrap().insert(path.to_path_buf(), mode);
        Ok(())
    }

    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        directories: &[&Path],
        mode: u32,
    ) -> Result<(), PhpOpError> {
        let mut created = self.created_as_account.lock().unwrap();
        let mut known = self.directories.lock().unwrap();
        let mut modes = self.modes.lock().unwrap();
        for directory in directories {
            created.push(directory.to_path_buf());
            known.insert(directory.to_path_buf());
            modes.insert(directory.to_path_buf(), mode);
        }
        Ok(())
    }

    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        *self.writes.lock().unwrap() += 1;

        let (status, stderr) = self.validation.lock().unwrap().clone();
        if status != 0 {
            // The real protocol restores the previous content before
            // returning, so the fake leaves the map untouched.
            return Err(PhpOpError::PoolValidation { stderr });
        }

        self.files
            .lock()
            .unwrap()
            .insert(target.to_path_buf(), contents.to_owned());
        Ok(())
    }

    fn remove_config(
        &self,
        target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        // Absent is a success that runs NOTHING, exactly as the real protocol
        // decides — so a test can tell "removed a pool" from "was asked about a
        // pool that never existed", which is the difference between a reload
        // that happened and one that did not.
        if !self.files.lock().unwrap().contains_key(target) {
            return Ok(());
        }

        *self.removals.lock().unwrap() += 1;

        let (status, stderr) = self.validation.lock().unwrap().clone();
        if status != 0 {
            // The real protocol puts the bytes back before returning, so the
            // fake leaves the map untouched.
            return Err(PhpOpError::PoolValidation { stderr });
        }

        self.files.lock().unwrap().remove(target);
        Ok(())
    }

    fn validate_and_reload(
        &self,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        // Both commands are RECORDED even when the validator refuses, so a
        // test can tell "validated and then refused to reload" from "never
        // asked the validator at all" — the difference an empty recording
        // would hide.
        self.recording
            .record(validator.program, validator.arguments);

        let (status, stderr) = self.validation.lock().unwrap().clone();
        if status != 0 {
            return Err(PhpOpError::PoolValidation { stderr });
        }

        let reloaded = self.recording.record(reload.program, reload.arguments);
        if reloaded.status != 0 {
            return Err(PhpOpError::ReloadFailed {
                stderr: reloaded.stderr,
            });
        }

        Ok(())
    }
}

/// The adapter every test in this folder runs against. Which family is
/// immaterial to the decisions being tested; that the facts come from the
/// adapter rather than a literal is the point.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// A pool for `acme` at 8.3 with `overrides` and a ten-worker budget.
pub(crate) fn pool_input(overrides: Vec<PhpOverride>) -> PoolInput {
    PoolInput {
        account: AccountName::parse("acme").unwrap(),
        version: PhpVersion::parse("8.3").unwrap(),
        max_children: 10,
        overrides,
    }
}
