namespace FsharpExchangeDotNetStandard

open System

type public Currency =
    | BTC
    | USD

type Side =
    | Buy
    | Sell

type LimitOrder =
    { Side: Side; Quantity: decimal; Price: decimal }

type MarketOrder =
    { Side: Side; Quantity: decimal; }

type OrderBookSide =
    list<LimitOrder>

type OrderBook(buySide:OrderBookSide, sellSide:OrderBookSide) =

    new() = OrderBook([], [])

    member internal x.InsertOrder (limitOrder: LimitOrder): OrderBook =
        match limitOrder.Side with
        | Side.Buy ->
            OrderBook(limitOrder::buySide, sellSide)
        | Side.Sell ->
            OrderBook(buySide, limitOrder::sellSide)

    member x.Item
        with get (side: Side) =
            match side with
            | Side.Buy -> buySide
            | Side.Sell -> sellSide

type public Market =
    { BuyCurrency: Currency; SellCurrency: Currency }

type public Exchange() =
    let mutable markets: Map<Market, OrderBook> = Map.empty

    let lockObject = Object()

    member x.SendMarketOrder (limitOrder: MarketOrder, market: Market): unit =
        raise (new NotImplementedException())

    member x.SendLimitOrder (limitOrder: LimitOrder, market: Market) =
        lock lockObject (
            fun _ ->
                let maybeOrderBook = Map.tryFind market markets
                let orderBook =
                    match maybeOrderBook with
                    | None ->
                        OrderBook()
                    | Some(orderBookFound) ->
                        orderBookFound
                let newOrderBook = orderBook.InsertOrder limitOrder
                markets <- markets.Add(market, newOrderBook)
            )

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
