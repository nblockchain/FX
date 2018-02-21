//
// Copyright (C) 2017-2018 Andres G. Aragoneses - All Rights Reserved
//


﻿
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

            SendOrder(exchange, limitOrder, market);
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

        private static void SendOrder(Exchange exchange,
                                       LimitOrder limitOrder,
                                       Market market)
        {
            var makerOnlyLimitOrder =
                new LimitOrderRequest(limitOrder, LimitOrderRequestType.MakerOnly);
            exchange.SendLimitOrder(makerOnlyLimitOrder, market);
        }

        private static void MakerOnly_orders_of_same_side_never_match(Side side)
        {
            var quantity = 1;
            var price = 10000;
            var secondAndThirdPrice = price + 1;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            var orderBook = exchange[market];

            var firstLimitOrder =
                new LimitOrder(new OrderInfo(side, quantity), price);
            SendOrder(exchange, firstLimitOrder, market);

            var secondLimitOrder =
                new LimitOrder(new OrderInfo(side, quantity), secondAndThirdPrice);
            SendOrder(exchange, secondLimitOrder, market);

            var thirdLimitOrder =
                new LimitOrder(new OrderInfo(side, quantity), secondAndThirdPrice);
            SendOrder(exchange, secondLimitOrder, market);

            var allLimitOrdersSent = new List<LimitOrder> {
                firstLimitOrder, secondLimitOrder, thirdLimitOrder
            };

            var orderBookAgain = exchange[market];

            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(0));
            var ourSide = new List<LimitOrder>(orderBookAgain[side]);
            LimitOrders.AssertAreSameOrdersRegardlessOfOrder(allLimitOrdersSent,
                                                             ourSide);
        }

        [Test]
        public void MakerOnly_orders_of_same_side_never_match()
        {
            MakerOnly_orders_of_same_side_never_match(Side.Buy);

            MakerOnly_orders_of_same_side_never_match(Side.Sell);
        }

        private static void MakerOnly_orders_of_different_sides_but_different_price_dont_match
            (Side side)
        {
            var quantity = 1;
            var price = 10000;
            var opposingPrice = side == Side.Buy ? price + 1 : price - 1;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            var orderBook = exchange[market];

            var firstMakerOnlyOrder =
                new LimitOrder(new OrderInfo(side, quantity), price);
            SendOrder(exchange, firstMakerOnlyOrder, market);

            var secondMakerOnlyOrder =
                new LimitOrder(new OrderInfo(side.Other(), quantity),
                               opposingPrice);
            SendOrder(exchange, secondMakerOnlyOrder, market);

            var orderBookAgain = exchange[market];

            Assert.That(orderBookAgain[side].Count(), Is.EqualTo(1));
            var aLimitOrder = orderBookAgain[side].ElementAt(0);
            Assert.That(aLimitOrder.OrderInfo.Side,
                        Is.EqualTo(firstMakerOnlyOrder.OrderInfo.Side));
            Assert.That(aLimitOrder.Price,
                        Is.EqualTo(firstMakerOnlyOrder.Price));
            Assert.That(aLimitOrder.OrderInfo.Quantity,
                        Is.EqualTo(firstMakerOnlyOrder.OrderInfo.Quantity));

            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(1));
            var anotherLimitOrder = orderBookAgain[side.Other()].ElementAt(0);
            Assert.That(anotherLimitOrder.OrderInfo.Side,
                        Is.EqualTo(secondMakerOnlyOrder.OrderInfo.Side));
            Assert.That(anotherLimitOrder.Price,
                        Is.EqualTo(secondMakerOnlyOrder.Price));
            Assert.That(anotherLimitOrder.OrderInfo.Quantity,
                        Is.EqualTo(secondMakerOnlyOrder.OrderInfo.Quantity));
        }

        [Test]
        public void MakerOnly_orders_of_different_sides_but_different_price_dont_match()
        {
            MakerOnly_orders_of_different_sides_but_different_price_dont_match(Side.Buy);

            MakerOnly_orders_of_different_sides_but_different_price_dont_match(Side.Sell);
        }

        private static void MakerOnly_order_can_not_cross_another_limit_order_of_same_amount_and_same_price_and_should_be_rejected
        (Side side)
        {
            var quantity = 1;
            var price = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            var orderBook = exchange[market];

            var firstLimitOrder =
                new LimitOrder(new OrderInfo(side, quantity), price);
            SendOrder(exchange, firstLimitOrder, market);

            var secondLimitMatchingOrder =
                new LimitOrder(new OrderInfo(side.Other(), quantity), price);
            Assert.Throws<MatchExpectationsUnmet>(() =>
            {
                SendOrder(exchange, secondLimitMatchingOrder, market);
            });

            var orderBookAgain = exchange[market];
            Assert.That(orderBookAgain[side].Count(), Is.EqualTo(1));
            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(0));
        }

        [Test]
        public void MakerOnly_order_can_not_cross_another_limit_order_of_same_amount_and_same_price_and_should_be_rejected()
        {
            MakerOnly_order_can_not_cross_another_limit_order_of_same_amount_and_same_price_and_should_be_rejected(Side.Buy);

            MakerOnly_order_can_not_cross_another_limit_order_of_same_amount_and_same_price_and_should_be_rejected(Side.Sell);
        }
    }
}
