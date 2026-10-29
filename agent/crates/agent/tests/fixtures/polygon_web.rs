//! The polygon's own nginx, and a plain HTTP client for asking it questions.
//!
//! The helpers the PHP socket-migration suite needs from the web server: start
//! the master once, reload it, and fetch a page from it over a real socket. A
//! hand-written request rather than an HTTP client dependency, because one GET
//! with a `Host` header is the whole protocol this needs (rules/security.md
//! item 11 makes a new dependency something to justify).

use std::io::{Read as _, Write as _};
use std::path::Path;
use std::time::{Duration, Instant};

use maran_distro::DistroAdapter;

/// How long one HTTP exchange against the polygon's nginx is given.
///
/// Bounded on purpose: a request to a socket nothing answers would otherwise
/// hang the run, and a hang is read as a flaky runner and retried.
const HTTP_TIMEOUT: Duration = Duration::from_secs(15);

/// How long [`PolygonWeb::fetch_until`] keeps asking before it reports what it got.
const SERVE_DEADLINE: Duration = Duration::from_secs(20);

/// How long [`PolygonWeb::fetch_until`] waits between two attempts.
const SERVE_POLL_INTERVAL: Duration = Duration::from_millis(100);

/// The polygon's nginx: one unit, because starting it, reloading it and asking
/// it for a page are one subject — what this host's web server answers.
pub struct PolygonWeb;

impl PolygonWeb {
    /// Starts an nginx master if this run has not started one yet.
    ///
    /// Without a running master the stand-in service manager has nothing to
    /// signal and answers a reload with success, so the reload step would be a
    /// no-op no test could tell from a real one. Idempotent: starting a second
    /// master would fail on the listening socket.
    ///
    /// # Panics
    ///
    /// Panics when nginx cannot be started.
    pub fn ensure_running(distro: &dyn DistroAdapter) {
        if std::fs::read_to_string("/run/nginx.pid")
            .ok()
            .and_then(|pid| pid.trim().parse::<u32>().ok())
            .is_some_and(|pid| Path::new(&format!("/proc/{pid}")).exists())
        {
            return;
        }

        let started = std::process::Command::new(distro.nginx_binary())
            .output()
            .expect("the polygon image installs nginx");
        assert!(
            started.status.success(),
            "nginx must start in the polygon:\n{}",
            String::from_utf8_lossy(&started.stderr)
        );
    }

    /// Reloads the running nginx so the configuration on disk is the one served.
    ///
    /// # Panics
    ///
    /// Panics when nginx refuses the reload.
    pub fn reload(distro: &dyn DistroAdapter) {
        let reloaded = std::process::Command::new(distro.nginx_binary())
            .args(["-s", "reload"])
            .output()
            .expect("the polygon image installs nginx");
        assert!(
            reloaded.status.success(),
            "nginx must reload in the polygon:\n{}",
            String::from_utf8_lossy(&reloaded.stderr)
        );
    }

    /// Asks the polygon's nginx for `path` on `host` over a real socket and
    /// returns the whole response, status line first.
    ///
    /// # Panics
    ///
    /// Panics when nothing answers on port 80 within [`HTTP_TIMEOUT`].
    pub fn fetch(host: &str, path: &str) -> String {
        let mut stream = std::net::TcpStream::connect_timeout(
            &std::net::SocketAddr::from(([127, 0, 0, 1], 80)),
            HTTP_TIMEOUT,
        )
        .expect("nginx must be listening on port 80 in the polygon");
        stream
            .set_read_timeout(Some(HTTP_TIMEOUT))
            .expect("a read timeout can always be set on a connected socket");
        stream
            .set_write_timeout(Some(HTTP_TIMEOUT))
            .expect("a write timeout can always be set on a connected socket");

        // `Connection: close`, so the read ends at end-of-stream instead of
        // waiting out a keep-alive timeout on a response it already has.
        let request = format!("GET {path} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n");
        stream
            .write_all(request.as_bytes())
            .expect("the request must reach nginx");

        let mut response = Vec::new();
        stream
            .read_to_end(&mut response)
            .expect("nginx must answer within the timeout");

        String::from_utf8_lossy(&response).into_owned()
    }

    /// Asks for `path` on `host` until `accepted` is satisfied or the deadline
    /// passes, and returns the LAST response either way.
    ///
    /// It returns instead of panicking so a site that never becomes reachable
    /// fails at the call site with the wrong answer printed, not with a timeout
    /// message that says nothing about what nginx served.
    pub fn fetch_until(host: &str, path: &str, accepted: impl Fn(&str) -> bool) -> String {
        let deadline = Instant::now() + SERVE_DEADLINE;
        let mut last = Self::fetch(host, path);

        while !accepted(&last) && Instant::now() < deadline {
            std::thread::sleep(SERVE_POLL_INTERVAL);
            last = Self::fetch(host, path);
        }

        last
    }
}
