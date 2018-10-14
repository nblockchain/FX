//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

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

        [Test]
        [Ignore("doesn't work yet")]
        public void SendingALimitOrderMakesOrderBeVisibleInRedis()
        {
            var exchange = new Exchange(Persistence.Redis);

            var quantity = 1;
            var price = 10000;
            var side = Side.Buy;
            var market = new Market(Currency.BTC, Currency.USD);

            // TODO: assert orderbook is empty first
            var orderBook = exchange[market];

            var tipQuery = new MarketQuery(market, side, true);

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

            var limitOrder =
                new LimitOrder(new OrderInfo(side, quantity), price);
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
            }
        }

    }
}
