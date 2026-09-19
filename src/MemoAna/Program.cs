using System.Runtime.CompilerServices;
using MemoAna.Application.Localization;
using MemoAna.Application.Matchmaking;
using MemoAna.Extensions;
using MemoAna.Infrastructure.Localization;
using MemoAna.Infrastructure.Sessions;
using MemoAna.Presentation.Grpc;

var builder = WebApplication.CreateBuilder(args);
builder.AddPresentation()
    .AddInfrastructure()
    .AddApplication();
var app = builder.Build();

// Configure the HTTP request pipeline.

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapControllers();
app.MapGrpcService<GameMatchmakingGrpcService>();
await app.RunAsync();

public partial class Program;
