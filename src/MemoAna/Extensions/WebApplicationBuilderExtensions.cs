using MemoAna.Application.Localization;
using MemoAna.Application.Matchmaking;
using MemoAna.Infrastructure.Localization;
using MemoAna.Infrastructure.Sessions;

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
