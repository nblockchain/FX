
namespace FsharpExchangeDotNetStandard.Redis

open System

open FsharpExchangeDotNetStandard

open System.Text.Json
open System.Text.Json.Serialization
open StackExchange.Redis

[<AutoOpen>]
module Serialization =
    // TODO: use FSharp.SystemTextJson for Discriminated Union support,
    // in that case custom type converters are no longer needed
    type SideTypeConverter() =
        inherit JsonConverter<Side>()

        override this.Read(reader, _typeToConvert, _options) =
            reader.GetString() |> Side.Parse

        override this.Write(writer, value, _options ) =
            writer.WriteStringValue(value.ToString())

    type CurrencyTypeConverter() =
        inherit JsonConverter<Currency>()

        override this.Read(reader, _typeToConvert, _options) =
            match reader.GetString() with
            | "BTC" -> BTC
            | "USD" -> USD
            | unknownCurrency -> failwithf "Unknown currency: %s" unknownCurrency

        override this.Write(writer, value, _options ) =
            writer.WriteStringValue(value.ToString())

    // code from https://gist.github.com/mbuhot/c224f15e0266adf5ba8ca4e882f88a75
    // Converts Option<T> to/from JSON by projecting to null or T
    type OptionValueConverter<'T>() =
        inherit JsonConverter<Option<'T>>()

        override this.Read (reader: byref<Utf8JsonReader>, _typ: Type, options: JsonSerializerOptions) =
            match reader.TokenType with
            | JsonTokenType.Null -> None
            | _ -> Some <| JsonSerializer.Deserialize<'T>(&reader, options)

        override this.Write (writer: Utf8JsonWriter, value: Option<'T>, options: JsonSerializerOptions) =
            match value with
            | None -> writer.WriteNullValue ()
            | Some value -> JsonSerializer.Serialize(writer, value, options)

    // Instantiates the correct OptionValueConverter<T>
    type OptionConverter() =
        inherit JsonConverterFactory()
            override this.CanConvert(typ: Type) : bool =
                typ.IsGenericType &&
                typ.GetGenericTypeDefinition() = typedefof<Option<_>>

            override this.CreateConverter(typeToConvert: Type,
                                          _options: JsonSerializerOptions) : JsonConverter =
                let typ = typeToConvert.GetGenericArguments() |> Array.head
                let converterType = typedefof<OptionValueConverter<_>>.MakeGenericType(typ)
                Activator.CreateInstance(converterType) :?> JsonConverter

    let serializationOptions =
        let options = JsonSerializerOptions()
        options.Converters.Add(SideTypeConverter())
        options.Converters.Add(CurrencyTypeConverter()) 
        options.Converters.Add(OptionConverter())
        options


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
    let tipQueryStr = JsonSerializer.Serialize(tipQuery, serializationOptions)
    let tailQuery = { Market = market; Tip = false; Side = side }
    let tailQueryStr = JsonSerializer.Serialize(tailQuery, serializationOptions)
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

    let CreateTransaction() =
        db.CreateTransaction()

    let InsertOrder (limitOrder: LimitOrder): unit =
        let serializedOrder = JsonSerializer.Serialize(limitOrder, serializationOptions)
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

    let SetTipOrderGuid (transaction: StackExchange.Redis.ITransaction)
                        (orderBookSide: OrderBookSide)
                        (guidStr: string)
                            : unit =
        if guidStr.Contains " " then
            invalidArg guidStr "guids should not contain spaces"
        transaction.StringSetAsync (RedisKey.op_Implicit orderBookSide.TipQuery,
                                    RedisValue.op_Implicit guidStr)
            |> ignore

    let UnsetTipOrderGuid (transaction: StackExchange.Redis.ITransaction) (orderBookSide: OrderBookSide): unit =
        transaction.StringSetAsync(RedisKey.op_Implicit orderBookSide.TipQuery,
                                   RedisValue.op_Implicit String.Empty)
            |> ignore

    let GetTipOrder (orderBookSide: OrderBookSide): Option<LimitOrder> =
        let maybeTipOrderGuid = GetTipOrderGuid orderBookSide
        match maybeTipOrderGuid with
        | None -> None
        | Some tipOrderGuid ->
            let orderSerialized = db.StringGet (RedisKey.op_Implicit tipOrderGuid)
            if not orderSerialized.HasValue then
                failwithf "Something went wrong, order tip was %s but was not found" tipOrderGuid
            let tipOrder = JsonSerializer.Deserialize<LimitOrder>(orderSerialized.ToString(), serializationOptions)
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
            let order = JsonSerializer.Deserialize<LimitOrder>(orderSerialized.ToString(), serializationOptions)
            order |> Some

    let GetOrderByGuid (guid: Guid): Option<LimitOrder> =
        GetOrderByGuidString (guid.ToString())

    let GetTail (orderBookSide: OrderBookSide): List<string> =
        let tail = db.StringGet (RedisKey.op_Implicit orderBookSide.TailQuery)
        if not tail.HasValue then
            List.empty
        else
            JsonSerializer.Deserialize<List<string>>(tail.ToString(), serializationOptions)

    let SetTail (transaction: StackExchange.Redis.ITransaction)
                (limitOrderGuids: List<string>)
                (orderBookSide: OrderBookSide)
                    : unit =
        let serializedGuids = JsonSerializer.Serialize(limitOrderGuids, serializationOptions)
        transaction.StringSetAsync(RedisKey.op_Implicit orderBookSide.TailQuery,
                                   RedisValue.op_Implicit serializedGuids)
            |> ignore

type internal Transaction(redisTransaction: StackExchange.Redis.ITransaction) =
    member this.RedisTransaction = redisTransaction
    interface FsharpExchangeDotNetStandard.ITransaction

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

    let rec RemoveFirstElementXFromListY x y =
        match y with
        | [] -> failwith "x not found"
        | head::tail ->
            if (head = x) then
                tail
            else
                head::RemoveFirstElementXFromListY x tail

    member this.SyncAsRoot (transaction: StackExchange.Redis.ITransaction) =
        match tip with
        | Pointer tipGuid ->
            let tipGuidStr = tipGuid.ToString()
            let tail = OrderRedisManager.GetTail orderBookSide
            let newTail =
                 match GetElementsAfterXInListY tipGuidStr tail with
                 | None -> List.empty
                 | Some subTail -> subTail

            // TODO: do the two above in one batch?
            OrderRedisManager.SetTipOrderGuid transaction orderBookSide tipGuidStr
            OrderRedisManager.SetTail transaction newTail orderBookSide
        | Root ->
            ()
        | Empty ->
            OrderRedisManager.UnsetTipOrderGuid transaction orderBookSide
            OrderRedisManager.SetTail transaction List.empty orderBookSide

    member this.OrderExists (orderId): bool =
        let self = this :> IOrderBookSideFragment
        match self.Tip with
        | None ->
            false
        | Some limitOrder ->
            if limitOrder.OrderInfo.Id = orderId then
                true
            else
                match self.Tail with
                | None ->
                    false
                | Some tail ->
                    let castedTail = tail :?> RedisOrderBookSideFragment
                    castedTail.OrderExists orderId

    member this.RemoveOrder (orderId): OrderBookSideFragmentModification =
        (fun transaction ->
            match tip with
            | Empty ->
                failwithf "Could not find order %s" (orderId.ToString())
            | Pointer _ ->
                failwith "Assertion failed: remove order operation should only happen in Root fragments"
            | Root ->
                let maybeTipOrder = OrderRedisManager.GetTipOrder orderBookSide
                match maybeTipOrder with
                | None ->
                    failwithf "Could not find order %s" (orderId.ToString())
                | Some tipOrder ->

                    let redisTransaction = (transaction :?> Transaction).RedisTransaction

                    let redisTail = OrderRedisManager.GetTail orderBookSide
                    if tipOrder.OrderInfo.Id = orderId then
                        match redisTail with
                        | [] ->
                            OrderRedisManager.UnsetTipOrderGuid redisTransaction orderBookSide
                        | headOfTail::tailOfTail ->
                            OrderRedisManager.SetTipOrderGuid redisTransaction orderBookSide (headOfTail.ToString())
                            OrderRedisManager.SetTail redisTransaction tailOfTail orderBookSide
                    else
                        let newTailGuids = RemoveFirstElementXFromListY (orderId.ToString()) redisTail
                        OrderRedisManager.SetTail redisTransaction newTailGuids orderBookSide
                    this :> IOrderBookSideFragment
        )


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
                               : OrderBookSideFragmentModification =
            OrderRedisManager.InsertOrder limitOrder
            match tip with
            | Root ->
                let result = this :> IOrderBookSideFragment

                let maybeTipOrder = OrderRedisManager.GetTipOrder orderBookSide
                match maybeTipOrder with
                | None ->
                    (fun transaction ->
                        let redisTransaction = (transaction :?> Transaction).RedisTransaction
                        OrderRedisManager.SetTipOrderGuid redisTransaction orderBookSide (limitOrder.OrderInfo.Id.ToString())
                        result)
                | Some tipOrder ->
                    let tailGuids = OrderRedisManager.GetTail orderBookSide
                    if canPrepend limitOrder tipOrder then
                        (fun transaction ->
                            let newTailGuids = tipOrder.OrderInfo.Id.ToString()::tailGuids
                            let redisTransaction = (transaction :?> Transaction).RedisTransaction
                            OrderRedisManager.SetTail redisTransaction newTailGuids orderBookSide
                            OrderRedisManager.SetTipOrderGuid redisTransaction orderBookSide (limitOrder.OrderInfo.Id.ToString())
                            result
                        )
                    else
                        match tailGuids with
                        | [] ->
                            (fun transaction ->
                                let redisTransaction = (transaction :?> Transaction).RedisTransaction
                                OrderRedisManager.SetTail redisTransaction
                                                          (limitOrder.OrderInfo.Id.ToString()::List.empty)
                                                          orderBookSide
                                result
                            )
                        | head::_ ->
                            (fun transaction ->
                                let headGuid = head |> Guid
                                let fragment = RedisOrderBookSideFragment(orderBookSide, Pointer headGuid)
                                                   :> IOrderBookSideFragment
                                let subInsertFunc = fragment.Insert limitOrder canPrepend
                                subInsertFunc transaction |> ignore
                                result
                            )


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
                            (fun transaction ->
                                let redisTransaction = (transaction :?> Transaction).RedisTransaction
                                OrderRedisManager.SetTail redisTransaction newTailGuids orderBookSide
                                let newOrderBookSide =
                                    RedisOrderBookSideFragment(orderBookSide, Pointer limitOrder.OrderInfo.Id)
                                        :> IOrderBookSideFragment
                                newOrderBookSide
                            )
                        else
                            let tailOrderGuidStr = (tailOrder.OrderInfo.Id.ToString())
                            let tailGuidsWithNewHead = GetElementsAfterXInListY tailOrderGuidStr
                                                                                tailGuids
                            match tailGuidsWithNewHead with
                            | None ->
                                failwithf "Assertion failed, no %s found in tail" tailOrderGuidStr
                            | Some [] ->
                                (fun transaction ->
                                    let redisTransaction = (transaction :?> Transaction).RedisTransaction
                                    let newTailGuids = List.append tailGuids (limitOrder.OrderInfo.Id.ToString()::List.empty)
                                    OrderRedisManager.SetTail redisTransaction newTailGuids orderBookSide
                                    this :> IOrderBookSideFragment
                                )
                            | Some (head::_) ->
                                (fun transaction ->
                                    let headGuid = head |> Guid
                                    let fragment = RedisOrderBookSideFragment(orderBookSide, Pointer headGuid)
                                                       :> IOrderBookSideFragment
                                    (fragment.Insert limitOrder canPrepend) transaction
                                )

            | Empty ->
                (fun _ ->
                    let newOrderBookSide =
                        RedisOrderBookSideFragment(orderBookSide, Pointer limitOrder.OrderInfo.Id)
                                       :> IOrderBookSideFragment
                    newOrderBookSide
                )

        member this.Remove (orderId: Guid): Option<OrderBookSideFragmentModification> =
            if this.OrderExists orderId then
                this.RemoveOrder orderId |> Some
            else
                None

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

    let ExecuteRedisTransaction (redisTransaction: ITransaction) =
        let success: bool = redisTransaction.Execute CommandFlags.None
        if not success then
            false
        else
            redisTransaction.WaitAll()
            true

    let NewTransaction () =
        let redisTransaction = OrderRedisManager.CreateTransaction()
        let transaction = Transaction(redisTransaction) :> FsharpExchangeDotNetStandard.ITransaction
        transaction,redisTransaction

    let rec CancelOrder (orderId: Guid) (allMarkets: List<Market*OrderBook>): bool =
        match allMarkets with
        | [] ->
            failwith "Order not found in any market"
        | (headMarket,headOrderBook)::tail ->
            match headOrderBook.[Side.Ask].Remove orderId with
            | None ->
                match headOrderBook.[Side.Bid].Remove orderId with
                | None ->
                    CancelOrder orderId tail
                | Some modificationFunc ->
                    let transaction,redisTransaction = NewTransaction ()
                    modificationFunc transaction |> ignore
                    ExecuteRedisTransaction redisTransaction
            | Some modificationFunc ->
                let transaction,redisTransaction = NewTransaction ()
                modificationFunc transaction |> ignore
                ExecuteRedisTransaction redisTransaction

    interface IMarketStore with

        member this.GetOrderBook (market: Market): OrderBook =
            lock lockObject (fun _ ->
                GetOrderBookInternal market
            )

        member this.CancelOrder (orderId: Guid) =
            lock lockObject (fun _ ->
                match CancelOrder orderId (Map.toList markets) with
                | false ->
                    let orderId = orderId.ToString()
                    failwithf "Something wrong happened with the transaction, had to be rolledback; OrderID to cancel: %s"
                              orderId
                | _ ->
                    ()
            )

        member this.ReceiveOrder (order: OrderRequest) (market: Market) =
            lock lockObject (
                fun _ ->
                    let orderBook = GetOrderBookInternal market
                    if OrderRedisManager.OrderExists order.Id then
                        raise OrderAlreadyExists

                    let transaction,redisTransaction = NewTransaction ()

                    let newOrderBook,maybeMatch = (orderBook.InsertOrder order) transaction
                    let bidSide = newOrderBook.[Side.Bid] :?> RedisOrderBookSideFragment
                    let askSide = newOrderBook.[Side.Ask] :?> RedisOrderBookSideFragment
                    bidSide.SyncAsRoot redisTransaction
                    askSide.SyncAsRoot redisTransaction
                    let success: bool = ExecuteRedisTransaction redisTransaction
                    if not success then
                        let orderId = order.Id.ToString()
                        failwithf "Something wrong happened with the transaction, had to be rolledback; OrderID to be added: %s"
                                  orderId
                    maybeMatch
                )
