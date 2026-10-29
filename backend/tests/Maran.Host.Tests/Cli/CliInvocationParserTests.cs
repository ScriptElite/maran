using Maran.Host.Cli;

namespace Maran.Host.Tests.Cli;

/// <summary>How the process arguments become a command-line invocation.</summary>
public sealed class CliInvocationParserTests
{
    /// <summary>The arguments the verb in the first test is expected to receive.</summary>
    private static readonly string[] ExpectedArguments = ["alice", "--clear-two-factor"];

    /// <summary>The verb and its arguments are everything after the flag, in order.</summary>
    [Fact]
    public void The_verb_and_its_arguments_are_everything_after_the_cli_flag()
    {
        var invocation = CliInvocationParser.Parse(
            ["--cli", "admin-reset-password", "alice", "--clear-two-factor"], true, false, null);

        Assert.NotNull(invocation);
        Assert.Equal("admin-reset-password", invocation.Verb);
        Assert.Equal(ExpectedArguments, invocation.Arguments);
    }

    /// <summary>The installer's migrate flag is the migrate verb with no arguments.</summary>
    [Fact]
    public void The_migrate_flag_is_an_alias_of_the_migrate_verb()
    {
        var invocation = CliInvocationParser.Parse(["--migrate"], false, false, null);

        Assert.NotNull(invocation);
        Assert.Equal("migrate", invocation.Verb);
        Assert.Empty(invocation.Arguments);
    }

    /// <summary>An ordinary web-host start is not an invocation (the inverse control for the two above).</summary>
    [Fact]
    public void Arguments_without_either_flag_are_not_a_cli_run()
    {
        Assert.Null(CliInvocationParser.Parse(["--urls", "http://localhost:5080"], true, false, null));
    }

    /// <summary>A bare flag still yields an invocation, with an empty verb the runner will refuse.</summary>
    [Fact]
    public void The_cli_flag_alone_yields_an_empty_verb()
    {
        var invocation = CliInvocationParser.Parse(["--cli"], true, false, null);

        Assert.NotNull(invocation);
        Assert.Equal(string.Empty, invocation.Verb);
    }

    /// <summary>The process facts are carried through, and a blank sudo user becomes null.</summary>
    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("  alice  ", "alice")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void The_sudo_user_is_trimmed_and_blank_means_absent(string? raw, string? expected)
    {
        var invocation = CliInvocationParser.Parse(["--cli", "status"], true, true, raw);

        Assert.NotNull(invocation);
        Assert.Equal(expected, invocation.SudoUser);
        Assert.True(invocation.StandardOutputIsTerminal);
        Assert.True(invocation.RunningAsRoot);
    }
}
