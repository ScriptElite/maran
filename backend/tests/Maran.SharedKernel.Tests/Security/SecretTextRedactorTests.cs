using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>Behavioral contract of <see cref="SecretTextRedactor"/>.</summary>
public sealed class SecretTextRedactorTests
{
    /// <summary>A literal comfortably above the redaction floor.</summary>
    private const string Literal = "Xk7-hunter2-Qw9";

    /// <summary>A forty-plus character base64-shaped run.</summary>
    private const string Base64Blob = "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSj";

    /// <summary>A JWT-shaped value.</summary>
    private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl";

    /// <summary>No configured literals.</summary>
    private static readonly string[] NoLiterals = [];

    /// <summary>The sent secret is removed from key-material redaction.</summary>
    [Fact]
    public void Key_material_redaction_removes_the_sent_secret()
    {
        var result = SecretTextRedactor.RedactKeyMaterial(
            $"denied (using password: '{Literal}')",
            new SensitiveString(Literal));

        Assert.Equal("denied (using password: '[redacted]')", result);
    }

    /// <summary>A PEM block is replaced as one unit and an unterminated one takes the rest.</summary>
    [Fact]
    public void Key_material_redaction_replaces_pem_blocks_including_unterminated_ones()
    {
        var closed = SecretTextRedactor.RedactKeyMaterial("a -----BEGIN KEY-----\nabc\n-----END KEY----- b", null);
        var open = SecretTextRedactor.RedactKeyMaterial("a -----BEGIN KEY-----\nabc", null);

        Assert.Equal("a [pem redacted] b", closed);
        Assert.Equal("a [pem redacted]", open);
    }

    /// <summary>A bare base64 run of forty characters is replaced.</summary>
    [Fact]
    public void Key_material_redaction_replaces_a_bare_base64_run()
    {
        var result = SecretTextRedactor.RedactKeyMaterial($"invalid base64: {Base64Blob}", null);

        Assert.Equal("invalid base64: [redacted]", result);
    }

    /// <summary>Prose, a path and a short value pass through key-material redaction untouched.</summary>
    [Fact]
    public void Key_material_redaction_leaves_prose_paths_and_short_values_untouched()
    {
        const string text = "cannot open /etc/nginx/conf.d/maran.conf: token=abc password=hunter";

        Assert.Equal(text, SecretTextRedactor.RedactKeyMaterial(text, null));
    }

    /// <summary>A sent secret under the floor is not searched for.</summary>
    [Fact]
    public void Key_material_redaction_does_not_search_for_a_sent_secret_under_the_floor()
    {
        const string text = "user abc1234 failed";

        Assert.Equal(text, SecretTextRedactor.RedactKeyMaterial(text, new SensitiveString("abc1234")));
    }

    /// <summary>A configured literal is removed from diagnostics.</summary>
    [Fact]
    public void Diagnostics_redaction_removes_a_configured_literal()
    {
        var result = SecretTextRedactor.RedactDiagnostics($"value is {Literal}.", [Literal]);

        Assert.Equal("value is [redacted].", result);
    }

    /// <summary>A literal under the redaction floor is not searched for.</summary>
    [Fact]
    public void Diagnostics_redaction_does_not_search_for_a_literal_under_the_floor()
    {
        const string text = "the word abc1234 appears";

        Assert.Equal(text, SecretTextRedactor.RedactDiagnostics(text, ["abc1234"]));
    }

    /// <summary>A literal containing a line break is found across the boundary.</summary>
    [Fact]
    public void Diagnostics_redaction_finds_a_literal_across_a_line_boundary()
    {
        const string multiLine = "first-part-of-key\nsecond-part-of-key";

        var result = SecretTextRedactor.RedactDiagnostics($"before\n{multiLine}\nafter", [multiLine]);

        Assert.Equal("before\n[redacted]\nafter", result);
    }

    /// <summary>The key-material patterns still apply to diagnostics.</summary>
    [Fact]
    public void Diagnostics_redaction_includes_pem_and_base64_patterns()
    {
        var result = SecretTextRedactor.RedactDiagnostics(
            $"-----BEGIN X-----\nq\n-----END X----- and {Base64Blob}",
            NoLiterals);

        Assert.Equal("[pem redacted] and [redacted]", result);
    }

    /// <summary>A JWT is replaced.</summary>
    [Fact]
    public void Diagnostics_redaction_replaces_a_jwt()
    {
        var result = SecretTextRedactor.RedactDiagnostics($"jwt {Jwt} end", NoLiterals);

        Assert.Equal("jwt [redacted] end", result);
    }

    /// <summary>An Argon2id hash is replaced.</summary>
    [Fact]
    public void Diagnostics_redaction_replaces_an_argon2id_hash()
    {
        var result = SecretTextRedactor.RedactDiagnostics(
            "hash $argon2id$v=19$m=65536,t=3,p=1$c2FsdA$aGFzaA done",
            NoLiterals);

        Assert.Equal("hash [redacted] done", result);
    }

    /// <summary>Authorization, Cookie and Set-Cookie header values are replaced to the end of the line.</summary>
    [Theory]
    [InlineData("Authorization: Basic dXNlcjpwYXNz", "Authorization: [redacted]")]
    [InlineData("cookie: session=abc; other=def", "cookie: [redacted]")]
    [InlineData("Set-Cookie: refresh=zzz; HttpOnly", "Set-Cookie: [redacted]")]
    public void Diagnostics_redaction_replaces_header_values(string input, string expected)
    {
        Assert.Equal(expected, SecretTextRedactor.RedactDiagnostics(input, NoLiterals));
    }

    /// <summary>A Bearer credential outside a header line is replaced.</summary>
    [Fact]
    public void Diagnostics_redaction_replaces_a_bearer_credential()
    {
        var result = SecretTextRedactor.RedactDiagnostics("sent Bearer abc.def-ghi_jkl to api", NoLiterals);

        Assert.Equal("sent Bearer [redacted] to api", result);
    }

    /// <summary>Password, secret, token and key pairs are replaced, including a token in a query string.</summary>
    [Theory]
    [InlineData("password=hunter2 ok", "password=[redacted] ok")]
    [InlineData("DB_SECRET=s3cr3t", "DB_SECRET=[redacted]")]
    [InlineData("GET /reset?token=abc123&x=1", "GET /reset?token=[redacted]&x=1")]
    [InlineData("Panel__ApiKey=k-1", "Panel__ApiKey=[redacted]")]
    public void Diagnostics_redaction_replaces_named_pairs(string input, string expected)
    {
        Assert.Equal(expected, SecretTextRedactor.RedactDiagnostics(input, NoLiterals));
    }

    /// <summary>Prose, a path and a short value pass through diagnostics redaction untouched.</summary>
    [Fact]
    public void Diagnostics_redaction_leaves_prose_paths_and_short_values_untouched()
    {
        const string text = "nginx: file /etc/nginx/nginx.conf test is successful, port=8080, user=maran";

        Assert.Equal(text, SecretTextRedactor.RedactDiagnostics(text, ["short"]));
    }
}
