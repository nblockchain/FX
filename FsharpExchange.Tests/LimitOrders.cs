using System;
using System.Collections.Generic;
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

        private static void AssertAreSameOrdersRegardlessOfOrder
            (ICollection<LimitOrder> a,
             ICollection<LimitOrder> b)
        {
            Assert.That(a.Count, Is.EqualTo(b.Count));

            if (a.Count > 0)
            {
                var first = a.First();

                LimitOrder otherEquivalentFound = null;
                foreach(var otherOrderInB in b)
                {
                    if (first.Side != otherOrderInB.Side)
                        throw new Exception("Something went very wrong, it seems we're comparing a Bid orderBookSide with an Ask one?");

                    if (first.Price == otherOrderInB.Price &&
                        first.Quantity == otherOrderInB.Quantity)
                    {
                        otherEquivalentFound = otherOrderInB;
                        break;
                    }
                }
                if (otherEquivalentFound == null)
                {
                    throw new Exception($"Order with price {first.Price} and quantity {first.Quantity} not found on the other side");
                }

                var newA = new List<LimitOrder>(a);
                newA.Remove(first);
                var newB = new List<LimitOrder>(b);
                newB.Remove(otherEquivalentFound);
                AssertAreSameOrdersRegardlessOfOrder(newA, newB);
            }
        }

        private static void Limit_orders_of_same_side_never_match(Side side)
        {
            var quantity = 1;
            var price = 10000;
            var secondAndThirdPrice = price + 1;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            // first make sure exchange's orderbook is empty
            var orderBook = exchange[market];

            var firstLimitOrder = new LimitOrder(side, quantity, price);
            exchange.SendLimitOrder(firstLimitOrder, market);

            var secondLimitOrder =
                new LimitOrder(side, quantity, secondAndThirdPrice);
            exchange.SendLimitOrder(secondLimitOrder, market);

            var thirdLimitOrder =
                new LimitOrder(side, quantity, secondAndThirdPrice);
            exchange.SendLimitOrder(secondLimitOrder, market);

            var allLimitOrdersSent = new List<LimitOrder> {
                firstLimitOrder, secondLimitOrder, thirdLimitOrder
            };

            var orderBookAgain = exchange[market];

            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(0));
            var ourSide = new List<LimitOrder>(orderBookAgain[side]);
            AssertAreSameOrdersRegardlessOfOrder(allLimitOrdersSent, ourSide);
        }

        [Test]
        public void Limit_orders_of_same_side_never_match()
        {
            Limit_orders_of_same_side_never_match(Side.Buy);

            Limit_orders_of_same_side_never_match(Side.Sell);
        }

        private static void Limit_orders_of_different_sides_but_different_price_dont_match
            (Side side)
        {
            var quantity = 1;
            var price = 10000;
            var opposingPrice = side == Side.Buy ? price + 1 : price - 1;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            // first make sure exchange's orderbook is empty
            var orderBook = exchange[market];

            var firstLimitOrder = new LimitOrder(side, quantity, price);
            exchange.SendLimitOrder(firstLimitOrder, market);

            var secondLimitOrder =
                new LimitOrder(side.Other(), quantity, opposingPrice);
            exchange.SendLimitOrder(secondLimitOrder, market);

            var orderBookAgain = exchange[market];

            Assert.That(orderBookAgain[side].Count(), Is.EqualTo(1));
            var aLimitOrder = orderBookAgain[side].ElementAt(0);
            Assert.That(aLimitOrder.Side,
                        Is.EqualTo(firstLimitOrder.Side));
            Assert.That(aLimitOrder.Price,
                        Is.EqualTo(firstLimitOrder.Price));
            Assert.That(aLimitOrder.Quantity,
                        Is.EqualTo(firstLimitOrder.Quantity));

            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(1));
            var anotherLimitOrder = orderBookAgain[side.Other()].ElementAt(0);
            Assert.That(anotherLimitOrder.Side,
                        Is.EqualTo(secondLimitOrder.Side));
            Assert.That(anotherLimitOrder.Price,
                        Is.EqualTo(secondLimitOrder.Price));
            Assert.That(anotherLimitOrder.Quantity,
                        Is.EqualTo(secondLimitOrder.Quantity));
        }

        [Test]
        public void Limit_orders_of_different_sides_but_different_price_dont_match()
        {
            Limit_orders_of_different_sides_but_different_price_dont_match(Side.Buy);

            Limit_orders_of_different_sides_but_different_price_dont_match(Side.Sell);
        }

        private static void Limit_order_can_cross_another_limit_order_of_same_amount_and_same_price
            (Side side)
        {
            var quantity = 1;
            var price = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            // first make sure exchange's orderbook is empty
            var orderBook = exchange[market];

            var firstLimitOrder = new LimitOrder(side, quantity, price);
            exchange.SendLimitOrder(firstLimitOrder, market);

            var secondLimitMatchingOrder =
                new LimitOrder(side.Other(), quantity, price);
            exchange.SendLimitOrder(secondLimitMatchingOrder, market);

            var orderBookAgain = exchange[market];
            Assert.That(orderBookAgain[side].Count(), Is.EqualTo(0));
            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(0));
        }

        [Test]
        public void Limit_order_can_cross_another_limit_order_of_same_amount_and_same_price()
        {
            Limit_order_can_cross_another_limit_order_of_same_amount_and_same_price(Side.Buy);

            Limit_order_can_cross_another_limit_order_of_same_amount_and_same_price(Side.Sell);
        }

        private static void Limit_orders_of_different_sides_and_different_price_can_match
            (Side side)
        {
            var quantity = 1;
            var price = 1000;
            var opposingPrice = side == Side.Buy ? price - 1 : price + 1;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            var orderBook = exchange[market];

            var firstLimitOrder = new LimitOrder(side, quantity, price);
            exchange.SendLimitOrder(firstLimitOrder, market);

            var secondLimitOrder =
                new LimitOrder(side.Other(), quantity, opposingPrice);
            exchange.SendLimitOrder(secondLimitOrder, market);

            var orderBookAgain = exchange[market];

            Assert.That(orderBookAgain[side].Count(), Is.EqualTo(0));
            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(0));
        }

        [Test] // they match at the price of the original order
        public void Limit_orders_of_different_sides_and_different_price_can_match()
        {
            Limit_orders_of_different_sides_and_different_price_can_match(Side.Buy);

            Limit_orders_of_different_sides_and_different_price_can_match(Side.Sell);
        }

        private static void Limit_order_half_crosses_another_limit_order_of_same_price
            (Side side)
        {
            var quantityOfFirstOrder = 2;
            var quantityOfSecondOrder = quantityOfFirstOrder - 1;
            var price = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            // first make sure exchange's orderbook is empty
            var orderBook = exchange[market];

            var firstLimitOrder =
                new LimitOrder(side, quantityOfFirstOrder, price);
            exchange.SendLimitOrder(firstLimitOrder, market);

            var secondLimitMatchingOrder =
                new LimitOrder(side.Other(), quantityOfSecondOrder, price);
            exchange.SendLimitOrder(secondLimitMatchingOrder, market);

            var orderBookAgain = exchange[market];
            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(0));
            Assert.That(orderBookAgain[side].Count(), Is.EqualTo(1));
            var leftOverLimitOrder = orderBookAgain[side].ElementAt(0);
            Assert.That(leftOverLimitOrder.Side,
                        Is.EqualTo(side));
            Assert.That(leftOverLimitOrder.Price,
                        Is.EqualTo(price));
            Assert.That(leftOverLimitOrder.Quantity,
                        Is.EqualTo(quantityOfFirstOrder - quantityOfSecondOrder));
        }

        [Test]
        public void Limit_order_half_crosses_another_limit_order_of_same_price()
        {
            Limit_order_half_crosses_another_limit_order_of_same_price(Side.Buy);

            Limit_order_half_crosses_another_limit_order_of_same_price(Side.Sell);
        }

        private static void Limit_order_crosses_two_limit_orders_of_same_price (Side side)
        {
            var quantityOfEachOfTheSittingOrders = 1;
            var quantityOfIncomingOrder = quantityOfEachOfTheSittingOrders * 2;
            var price = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            // first make sure exchange's orderbook is empty
            var orderBook = exchange[market];

            var firstLimitOrder =
                new LimitOrder(side, quantityOfEachOfTheSittingOrders, price);
            exchange.SendLimitOrder(firstLimitOrder, market);

            var secondLimitMatchingOrder =
                new LimitOrder(side, quantityOfEachOfTheSittingOrders, price);
            exchange.SendLimitOrder(secondLimitMatchingOrder, market);

            var incomingLimitMatchingOrder =
                new LimitOrder(side.Other(), quantityOfIncomingOrder, price);
            exchange.SendLimitOrder(incomingLimitMatchingOrder, market);

            var orderBookAgain = exchange[market];
            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(0));
            Assert.That(orderBookAgain[side].Count(), Is.EqualTo(0));
        }

        [Test]
        public void Limit_order_crosses_two_limit_orders_of_same_price()
        {
            Limit_order_crosses_two_limit_orders_of_same_price(Side.Buy);

            Limit_order_crosses_two_limit_orders_of_same_price(Side.Sell);
        }

        private static void Limit_order_crosses_one_limit_order_and_stays_partially_after_no_more_liquidity_left_in_one_side(Side side)
        {
            var quantityOfThePreviouslySittingOrder = 1;
            var quantityOfIncomingOrder = quantityOfThePreviouslySittingOrder + 1;
            var price = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            // first make sure exchange's orderbook is empty
            var orderBook = exchange[market];

            var firstLimitOrder =
                new LimitOrder(side, quantityOfThePreviouslySittingOrder, price);
            exchange.SendLimitOrder(firstLimitOrder, market);

            var incomingLimitMatchingOrder =
                new LimitOrder(side.Other(), quantityOfIncomingOrder, price);
            exchange.SendLimitOrder(incomingLimitMatchingOrder, market);

            var orderBookAgain = exchange[market];
            Assert.That(orderBookAgain[side].Count(), Is.EqualTo(0));
            Assert.That(orderBookAgain[side.Other()].Count(), Is.EqualTo(1));

            var leftOverLimitOrder = orderBookAgain[side.Other()].ElementAt(0);
            Assert.That(leftOverLimitOrder.Side,
                        Is.EqualTo(incomingLimitMatchingOrder.Side));
            Assert.That(leftOverLimitOrder.Price,
                        Is.EqualTo(price));
            Assert.That(leftOverLimitOrder.Quantity,
                        Is.EqualTo(quantityOfIncomingOrder - quantityOfThePreviouslySittingOrder));
        }

        [Test]
        public void Limit_order_crosses_one_limit_order_and_stays_partially_after_no_more_liquidity_left_in_one_side()
        {
            Limit_order_crosses_one_limit_order_and_stays_partially_after_no_more_liquidity_left_in_one_side(Side.Buy);

            Limit_order_crosses_one_limit_order_and_stays_partially_after_no_more_liquidity_left_in_one_side(Side.Sell);
        }

        private static OrderBook CreateNewExchangeAndSendTheseOrdersToIt
        (IEnumerable<LimitOrder> orders)
        {
            var exchange = new Exchange();
            var someMarket = new Market(Currency.BTC, Currency.USD);
            foreach(var order in orders)
            {
                exchange.SendLimitOrder(order, someMarket);
            }
            return exchange[someMarket];
        }

        private static void Limit_order_should_always_cross_if_there_is_a_matching_limit_order_regardless_of_the_order_they_were_inserted_in_previously
            (Side side)
        {
            var quantity = 1;
            var lowestPriceOfOrderBookSide = 10000;
            var highestPriceOfOrderBookSide = 15000;
            var tipPrice = side == Side.Buy ?
                highestPriceOfOrderBookSide : lowestPriceOfOrderBookSide;
            var nonTipPrice = side == Side.Buy ?
                lowestPriceOfOrderBookSide : highestPriceOfOrderBookSide;

            var lowestSittingLimitOrder =
                new LimitOrder(side, quantity, lowestPriceOfOrderBookSide);

            var highestSittingLimitOrder =
                new LimitOrder(side, quantity, highestPriceOfOrderBookSide);

            var limitOrderMatchingWithTipPrice =
                new LimitOrder(side.Other(), quantity, tipPrice);


            var combination1 = new[] { lowestSittingLimitOrder, highestSittingLimitOrder, limitOrderMatchingWithTipPrice };
            var combination2 = new[] { highestSittingLimitOrder, lowestSittingLimitOrder, limitOrderMatchingWithTipPrice };

            var allCombinations = new[] {
                combination1,
                combination2,
            };

            int combinationCount = 1;
            foreach (var combination in allCombinations) {
                Assert.That(combination.Count(), Is.EqualTo(3),
                            "this test was meant to just test combinations of 3 orders, not more");

                var testMsg =
                    $"testing combination {combinationCount} with {side}";
                var orderBook =
                    CreateNewExchangeAndSendTheseOrdersToIt(combination);
                Assert.That(orderBook[side.Other()].Count(), Is.EqualTo(0),
                            "(count of the other side) " + testMsg);
                Assert.That(orderBook[side].Count(), Is.EqualTo(1),
                            "(count of this side) " + testMsg);

                var leftOverLimitOrder = orderBook[side].ElementAt(0);
                Assert.That(leftOverLimitOrder.Side,
                            Is.EqualTo(side));

                Assert.That(leftOverLimitOrder.Quantity,
                            Is.EqualTo(quantity));

                var lastOrderWhichIsTheMatchingOrder = combination.Last();
                Assert.That(leftOverLimitOrder.Price,
                            Is.Not.EqualTo(tipPrice),
                            testMsg);

                Assert.That(leftOverLimitOrder.Price,
                            Is.EqualTo(nonTipPrice));

                combinationCount++;
            }
        }

        [Test]
        [Ignore("doesn't work yet, at the moment of writing only works with combination2 and combination3, but not 1 or 4")]
        public void Limit_order_should_always_cross_if_there_is_a_matching_limit_order_regardless_of_the_order_they_were_inserted_in_previously()
        {
            Limit_order_should_always_cross_if_there_is_a_matching_limit_order_regardless_of_the_order_they_were_inserted_in_previously(Side.Buy);

            Limit_order_should_always_cross_if_there_is_a_matching_limit_order_regardless_of_the_order_they_were_inserted_in_previously(Side.Sell);
        }
    }
}
