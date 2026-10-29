using System.Globalization;
using Maran.SharedKernel.Constants;
using Serilog;
using Serilog.Events;

namespace Maran.Host.Extensions;

/// <summary>
/// Structured logging for the panel. Operators read these logs while something is broken, so every
/// line is structured (not a formatted sentence), carries the request's correlation id, and never
/// contains a secret (rules/security.md, rules/csharp.md).
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Configures Serilog as the host's logger. Reads its sinks and levels from configuration, so a
    /// deployment can raise verbosity without a rebuild; the console sink is the default because
    /// systemd captures stdout into the journal.
    /// </summary>
    /// <param name="builder">The host builder to attach logging to.</param>
    /// <param name="commandLineMode">
    /// True for a <c>--cli</c> or <c>--migrate</c> run: the console sink then writes to standard error
    /// and only at Warning or above, so standard output carries nothing but the verb's own answer
    /// (a reset link must not share a stream with log lines, and a pipeline reading a verb's output
    /// must not parse Serilog).
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHostBuilder AddPanelObservability(this IHostBuilder builder, bool commandLineMode = false)
    {
        return builder.UseSerilog((context, configuration) =>
        {
            if (commandLineMode)
            {
                ConfigureCommandLineLogger(context, configuration);
                return;
            }

            ConfigureLogger(context, configuration);
        });
    }

    /// <summary>Builds the logger configuration for a command-line run: standard error, Warning and above.</summary>
    /// <param name="context">The host context, source of configuration.</param>
    /// <param name="configuration">The logger configuration to populate.</param>
    private static void ConfigureCommandLineLogger(HostBuilderContext context, LoggerConfiguration configuration)
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .MinimumLevel.Warning()
            .Enrich.FromLogContext()
            // A configuration file may lower a namespace's level (the Development file does), so the
            // floor is enforced again as a filter rather than trusted to the minimum alone.
            .Filter.ByIncludingOnly(logEvent => { return logEvent.Level >= LogEventLevel.Warning; })
            .WriteTo.Console(
                standardErrorFromLevel: LogEventLevel.Verbose,
                formatProvider: CultureInfo.InvariantCulture);
    }

    /// <summary>Builds the logger configuration for a host context.</summary>
    /// <param name="context">The host context, source of configuration.</param>
    /// <param name="configuration">The logger configuration to populate.</param>
    private static void ConfigureLogger(HostBuilderContext context, LoggerConfiguration configuration)
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .MinimumLevel.Information()
            // ASP.NET Core's own request logs are replaced by UseSerilogRequestLogging below:
            // one summary line per request instead of three framework lines.
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            // Invariant culture: log lines are machine-parsed and compared across servers, so they
            // must not vary with the host's locale.
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Adds one summary log line per request, enriched with the correlation id so a user-reported
    /// id leads straight to the request and everything logged inside it.
    /// </summary>
    /// <param name="app">The application pipeline builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IApplicationBuilder UsePanelRequestLogging(this IApplicationBuilder app)
    {
        return app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                if (httpContext.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var correlationId)
                    && correlationId is string id)
                {
                    diagnosticContext.Set(CorrelationIdKeys.PayloadField, id);
                }
            };
        });
    }
}
