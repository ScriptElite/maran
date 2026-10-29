using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// A freshly minted administrator recovery as it exists for the instant between being issued and being
/// printed on the operator's terminal: whose it is, and the link that carries the plaintext token.
/// </summary>
/// <remarks>
/// The link is a <see cref="SensitiveString"/> because this is a <c>record</c> and a record's
/// generated <c>ToString()</c> prints every property; the link embeds the reset token, so it is as
/// secret as the token. It is never stored, journalled or logged — only the console that asked for it
/// calls <see cref="SensitiveString.Reveal"/>.
/// </remarks>
/// <param name="Username">The administrator's login name.</param>
/// <param name="Link">The reset link, absolute when the panel has a public address, else root-relative.</param>
/// <param name="LinkIsRelative">
/// <c>true</c> when no public address is configured, so <paramref name="Link"/> has no host and the
/// operator must prefix the origin they reach the panel at.
/// </param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
/// <param name="TwoFactorCleared">Whether the administrator's two-factor enrolment was removed.</param>
public sealed record IssuedAdminRecovery(
    string Username,
    SensitiveString Link,
    bool LinkIsRelative,
    DateTimeOffset ExpiresAt,
    bool TwoFactorCleared);
