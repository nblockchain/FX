
using System;

using FsharpExchangeDotNetStandard;

using NUnit.Framework;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class MarketOrders
    {
        [TearDown]
        public void TearDown()
        {
            BasicTests.ClearRedisStorage();
        }

        private void Market_order_exact_match_on_exchange_with_one_limit_order(Side side)
        {
            var quantity = 1;
            var priceForLimitOrder = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var otherSide = side.Other();

            var limitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantity),
                               priceForLimitOrder);
            foreach (var exchange in
                LimitOrders.Limit_order_is_accepted_by_empty_exchange(limitOrder, market))
            {
                var marketOrder = new OrderInfo(Guid.NewGuid(), otherSide, quantity);
                exchange.SendMarketOrder(marketOrder, market);
                var btcUsdOrderBookAfterMatching = exchange[market];
                Assert.That(btcUsdOrderBookAfterMatching[Side.Bid].Count(),
                            Is.EqualTo(0));
                Assert.That(btcUsdOrderBookAfterMatching[Side.Ask].Count(),
                            Is.EqualTo(0));
            }
        }

        [Test]
        public void Market_order_exact_match_on_exchange_with_one_limit_order()
        {
            Market_order_exact_match_on_exchange_with_one_limit_order(Side.Bid);

            Market_order_exact_match_on_exchange_with_one_limit_order(Side.Ask);
        }

        private void Market_order_throws_on_exchange_with_no_limit_orders_and_orderbooks_are_left_intact(Side side)
        {
            var quantityForMarketOrder = 1;
            var market = new Market(Currency.BTC, Currency.USD);

            foreach (var exchange in BasicTests.CreateExchangesOfDifferentTypes())
            {
                var marketOrder =
                    new OrderInfo(Guid.NewGuid(), side, quantityForMarketOrder);
                Assert.Throws<LiquidityProblem>(() =>
                {
                    exchange.SendMarketOrder(marketOrder, market);
                });

                var btcUsdOrderBookAfterException = exchange[market];
                Assert.That(btcUsdOrderBookAfterException[Side.Ask].Count(),
                            Is.EqualTo(0));
                Assert.That(btcUsdOrderBookAfterException[Side.Bid].Count(),
                            Is.EqualTo(0));
            }
        }

        [Test]
        public void Market_order_throws_on_exchange_with_no_limit_orders_and_orderbooks_are_left_intact()
        {
            Market_order_throws_on_exchange_with_no_limit_orders_and_orderbooks_are_left_intact(Side.Ask);

            Market_order_throws_on_exchange_with_no_limit_orders_and_orderbooks_are_left_intact(Side.Bid);
        }

        private void Market_order_throws_on_exchange_with_not_enough_liquidity_in_single_limit_order_and_orderbooks_are_left_intact(Side side)
        {
            var quantityForLimitOrder = 1;

            // to make the exchange run out of funds:
            var quantityForMarketOrder = quantityForLimitOrder + 1;

            var priceForLimitOrder = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var otherSide = side.Other();

            var limitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), otherSide, quantityForLimitOrder),
                               priceForLimitOrder);
            foreach (var exchange in
                     LimitOrders.Limit_order_is_accepted_by_empty_exchange(limitOrder, market))
            {
                var marketOrder =
                    new OrderInfo(Guid.NewGuid(), side, quantityForMarketOrder);
                Assert.Throws<LiquidityProblem>(() =>
                {
                    exchange.SendMarketOrder(marketOrder, market);
                });

                var btcUsdOrderBookAfterException = exchange[market];
                Assert.That(btcUsdOrderBookAfterException[side].Count(),
                            Is.EqualTo(0));
                Assert.That(btcUsdOrderBookAfterException[otherSide].Count(),
                            Is.EqualTo(1));
                var uniqueLimitOrder =
                    btcUsdOrderBookAfterException[otherSide].Tip.Value;
                Assert.That(uniqueLimitOrder.OrderInfo.Side, Is.EqualTo(otherSide));
                Assert.That(uniqueLimitOrder.Price,
                            Is.EqualTo(limitOrder.Price));
                Assert.That(uniqueLimitOrder.OrderInfo.Quantity,
                            Is.EqualTo(limitOrder.OrderInfo.Quantity));
            }

        }

        [Test]
        public void Market_order_throws_on_exchange_with_not_enough_liquidity_in_single_limit_order_and_orderbooks_are_left_intact()
        {
            Market_order_throws_on_exchange_with_not_enough_liquidity_in_single_limit_order_and_orderbooks_are_left_intact(Side.Ask);

            Market_order_throws_on_exchange_with_not_enough_liquidity_in_single_limit_order_and_orderbooks_are_left_intact(Side.Bid);
        }

        private void Market_order_throws_on_exchange_with_not_enough_liquidity_in_limit_orders_and_orderbooks_are_left_intact(Side side)
        {
            var quantityForLimitOrder1 = 1;
            var quantityForLimitOrder2 = 1;

            // to make the exchange run out of funds:
            var quantityForMarketOrder = quantityForLimitOrder1 + quantityForLimitOrder2 + 1;

            var priceForLimitOrder = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var otherSide = side.Other();

            var limitOrder1 =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), otherSide, 
                                             quantityForLimitOrder1),
                               priceForLimitOrder);
            foreach (var exchange in
                     LimitOrders.Limit_order_is_accepted_by_empty_exchange
                     (limitOrder1, market))
            {
                var limitOrder2 =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), otherSide, quantityForLimitOrder2),
                                   priceForLimitOrder);
                LimitOrders.SendOrder(exchange, limitOrder2, market);

                var marketOrder =
                    new OrderInfo(Guid.NewGuid(), side, quantityForMarketOrder);
                Assert.Throws<LiquidityProblem>(() =>
                {
                    exchange.SendMarketOrder(marketOrder, market);
                });

                var btcUsdOrderBookAfterException = exchange[market];
                Assert.That(btcUsdOrderBookAfterException[side].Count(),
                            Is.EqualTo(0));
                Assert.That(btcUsdOrderBookAfterException[otherSide].Count(),
                            Is.EqualTo(2));

                var firstLimitOrderAfterException =
                    btcUsdOrderBookAfterException[otherSide].Tip.Value;
                Assert.That(firstLimitOrderAfterException.OrderInfo.Side,
                            Is.EqualTo(otherSide));
                Assert.That(firstLimitOrderAfterException.Price,
                            Is.EqualTo(limitOrder1.Price));
                Assert.That(firstLimitOrderAfterException.OrderInfo.Quantity,
                            Is.EqualTo(limitOrder1.OrderInfo.Quantity));
                var secondLimitOrderAfterException =
                    btcUsdOrderBookAfterException[otherSide].Tail.Value.Tip.Value;
                Assert.That(secondLimitOrderAfterException.OrderInfo.Side,
                            Is.EqualTo(otherSide));
                Assert.That(secondLimitOrderAfterException.Price,
                            Is.EqualTo(limitOrder2.Price));
                Assert.That(secondLimitOrderAfterException.OrderInfo.Quantity,
                            Is.EqualTo(limitOrder2.OrderInfo.Quantity));
            }
        }

        [Test]
        public void Market_order_throws_on_exchange_with_not_enough_liquidity_in_limit_orders_and_orderbooks_are_left_intact()
        {
            Market_order_throws_on_exchange_with_not_enough_liquidity_in_limit_orders_and_orderbooks_are_left_intact(Side.Ask);

            Market_order_throws_on_exchange_with_not_enough_liquidity_in_limit_orders_and_orderbooks_are_left_intact(Side.Bid);
        }

        private void Market_order_matches_with_more_than_one_limit_order(Side side)
        {
            var quantityForLimitOrder1 = 1;
            var quantityForLimitOrder2 = 1;

            // to make the exchange run out of funds:
            var quantityForMarketOrder = quantityForLimitOrder1 + quantityForLimitOrder2;

            var priceForLimitOrder = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var otherSide = side.Other();

            var limitOrder1 =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), otherSide, quantityForLimitOrder1),
                               priceForLimitOrder);
            foreach (var exchange in
                     LimitOrders.Limit_order_is_accepted_by_empty_exchange
                     (limitOrder1, market))
            {
                var limitOrder2 =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), otherSide, quantityForLimitOrder2),
                                   priceForLimitOrder);
                LimitOrders.SendOrder(exchange, limitOrder2, market);

                var marketOrder =
                    new OrderInfo(Guid.NewGuid(), side, quantityForMarketOrder);

                exchange.SendMarketOrder(marketOrder, market);

                var btcUsdOrderBookAfterException = exchange[market];
                Assert.That(btcUsdOrderBookAfterException[side].Count(),
                            Is.EqualTo(0));
                Assert.That(btcUsdOrderBookAfterException[otherSide].Count(),
                            Is.EqualTo(0));
            }
        }

        [Test]
        public void Market_order_matches_with_more_than_one_limit_order()
        {
            Market_order_matches_with_more_than_one_limit_order(Side.Ask);

            Market_order_matches_with_more_than_one_limit_order(Side.Bid);
        }

        private void Market_order_partial_match_on_exchange_with_one_limit_order(Side side)
        {
            var quantityForLimitOrder = 2;
            var quantityForMarketOrder = quantityForLimitOrder / 2;
            var priceForLimitOrder = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var otherSide = side.Other();

            var limitOrder =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), side,
                                             quantityForLimitOrder),
                               priceForLimitOrder);
            foreach (var exchange in
                     LimitOrders.Limit_order_is_accepted_by_empty_exchange
                     (limitOrder, market))
            {

                var marketOrder = new OrderInfo(Guid.NewGuid(), otherSide, quantityForMarketOrder);
                exchange.SendMarketOrder(marketOrder, market);
                var btcUsdOrderBookAfterMatching = exchange[market];
                Assert.That(btcUsdOrderBookAfterMatching[otherSide].Count(),
                            Is.EqualTo(0));
                Assert.That(btcUsdOrderBookAfterMatching[side].Count(),
                            Is.EqualTo(1));
                var limitOrderLeftAfterPartialMatch =
                    btcUsdOrderBookAfterMatching[side].Tip.Value;
                Assert.That(limitOrderLeftAfterPartialMatch.OrderInfo.Side,
                            Is.EqualTo(side));
                Assert.That(limitOrderLeftAfterPartialMatch.Price,
                            Is.EqualTo(limitOrder.Price));
                Assert.That(limitOrderLeftAfterPartialMatch.OrderInfo.Quantity,
                            Is.EqualTo(quantityForLimitOrder - quantityForMarketOrder));
            }
        }

        [Test]
        public void Market_order_partial_match_on_exchange_with_one_limit_order()
        {
            Market_order_partial_match_on_exchange_with_one_limit_order(Side.Bid);

            Market_order_partial_match_on_exchange_with_one_limit_order(Side.Ask);
        }

        private void Market_order_partial_match_on_exchange_with_2nd_limit_order(Side side)
        {
            var quantityForLimitOrders = 2;
            var quantityForMarketOrder = 3;
            var priceForLimitOrder = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var otherSide = side.Other();

            var limitOrder1 =
                new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantityForLimitOrders),
                               priceForLimitOrder);
            foreach (var exchange in
                     LimitOrders.Limit_order_is_accepted_by_empty_exchange
                     (limitOrder1, market))
            {
                var limitOrder2 =
                    new LimitOrder(new OrderInfo(Guid.NewGuid(), side, quantityForLimitOrders),
                                   priceForLimitOrder);
                LimitOrders.SendOrder(exchange, limitOrder2, market);

                var marketOrder = new OrderInfo(Guid.NewGuid(), otherSide, quantityForMarketOrder);
                exchange.SendMarketOrder(marketOrder, market);
                var btcUsdOrderBookAfterMatching = exchange[market];
                Assert.That(btcUsdOrderBookAfterMatching[otherSide].Count(),
                            Is.EqualTo(0));
                Assert.That(btcUsdOrderBookAfterMatching[side].Count(),
                            Is.EqualTo(1));
                var limitOrderLeftAfterPartialMatch =
                    btcUsdOrderBookAfterMatching[side].Tip.Value;
                Assert.That(limitOrderLeftAfterPartialMatch.OrderInfo.Side,
                            Is.EqualTo(side));
                Assert.That(limitOrderLeftAfterPartialMatch.Price,
                            Is.EqualTo(limitOrder1.Price));
                Assert.That(limitOrderLeftAfterPartialMatch.OrderInfo.Quantity,
                            Is.EqualTo(2 + 2 - 3));
            }
        }

        [Test]
        public void Market_order_partial_match_on_exchange_with_2nd_limit_order()
        {
            Market_order_partial_match_on_exchange_with_2nd_limit_order(Side.Bid);

            Market_order_partial_match_on_exchange_with_2nd_limit_order(Side.Ask);
        }

    }
}
