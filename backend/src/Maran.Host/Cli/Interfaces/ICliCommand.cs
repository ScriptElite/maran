using Maran.Host.Cli.Models;

namespace Maran.Host.Cli.Interfaces;

/// <summary>One verb of <c>Maran.Host --cli &lt;verb&gt;</c>.</summary>
public interface ICliCommand
{
    /// <summary>The word that selects this verb on the command line.</summary>
    string Verb { get; }

    /// <summary>Runs the verb.</summary>
    /// <param name="invocation">The parsed command line and the process facts.</param>
    /// <param name="console">The streams to read and write; a verb writes nowhere else.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>The status to exit with.</returns>
    Task<CliResult> ExecuteAsync(CliInvocation invocation, CliConsole console, CancellationToken cancellationToken);
}
