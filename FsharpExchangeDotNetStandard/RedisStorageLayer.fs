//
// Copyright (C) 2018 Diginex Ltd. (www.diginex.com)
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

type OrderBookSide(market: Market, side: Side) =
    let tipQuery = { Market = market; Tip = true; Side = side }
    let tipQueryStr = JsonConvert.SerializeObject tipQuery
    let tailQuery = { Market = market; Tip = false; Side = side }
    let tailQueryStr = JsonConvert.SerializeObject tailQuery
    member __.TipQuery = tipQueryStr
    member __.TailQuery = tailQueryStr

type HeadPointer =
    | Root
    | Pointer of Guid
    | Empty

module OrderRedisManager =

    let redisHostEnvValue = Environment.GetEnvironmentVariable "REDIS_HOST" |> Option.ofObj
    let redisHostAddress =
        match redisHostEnvValue with
        | None | Some "" -> "localhost"
        | Some realValue -> realValue

    // TODO: dispose
    let redis = ConnectionMultiplexer.Connect redisHostAddress
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
        if guidStr.Contains " " then
            invalidArg guidStr "guids should not contain spaces"
        let success = db.StringSet(RedisKey.op_Implicit orderBookSide.TipQuery,
                                   RedisValue.op_Implicit guidStr)
        if not success then
            failwith "Redis set failed, something wrong must be going on"

    let UnsetTipOrderGuid (orderBookSide: OrderBookSide): unit =
        let success = db.StringSet(RedisKey.op_Implicit orderBookSide.TipQuery,
                                   RedisValue.op_Implicit String.Empty)
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

    let private GetOrderSerialized (guidStr: string): Option<string> =
        let orderSerialized = db.StringGet (RedisKey.op_Implicit guidStr)
        if not orderSerialized.HasValue then
            None
        else
            orderSerialized.ToString() |> Some

    let OrderExists (guid: Guid): bool =
        let guidStr = guid.ToString()
        let maybeOrderSerialized = GetOrderSerialized guidStr
        match maybeOrderSerialized with
        | None -> false
        | _ -> true

    let GetOrderByGuidString (guid: string): Option<LimitOrder> =
        let maybeOrderSerialized = GetOrderSerialized guid
        match maybeOrderSerialized with
        | None -> None
        | Some orderSerialized ->
            let order = JsonConvert.DeserializeObject<LimitOrder> (orderSerialized.ToString())
            order |> Some

    let GetOrderByGuid (guid: Guid): Option<LimitOrder> =
        GetOrderByGuidString (guid.ToString())

    let GetTail (orderBookSide: OrderBookSide): List<string> =
        let tail = db.StringGet (RedisKey.op_Implicit orderBookSide.TailQuery)
        if not tail.HasValue then
            List.empty
        else
            JsonConvert.DeserializeObject<List<string>> (tail.ToString())

    let SetTail (limitOrderGuids: List<string>) (orderBookSide: OrderBookSide): unit =
        let serializedGuids = JsonConvert.SerializeObject limitOrderGuids
        let success = db.StringSet(RedisKey.op_Implicit orderBookSide.TailQuery,
                                   RedisValue.op_Implicit serializedGuids)
        if not success then
            failwith "Redis set(nonTip) failed, something wrong must be going on"

