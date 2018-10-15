//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard

open FsharpExchangeDotNetStandard.Redis

open System

type public Exchange(persistenceType: Persistence) =

    let mutable markets: Map<Market, OrderBook> = Map.empty

    let lockObject = Object()

    let GetItemInternal (market: Market): OrderBook =
        let maybeOrderBook = Map.tryFind market markets
        match maybeOrderBook with
        | None ->
            match persistenceType with
            | Memory ->
                OrderBook(MemoryOrderBookSideFragment(List.empty) :> IOrderBookSideFragment,
                          MemoryOrderBookSideFragment(List.empty) :> IOrderBookSideFragment,
                          (fun _ -> MemoryOrderBookSideFragment(List.empty) :> IOrderBookSideFragment))
            | Redis ->
                let bidSide = OrderBookSide(market, Side.Buy)
                let askSide = OrderBookSide(market, Side.Sell)
                OrderBook(RedisOrderBookSideFragment(bidSide, Root) :> IOrderBookSideFragment,
                          RedisOrderBookSideFragment(askSide, Root) :> IOrderBookSideFragment,
                          (fun side -> RedisOrderBookSideFragment(OrderBookSide(market, side), Empty)
                                           :> IOrderBookSideFragment))

        | Some(orderBookFound) ->
            orderBookFound

    let ReceiveOrderInternal (order: OrderRequest) (market: Market) =
        let orderBook = GetItemInternal market
        let newOrderBook = orderBook.InsertOrder order
        markets <- markets.Add(market, newOrderBook)
        newOrderBook.SyncAsRoot()

    let ReceiveOrder (order: OrderRequest) (market: Market) =
        lock lockObject (
            fun _ ->
                ReceiveOrderInternal order market
            )

    new() = Exchange(Persistence.Memory)

    member __.Item
        with get (market: Market): OrderBook =
            lock lockObject (
                fun _ ->
                    GetItemInternal market
                )

    member x.SendMarketOrder (order: OrderInfo, market: Market) =
        ReceiveOrder (OrderRequest.Market(order)) market

    member x.SendLimitOrder (order: LimitOrderRequest, market: Market) =
        ReceiveOrder (OrderRequest.Limit(order)) market

