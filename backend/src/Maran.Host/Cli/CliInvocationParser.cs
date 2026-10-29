using Maran.Host.Cli.Models;

namespace Maran.Host.Cli;

/// <summary>
/// Recognises a command-line run in the process arguments and captures the process facts the verbs
/// consult. <c>--cli &lt;verb&gt; …</c> is the front door; <c>--migrate</c> stays a first-class alias of
/// <c>--cli migrate</c> because the installer calls it.
/// </summary>
public static class CliInvocationParser
{
    /// <summary>The flag that selects command-line mode; the verb and its arguments follow it.</summary>
    public const string CliFlag = "--cli";

    /// <summary>The installer's historical spelling of <c>--cli migrate</c>.</summary>
    public const string MigrateFlag = "--migrate";

    /// <summary>The verb <see cref="MigrateFlag"/> stands for.</summary>
    public const string MigrateVerb = "migrate";

    /// <summary>The environment variable sudo sets to the login that invoked it.</summary>
    private const string SudoUserVariable = "SUDO_USER";

    /// <summary>Parses the process arguments, reading the process facts from the live environment.</summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The invocation, or null when this is an ordinary web-host start.</returns>
    public static CliInvocation? Parse(string[] args)
    {
        return Parse(
            args,
            !Console.IsOutputRedirected,
            Environment.IsPrivilegedProcess,
            Environment.GetEnvironmentVariable(SudoUserVariable));
    }

    /// <summary>Parses the process arguments against stated process facts.</summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="standardOutputIsTerminal">Whether standard output is a terminal.</param>
    /// <param name="runningAsRoot">Whether the process is privileged.</param>
    /// <param name="sudoUser">The raw <c>SUDO_USER</c> value, if any.</param>
    /// <returns>The invocation, or null when this is an ordinary web-host start.</returns>
    public static CliInvocation? Parse(
        string[] args,
        bool standardOutputIsTerminal,
        bool runningAsRoot,
        string? sudoUser)
    {
        var sudo = string.IsNullOrWhiteSpace(sudoUser) ? null : sudoUser.Trim();

        var cliIndex = Array.IndexOf(args, CliFlag);
        if (cliIndex >= 0)
        {
            var rest = args.Skip(cliIndex + 1).ToList();
            var verb = rest.Count > 0 ? rest[0] : string.Empty;
            var arguments = rest.Skip(1).ToList();
            return new CliInvocation(verb, arguments, standardOutputIsTerminal, runningAsRoot, sudo);
        }

        if (args.Contains(MigrateFlag, StringComparer.Ordinal))
        {
            return new CliInvocation(MigrateVerb, [], standardOutputIsTerminal, runningAsRoot, sudo);
        }

        return null;
    }
}