type RedisOrderBookSideFragment(orderBookSide: OrderBookSide, tip: HeadPointer) =

    let rec PrependElementXToListYBeforeElementZ (x: string) (y: List<string>) (z: string) =
        match y with
        | [] ->
            failwith "z not found"
        | head::tail ->
            if head = z then
                x::(z::tail)
            else
                head::(PrependElementXToListYBeforeElementZ x tail z)

    let rec GetElementsAfterXInListY (x: string) (y: List<string>) =
        match y with
        | [] ->
            None
        | head::tail ->
            if x = head then
                tail |> Some
            else
                GetElementsAfterXInListY x tail

    let rec FindNextElementAfterXInListY (x: string) (y: List<string>): Option<List<string>> =
        match y with
        | [] ->
            None
        | head::tail ->
            if head = x then
                Some tail
            else
                FindNextElementAfterXInListY x tail

    member this.SyncAsRoot () =
        match tip with
        | Pointer tipGuid ->
            let tipGuidStr = tipGuid.ToString()
            let tail = OrderRedisManager.GetTail orderBookSide
            let newTail =
                 match GetElementsAfterXInListY tipGuidStr tail with
                 | None -> List.empty
                 | Some subTail -> subTail

            // TODO: do the two above in one batch?
            OrderRedisManager.SetTipOrderGuid orderBookSide tipGuidStr
            OrderRedisManager.SetTail newTail orderBookSide
        | Root ->
            ()
        | Empty ->
            OrderRedisManager.UnsetTipOrderGuid orderBookSide
            OrderRedisManager.SetTail List.empty orderBookSide

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
            match tip with
            | Empty -> None
            | Root ->
                match (this:>IOrderBookSideFragment).Tip with
                | None -> None
                | _ ->
                    let tail = OrderRedisManager.GetTail orderBookSide
                    match tail with
                    | [] ->
                        RedisOrderBookSideFragment(orderBookSide, Empty) :> IOrderBookSideFragment |> Some
                    | head::_ ->
                        RedisOrderBookSideFragment(orderBookSide, Pointer (head |> Guid))
                            :> IOrderBookSideFragment |> Some
            | Pointer tipGuid ->
                let tail = OrderRedisManager.GetTail orderBookSide
                match tail with
                | [] ->
                    failwith "Assertion failed, there can't be a pointer fragment if tail is empty"
                | _ ->
                    let tipGuidStr = tipGuid.ToString()
                    let afterGuids = GetElementsAfterXInListY tipGuidStr tail
                    match afterGuids with
                    | None ->
                        failwithf "Assertion failed, no %s found in tail" tipGuidStr
                    | Some [] ->
                        RedisOrderBookSideFragment(orderBookSide, Empty) :> IOrderBookSideFragment |> Some
                    | Some (head::_) ->
                        RedisOrderBookSideFragment(orderBookSide, Pointer (head |> Guid))
                            :> IOrderBookSideFragment |> Some

        member this.Insert (limitOrder: LimitOrder) (canPrepend: LimitOrder -> LimitOrder -> bool)
                               : IOrderBookSideFragment =
            OrderRedisManager.InsertOrder limitOrder
            match tip with
            | Root ->
                let maybeTipOrder = OrderRedisManager.GetTipOrder orderBookSide
                match maybeTipOrder with
                | None ->
                    OrderRedisManager.SetTipOrderGuid orderBookSide (limitOrder.OrderInfo.Id.ToString())
                    this :> IOrderBookSideFragment
                | Some tipOrder ->
                    let tailGuids = OrderRedisManager.GetTail orderBookSide
                    if canPrepend limitOrder tipOrder then
                        let newTailGuids = tipOrder.OrderInfo.Id.ToString()::tailGuids
                        OrderRedisManager.SetTail newTailGuids orderBookSide
                        OrderRedisManager.SetTipOrderGuid orderBookSide (limitOrder.OrderInfo.Id.ToString())
                        this :> IOrderBookSideFragment
                    else
                        match tailGuids with
                        | [] ->
                            OrderRedisManager.SetTail (limitOrder.OrderInfo.Id.ToString()::List.empty) orderBookSide
                        | head::_ ->
                            let headGuid = head |> Guid
                            let fragment = RedisOrderBookSideFragment(orderBookSide, Pointer headGuid)
                                               :> IOrderBookSideFragment
                            fragment.Insert limitOrder canPrepend
                                |> ignore

                        this :> IOrderBookSideFragment
            | Pointer tailOrderGuid ->
                let tailOrderGuidStr = tailOrderGuid.ToString()
                match OrderRedisManager.GetOrderByGuidString tailOrderGuidStr with
                | None ->
                    failwithf "Something went wrong, order pointer was %s but was not found" tailOrderGuidStr
                | Some tailOrder ->
                    let tailGuids = OrderRedisManager.GetTail orderBookSide
                    match tailGuids with
                    | [] ->
                        failwith "Assertion failed, had a Pointer fragment but tails is empty"
                    | _ ->

                        if canPrepend limitOrder tailOrder then
                            let newTailGuids = PrependElementXToListYBeforeElementZ (limitOrder.OrderInfo.Id.ToString())
                                                                                    tailGuids
                                                                                    (tailOrder.OrderInfo.Id.ToString())
                            OrderRedisManager.SetTail newTailGuids orderBookSide
                            RedisOrderBookSideFragment(orderBookSide, Pointer limitOrder.OrderInfo.Id)
                                :> IOrderBookSideFragment
                        else
                            let tailOrderGuidStr = (tailOrder.OrderInfo.Id.ToString())
                            let tailGuidsWithNewHead = GetElementsAfterXInListY tailOrderGuidStr
                                                                                tailGuids
                            match tailGuidsWithNewHead with
                            | None ->
                                failwithf "Assertion failed, no %s found in tail" tailOrderGuidStr
                            | Some [] ->
                                let newTailGuids = List.append tailGuids (limitOrder.OrderInfo.Id.ToString()::List.empty)
                                OrderRedisManager.SetTail newTailGuids orderBookSide
                                this :> IOrderBookSideFragment
                            | Some (head::_) ->
                                let headGuid = head |> Guid
                                let fragment = RedisOrderBookSideFragment(orderBookSide, Pointer headGuid)
                                                   :> IOrderBookSideFragment
                                fragment.Insert limitOrder canPrepend
            | Empty ->
                RedisOrderBookSideFragment(orderBookSide, Pointer limitOrder.OrderInfo.Id)
                                   :> IOrderBookSideFragment

        member this.Count () =
            match tip with
            | Empty -> 0
            | _ ->
                match (this:>IOrderBookSideFragment).Tip with
                | None ->
                    0
                | _ ->
                    1 + (OrderRedisManager.GetTail orderBookSide).Length

