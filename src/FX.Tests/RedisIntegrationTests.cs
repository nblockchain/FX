
using System;
using System.Collections.Generic;

using FsharpExchangeDotNetStandard;
using FsharpExchangeDotNetStandard.Redis;

using System.Text.Json;
using NUnit.Framework;
using StackExchange.Redis;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class RedisIntegrationTests
    {

        [SetUp]
        public void ClearRedis()
        {
            BasicTests.ClearRedisStorage();
        }

        private Exchange CreateExchangeAndSendFirstLimitOrder
                             (LimitOrder limitOrder)
        {
            var exchange = new Exchange(Persistence.Redis);

            var market = new Market(Currency.BTC, Currency.USD);

            // TODO: assert orderbook is empty first
            var orderBook = exchange[market];

            var tipQuery = new MarketQuery(market, limitOrder.OrderInfo.Side, true);

            //e.g. {"Market":{"BuyCurrency":"BTC","SellCurrency":"USD"},"Side":"Bid","Tip":true}"
            string tipQueryStr = JsonSerializer.Serialize(tipQuery, Serialization.serializationOptions);

            using (var redis = ConnectionMultiplexer.Connect("localhost"))
            {
                var db = redis.GetDatabase();

                var value = db.StringGet(tipQueryStr);
                Assert.That(value.HasValue, Is.EqualTo(false),
                            "should be empty market");
                Assert.That(value.IsNull, Is.EqualTo(true),
                            "should be empty(null) market");
            }

            var orderReq =
                new LimitOrderRequest(limitOrder, LimitOrderRequestType.Normal);
            exchange.SendLimitOrder(orderReq, market);

            // TODO: assert orderbook is non-empty now
            var afterOrderBook = exchange[market];

            using(var redis = ConnectionMultiplexer.Connect("localhost"))
            {
                var db = redis.GetDatabase();

                var value = db.StringGet(tipQueryStr);
                Assert.That(value.HasValue, Is.EqualTo(true),
                            "should have a tip in this market");
                Assert.That(value.IsNull, Is.EqualTo(false),
                            "should have a tip(not null) in this market");

                var orderId = value.ToString();
                Assert.That(orderId,
                            Is.EqualTo(limitOrder.OrderInfo.Id.ToString()),
                            "received order should have same ID");

                var order = db.StringGet(orderId);
                Assert.That(order.HasValue, Is.EqualTo(true),
                            "should have the order content");
                Assert.That(order.IsNull, Is.EqualTo(false),
                            "should have the order content(not null)");

                var limitOrderSerialized =
                    JsonSerializer.Serialize(limitOrder, Serialization.serializationOptions);
                Assert.That(order.ToString(),
                            Is.EqualTo(limitOrderSerialized),
                            "received order should have same content");
            }

            return exchange;
        }

        [Test]
        public void SendingFirstLimitOrderMakesTipOrderBeVisibleInRedis()
        {
            var quantity = 1;
            var price = 10000;
            var side = Side.Bid;
            var market = new Market(Currency.BTC, Currency.USD);

            var limitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantity), price);

            CreateExchangeAndSendFirstLimitOrder(limitOrder);
        }

        [Test]
        public void SendingSecondAndThirdLimitOrderMakesNonTipQueryWorkAfter()
        {
            var quantity = 1;
            var price = 10000;
            var side = Side.Bid;
            var market = new Market(Currency.BTC, Currency.USD);

            var firstLimitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(),
                                             side, quantity), price);

            var exchange = CreateExchangeAndSendFirstLimitOrder(firstLimitOrder);

            var secondLimitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(),
                                             side, quantity), price - 1);
            var orderReq = new LimitOrderRequest(secondLimitOrder,
                                                 LimitOrderRequestType.Normal);
            exchange.SendLimitOrder(orderReq, market);

            var thirdLimitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(),
                                 side, quantity), price - 2);
            orderReq = new LimitOrderRequest(thirdLimitOrder,
                                             LimitOrderRequestType.Normal);
            exchange.SendLimitOrder(orderReq, market);

            var nonTipQuery = new MarketQuery(market, side, false);

            //e.g. {"Market":{"BuyCurrency":"BTC","SellCurrency":"USD"},"Side":"Bid","Tip":false}"
            string nontipQueryStr = JsonSerializer.Serialize(nonTipQuery, Serialization.serializationOptions);

            using (var redis = ConnectionMultiplexer.Connect("localhost"))
            {
                var db = redis.GetDatabase();

                var values = db.StringGet(nontipQueryStr);
                Assert.That(String.IsNullOrEmpty(values), Is.False,
                            "should have nontip tail(not null) in this market");
                var orders = JsonSerializer.Deserialize<List<string>>(values, Serialization.serializationOptions);
                Assert.That(orders.Count, Is.EqualTo(2),
                    "should have nontip tail of 2 elements in this market now");

                Assert.That(orders[0],
                            Is.EqualTo(secondLimitOrder.OrderInfo.Id.ToString()),
                            "first order in tail is wrong");
                Assert.That(orders[1],
                            Is.EqualTo(thirdLimitOrder.OrderInfo.Id.ToString()),
                            "second order in tail is wrong");

                var order2 = db.StringGet(orders[0]);
                Assert.That(order2.HasValue, Is.EqualTo(true),
                            "should have the second order content");
                Assert.That(order2.IsNull, Is.EqualTo(false),
                            "should have the second order content(not null)");
                var secondLimitOrderSerialized =
                    JsonSerializer.Serialize(secondLimitOrder, Serialization.serializationOptions);
                Assert.That(order2.ToString(),
                            Is.EqualTo(secondLimitOrderSerialized),
                            "received second order should have same content");

                var order3 = db.StringGet(orders[1]);
                Assert.That(order3.HasValue, Is.EqualTo(true),
                            "should have the third order content");
                Assert.That(order3.IsNull, Is.EqualTo(false),
                            "should have the third order content(not null)");
                var thirdLimitOrderSerialized =
                    JsonSerializer.Serialize(thirdLimitOrder, Serialization.serializationOptions);
                Assert.That(order3.ToString(),
                            Is.EqualTo(thirdLimitOrderSerialized),
                            "received second order should have same content");
            }
        }

        [Test]
        public void TipIsReplaced()
        {
            var quantity = 1;
            var price = 10000;
            var side = Side.Bid;
            var market = new Market(Currency.BTC, Currency.USD);

            var firstLimitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(),
                                             side, quantity), price);

            var exchange = CreateExchangeAndSendFirstLimitOrder(firstLimitOrder);

            var secondLimitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(),
                                             side, quantity), price + 1);
            var orderReq = new LimitOrderRequest(secondLimitOrder,
                                                 LimitOrderRequestType.Normal);
            exchange.SendLimitOrder(orderReq, market);


            var nonTipQuery = new MarketQuery(market, side, false);

            //e.g. {"Market":{"BuyCurrency":"BTC","SellCurrency":"USD"},"Side":"Bid","Tip":false}"
            string nontipQueryStr = JsonSerializer.Serialize(nonTipQuery, Serialization.serializationOptions);

            using (var redis = ConnectionMultiplexer.Connect("localhost"))
            {
                var db = redis.GetDatabase();

                var values = db.StringGet(nontipQueryStr);
                Assert.That(String.IsNullOrEmpty(values), Is.False,
                            "should have nontip tail(not null) in this market");
                var orders = JsonSerializer.Deserialize<List<string>>(values, Serialization.serializationOptions);
                Assert.That(orders.Count, Is.EqualTo(1),
                    "should have nontip tail of 2 elements in this market now");

                Assert.That(orders[0],
                            Is.EqualTo(firstLimitOrder.OrderInfo.Id.ToString()),
                            "first order in tail should now be first order");

                var theOrder = db.StringGet(orders[0]);
                Assert.That(theOrder.HasValue, Is.EqualTo(true),
                            "should have the second order content");
                Assert.That(theOrder.IsNull, Is.EqualTo(false),
                            "should have the second order content(not null)");
                var firstLimitOrderSerialized =
                    JsonSerializer.Serialize(firstLimitOrder, Serialization.serializationOptions);
                Assert.That(theOrder.ToString(),
                            Is.EqualTo(firstLimitOrderSerialized),
                            "received second order should have same content");
            }
        }

    }
}
