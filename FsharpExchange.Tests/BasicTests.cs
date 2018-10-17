//
// Copyright (C) 2018 Diginex Ltd. (www.diginex.com)
//

using System;

using FsharpExchangeDotNetStandard;

using NUnit.Framework;
using StackExchange.Redis;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class BasicTests
    {
        internal static void ClearStorageIfNonVolatile()
        {
            using (var redis = ConnectionMultiplexer.Connect("localhost,allowAdmin=true"))
            {
                var server = redis.GetServer("localhost:6379");
                server.FlushDatabase();
            }
        }

        [Test]
        public void Sending_order_with_same_guid_should_be_rejected()
        {
            ClearStorageIfNonVolatile();

            var quantity = 1;
            var price = 10000;
            var someSide = Side.Buy;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            var orderBook = exchange[market];

            var someOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), someSide, quantity),
                               price);
            SendOrder(exchange, someOrder, market);

            var someOtherOrderWithSameGuid =
                new LimitOrder(
                    new OrderInfo(new Guid(someOrder.OrderInfo.Id.ToString()),
                                  someSide, quantity),
                    price);

            Assert.Throws<OrderAlreadyExists>(() =>
                SendOrder(exchange, someOtherOrderWithSameGuid, market)
            );
        }

        internal static void SendOrder(Exchange exchange,
                               LimitOrder limitOrder,
                               Market market)
        {
            var nonMakerOnlyLimitOrder =
                new LimitOrderRequest(limitOrder, LimitOrderRequestType.Normal);
            exchange.SendLimitOrder(nonMakerOnlyLimitOrder, market);
        }
    }
}
