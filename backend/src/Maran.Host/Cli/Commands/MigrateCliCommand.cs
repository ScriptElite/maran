using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Host.Cli.Services;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Applies every registered module's pending migrations, in one pass, and reports each by name. This
/// is the body <c>--migrate</c> always had, moved here so the installer's flag and
/// <c>--cli migrate</c> are one code path.
/// </summary>
/// <remarks>
/// <para>
/// rules/architecture.md says migrations are never applied by a starting process; the installer and
/// the update command apply them deliberately by asking this verb, which exits without serving
/// anything. A normal start migrates nothing.
/// </para>
/// <para>
/// A failure is not swallowed: it propagates to <see cref="CliRunner"/>, the process exits non-zero,
/// and the installer stops on it. An install that could not create the schema must fail where the
/// operator can see it, not sixty seconds later as an HTTP 500 with a correlation id.
/// </para>
/// </remarks>
public sealed class MigrateCliCommand : ICliCommand
{
    /// <summary>Opens the scope each context is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Discovers the registered contexts.</summary>
    private readonly IServiceProvider _services;

    /// <summary>Creates the verb.</summary>
    /// <param name="scopeFactory">Opens a scope per context, because a context is scoped.</param>
    /// <param name="services">The root provider, from which the contexts are discovered.</param>
    public MigrateCliCommand(IServiceScopeFactory scopeFactory, IServiceProvider services)
    {
        _scopeFactory = scopeFactory;
        _services = services;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return CliInvocationParser.MigrateVerb; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        var contextTypes = PanelDbContextDiscovery.Discover(_services);

        // A vacuity guard, because "nothing to migrate" and "nothing was found to migrate" look
        // identical in a log and only one of them is good news (rules/testing.md).
        if (contextTypes.Count == 0)
        {
            throw new InvalidOperationException(
                "--migrate found no registered DbContext at all, so it would report success having " +
                "migrated nothing. Refusing rather than leaving an empty schema behind.");
        }

        foreach (var contextType in contextTypes)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false)).ToList();
            if (pending.Count == 0)
            {
                await console.Output.WriteLineAsync($"{contextType.Name}: up to date.");
                continue;
            }

            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            await console.Output.WriteLineAsync($"{contextType.Name}: applied {pending.Count} migration(s).");
        }

        return new CliResult(CliExitCode.Success);
    }
}
