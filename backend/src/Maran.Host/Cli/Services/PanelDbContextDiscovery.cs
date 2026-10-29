using Microsoft.EntityFrameworkCore;

namespace Maran.Host.Cli.Services;

/// <summary>
/// Finds every <see cref="DbContext"/> the composed panel registers. Discovered from the container
/// rather than listed, so a module that registers a context is covered by existing.
/// </summary>
public static class PanelDbContextDiscovery
{
    /// <summary>Lists the registered context types, ordered by name.</summary>
    /// <param name="services">The built application's service provider.</param>
    /// <returns>The distinct context types.</returns>
    public static IReadOnlyList<Type> Discover(IServiceProvider services)
    {
        return services.GetServices<DbContextOptions>()
            .Select(options => { return options.ContextType; })
            .Distinct()
            .OrderBy(type => { return type.Name; }, StringComparer.Ordinal)
            .ToList();
    }
}
