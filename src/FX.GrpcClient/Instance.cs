using System;
using System.Threading.Tasks;

using Grpc.Core;
using Grpc.Net.Client;

using GrpcService;

namespace GrpcClient
{
    public class Instance
    {
        private static string serverFqdn =
            "localhost";

        public FXGrpcService.FXGrpcServiceClient Connect()
        {
            var channel = GrpcChannel.ForAddress($"http://{serverFqdn}:8080");
            var client = new FXGrpcService.FXGrpcServiceClient(channel);
            return client;
        }

        public async Task<string> SendMessage(string message)
        {
            var client = Connect();
            var reply = await client.GenericMethodAsync(
                new GenericInputParam { MsgIn = "hello" }
            );
            Console.WriteLine($"Got response: {reply.MsgOut}");

            return reply.MsgOut;
        }
    }
}
