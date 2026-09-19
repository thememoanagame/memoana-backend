using MemoAna.Application.Localization;
using MemoAna.Application.Matchmaking;
using MemoAna.Infrastructure.Localization;
using MemoAna.Infrastructure.Sessions;

namespace MemoAna.Extensions;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddApplication()
        {
            builder.Services.AddSingleton<InMemoryMatchmaking>();
            builder.Services.AddSingleton<IMatchmakingSessionGateway>(
                serviceProvider => serviceProvider.GetRequiredService<InMemoryMatchmaking>());
            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources/Localization");
            builder.Services.AddSingleton<ILocalizer, Localizer>();

            return builder;
        }
        public WebApplicationBuilder AddInfrastructure()
        {
            builder.Services.AddSingleton<SessionRuntimeRegistry>();
            builder.Services.AddSingleton<IMatchSessionRuntimeFactory, SessionRuntimeFactory>();
            return builder;
        }

        public WebApplicationBuilder AddPresentation()
        {
            builder.Services.AddGrpc();
            builder.Services.AddControllers();
            return builder;
        }
    }
}
