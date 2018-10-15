//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard.Redis

open System
open System.Linq

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

type HeadPointer =
    | Root
    | Pointer of Guid
    | Empty

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

type RedisOrderBookSide(market: Market, side: Side, tailTip: HeadPointer) =
    let tipQuery = { Market = market; Tip = true; Side = side }
    let tipQueryStr = JsonConvert.SerializeObject tipQuery

    let nonTipQuery = { Market = market; Tip = false; Side = side }
    let nonTipQueryStr = JsonConvert.SerializeObject nonTipQuery

    // TODO: dispose
    let redis = ConnectionMultiplexer.Connect "localhost"
    let db = redis.GetDatabase()


    let rec AppendElementXToListYAfterElementZ (x: string) (y: List<string>) (z: string) =
        match y with
        | [] ->
            failwith "z not found"
        | head::tail ->
            if head = z then
                head::(x::tail)
            else
                head::(AppendElementXToListYAfterElementZ x tail z)

    let rec PrependElementXToListYBeforeElementZ (x: string) (y: List<string>) (z: string) =
        match y with
        | [] ->
            failwith "z not found"
        | head::tail ->
            if head = z then
                x::(z::tail)
            else
                head::(PrependElementXToListYBeforeElementZ x tail z)

    let rec FindNextElementAfterXInListY (x: string) (y: List<string>): Option<List<string>> =
        match y with
        | [] ->
            None
        | head::tail ->
            if head = x then
                Some tail
            else
                FindNextElementAfterXInListY x tail

    member this.TipGuid: Option<string> =
        match tailTip with
        | Root ->
            let tipGuid = db.StringGet (RedisKey.op_Implicit tipQueryStr)
            if not tipGuid.HasValue then
                None
            else
                tipGuid.ToString() |> Some
        | Pointer guid ->
            guid.ToString() |> Some
        | Empty ->
            None

    interface IOrderBookSide with
        member this.IfEmptyElse (ifEmptyFunc) (elseFunc) =
            match this.TipGuid with
            | None ->
                ifEmptyFunc ()
            | Some tipGuidStr ->
                let orderSerialized = db.StringGet (RedisKey.op_Implicit tipGuidStr)
                if not orderSerialized.HasValue then
                    failwithf "Something went wrong, order tip was %s but was not found" tipGuidStr
                let limitOrder = JsonConvert.DeserializeObject<LimitOrder> (orderSerialized.ToString())

                let tail = db.StringGet (RedisKey.op_Implicit nonTipQueryStr)
                if not tail.HasValue then
                    let tail = RedisOrderBookSide(market, side, Empty):> IOrderBookSide
                    elseFunc limitOrder tail
                else
                    let tailGuids = JsonConvert.DeserializeObject<List<string>> (tail.ToString())
                    let maybeNextGuid = FindNextElementAfterXInListY tipGuidStr tailGuids
                    match maybeNextGuid with
                    | Some nextGuids ->
                        match nextGuids with
                        | [] ->
                            let tail = RedisOrderBookSide(market, side, Empty):> IOrderBookSide
                            elseFunc limitOrder tail
                        | nextGuid::_ ->
                            let nextOrderSerialized = db.StringGet (RedisKey.op_Implicit nextGuid)
                            if not nextOrderSerialized.HasValue then
                                failwithf "Something went wrong, next guid was %s but was not found" nextGuid
                            let nextOrder = JsonConvert.DeserializeObject<LimitOrder> (nextOrderSerialized.ToString())
                            let tail = RedisOrderBookSide(market, side, Pointer nextOrder.OrderInfo.Id):> IOrderBookSide
                            elseFunc limitOrder tail
                    | None ->
                        let tail = RedisOrderBookSide(market, side, Pointer (tailGuids.[0] |> Guid)):> IOrderBookSide
                        elseFunc limitOrder tail

        member this.Tip =
            match this.TipGuid with
            | None -> None
            | Some tipGuidStr ->
                let orderSerialized = db.StringGet (RedisKey.op_Implicit tipGuidStr)
                if not orderSerialized.HasValue then
                    failwithf "Something went wrong, order tip was %s but was not found" tipGuidStr
                JsonConvert.DeserializeObject<LimitOrder> (orderSerialized.ToString()) |> Some

        member this.Tail =
            match (this:>IOrderBookSide).Tip with
            | None -> None
            | Some tip ->
                RedisOrderBookSide(market, side, Pointer tip.OrderInfo.Id):> IOrderBookSide |> Some

        member this.Prepend (limitOrder: LimitOrder) =
            OrderRedisManager.InsertOrder limitOrder
            match tailTip with
            | Root ->
                let maybePreviousTipLimitOrderGuid = this.TipGuid
                let guidStr = limitOrder.OrderInfo.Id.ToString()
                let success = db.StringSet(RedisKey.op_Implicit tipQueryStr,
                                           RedisValue.op_Implicit guidStr)
                if not success then
                    failwith "Redis set failed, something wrong must be going on"

                match maybePreviousTipLimitOrderGuid with
                | Some previousTipLimitOrderGuid ->
                    let tail = db.StringGet (RedisKey.op_Implicit nonTipQueryStr)
                    let previousTailGuids =
                        if not tail.HasValue then
                            List.empty
                        else
                            JsonConvert.DeserializeObject<List<string>> (tail.ToString())

                    let tailGuids = previousTipLimitOrderGuid::previousTailGuids
                    OrderRedisManager.SetTail tailGuids nonTipQueryStr
                | _ ->
                    // no need to do anything else
                    ()
                this:> IOrderBookSide

            | Pointer tailTipOrderGuid ->
                let tailTipOrderGuidStr = tailTipOrderGuid.ToString()
                let tail = db.StringGet (RedisKey.op_Implicit nonTipQueryStr)
                if tail.HasValue then
                    let previousTailGuids = JsonConvert.DeserializeObject<List<string>> (tail.ToString())
                    let newTail =
                        // TODO: optimize?
                        if previousTailGuids.Any(fun item -> item = tailTipOrderGuidStr) then
                            PrependElementXToListYBeforeElementZ (limitOrder.OrderInfo.Id.ToString())
                                                                 previousTailGuids
                                                                 tailTipOrderGuidStr
                        else
                            AppendElementXToListYAfterElementZ tailTipOrderGuidStr
                                                               previousTailGuids
                                                               (limitOrder.OrderInfo.Id.ToString())
                    OrderRedisManager.SetTail newTail nonTipQueryStr
                else
                    OrderRedisManager.SetTail [tailTipOrderGuidStr] nonTipQueryStr

                RedisOrderBookSide(market, side, Pointer limitOrder.OrderInfo.Id) :> IOrderBookSide
            | Empty ->
                RedisOrderBookSide(market, side, Pointer limitOrder.OrderInfo.Id) :> IOrderBookSide

        member this.Count () =
            match this.TipGuid with
            | None -> 0
            | _ ->
                let tail = db.StringGet (RedisKey.op_Implicit nonTipQueryStr)
                if not tail.HasValue then
                    1
                else
                    let tailGuids = JsonConvert.DeserializeObject<List<string>> (tail.ToString())
                    1 + (tailGuids.Length)

        member this.SyncAsRoot () =
            match tailTip with
            | Pointer tip ->
                let success = db.StringSet(RedisKey.op_Implicit tipQueryStr,
                                           RedisValue.op_Implicit (tip.ToString()))
                if not success then
                    failwith "Redis set failed, something wrong must be going on"
            | Root ->
                ()
            | Empty ->
                failwith "NIE"
