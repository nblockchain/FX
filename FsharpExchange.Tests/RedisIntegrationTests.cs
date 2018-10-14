//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

using System;
using System.Linq;

using FsharpExchangeDotNetStandard;
using FsharpExchangeDotNetStandard.Redis;

using Newtonsoft.Json;
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
            using (var redis = ConnectionMultiplexer.Connect("localhost,allowAdmin=true"))
            {
                var server = redis.GetServer("localhost:6379");
                server.FlushDatabase();
            }
        }

        private Exchange CreateExchangeAndSendFirstLimitOrder
                             (LimitOrder limitOrder)
        {
            var exchange = new Exchange(Persistence.Redis);

            var market = new Market(Currency.BTC, Currency.USD);

            // TODO: assert orderbook is empty first
            var orderBook = exchange[market];

            var tipQuery = new MarketQuery(market, limitOrder.OrderInfo.Side, true);

            //e.g. {"Market":{"BuyCurrency":{"Case":"BTC"},"SellCurrency":{"Case":"USD"}},"Side":{"Case":"Buy"},"Tip":true}"
            string tipQueryStr = JsonConvert.SerializeObject(tipQuery);

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

                Assert.That((string)value,
                            Is.EqualTo(limitOrder.OrderInfo.Id.ToString()),
                            "received order should have same ID");
            }

            return exchange;
        }

        [Test]
        public void SendingFirstLimitOrderMakesTipOrderBeVisibleInRedis()
        {
            var quantity = 1;
            var price = 10000;
            var side = Side.Buy;
            var market = new Market(Currency.BTC, Currency.USD);

            var limitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantity), price);

            CreateExchangeAndSendFirstLimitOrder(limitOrder);
        }

        [Test]
        [Ignore("Not working yet")]
        public void SendingSecondAndThirdLimitOrderMakesNonTipQueryWorkAfter()
        {
            var quantity = 1;
            var price = 10000;
            var side = Side.Buy;
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

            var thirdLimitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(),
                                 side, quantity), price + 2);
            orderReq = new LimitOrderRequest(thirdLimitOrder,
                                             LimitOrderRequestType.Normal);
            exchange.SendLimitOrder(orderReq, market);

            var nonTipQuery = new MarketQuery(market, side, false);

            //e.g. {"Market":{"BuyCurrency":{"Case":"BTC"},"SellCurrency":{"Case":"USD"}},"Side":{"Case":"Buy"},"Tip":true}"
            string nontipQueryStr = JsonConvert.SerializeObject(nonTipQuery);

            using (var redis = ConnectionMultiplexer.Connect("localhost"))
            {
                var db = redis.GetDatabase();

                var values = db.StringGet(nontipQueryStr);
                Assert.That(String.IsNullOrEmpty(values), Is.False,
                            "should have nontip tail(not null) in this market");
            }
        }

    }
}
