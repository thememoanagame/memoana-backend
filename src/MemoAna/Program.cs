using MemoAna.Application.Localization;
using MemoAna.Extensions;
using MemoAna.Presentation.Grpc;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.AddPresentation()
    .AddInfrastructure()
    .AddApplication()
    .AddLogging();

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var localizer = app.Services.GetRequiredService<ILocalizer>();
logger.LogInformation(localizer["HostConfiguringString"]);

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapControllers();
app.MapGrpcService<GameMatchmakingGrpcService>();
logger.LogInformation(localizer["HostStartedString"]);
try
{
    await app.RunAsync();
}
catch (Exception exception)
{
    logger.LogCritical(exception, localizer["HostFailureString"]);
    throw;
}
finally
{
    logger.LogInformation(localizer["HostStoppingString"]);
}

/// <summary>Exposes the generated application type used by integration tests and the ASP.NET Core host.</summary>
public partial class Program;
