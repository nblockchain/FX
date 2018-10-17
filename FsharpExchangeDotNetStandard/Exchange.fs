//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard

type public Exchange(persistenceType: Persistence) =

    let marketStore =
        match persistenceType with
        | Persistence.Memory -> MemoryStorageLayer.MarketStore() :> IMarketStore
        | Persistence.Redis -> Redis.MarketStore() :> IMarketStore

    new() = Exchange(Persistence.Redis)

    member __.Item
        with get (market: Market): OrderBook =
            marketStore.GetOrderBook market

    member __.SendMarketOrder (order: OrderInfo, market: Market) =
        marketStore.ReceiveOrder (OrderRequest.Market(order)) market

    member __.SendLimitOrder (order: LimitOrderRequest, market: Market) =
        marketStore.ReceiveOrder (OrderRequest.Limit(order)) market

