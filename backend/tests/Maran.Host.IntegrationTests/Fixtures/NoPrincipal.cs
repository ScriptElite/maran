using Maran.SharedKernel.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The "current user" of a background service: nobody. Not an administrator and owning no account,
/// so a tenant query filter that is not bypassed returns nothing at all.
/// </summary>
public sealed class NoPrincipal : ICurrentUser
{
    /// <inheritdoc />
    public Guid UserId { get; } = Guid.Empty;

    /// <inheritdoc />
    public string Username
    {
        get
        {
            return "system";
        }
    }

    /// <inheritdoc />
    public Guid? AccountId
    {
        get
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsAdmin
    {
        get
        {
            return false;
        }
    }
}
