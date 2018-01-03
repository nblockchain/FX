namespace FsharpExchangeDotNetStandard

open System

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
