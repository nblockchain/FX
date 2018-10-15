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

type OrderBookSide(market: Market, side: Side) =
    let tipQuery = { Market = market; Tip = true; Side = side }
    let tipQueryStr = JsonConvert.SerializeObject tipQuery
    let nonTipQuery = { Market = market; Tip = false; Side = side }
    let nonTipQueryStr = JsonConvert.SerializeObject nonTipQuery
    member __.TipQuery = tipQueryStr
    member __.NonTipQuery = nonTipQueryStr

type HeadPointer =
    | Root
    | Pointer of Guid
    | Empty

module OrderRedisManager =
    // TODO: dispose
    let redis = ConnectionMultiplexer.Connect "localhost"
    let db = redis.GetDatabase()

    let InsertOrder (limitOrder: LimitOrder): unit =
        let serializedOrder = JsonConvert.SerializeObject limitOrder
        let success = db.StringSet(RedisKey.op_Implicit (limitOrder.OrderInfo.Id.ToString()),
                                   RedisValue.op_Implicit (serializedOrder))
        if not success then
            failwith "Redis set(order) failed, something wrong must be going on"

    let GetTipOrderGuid (orderBookSide: OrderBookSide): Option<string> =
        let tipGuidResult = db.StringGet (RedisKey.op_Implicit orderBookSide.TipQuery)
        if not tipGuidResult.HasValue then
            None
        else
            tipGuidResult.ToString() |> Some

    let SetTipOrderGuid (orderBookSide: OrderBookSide) (guidStr: string): unit =
        let success = db.StringSet(RedisKey.op_Implicit orderBookSide.TipQuery,
                                   RedisValue.op_Implicit guidStr)
        if not success then
            failwith "Redis set failed, something wrong must be going on"

    let GetTipOrder (orderBookSide: OrderBookSide): Option<LimitOrder> =
        let maybeTipOrderGuid = GetTipOrderGuid orderBookSide
        match maybeTipOrderGuid with
        | None -> None
        | Some tipOrderGuid ->
            let orderSerialized = db.StringGet (RedisKey.op_Implicit tipOrderGuid)
            if not orderSerialized.HasValue then
                failwithf "Something went wrong, order tip was %s but was not found" tipOrderGuid
            let tipOrder = JsonConvert.DeserializeObject<LimitOrder> (orderSerialized.ToString())
            tipOrder |> Some

    let GetOrderByGuidString (guid: string): Option<LimitOrder> =
        let orderSerialized = db.StringGet (RedisKey.op_Implicit guid)
        if not orderSerialized.HasValue then
            None
        else
            let order = JsonConvert.DeserializeObject<LimitOrder> (orderSerialized.ToString())
            order |> Some

    let GetOrderByGuid (guid: Guid): Option<LimitOrder> =
        GetOrderByGuidString (guid.ToString())

    let GetTail (orderBookSide: OrderBookSide): List<string> =
        let tail = db.StringGet (RedisKey.op_Implicit orderBookSide.NonTipQuery)
        if not tail.HasValue then
            List.empty
        else
            JsonConvert.DeserializeObject<List<string>> (tail.ToString())

    let SetTail (limitOrderGuids: List<string>) (orderBookSide: OrderBookSide): unit =
        let serializedGuids = JsonConvert.SerializeObject limitOrderGuids
        let success = db.StringSet(RedisKey.op_Implicit orderBookSide.NonTipQuery,
                                   RedisValue.op_Implicit serializedGuids)
        if not success then
            failwith "Redis set(nonTip) failed, something wrong must be going on"

