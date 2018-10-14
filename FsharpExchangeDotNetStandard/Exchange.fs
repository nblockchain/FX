//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard

open System

type public Exchange(persistenceType: Persistence) =

    let mutable markets: Map<Market, OrderBook> = Map.empty

    let lockObject = Object()

    let ReceiveOrder (order: OrderRequest) (market: Market) =
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

    new() = Exchange(Persistence.Memory)

    member x.SendMarketOrder (order: OrderInfo, market: Market) =
        ReceiveOrder (OrderRequest.Market(order)) market

    member x.SendLimitOrder (order: LimitOrderRequest, market: Market) =
        ReceiveOrder (OrderRequest.Limit(order)) market

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
