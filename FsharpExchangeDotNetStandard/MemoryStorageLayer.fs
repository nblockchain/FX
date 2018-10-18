//
// Copyright (C) 2018 Diginex Ltd. (www.diginex.com)
//

namespace FsharpExchangeDotNetStandard.MemoryStorageLayer

open FsharpExchangeDotNetStandard

open System
open System.Linq

type MemoryOrderBookSideFragment(memoryList: List<LimitOrder>) =
    let rec AnalyzeList (lst: List<LimitOrder>): ListAnalysis<LimitOrder,IOrderBookSideFragment> =
        match lst with
        | [] -> ListAnalysis.EmptyList
        | head::tail ->
            NonEmpty {
                Head = head
                Tail = (fun _ -> MemoryOrderBookSideFragment(tail):>IOrderBookSideFragment)
            }
    let rec InsertOrder (lst: List<LimitOrder>) (limitOrder: LimitOrder) (canPrepend: LimitOrder -> LimitOrder -> bool)
                       : List<LimitOrder> =
        match lst with
        | [] -> limitOrder::List.empty
        | head::tail ->
            if canPrepend limitOrder head then
                limitOrder::(head::tail)
            else
                head::(InsertOrder tail limitOrder canPrepend)

    member __.OrderExists guid =
        memoryList.Any(fun limitOrder -> limitOrder.OrderInfo.Id = guid)

    interface IOrderBookSideFragment with
        member this.Analyze() =
            AnalyzeList memoryList
        member this.Tip =
            List.tryHead memoryList
        member this.Tail =
            match memoryList with
            | [] -> None
            | _::tail -> MemoryOrderBookSideFragment(tail) :> IOrderBookSideFragment |> Some
        member this.Insert (limitOrder: LimitOrder) (canPrepend: LimitOrder -> LimitOrder -> bool)
                               : IOrderBookSideFragment =
            MemoryOrderBookSideFragment(InsertOrder memoryList limitOrder canPrepend) :> IOrderBookSideFragment
        member this.Count () =
            memoryList.Length

type MarketStore() =
    let mutable markets: Map<Market, OrderBook> = Map.empty
    let lockObject = Object()

    let GetOrderBookInternal (market: Market): OrderBook =
        let maybeOrderBook = Map.tryFind market markets
        match maybeOrderBook with
        | None ->
            OrderBook(MemoryOrderBookSideFragment(List.empty) :> IOrderBookSideFragment,
                      MemoryOrderBookSideFragment(List.empty) :> IOrderBookSideFragment,
                      (fun _ -> MemoryOrderBookSideFragment(List.empty) :> IOrderBookSideFragment))

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
                    let bidSide = orderBook.[Side.Buy] :?> MemoryOrderBookSideFragment
                    let askSide = orderBook.[Side.Sell] :?> MemoryOrderBookSideFragment
                    if askSide.OrderExists order.Id || bidSide.OrderExists order.Id then
                        raise OrderAlreadyExists
                    let newOrderBook = orderBook.InsertOrder order
                    markets <- markets.Add(market, newOrderBook)
                )
