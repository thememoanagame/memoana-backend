using Grpc.Core;

namespace MemoAna.Services;

/// <summary>Provides the sample gRPC greeting endpoint included by the template.</summary>
public sealed class GreeterService(ILogger<GreeterService> logger) : Greeter.GreeterBase
{
    /// <summary>Returns a greeting for the supplied name.</summary>
    /// <param name="request">The gRPC greeting request.</param>
    /// <param name="context">The current gRPC call context.</param>
    /// <returns>A greeting response containing the requested name.</returns>
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        logger.LogInformation("The message is received from {Name}", request.Name);
        return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
    }
}
