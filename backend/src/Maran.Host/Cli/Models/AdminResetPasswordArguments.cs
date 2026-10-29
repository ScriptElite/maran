namespace Maran.Host.Cli.Models;

/// <summary>The parsed arguments of <c>admin-reset-password</c>.</summary>
/// <param name="EmailOrUsername">Who to recover: an administrator's e-mail address or username.</param>
/// <param name="ClearTwoFactor">Whether to also remove the administrator's second factor.</param>
/// <param name="AllowNonTerminal">Whether the link may be written to a pipe or file.</param>
public sealed record AdminResetPasswordArguments(string EmailOrUsername, bool ClearTwoFactor, bool AllowNonTerminal);