type RedisOrderBookSideFragment(orderBookSide: OrderBookSide, tip: HeadPointer) =

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

    interface IOrderBookSideFragment with

        member this.Analyze () =
            match tip with
            | Empty ->
                ListAnalysis.EmptyList

            | Root ->
                let maybeTipOrder = OrderRedisManager.GetTipOrder orderBookSide
                match maybeTipOrder with
                | None ->
                    ListAnalysis.EmptyList
                | Some tipOrder ->
                    let redisTail = OrderRedisManager.GetTail orderBookSide
                    match redisTail with
                    | [] ->
                        NonEmpty {
                            Head = tipOrder;
                            Tail = (fun _ -> RedisOrderBookSideFragment(orderBookSide, Empty) :> IOrderBookSideFragment)
                        }
                    | head::_ ->
                        let tailHeadGuid = head |> Guid
                        NonEmpty {
                            Head = tipOrder;
                            Tail = (fun _ -> RedisOrderBookSideFragment(orderBookSide, Pointer tailHeadGuid)
                                                 :> IOrderBookSideFragment)
                        }

            | Pointer tipGuid ->
                let tipGuidStr = tipGuid.ToString()
                match OrderRedisManager.GetOrderByGuidString tipGuidStr with
                | None ->
                    failwithf "Something went wrong, order tip was %s but was not found" tipGuidStr
                | Some headOrder ->

                    match OrderRedisManager.GetTail orderBookSide with
                    | [] ->
                        NonEmpty {
                            Head = headOrder;
                            Tail = (fun _ -> RedisOrderBookSideFragment(orderBookSide, Empty) :> IOrderBookSideFragment)
                        }
                    | tailGuids ->
                        let maybeNextGuid = FindNextElementAfterXInListY tipGuidStr tailGuids
                        match maybeNextGuid with
                        | Some nextGuids ->
                            match nextGuids with
                            | [] ->
                                NonEmpty {
                                    Head = headOrder;
                                    Tail = (fun _ -> RedisOrderBookSideFragment(orderBookSide, Empty)
                                                         :> IOrderBookSideFragment)
                                }
                            | nextGuid::_ ->
                                match OrderRedisManager.GetOrderByGuidString nextGuid with
                                | None ->
                                    failwithf "Something went wrong, next guid was %s but was not found" nextGuid
                                | Some nextOrder ->
                                    NonEmpty {
                                        Head = headOrder;
                                        Tail = (fun _ -> RedisOrderBookSideFragment(orderBookSide,
                                                                                    Pointer nextOrder.OrderInfo.Id)
                                                             :> IOrderBookSideFragment)
                                    }
                        | None ->
                            NonEmpty {
                                Head = headOrder;
                                Tail = (fun _ -> RedisOrderBookSideFragment(orderBookSide, Pointer (tailGuids.[0] |> Guid))
                                                     :> IOrderBookSideFragment)
                            }

        member this.Tip =
            match tip with
            | Empty -> None
            | Root ->
                OrderRedisManager.GetTipOrder orderBookSide
            | Pointer orderGuid ->
                let orderGuidStr = orderGuid.ToString()
                match OrderRedisManager.GetOrderByGuidString orderGuidStr with
                | None ->
                    failwithf "Something went wrong, order tip was %s but was not found" orderGuidStr
                | someOrder ->
                    someOrder

        member this.Tail =
            match (this :> IOrderBookSideFragment).Tip with
            | None -> None
            | Some tip ->
                RedisOrderBookSideFragment(orderBookSide, Pointer tip.OrderInfo.Id):> IOrderBookSideFragment |> Some

        member this.Prepend (limitOrder: LimitOrder) =
            OrderRedisManager.InsertOrder limitOrder
            match tip with
            | Root ->
                let maybePreviousTipLimitOrderGuid = OrderRedisManager.GetTipOrderGuid orderBookSide
                let guidStr = limitOrder.OrderInfo.Id.ToString()
                OrderRedisManager.SetTipOrderGuid orderBookSide guidStr

                match maybePreviousTipLimitOrderGuid with
                | Some previousTipLimitOrderGuid ->
                    let previousTailGuids = OrderRedisManager.GetTail orderBookSide

                    let tailGuids = previousTipLimitOrderGuid::previousTailGuids
                    OrderRedisManager.SetTail tailGuids orderBookSide
                | _ ->
                    // no need to do anything else
                    ()
                this:> IOrderBookSideFragment

            | Pointer tailTipOrderGuid ->
                let tailTipOrderGuidStr = tailTipOrderGuid.ToString()
                let tail = OrderRedisManager.GetTail orderBookSide
                match tail with
                | [] ->
                    OrderRedisManager.SetTail (tailTipOrderGuidStr::List.Empty) orderBookSide
                | previousTailGuids ->
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
                    OrderRedisManager.SetTail newTail orderBookSide

                RedisOrderBookSideFragment(orderBookSide, Pointer limitOrder.OrderInfo.Id) :> IOrderBookSideFragment
            | Empty ->
                RedisOrderBookSideFragment(orderBookSide, Pointer limitOrder.OrderInfo.Id) :> IOrderBookSideFragment

        member this.Count () =
            match tip with
            | Empty -> 0
            | _ ->
                1 + (OrderRedisManager.GetTail orderBookSide).Length

        member this.SyncAsRoot () =
            match tip with
            | Pointer tip ->
                OrderRedisManager.SetTipOrderGuid orderBookSide (tip.ToString())
            | Root ->
                ()
            | Empty ->
                raise <| NotImplementedException()
