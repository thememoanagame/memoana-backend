using MemoAna.Extensions;
using MemoAna.Presentation.Grpc;

var builder = WebApplication.CreateBuilder(args);
builder.AddPresentation()
    .AddInfrastructure()
    .AddApplication();

var app = builder.Build();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapControllers();
app.MapGrpcService<GameMatchmakingGrpcService>();
await app.RunAsync();

/// <summary>Exposes the generated application type used by integration tests and the ASP.NET Core host.</summary>
public partial class Program;
