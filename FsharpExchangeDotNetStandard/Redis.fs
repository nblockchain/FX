//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard.Redis

open System

open FsharpExchangeDotNetStandard

open Newtonsoft.Json
open StackExchange.Redis

type OrderQuery =
    {
        OrderId: string;
    }

type MarketQuery =
    {
        Market: Market;
        Side: Side;
        Tip: bool;
    }

type Query =
    | MarketSide of MarketQuery
    | Order of OrderQuery


module OrderRedisManager =
    // TODO: dispose
    let redis = ConnectionMultiplexer.Connect "localhost"
    let db = redis.GetDatabase()

    let InsertOrder (limitOrder: LimitOrder) =
        let serializedOrder = JsonConvert.SerializeObject limitOrder
        let success = db.StringSet(RedisKey.op_Implicit (limitOrder.OrderInfo.Id.ToString()),
                                   RedisValue.op_Implicit (serializedOrder))
        if not success then
            failwith "Redis set(order) failed, something wrong must be going on"

    let SetTail (limitOrderGuids: List<string>) (nonTipQueryStr: string) =
        let serializedGuids = JsonConvert.SerializeObject limitOrderGuids
        let success = db.StringSet(RedisKey.op_Implicit nonTipQueryStr,
                                   RedisValue.op_Implicit serializedGuids)
        if not success then
            failwith "Redis set(nonTip) failed, something wrong must be going on"

