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

        public static readonly int Port = 5178;
        public static readonly int HttpsPort = 7178;

        public FXGrpcService.FXGrpcServiceClient Connect()
        {
            var channel = GrpcChannel.ForAddress($"https://{serverFqdn}:{HttpsPort}");
            var client = new FXGrpcService.FXGrpcServiceClient(channel);
            return client;
        }

        public async Task<string> SendMessage(string message)
        {
            var client = Connect();
            var reply = await client.GenericMethodAsync(
                new GenericInputParam { MsgIn = message }
            );
            Console.WriteLine($"Got response: {reply.MsgOut}");
            return reply.MsgOut;
        }
    }
}
