using System;
using System.Linq;

using FsharpExchangeDotNetStandard;

using NUnit.Framework;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class Unit
    {
        private Side OtherSide(Side side)
        {
            return side == Side.Sell ? Side.Buy : Side.Sell;
        }

        private Exchange Limit_order_is_accepted_by_empty_exchange
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

            var otherSide = OtherSide(side);
            Assert.That(btcUsdOrderBookAgain[otherSide].Count(), Is.EqualTo(0));

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

        private void Market_order_match_on_exchange_with_one_limit_order(Side side)
        {
            var quantity = 1;
            var priceForLimitOrder = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var otherSide = OtherSide(side);

            var limitOrder =
                new LimitOrder(side, quantity, priceForLimitOrder);
            var exchange =
                Limit_order_is_accepted_by_empty_exchange(limitOrder, market);

            var marketOrder = new MarketOrder(otherSide, quantity);
            exchange.SendMarketOrder(marketOrder, market);
            var btcUsdOrderBookAfterMatching = exchange[market];
            Assert.That(btcUsdOrderBookAfterMatching[Side.Buy].Count(),
                        Is.EqualTo(0));
            Assert.That(btcUsdOrderBookAfterMatching[Side.Sell].Count(),
                        Is.EqualTo(0));
        }

        [Test]
        public void Market_order_match_on_exchange_with_one_limit_order()
        {
            Market_order_match_on_exchange_with_one_limit_order(Side.Buy);

            Market_order_match_on_exchange_with_one_limit_order(Side.Sell);
        }

        private void Market_order_throw_on_exchange_with_not_enough_limit_orders_and_orderbooks_are_left_intact(Side side)
        {
            var quantityForMarketOrder = 1;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchange = new Exchange();

            var marketOrder =
                new MarketOrder(side, quantityForMarketOrder);
            Assert.Throws<LiquidityProblem>(() => {
                exchange.SendMarketOrder(marketOrder, market);
            });

            var btcUsdOrderBookAfterException = exchange[market];
            Assert.That(btcUsdOrderBookAfterException[Side.Sell].Count(),
                        Is.EqualTo(0));
            Assert.That(btcUsdOrderBookAfterException[Side.Buy].Count(),
                        Is.EqualTo(0));
        }

        [Test]
        public void Market_order_throw_on_exchange_with_not_enough_limit_orders_and_orderbooks_are_left_intact()
        {
            Market_order_throw_on_exchange_with_not_enough_limit_orders_and_orderbooks_are_left_intact(Side.Sell);

            Market_order_throw_on_exchange_with_not_enough_limit_orders_and_orderbooks_are_left_intact(Side.Buy);
        }

        private void Market_order_throw_on_exchange_with_not_enough_liquidity_in_limit_orders_and_orderbooks_are_left_intact(Side side)
        {
            var quantityForLimitOrder = 1;

            // to make the exchange run out of funds:
            var quantityForMarketOrder = quantityForLimitOrder + 1;

            var priceForLimitOrder = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var otherSide = OtherSide(side);

            var limitOrder =
                new LimitOrder(otherSide, quantityForLimitOrder, priceForLimitOrder);
            var exchangeToSell =
                Limit_order_is_accepted_by_empty_exchange(limitOrder, market);

            var marketOrder =
                new MarketOrder(side, quantityForMarketOrder);
            Assert.Throws<LiquidityProblem>(() => {
                exchangeToSell.SendMarketOrder(marketOrder, market);
            });

            var btcUsdOrderBookAfterException = exchangeToSell[market];
            Assert.That(btcUsdOrderBookAfterException[side].Count(),
                        Is.EqualTo(0));
            Assert.That(btcUsdOrderBookAfterException[otherSide].Count(),
                        Is.EqualTo(1));
            var uniqueLimitOrder =
                btcUsdOrderBookAfterException[otherSide].ElementAt(0);
            Assert.That(uniqueLimitOrder.Side, Is.EqualTo(otherSide));
            Assert.That(uniqueLimitOrder.Price,
                        Is.EqualTo(limitOrder.Price));
            Assert.That(uniqueLimitOrder.Quantity,
                        Is.EqualTo(limitOrder.Quantity));
        }

        [Test]
        public void Market_order_throw_on_exchange_with_not_enough_liquidity_in_limit_orders_and_orderbooks_are_left_intact()
        {
            Market_order_throw_on_exchange_with_not_enough_liquidity_in_limit_orders_and_orderbooks_are_left_intact(Side.Sell);

            Market_order_throw_on_exchange_with_not_enough_liquidity_in_limit_orders_and_orderbooks_are_left_intact(Side.Buy);
        }
    }
}
