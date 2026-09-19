using MemoAna.Application.Matchmaking;
using MemoAna.Infrastructure.Sessions;
using MemoAna.Presentation.Grpc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddControllers();
builder.Services.AddSingleton<SessionRuntimeRegistry>();
builder.Services.AddSingleton<IMatchSessionRuntimeFactory, SessionRuntimeFactory>();
builder.Services.AddSingleton<InMemoryMatchmaking>();
builder.Services.AddSingleton<IMatchmakingSessionGateway>(
    serviceProvider => serviceProvider.GetRequiredService<InMemoryMatchmaking>());
var app = builder.Build();

// Configure the HTTP request pipeline.

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapControllers();
app.MapGrpcService<GameMatchmakingGrpcService>();
await app.RunAsync();

public partial class Program;
