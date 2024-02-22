using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Grpc.Core;

using FsharpExchangeDotNetStandard;
using System.IO;

namespace GrpcService.Services
{
    public class FXService : FXGrpcService.FXGrpcServiceBase
    {
        private readonly Exchange exchange = new Exchange(Persistence.Redis);

        public override async Task<GenericOutputParam> GenericMethod(GenericInputParam request, ServerCallContext context)
        {
            Console.WriteLine($"Received {request.MsgIn}");

            var (type, _version) = GrpcModels.Marshaller.ExtractMetadata(request.MsgIn);

            var deserializedRequest = GrpcModels.Marshaller.DeserializeAbstract(request.MsgIn, type);

            if (deserializedRequest is GrpcModels.LimitOrder { } limitOrder)
            {
                var orderInfo = new OrderInfo(Guid.NewGuid(), Side.Parse(limitOrder.Side), limitOrder.Quantity);
                var exchangeLimitOrder = new LimitOrder(orderInfo, limitOrder.Price);
                var limitOrderReq = new LimitOrderRequest(exchangeLimitOrder, LimitOrderRequestType.Normal);
                var marketForLimitOrder = new Market(Currency.BTC, Currency.USD);

                // TODO: make async
                var matchType = exchange.SendLimitOrder(limitOrderReq, marketForLimitOrder);
                return await Task.FromResult(new GenericOutputParam { MsgOut = GrpcModels.Marshaller.Serialize(matchType) });
            }
            else if (deserializedRequest is GrpcModels.MarketOrder { } marketOrder)
            {
                var orderInfo = new OrderInfo(Guid.NewGuid(), Side.Parse(marketOrder.Side), marketOrder.Quantity);
                var marketForMarketOrder = new Market(Currency.BTC, Currency.USD);

                // TODO: make async
                exchange.SendMarketOrder(orderInfo, marketForMarketOrder);
                // return empty string?
                return await Task.FromResult(new GenericOutputParam { MsgOut = String.Empty });
            }
            else if (deserializedRequest is GrpcModels.CancelOrderRequest { } cancelOrderRequest)
            {
                // TODO: make async
                exchange.CancelLimitOrder(cancelOrderRequest.OrderId);
                // return empty string?
                return await Task.FromResult(new GenericOutputParam { MsgOut = String.Empty });
            }
            else
            {
                throw new InvalidOperationException("Unable to deserialize request: " + request.MsgIn);
            }
        }

        public override async Task GenericStreamOutputMethod(GenericInputParam request, IServerStreamWriter<GenericOutputParam> responseStream, ServerCallContext context)
        {
            Console.WriteLine(request.MsgIn);

            await responseStream.WriteAsync(new GenericOutputParam { MsgOut = "received " + request.MsgIn });
        }
    }
}
