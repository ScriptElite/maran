namespace Maran.Host.Cli.Models;

/// <summary>
/// The three streams a verb talks through. Injected so a test captures what a verb printed; the
/// production run passes the process's own streams. A verb never touches <see cref="Console"/>.
/// </summary>
/// <param name="Input">Standard input.</param>
/// <param name="Output">Standard output: the verb's answer and nothing else.</param>
/// <param name="Error">Standard error: diagnostics, refusals and progress.</param>
public sealed record CliConsole(TextReader Input, TextWriter Output, TextWriter Error);
