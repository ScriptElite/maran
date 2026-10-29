//! A real php-fpm master started by a test, on a configuration of its own.
//!
//! The polygon has no init system, so no php-fpm master runs unless a test
//! starts one. This one includes exactly the pool files the test names (not the
//! family's whole pool directory, whose default pool listens in a directory the
//! image does not create), keeps its pid file and log in a directory of its own,
//! and is the process `systemctl-stand-in.sh` finds by its title and signals
//! when the agent reloads a php-fpm unit.

use std::os::unix::net::UnixStream;
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use maran_distro::DistroAdapter;

use crate::polygon_php::PolygonPhp;

/// Where a master keeps its pid file and log, one directory per master.
const MASTER_DIRECTORY_PREFIX: &str = "/run/maran-polygon-fpm-";

/// How long a master is given to write its pid file, and to leave after a TERM.
const MASTER_BUDGET: Duration = Duration::from_secs(20);

/// How long [`PolygonPhpMaster::poll`] waits between two looks.
const POLL_INTERVAL: Duration = Duration::from_millis(50);

/// A running php-fpm master, stopped when this value is dropped.
pub struct PolygonPhpMaster {
    /// The directory holding its configuration, pid file and log.
    directory: PathBuf,
    /// The master's process id, read from its own pid file.
    pid: rustix::process::Pid,
}

impl PolygonPhpMaster {
    /// Starts a daemonised master that serves the pool file at `pool_file`.
    ///
    /// `tag` names the directory the master keeps its files in, so two tests
    /// never share one.
    ///
    /// # Panics
    ///
    /// Panics when php-fpm cannot be run, refuses the configuration, or writes
    /// no pid file within [`MASTER_BUDGET`]; the message carries its own log.
    pub fn start(distro: &dyn DistroAdapter, pool_file: &Path, tag: &str) -> Self {
        let directory = PathBuf::from(format!("{MASTER_DIRECTORY_PREFIX}{tag}"));
        let _ = std::fs::remove_dir_all(&directory);
        std::fs::create_dir_all(&directory).expect("the master's directory must be creatable");

        let pid_file = directory.join("php-fpm.pid");
        let log_file = directory.join("php-fpm.log");
        let configuration = directory.join("php-fpm.conf");
        std::fs::write(
            &configuration,
            format!(
                "[global]\npid = {}\nerror_log = {}\ndaemonize = yes\ninclude = {}\n",
                pid_file.display(),
                log_file.display(),
                pool_file.display()
            ),
        )
        .expect("the master's configuration must be writable");

        let binary = distro.php_fpm_binary(&PolygonPhp::version());
        let started = std::process::Command::new(&binary)
            .arg("-y")
            .arg(&configuration)
            .output()
            .unwrap_or_else(|error| panic!("the polygon image installs {binary}: {error}"));
        assert!(
            started.status.success(),
            "php-fpm must start on the pool {pool_file:?}:\n{}\n{}",
            String::from_utf8_lossy(&started.stderr),
            std::fs::read_to_string(&log_file).unwrap_or_default()
        );

        let mut pid = None;
        Self::poll(MASTER_BUDGET, || {
            pid = std::fs::read_to_string(&pid_file)
                .ok()
                .and_then(|text| text.trim().parse::<i32>().ok())
                .and_then(rustix::process::Pid::from_raw);
            pid.is_some()
        });
        let pid = pid.unwrap_or_else(|| {
            panic!(
                "php-fpm wrote no pid file within {MASTER_BUDGET:?}; its log:\n{}",
                std::fs::read_to_string(&log_file).unwrap_or_default()
            )
        });

        Self { directory, pid }
    }

    /// Whether a connection to the unix socket at `socket` is accepted right now.
    pub fn accepts_connections(socket: &Path) -> bool {
        UnixStream::connect(socket).is_ok()
    }

    /// Whether the master is still a live process.
    pub fn is_alive(&self) -> bool {
        Path::new(&format!("/proc/{}", self.pid.as_raw_nonzero())).exists()
    }

    /// Runs `condition` until it holds or `budget` passes; says which.
    ///
    /// A bounded poll and not a sleep: the caller learns the moment the state it
    /// waits for appears, and a state that never appears is a `false`, not a hang.
    pub fn poll(budget: Duration, mut condition: impl FnMut() -> bool) -> bool {
        let deadline = Instant::now() + budget;
        loop {
            if condition() {
                return true;
            }
            if Instant::now() >= deadline {
                return false;
            }
            std::thread::sleep(POLL_INTERVAL);
        }
    }
}

impl Drop for PolygonPhpMaster {
    /// Stops the master with TERM, waits for it to leave, and removes its directory.
    ///
    /// Reported and not panicked, like every other cleanup in the polygon: a
    /// master left behind would answer the next test's reload.
    fn drop(&mut self) {
        let _ = rustix::process::kill_process(self.pid, rustix::process::Signal::TERM);
        if !Self::poll(MASTER_BUDGET, || !self.is_alive()) {
            eprintln!("the polygon php-fpm master {:?} did not stop", self.pid);
            let _ = rustix::process::kill_process(self.pid, rustix::process::Signal::KILL);
        }
        let _ = std::fs::remove_dir_all(&self.directory);
    }
}
