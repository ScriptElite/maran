using Maran.Host.Cli.Models;

namespace Maran.Host.Tests.Cli.Fixtures;

/// <summary>A <see cref="CliConsole"/> whose output streams a test can read back.</summary>
internal sealed class CapturedCliConsole : IDisposable
{
    /// <summary>Collects what the verb wrote to standard output.</summary>
    private readonly StringWriter _output = new();

    /// <summary>Collects what the verb wrote to standard error.</summary>
    private readonly StringWriter _error = new();

    /// <summary>Creates a console whose standard input is the given text.</summary>
    /// <param name="input">What standard input will deliver.</param>
    public CapturedCliConsole(string input = "")
    {
        Console = new CliConsole(new StringReader(input), _output, _error);
    }

    /// <summary>The console to hand to a verb.</summary>
    public CliConsole Console { get; }

    /// <summary>Everything written to standard output so far.</summary>
    public string Output
    {
        get { return _output.ToString(); }
    }

    /// <summary>Everything written to standard error so far.</summary>
    public string Error
    {
        get { return _error.ToString(); }
    }

    /// <summary>Releases the captured output writers.</summary>
    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }
}
