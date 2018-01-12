using System;
using System.Collections.Generic;
using System.Linq;

using FsharpExchangeDotNetStandard;

using NUnit.Framework;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class MakerOnlyOrders
    {
        internal static Exchange Limit_order_is_accepted_by_empty_exchange
            (LimitOrder limitOrder,
             Market market)
        {
            var exchange = new Exchange();
            var side = limitOrder.OrderInfo.Side;

            // first make sure exchange's orderbook is empty
            var someOrderBook = exchange[market];
            Assert.That(someOrderBook[Side.Buy].Count(), Is.EqualTo(0),
                        "initial exchange state should be zero orders (buy)");
            Assert.That(someOrderBook[Side.Sell].Count(), Is.EqualTo(0),
                        "initial exchange state should be zero orders (sell)");

            var orderRequest =
                new LimitOrderRequest(limitOrder, LimitOrderRequestType.MakerOnly);
            exchange.SendLimitOrder(orderRequest, market);
            var someOrderBookAgain = exchange[market];
            Assert.That(someOrderBookAgain[side].Count(), Is.EqualTo(1));
            var uniqueLimitOrder = someOrderBookAgain[side].ElementAt(0);
            Assert.That(uniqueLimitOrder.OrderInfo.Side, Is.EqualTo(side));
            Assert.That(uniqueLimitOrder.Price,
                        Is.EqualTo(limitOrder.Price));
            Assert.That(uniqueLimitOrder.OrderInfo.Quantity,
                        Is.EqualTo(limitOrder.OrderInfo.Quantity));

            Assert.That(someOrderBookAgain[side.Other()].Count(), Is.EqualTo(0));

            return exchange;
        }

        [Test]
        public void MakerOnly_order_is_sent_properly_and_shows_up_in_order_book()
        {
            var quantity = 1;
            var price = 10000;
            var someMarket = new Market(Currency.BTC, Currency.USD);

            var buyOrder =
                new LimitOrder(new OrderInfo(Side.Buy, quantity), price);
            Limit_order_is_accepted_by_empty_exchange(buyOrder, someMarket);

            var sellOrder =
                new LimitOrder(new OrderInfo(Side.Sell, quantity), price);
            Limit_order_is_accepted_by_empty_exchange(sellOrder, someMarket);
        }
    }
}
