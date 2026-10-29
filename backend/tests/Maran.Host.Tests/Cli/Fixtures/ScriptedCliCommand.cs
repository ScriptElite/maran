using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;

namespace Maran.Host.Tests.Cli.Fixtures;

/// <summary>A verb that either answers with a fixed status or throws, and records that it ran.</summary>
internal sealed class ScriptedCliCommand : ICliCommand
{
    /// <summary>The status returned when no exception is scripted.</summary>
    private readonly CliExitCode _exitCode;

    /// <summary>The exception thrown instead of answering, if any.</summary>
    private readonly Exception? _failure;

    /// <summary>Creates the verb.</summary>
    /// <param name="verb">The word that selects it.</param>
    /// <param name="exitCode">The status it returns.</param>
    /// <param name="failure">When set, thrown instead of returning.</param>
    public ScriptedCliCommand(string verb, CliExitCode exitCode, Exception? failure = null)
    {
        Verb = verb;
        _exitCode = exitCode;
        _failure = failure;
    }

    /// <inheritdoc />
    public string Verb { get; }

    /// <summary>How many times the verb was executed.</summary>
    public int Runs { get; private set; }

    /// <inheritdoc />
    public Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        Runs++;
        if (_failure is not null)
        {
            throw _failure;
        }

        return Task.FromResult(new CliResult(_exitCode));
    }
}
