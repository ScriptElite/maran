namespace Maran.Host.Cli.Enums;

/// <summary>
/// The process exit status of a <c>--cli</c> run. The numbers follow the BSD <c>sysexits</c>
/// convention where one fits, so the root-only wrapper script and a human reading <c>$?</c> share one
/// vocabulary; <see cref="Refused"/> is this product's own.
/// </summary>
public enum CliExitCode
{
    /// <summary>The verb did what it was asked.</summary>
    Success = 0,

    /// <summary>The verb ran and found a fault, or failed for a reason none of the codes below names.</summary>
    Failure = 1,

    /// <summary>The verb understood the request and refused it by policy (not an administrator, no terminal).</summary>
    Refused = 3,

    /// <summary>The command line was malformed: unknown verb, missing or surplus argument.</summary>
    Usage = 64,

    /// <summary>A dependency the verb needs (database, agent) did not answer.</summary>
    Unavailable = 69,

    /// <summary>The failure is expected to clear on its own; running the verb again later is reasonable.</summary>
    TemporaryFailure = 75,

    /// <summary>The process runs as root, which the CLI refuses: it must run as the unprivileged panel user.</summary>
    PermissionDenied = 77,
}
