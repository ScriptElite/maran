using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IAccountDirectory"/> that knows a fixed set of accounts, for a test that needs more
/// than the one <see cref="OneAccountDirectory"/> holds.
/// </summary>
public sealed class FixedAccountDirectory : IAccountDirectory
{
    /// <summary>Every account this directory knows.</summary>
    private readonly IReadOnlyList<AccountSnapshot> _snapshots;

    /// <summary>Creates the directory.</summary>
    /// <param name="snapshots">The accounts it answers for.</param>
    public FixedAccountDirectory(params AccountSnapshot[] snapshots)
    {
        _snapshots = snapshots;
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_snapshots.FirstOrDefault(snapshot =>
        {
            return snapshot.Id == accountId;
        }));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_snapshots);
    }
}
