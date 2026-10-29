using Maran.Host.Configuration;
using Maran.Modules.Identity.Options;
using Microsoft.Extensions.Options;

namespace Maran.Host.Cli.Services;

/// <summary>
/// The secret values this panel is configured with, as literal strings, so the redactor can remove
/// them from text that has already left the process (a journal, a config dump) even where no pattern
/// would recognise them.
/// </summary>
/// <remarks>
/// Read from the bound options, which is the same configuration the running panel uses: the list is
/// the panel's actual secrets and not a list of names someone remembered to keep current.
/// </remarks>
public sealed class CliSecretLiterals
{
    /// <summary>The bound database settings, holding the database password.</summary>
    private readonly DatabaseOptions _database;

    /// <summary>The bound security settings, holding the encryption key.</summary>
    private readonly SecurityOptions _security;

    /// <summary>The bound token settings, holding the signing key.</summary>
    private readonly JwtOptions _jwt;

    /// <summary>The bound setup settings, holding the installer's one-time token.</summary>
    private readonly SetupOptions _setup;

    /// <summary>Creates the source from the bound options.</summary>
    /// <param name="database">The database settings.</param>
    /// <param name="security">The security settings.</param>
    /// <param name="jwt">The token settings.</param>
    /// <param name="setup">The setup settings.</param>
    public CliSecretLiterals(
        IOptions<DatabaseOptions> database,
        IOptions<SecurityOptions> security,
        IOptions<JwtOptions> jwt,
        IOptions<SetupOptions> setup)
    {
        _database = database.Value;
        _security = security.Value;
        _jwt = jwt.Value;
        _setup = setup.Value;
    }

    /// <summary>Lists the configured secret values that are actually set.</summary>
    /// <returns>The non-empty secrets: database password, encryption key, signing key, setup token.</returns>
    public IReadOnlyList<string> Current()
    {
        return new[] { _database.Password, _security.EncryptionKey, _jwt.SigningKey, _setup.Token }
            .Where(value => { return !string.IsNullOrWhiteSpace(value); })
            .ToList();
    }
}
