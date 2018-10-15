//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard

open FsharpExchangeDotNetStandard.Redis

open System

open Newtonsoft.Json
open StackExchange.Redis

type public Exchange(persistenceType: Persistence) =

    let mutable markets: Map<Market, OrderBook> = Map.empty

    let lockObject = Object()

    let GetItemInternal (market: Market): OrderBook =
        let maybeOrderBook = Map.tryFind market markets
        match maybeOrderBook with
        | None ->
            match persistenceType with
            | Memory ->
                OrderBook(MemoryOrderBookSide([]):>IOrderBookSide,
                          MemoryOrderBookSide([]):>IOrderBookSide,
                          (fun _ -> MemoryOrderBookSide([]):>IOrderBookSide))
            | Redis ->
                OrderBook(RedisOrderBookSide(market, Side.Buy, Root):>IOrderBookSide,
                          RedisOrderBookSide(market, Side.Sell, Root):>IOrderBookSide,
                          (fun side -> RedisOrderBookSide(market, side, Empty):>IOrderBookSide))

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


