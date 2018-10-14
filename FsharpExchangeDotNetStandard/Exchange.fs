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

    let ReceiveOrderMemory (order: OrderRequest) (market: Market) =
        let maybeOrderBook = Map.tryFind market markets
        let orderBook =
            match maybeOrderBook with
            | None ->
                OrderBook()
            | Some(orderBookFound) ->
                orderBookFound
        let newOrderBook = orderBook.InsertOrder order
        markets <- markets.Add(market, newOrderBook)

    let ReceiveOrderRedis (order: OrderRequest) (market: Market) =
        // TODO: dispose
        let redis = ConnectionMultiplexer.Connect "localhost"
        let db = redis.GetDatabase()

        let tipQuery = { Market = market; Tip = true; Side = order.Side }
        let tipQueryStr = JsonConvert.SerializeObject tipQuery
        let value = db.StringGet (RedisKey.op_Implicit tipQueryStr)
        if not value.HasValue then
            match order with
            | Limit limitOrder ->
                match limitOrder.RequestType with
                | LimitOrderRequestType.Normal ->
                    let success = db.StringSet(RedisKey.op_Implicit tipQueryStr,
                                               RedisValue.op_Implicit (order.Id.ToString()))
                    if not success then
                        failwith "Redis set failed, something wrong must be going on"
                    OrderRedisManager.InsertOrder limitOrder.Order
                | _ ->
                    failwith "makerOnly not ready yet for redis"
            | Market marketOrder ->
                failwith "market not ready yet for redis"
        else
            OrderBook.InsertOrderRedis order market (Guid(value.ToString()))

    let ReceiveOrder (order: OrderRequest) (market: Market) =
        lock lockObject (
            fun _ ->
                match persistenceType with
                | Memory ->
                    ReceiveOrderMemory order market
                | Redis ->
                    ReceiveOrderRedis order market
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
