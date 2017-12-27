namespace FsharpExchangeDotNetStandard

open System

type public Currency =
    | BTC
    | USD

type Side =
    | Buy
    | Sell

type LimitOrder =
    { Side: Side; Quantity: decimal; Price: decimal }

type OrderBookSide =
    list<LimitOrder>

type OrderBook() =
    let mutable buySide:OrderBookSide = []
    let mutable sellSide:OrderBookSide = []

    member internal x.InsertOrder (limitOrder: LimitOrder) =
        // NO LOCKING BECAUSE Exchange.SendOrder() is assumed to hold a lock already
        match limitOrder.Side with
        | Side.Buy ->
            buySide <- limitOrder::buySide
        | Side.Sell ->
            sellSide <- limitOrder::sellSide
    member x.Item
        with get (side: Side) =
            match side with
            | Side.Buy -> buySide
            | Side.Sell -> sellSide

type public Market =
    { BuyCurrency: Currency; SellCurrency: Currency }

type public Exchange() =
    let mutable markets: Map<Market, OrderBook> = Map.empty

    let lockObject = Object()

    member x.SendOrder (limitOrder: LimitOrder, market: Market) =
        lock lockObject (
            fun _ ->
                let maybeOrderBook = Map.tryFind market markets
                let orderBook =
                    match maybeOrderBook with
                    | None ->
                        let newOrderBook = OrderBook()
                        markets <- markets.Add(market, newOrderBook)
                        newOrderBook
                    | Some(orderBookFound) ->
                        orderBookFound
                orderBook.InsertOrder limitOrder
            )

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
