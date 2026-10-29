using System.Text.RegularExpressions;

namespace Maran.SharedKernel.Security;

/// <summary>
/// Removes secret material from text that is about to be logged or packaged: the agent-error log
/// line (<see cref="RedactKeyMaterial"/>) and the break-glass diagnostics bundle
/// (<see cref="RedactDiagnostics"/>).
/// </summary>
/// <remarks>
/// It lives in <c>Security/</c> because it is the panel's one answer to "what does a secret look
/// like in text", sharing the length floor in <see cref="SecretRedactionPolicy"/> with the code that
/// mints secrets. Two entry points, one file, so a pattern is never written twice: the diagnostics
/// set is a strict superset of the key-material set. Redaction is deliberately over-eager — a
/// truncated diagnostic is a smaller loss than a secret in a bundle an operator attaches to a ticket.
/// </remarks>
public static partial class SecretTextRedactor
{
    /// <summary>What replaces a PEM block in redacted text.</summary>
    private const string RedactedPem = "[pem redacted]";

    /// <summary>What replaces any other redacted run or value.</summary>
    private const string RedactedSecret = "[redacted]";

    /// <summary>
    /// Removes the secret a call sent, every PEM block and every bare run of forty or more
    /// base64-shaped characters from <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text about to be logged.</param>
    /// <param name="sentSecret">The secret the call carried, or null when it carried none.</param>
    /// <returns>The text with that material replaced.</returns>
    /// <remarks>
    /// The sent secret goes first (knowledge beats a guess, and a base64-shaped password is then
    /// removed as a secret we recognise), then armoured PEM blocks (an unterminated block takes the
    /// rest of the string), then bare base64 runs. The literal is matched ordinally and
    /// case-sensitively, and only at or above <see cref="SecretRedactionPolicy.ShortestRecognisableSecret"/>.
    /// </remarks>
    public static string RedactKeyMaterial(string text, SensitiveString? sentSecret)
    {
        var withoutSentSecret = RemoveLiteral(text, sentSecret?.Reveal());
        var withoutPemBlocks = PemBlock().Replace(withoutSentSecret, RedactedPem);

        return Base64Run().Replace(withoutPemBlocks, RedactedSecret);
    }

    /// <summary>
    /// Removes everything <see cref="RedactKeyMaterial"/> removes, plus the configured secret
    /// literals and the shapes secrets take in a log, a unit file or an environment dump.
    /// </summary>
    /// <param name="text">The diagnostic text to clean.</param>
    /// <param name="literals">
    /// Live configuration values (keys, passwords, tokens) to remove wherever they appear; values
    /// shorter than <see cref="SecretRedactionPolicy.ShortestRecognisableSecret"/> are not searched.
    /// </param>
    /// <returns>The text with those values and shapes replaced.</returns>
    /// <remarks>
    /// Literals run first, longest first, so a value containing another is removed whole. Then PEM
    /// blocks, JWT-shaped tokens, <c>$argon2id$</c> hashes, <c>Authorization</c>/<c>Cookie</c>/
    /// <c>Set-Cookie</c> header values, <c>Bearer</c> credentials, <c>name=value</c> pairs whose name
    /// ends in password, secret, token or key (which also covers <c>token=</c> in a query string),
    /// and finally bare base64 runs. A literal that itself spans a line break is found, because the
    /// search is over the whole text rather than per line.
    /// </remarks>
    public static string RedactDiagnostics(string text, IEnumerable<string> literals)
    {
        var result = text;

        foreach (var literal in literals.OrderByDescending(l => { return l.Length; }))
        {
            result = RemoveLiteral(result, literal);
        }

        result = PemBlock().Replace(result, RedactedPem);
        result = Jwt().Replace(result, RedactedSecret);
        result = Argon2Hash().Replace(result, RedactedSecret);
        result = HeaderValue().Replace(result, "$1" + RedactedSecret);
        result = BearerCredential().Replace(result, "$1" + RedactedSecret);
        result = NamedPair().Replace(result, "$1=" + RedactedSecret);

        return Base64Run().Replace(result, RedactedSecret);
    }

    /// <summary>Replaces every ordinal occurrence of a literal, when it is long enough to search for.</summary>
    /// <param name="text">The text to clean.</param>
    /// <param name="literal">The value to remove, or null.</param>
    /// <returns>The text unchanged when there is nothing safe to look for.</returns>
    private static string RemoveLiteral(string text, string? literal)
    {
        if (literal is null || literal.Length < SecretRedactionPolicy.ShortestRecognisableSecret)
        {
            return text;
        }

        return text.Replace(literal, RedactedSecret, StringComparison.Ordinal);
    }

    /// <summary>The PEM block pattern: a BEGIN marker through its END marker, or through the end.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(@"-----BEGIN[\s\S]*?(?:-----END[^-]*-----|$)")]
    private static partial Regex PemBlock();

    /// <summary>The bare-material pattern: an unbroken run of forty or more base64 characters.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex("[A-Za-z0-9+/=]{40,}")]
    private static partial Regex Base64Run();

    /// <summary>The JWT shape: a base64url header starting <c>eyJ</c>, a payload and a signature.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*")]
    private static partial Regex Jwt();

    /// <summary>An Argon2id PHC-format password hash.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(@"\$argon2id\$[^\s""']+")]
    private static partial Regex Argon2Hash();

    /// <summary>The value of an Authorization, Cookie or Set-Cookie header, to the end of the line.</summary>
    /// <returns>The generated matcher; group 1 is the header name and colon.</returns>
    [GeneratedRegex(@"(\b(?:Authorization|Set-Cookie|Cookie)[ \t]*:[ \t]*)[^\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderValue();

    /// <summary>A Bearer credential outside a header line.</summary>
    /// <returns>The generated matcher; group 1 is the scheme and its trailing space.</returns>
    [GeneratedRegex(@"(\bBearer[ \t]+)[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerCredential();

    /// <summary>A <c>name=value</c> pair whose name ends in password, secret, token or key.</summary>
    /// <returns>The generated matcher; group 1 is the name.</returns>
    [GeneratedRegex(@"\b([A-Za-z0-9_.-]*(?:password|secret|token|key))=[^\s&""';]+", RegexOptions.IgnoreCase)]
    private static partial Regex NamedPair();
}
