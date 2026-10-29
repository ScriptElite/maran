using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Host.Cli.Services;
using Maran.SharedKernel.Security;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Reads text on standard input and writes it to standard output with secrets removed: the redaction
/// patterns the panel already applies to agent text, plus the literal values of this panel's own
/// configured secrets. The diagnostics collector pipes every file it gathers through it.
/// </summary>
/// <remarks>
/// The input is read whole, because a secret block (a PEM key) spans lines and a line-at-a-time
/// filter would see none of its body. Diagnostics are capped by the collector before they reach here.
/// </remarks>
public sealed class RedactCliCommand : ICliCommand
{
    /// <summary>Supplies the live secret values.</summary>
    private readonly CliSecretLiterals _secrets;

    /// <summary>Creates the verb.</summary>
    /// <param name="secrets">The live secret values to remove by literal match.</param>
    public RedactCliCommand(CliSecretLiterals secrets)
    {
        _secrets = secrets;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return "redact"; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        if (invocation.Arguments.Count > 0)
        {
            await console.Error.WriteLineAsync("redact takes no arguments; it filters standard input.");
            return new CliResult(CliExitCode.Usage);
        }

        var text = await console.Input.ReadToEndAsync(cancellationToken);
        await console.Output.WriteAsync(SecretTextRedactor.RedactDiagnostics(text, _secrets.Current()));
        return new CliResult(CliExitCode.Success);
    }
}
