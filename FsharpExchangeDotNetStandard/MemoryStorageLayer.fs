//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard.MemoryStorageLayer

open FsharpExchangeDotNetStandard

open System

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
                    let newOrderBook = orderBook.InsertOrder order
                    markets <- markets.Add(market, newOrderBook)
                )
