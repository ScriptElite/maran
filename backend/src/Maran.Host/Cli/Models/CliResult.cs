using Maran.Host.Cli.Enums;

namespace Maran.Host.Cli.Models;

/// <summary>What a verb reports back to the runner.</summary>
/// <param name="ExitCode">The status the process exits with.</param>
public sealed record CliResult(CliExitCode ExitCode);
