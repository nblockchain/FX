
using System;
using System.Collections.Generic;

using FsharpExchangeDotNetStandard;

using NUnit.Framework;
using StackExchange.Redis;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class BasicTests
    {
        internal static void ClearRedisStorage()
        {
            using (var redis = ConnectionMultiplexer.Connect("localhost,allowAdmin=true"))
            {
                var server = redis.GetServer("localhost:6379");
                server.FlushDatabase();
            }
        }

        [TearDown]
        public void TearDown()
        {
            ClearRedisStorage();
        }

        internal static IEnumerable<Exchange> CreateExchangesOfDifferentTypes()
        {
            yield return new Exchange(Persistence.Memory);

            ClearRedisStorage();
            yield return new Exchange(Persistence.Redis);
        }

        [Test]
        public void Sending_order_with_same_guid_should_be_rejected()
        {
            var quantity = 1;
            var price = 10000;
            var someSide = Side.Bid;
            var market = new Market(Currency.BTC, Currency.USD);

            foreach (var exchange in CreateExchangesOfDifferentTypes())
            {
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
        }

        internal static void SendOrder(Exchange exchange,
                               LimitOrder limitOrder,
                               Market market)
        {
            var nonMakerOnlyLimitOrder =
                new LimitOrderRequest(limitOrder, LimitOrderRequestType.Normal);
            exchange.SendLimitOrder(nonMakerOnlyLimitOrder, market);
        }

        [Test]
        public void Cancelling_order_works()
        {
            var quantity = 1;
            var price = 10000;
            var someSide = Side.Bid;
            var market = new Market(Currency.BTC, Currency.USD);

            foreach (var exchange in CreateExchangesOfDifferentTypes())
            {
                var someOrder =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), someSide, quantity),
                                   price);
                SendOrder(exchange, someOrder, market);

                Assert.That(exchange[market][someSide].Count(), Is.EqualTo(1));

                exchange.CancelLimitOrder(someOrder.OrderInfo.Id);

                Assert.That(exchange[market][someSide].Count(), Is.EqualTo(0));
            }
        }
    }
}
