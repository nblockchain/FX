using System;
using System.Linq;

using FsharpExchangeDotNetStandard;

using NUnit.Framework;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class Unit
    {
        [Test]
        public void Limit_order_is_sent_properly_and_shows_up_in_order_book()
        {
            var exchange = new Exchange();
            var market = new Market(Currency.BTC, Currency.USD);

            // first make sure exchange's orderbook is empty
            var btcUsdOrderBook = exchange[market];
            Assert.That(btcUsdOrderBook[Side.Buy].Count(), Is.EqualTo(0),
                        "initial exchange state should be zero orders (buy)");
            Assert.That(btcUsdOrderBook[Side.Sell].Count(), Is.EqualTo(0),
                        "initial exchange state should be zero orders (sell)");

            var amountOfBitcoinToPurchase = 1;
            var priceOfBitcoinInUsd = 10000;
            var order = new LimitOrder(Side.Buy,
                                       amountOfBitcoinToPurchase,
                                       priceOfBitcoinInUsd);

            exchange.SendOrder(order, market);
            var btcUsdOrderBookAgain = exchange[market];
            Assert.That(btcUsdOrderBookAgain[Side.Buy].Count(), Is.EqualTo(1));
            Assert.That(btcUsdOrderBookAgain[Side.Sell].Count(), Is.EqualTo(0));
            var uniqueLimitOrder = btcUsdOrderBookAgain[Side.Buy].ElementAt(0);
            Assert.That(uniqueLimitOrder.Side, Is.EqualTo(Side.Buy));
            Assert.That(uniqueLimitOrder.Price,
                        Is.EqualTo(priceOfBitcoinInUsd));
            Assert.That(uniqueLimitOrder.Quantity,
                        Is.EqualTo(amountOfBitcoinToPurchase));
        }
    }
}
