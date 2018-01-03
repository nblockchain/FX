namespace FsharpExchangeDotNetStandard

open System

exception LiquidityProblem

type public Currency =
    | BTC
    | USD

type Side =
    | Buy
    | Sell
    member self.Other() =
        match self with
        | Side.Buy -> Side.Sell
        | Side.Sell -> Side.Buy

type LimitOrder =
    { Side: Side; Quantity: decimal; Price: decimal }

type MarketOrder =
    { Side: Side; Quantity: decimal; }

type internal Order =
    | Limit of LimitOrder
    | Market of MarketOrder

type OrderBookSide =
    list<LimitOrder>

type OrderBook(bidSide: OrderBookSide, askSide: OrderBookSide) =

    let rec MatchMarket (quantityLeftToMatch: decimal) (orderBookSide: OrderBookSide): OrderBookSide =
        match orderBookSide with
        | [] -> raise LiquidityProblem
        | firstLimitOrder::tail ->
            if (quantityLeftToMatch > firstLimitOrder.Quantity) then
                MatchMarket (quantityLeftToMatch - firstLimitOrder.Quantity) tail
            elif (quantityLeftToMatch = firstLimitOrder.Quantity) then
                tail
            else //if (quantityLeftToMatch < firstLimitOrder.Quantity)
                let newPartialLimitOrder = { Side = firstLimitOrder.Side;
                                             Price = firstLimitOrder.Price;
                                             Quantity = firstLimitOrder.Quantity - quantityLeftToMatch }
                newPartialLimitOrder::tail

    let rec MatchLimits (limitOrder: LimitOrder) (orderBookSide: OrderBook): OrderBook =
        match limitOrder.Side with
        | Side.Buy ->
            match askSide with
            | [] -> OrderBook(limitOrder::bidSide, askSide)
            | firstSellLimitOrder::restOfAskSide ->
                if (firstSellLimitOrder.Price = limitOrder.Price) then
                    OrderBook(bidSide, restOfAskSide)
                else
                    OrderBook(limitOrder::bidSide, askSide)
        | Side.Sell ->
            match bidSide with
            | [] -> OrderBook(bidSide, limitOrder::askSide)
            | firstBuyLimitOrder::restOfBidSide ->
                if (firstBuyLimitOrder.Price = limitOrder.Price) then
                    OrderBook(restOfBidSide, askSide)
                else
                    OrderBook(bidSide, limitOrder::askSide)

    new() = OrderBook([], [])

    member internal this.InsertOrder (order: Order): OrderBook =
        match order with
        | Limit(limitOrder) ->
            MatchLimits limitOrder this
        | Market(marketOrder) ->
            match marketOrder.Side with
            | Side.Buy ->
                OrderBook(bidSide, MatchMarket marketOrder.Quantity askSide)
            | Side.Sell ->
                OrderBook(MatchMarket marketOrder.Quantity bidSide, askSide)

    member x.Item
        with get (side: Side) =
            match side with
            | Side.Buy -> bidSide
            | Side.Sell -> askSide

type public Market =
    { BuyCurrency: Currency; SellCurrency: Currency }

type public Exchange() =
    let mutable markets: Map<Market, OrderBook> = Map.empty

    let lockObject = Object()

    let ReceiveOrder (order: Order) (market: Market) =
        lock lockObject (
            fun _ ->
                let maybeOrderBook = Map.tryFind market markets
                let orderBook =
                    match maybeOrderBook with
                    | None ->
                        OrderBook()
                    | Some(orderBookFound) ->
                        orderBookFound
                let newOrderBook = orderBook.InsertOrder order
                markets <- markets.Add(market, newOrderBook)
            )

    member x.SendMarketOrder (order: MarketOrder, market: Market): unit =
        ReceiveOrder (Market(order)) market

    member x.SendLimitOrder (order: LimitOrder, market: Market) =
        ReceiveOrder (Limit(order)) market

    member x.Item
        with get (market: Market): OrderBook =
            lock lockObject (
                fun _ ->
                    let maybeOrderBook = Map.tryFind market markets
                    let orderBook =
                        match maybeOrderBook with
                        | None ->
                            OrderBook()
                        | Some(orderBookFound) ->
                            orderBookFound
                    orderBook
                )
