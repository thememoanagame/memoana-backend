using MemoAna.Application.Localization;
using MemoAna.Application.Matchmaking;
using MemoAna.Infrastructure.Localization;
using MemoAna.Infrastructure.Sessions;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Sinks.ILogger;

namespace MemoAna.Extensions;

/// <summary>Provides dependency-injection registration extensions for the application layers.</summary>
public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>Registers application services, matchmaking, and localization.</summary>
        /// <returns>The same builder instance for fluent configuration.</returns>
        public WebApplicationBuilder AddApplication()
        {
            builder.Services.AddSingleton<InMemoryMatchmaking>();
            builder.Services.AddSingleton<IMatchmakingSessionGateway>(serviceProvider => serviceProvider.GetRequiredService<InMemoryMatchmaking>());
            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources/Localization");
            builder.Services.AddSingleton<ILocalizer, Localizer>();
            return builder;
        }

        /// <summary>Configures Serilog as the provider behind the application's <see cref="ILogger{TCategoryName}"/> abstraction.</summary>
        /// <returns>The same builder instance for fluent configuration.</returns>
        public WebApplicationBuilder AddLogging()
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "memoana-.log");
            var bridgeFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
            var bridgeLogger = bridgeFactory.CreateLogger("SerilogBridge");
            var serilog = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true)
                .WriteTo.ILogger(bridgeLogger, restrictedToMinimumLevel: LogEventLevel.Fatal)
                .CreateLogger();

            builder.Services.AddSingleton(bridgeFactory);
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new SerilogLoggerProvider(serilog, dispose: true));
            return builder;
        }

        /// <summary>Registers infrastructure services used by authoritative sessions.</summary>
        /// <returns>The same builder instance for fluent configuration.</returns>
        public WebApplicationBuilder AddInfrastructure()
        {
            builder.Services.AddSingleton<SessionRuntimeRegistry>();
            builder.Services.AddSingleton<IMatchSessionRuntimeFactory, SessionRuntimeFactory>();
            return builder;
        }

        /// <summary>Registers REST controllers and gRPC services.</summary>
        /// <returns>The same builder instance for fluent configuration.</returns>
        public WebApplicationBuilder AddPresentation()
        {
            builder.Services.AddGrpc();
            builder.Services.AddControllers();
            return builder;
        }
    }
}
