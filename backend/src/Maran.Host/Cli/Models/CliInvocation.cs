namespace Maran.Host.Cli.Models;

/// <summary>
/// One parsed <c>--cli</c> command line together with the facts about the process that the verbs
/// consult. The environment facts are fields rather than reads inside the verbs so a test states them.
/// </summary>
/// <param name="Verb">The verb, such as <c>status</c>; empty when none was given.</param>
/// <param name="Arguments">Everything after the verb, in order, unparsed.</param>
/// <param name="StandardOutputIsTerminal">Whether standard output is a terminal rather than a pipe or file.</param>
/// <param name="RunningAsRoot">Whether the process runs with an effective user id of 0.</param>
/// <param name="SudoUser">The login that invoked sudo (<c>SUDO_USER</c>), or null when absent or blank.</param>
public sealed record CliInvocation(
    string Verb,
    IReadOnlyList<string> Arguments,
    bool StandardOutputIsTerminal,
    bool RunningAsRoot,
    string? SudoUser);
