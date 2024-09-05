
using System;
using System.Collections.Generic;

using FsharpExchangeDotNetStandard;

using NUnit.Framework;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class MakerOnlyOrders
    {
        [TearDown]
        public void TearDown()
        {
            BasicTests.ClearRedisStorage();
        }

        internal static IEnumerable<Exchange>
            Limit_order_is_accepted_by_empty_exchange
            (LimitOrder limitOrder,
             Market market)
        {
            foreach (var exchange in BasicTests.CreateExchangesOfDifferentTypes())
            {
                var side = limitOrder.OrderInfo.Side;

                // first make sure exchange's orderbook is empty
                var someOrderBook = exchange[market];
                Assert.That(someOrderBook[Side.Bid].Count(), Is.EqualTo(0),
                            "initial exchange state should be zero orders (buy)");
                Assert.That(someOrderBook[Side.Ask].Count(), Is.EqualTo(0),
                            "initial exchange state should be zero orders (sell)");

                SendOrder(exchange, limitOrder, market);
                var someOrderBookAgain = exchange[market];
                Assert.That(someOrderBookAgain[side].Count(), Is.EqualTo(1));
                var uniqueLimitOrder = someOrderBookAgain[side].Tip.Value;
                Assert.That(uniqueLimitOrder.OrderInfo.Side, Is.EqualTo(side));
                Assert.That(uniqueLimitOrder.Price,
                            Is.EqualTo(limitOrder.Price));
                Assert.That(uniqueLimitOrder.OrderInfo.Quantity,
                            Is.EqualTo(limitOrder.OrderInfo.Quantity));

                Assert.That(someOrderBookAgain[side.Other()].Count(), Is.EqualTo(0));

                yield return exchange;
            }
        }

        [Test]
        public void MakerOnly_order_is_sent_properly_and_shows_up_in_order_book()
        {
            var quantity = 1;
            var price = 10000;
            var someMarket = new Market(Currency.BTC, Currency.USD);

            var buyOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), Side.Bid, quantity), price);
            Limit_order_is_accepted_by_empty_exchange(buyOrder, someMarket);

            var sellOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), Side.Ask, quantity), price);
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

            foreach (var exchange in BasicTests.CreateExchangesOfDifferentTypes())
            {
                var orderBook = exchange[market];

                var firstLimitOrder =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantity), price);
                SendOrder(exchange, firstLimitOrder, market);

                var secondLimitOrder =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantity), secondAndThirdPrice);
                SendOrder(exchange, secondLimitOrder, market);

                var thirdLimitOrder =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantity), secondAndThirdPrice);
                SendOrder(exchange, thirdLimitOrder, market);

                var allLimitOrdersSent = new List<LimitOrder> {
                    firstLimitOrder, secondLimitOrder, thirdLimitOrder
                };

                var orderBookAgain = exchange[market];

                Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(0));
                var ourSide = new List<LimitOrder>();
                var orderBookThisSide = orderBookAgain[side];
                while (true)
                {
                    try
                    {
                        var tip = orderBookThisSide.Tip.Value;
                        ourSide.Add(tip);
                        orderBookThisSide = orderBookThisSide.Tail.Value;
                    }
                    catch
                    {
                        break;
                    }
                }
                LimitOrders.AssertAreSameOrdersRegardlessOfOrder(allLimitOrdersSent,
                                                                 ourSide);
            }
        }

        [Test]
        public void MakerOnly_orders_of_same_side_never_match()
        {
            MakerOnly_orders_of_same_side_never_match(Side.Bid);

            MakerOnly_orders_of_same_side_never_match(Side.Ask);
        }

        private static void MakerOnly_orders_of_different_sides_but_different_price_dont_match
            (Side side)
        {
            var quantity = 1;
            var price = 10000;
            var opposingPrice = side == Side.Bid ? price + 1 : price - 1;
            var market = new Market(Currency.BTC, Currency.USD);

            foreach (var exchange in BasicTests.CreateExchangesOfDifferentTypes())
            {
                var orderBook = exchange[market];

                var firstMakerOnlyOrder =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantity), price);
                SendOrder(exchange, firstMakerOnlyOrder, market);

                var secondMakerOnlyOrder =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), side.Other(), quantity),
                                   opposingPrice);
                SendOrder(exchange, secondMakerOnlyOrder, market);

                var orderBookAgain = exchange[market];

                Assert.That(orderBookAgain[side].Count(), Is.EqualTo(1));
                var aLimitOrder = orderBookAgain[side].Tip.Value;
                Assert.That(aLimitOrder.OrderInfo.Side,
                            Is.EqualTo(firstMakerOnlyOrder.OrderInfo.Side));
                Assert.That(aLimitOrder.Price,
                            Is.EqualTo(firstMakerOnlyOrder.Price));
                Assert.That(aLimitOrder.OrderInfo.Quantity,
                            Is.EqualTo(firstMakerOnlyOrder.OrderInfo.Quantity));

                Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(1));
                var anotherLimitOrder = orderBookAgain[side.Other()].Tip.Value;
                Assert.That(anotherLimitOrder.OrderInfo.Side,
                            Is.EqualTo(secondMakerOnlyOrder.OrderInfo.Side));
                Assert.That(anotherLimitOrder.Price,
                            Is.EqualTo(secondMakerOnlyOrder.Price));
                Assert.That(anotherLimitOrder.OrderInfo.Quantity,
                            Is.EqualTo(secondMakerOnlyOrder.OrderInfo.Quantity));
            }
        }

        [Test]
        public void MakerOnly_orders_of_different_sides_but_different_price_dont_match()
        {
            MakerOnly_orders_of_different_sides_but_different_price_dont_match(Side.Bid);

            MakerOnly_orders_of_different_sides_but_different_price_dont_match(Side.Ask);
        }

        private static void MakerOnly_order_can_not_cross_another_limit_order_of_same_amount_and_same_price_and_should_be_rejected
        (Side side)
        {
            var quantity = 1;
            var price = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            foreach (var exchange in BasicTests.CreateExchangesOfDifferentTypes())
            {
                var orderBook = exchange[market];

                var firstLimitOrder =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantity), price);
                SendOrder(exchange, firstLimitOrder, market);

                var secondLimitMatchingOrder =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), side.Other(), quantity), price);
                Assert.Throws<MatchExpectationsUnmet>(() =>
                {
                    SendOrder(exchange, secondLimitMatchingOrder, market);
                });

                var orderBookAgain = exchange[market];
                Assert.That(orderBookAgain[side].Count(), Is.EqualTo(1));
                Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(0));
            }
        }

        [Test]
        public void MakerOnly_order_can_not_cross_another_limit_order_of_same_amount_and_same_price_and_should_be_rejected()
        {
            MakerOnly_order_can_not_cross_another_limit_order_of_same_amount_and_same_price_and_should_be_rejected(Side.Bid);

            MakerOnly_order_can_not_cross_another_limit_order_of_same_amount_and_same_price_and_should_be_rejected(Side.Ask);
        }
    }
}
