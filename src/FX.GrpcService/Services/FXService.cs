using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Grpc.Core;

namespace GrpcService.Services
{
    public class FXService : FXGrpcService.FXGrpcServiceBase
    {
        public override async Task<GenericOutputParam> GenericMethod(GenericInputParam request, ServerCallContext context)
        {
            Console.WriteLine($"Received {request.MsgIn}");

            return await Task.FromResult(new GenericOutputParam { MsgOut = "received " + request.MsgIn });
        }

        public override async Task GenericStreamOutputMethod(GenericInputParam request, IServerStreamWriter<GenericOutputParam> responseStream, ServerCallContext context)
        {
            Console.WriteLine(request.MsgIn);

            await responseStream.WriteAsync(new GenericOutputParam { MsgOut = "received " + request.MsgIn });
        }
    }
}
