using System;
using System.Linq;

using FsharpExchangeDotNetStandard;

using NUnit.Framework;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class LimitOrders
    {
        internal static Exchange Limit_order_is_accepted_by_empty_exchange
            (LimitOrder limitOrder,
             Market market)
        {
            var exchange = new Exchange();
            var side = limitOrder.Side;

            // first make sure exchange's orderbook is empty
            var btcUsdOrderBook = exchange[market];
            Assert.That(btcUsdOrderBook[Side.Buy].Count(), Is.EqualTo(0),
                        "initial exchange state should be zero orders (buy)");
            Assert.That(btcUsdOrderBook[Side.Sell].Count(), Is.EqualTo(0),
                        "initial exchange state should be zero orders (sell)");

            exchange.SendLimitOrder(limitOrder, market);
            var btcUsdOrderBookAgain = exchange[market];
            Assert.That(btcUsdOrderBookAgain[side].Count(), Is.EqualTo(1));
            var uniqueLimitOrder = btcUsdOrderBookAgain[side].ElementAt(0);
            Assert.That(uniqueLimitOrder.Side, Is.EqualTo(side));
            Assert.That(uniqueLimitOrder.Price,
                        Is.EqualTo(limitOrder.Price));
            Assert.That(uniqueLimitOrder.Quantity,
                        Is.EqualTo(limitOrder.Quantity));

            Assert.That(btcUsdOrderBookAgain[side.Other()].Count(), Is.EqualTo(0));

            return exchange;
        }

        [Test]
        public void Limit_order_is_sent_properly_and_shows_up_in_order_book()
        {
            var quantity = 1;
            var price = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var buyOrder = new LimitOrder(Side.Buy, quantity, price);
            Limit_order_is_accepted_by_empty_exchange(buyOrder, market);

            var sellOrder = new LimitOrder(Side.Sell, quantity, price);
            Limit_order_is_accepted_by_empty_exchange(sellOrder, market);
        }
    }
}
