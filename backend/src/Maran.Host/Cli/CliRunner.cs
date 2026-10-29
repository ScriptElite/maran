using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Host.Cli.Services;
using Maran.SharedKernel.Security;

namespace Maran.Host.Cli;

/// <summary>
/// Dispatches a parsed command line to the verb that owns it and turns every outcome, including a
/// thrown exception, into an exit status. Owns the two refusals that apply to every verb.
/// </summary>
/// <remarks>
/// <b>It refuses to run as root.</b> The CLI opens the panel's database with the panel's own
/// credentials; the wrapper that exposes it to the operator drops to the unprivileged panel user first.
/// A root process here would be reading <c>panel.env</c> as root and could write root-owned files into
/// the panel's directories, so a direct root invocation is a wrapper bug worth failing loudly on.
/// </remarks>
public sealed class CliRunner
{
    /// <summary>The verbs this runner knows, keyed by their command-line word.</summary>
    private readonly Dictionary<string, ICliCommand> _commands;

    /// <summary>Supplies the live secret values an error message is scrubbed of.</summary>
    private readonly CliSecretLiterals _secrets;

    /// <summary>Creates the runner.</summary>
    /// <param name="commands">Every registered verb.</param>
    /// <param name="secrets">The live secret values, for scrubbing error text.</param>
    public CliRunner(IEnumerable<ICliCommand> commands, CliSecretLiterals secrets)
    {
        _commands = commands.ToDictionary(command => { return command.Verb; }, StringComparer.Ordinal);
        _secrets = secrets;
    }

    /// <summary>Runs the verb the invocation names.</summary>
    /// <param name="invocation">The parsed command line and the process facts.</param>
    /// <param name="console">The streams the verb talks through.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>The status to exit with.</returns>
    public async Task<int> RunAsync(CliInvocation invocation, CliConsole console, CancellationToken cancellationToken)
    {
        if (invocation.RunningAsRoot)
        {
            await console.Error.WriteLineAsync(
                "Refusing to run as root. Run this through the maran wrapper, which drops to the panel user.");
            return (int)CliExitCode.PermissionDenied;
        }

        if (!_commands.TryGetValue(invocation.Verb, out var command))
        {
            var known = string.Join(", ", _commands.Keys.Order(StringComparer.Ordinal));
            await console.Error.WriteLineAsync(
                $"Unknown or missing verb '{invocation.Verb}'. Known verbs: {known}.");
            return (int)CliExitCode.Usage;
        }

        try
        {
            var result = await command.ExecuteAsync(invocation, console, cancellationToken);
            return (int)result.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync($"'{invocation.Verb}' was interrupted.");
            return (int)CliExitCode.TemporaryFailure;
        }
        catch (Exception exception)
        {
            // The message is scrubbed: a connection failure can quote the connection string, and this
            // text goes to a terminal and from there to a ticket.
            var text = SecretTextRedactor.RedactDiagnostics(
                $"{exception.GetType().Name}: {exception.Message}",
                _secrets.Current());
            await console.Error.WriteLineAsync($"'{invocation.Verb}' failed: {text}");
            return (int)CliExitCode.Failure;
        }
    }
}
