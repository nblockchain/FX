namespace FsharpExchangeDotNetStandard

open System
open System.Collections.Generic

type public Currency =
    | BTC
    | USD

type Side =
    | Buy
    | Sell

type LimitOrder =
    { Side: Side; Quantity: decimal; Price: decimal }

type OrderBookSide() =
    inherit List<LimitOrder>()

type OrderBook() =
    let buySide = OrderBookSide()
    let sellSide = OrderBookSide()

    member internal x.InsertOrder (limitOrder: LimitOrder) =
        // NO LOCKING BECAUSE Exchange.SendOrder() is assumed to hold a lock already
        let side =
            match limitOrder.Side with
            | Side.Buy -> buySide
            | Side.Sell -> sellSide
        side.Add limitOrder
    member x.Item
        with get (side: Side) =
            match side with
            | Side.Buy -> buySide
            | Side.Sell -> sellSide

type public Market =
    { BuyCurrency: Currency; SellCurrency: Currency }

type public Exchange() =
    let markets = Dictionary<Market, OrderBook>()

    let lockObject = Object()

    member x.SendOrder (limitOrder: LimitOrder, market: Market) =
        lock lockObject (
            fun _ ->
                let exists,maybeOrderBook = markets.TryGetValue market
                let orderBook =
                    match (exists,maybeOrderBook) with
                    | false,_ ->
                        let newOrderBook = OrderBook()
                        markets.Add(market,newOrderBook)
                        newOrderBook
                    | true,orderBookFound ->
                        orderBookFound
                orderBook.InsertOrder limitOrder
            )

    member x.Item
        with get (market: Market): OrderBook =
            lock lockObject (
                fun _ ->
                    let exists,maybeOrderBook = markets.TryGetValue market
                    let orderBook =
                        match (exists,maybeOrderBook) with
                        | false,_ ->
                            OrderBook()
                        | true,orderBookFound ->
                            orderBookFound
                    orderBook
                )