type MarketStore() =
    let mutable markets: Map<Market, OrderBook> = Map.empty
    let lockObject = Object()

    let GetOrderBookInternal (market: Market): OrderBook =
        let maybeOrderBook = Map.tryFind market markets
        match maybeOrderBook with
        | None ->
            let bidSide = OrderBookSide(market, Side.Bid)
            let askSide = OrderBookSide(market, Side.Ask)
            let newOrderBook =
                OrderBook(RedisOrderBookSideFragment(bidSide, Root) :> IOrderBookSideFragment,
                          RedisOrderBookSideFragment(askSide, Root) :> IOrderBookSideFragment,
                          (fun side -> RedisOrderBookSideFragment(OrderBookSide(market, side), Empty)
                                           :> IOrderBookSideFragment))
            markets <- markets.Add(market, newOrderBook)
            newOrderBook
        | Some(orderBookFound) ->
            orderBookFound

    interface IMarketStore with

        member this.GetOrderBook (market: Market): OrderBook =
            lock lockObject (fun _ ->
                GetOrderBookInternal market
            )

        member this.ReceiveOrder (order: OrderRequest) (market: Market) =
            lock lockObject (
                fun _ ->
                    let orderBook = GetOrderBookInternal market
                    if OrderRedisManager.OrderExists order.Id then
                        raise OrderAlreadyExists
                    let newOrderBook = orderBook.InsertOrder order
                    let bidSide = newOrderBook.[Side.Bid] :?> RedisOrderBookSideFragment
                    let askSide = newOrderBook.[Side.Ask] :?> RedisOrderBookSideFragment
                    bidSide.SyncAsRoot()
                    askSide.SyncAsRoot()
                )
