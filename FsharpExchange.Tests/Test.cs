using System;
using System.Linq;

using FsharpExchangeDotNetStandard;

using NUnit.Framework;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class Unit
    {
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

            var otherSide =
                (side == Side.Sell) ? Side.Buy : Side.Sell;
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

        [Test]
        public void Market_order_match_on_exchange_with_one_limit_order()
        {
            var quantity = 1;
            var priceForLimitOrder = 10000;
            var market = new Market(Currency.BTC, Currency.USD);

            var buyLimitOrder =
                new LimitOrder(Side.Buy, quantity, priceForLimitOrder);
            var exchangeToSell =
                Limit_order_is_accepted_by_empty_exchange(buyLimitOrder, market);

            var sellMarketOrder = new MarketOrder(Side.Sell, quantity);
            exchangeToSell.SendMarketOrder(sellMarketOrder, market);
            var btcUsdOrderBookAfterMatching1 = exchangeToSell[market];
            Assert.That(btcUsdOrderBookAfterMatching1[Side.Buy].Count(),
                        Is.EqualTo(0));
            Assert.That(btcUsdOrderBookAfterMatching1[Side.Sell].Count(),
                        Is.EqualTo(0));

            var sellLimitOrder =
                new LimitOrder(Side.Sell, quantity, priceForLimitOrder);
            var exchangeToBuy =
                Limit_order_is_accepted_by_empty_exchange(sellLimitOrder, market);
            var buyMarketOrder = new MarketOrder(Side.Buy, quantity);
            exchangeToBuy.SendMarketOrder(buyMarketOrder, market);
            var btcUsdOrderBookAfterMatching2 = exchangeToBuy[market];
            Assert.That(btcUsdOrderBookAfterMatching2[Side.Buy].Count(),
                        Is.EqualTo(0));
            Assert.That(btcUsdOrderBookAfterMatching2[Side.Sell].Count(),
                        Is.EqualTo(0));
        }

        [Test]
        public void Market_order_throw_on_exchange_with_not_enough_limit_orders_and_orderbooks_are_left_intact()
        {
            var quantityForMarketOrder = 1;
            var market = new Market(Currency.BTC, Currency.USD);

            var exchangeToSell = new Exchange();

            var sellMarketOrder =
                new MarketOrder(Side.Sell, quantityForMarketOrder);
            Assert.Throws<LiquidityProblem>(() => {
                exchangeToSell.SendMarketOrder(sellMarketOrder, market);
            });

            var btcUsdOrderBookAfterException1 = exchangeToSell[market];
            Assert.That(btcUsdOrderBookAfterException1[Side.Sell].Count(),
                        Is.EqualTo(0));
            Assert.That(btcUsdOrderBookAfterException1[Side.Buy].Count(),
                        Is.EqualTo(0));


            var exchangeToBuy = new Exchange();
            var buyMarketOrder =
                new MarketOrder(Side.Buy, quantityForMarketOrder);
            Assert.Throws<LiquidityProblem>(() => {
                exchangeToBuy.SendMarketOrder(sellMarketOrder, market);
            });

            var btcUsdOrderBookAfterException2 = exchangeToBuy[market];
            Assert.That(btcUsdOrderBookAfterException1[Side.Buy].Count(),
                        Is.EqualTo(0));
            Assert.That(btcUsdOrderBookAfterException1[Side.Sell].Count(),
                        Is.EqualTo(0));
        }
    }
}
