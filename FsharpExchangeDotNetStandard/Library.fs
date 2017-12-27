namespace FsharpExchangeDotNetStandard

open System.Collections.Generic

type public Currency =
    | BTC
    | USD

type Side =
    | Buy
    | Sell

type LimitOrder =
    { Side: Side; Quantity: decimal; Price: decimal }

type OrderBookSide() =
    inherit List<LimitOrder>()

type OrderBook() =
    member x.Item
        with get (side: Side) =
            let someSide: OrderBookSide = new OrderBookSide()
            someSide

type public Market =
    { BuyCurrency: Currency; SellCurrency: Currency }


type public Exchange() =
    let markets = Dictionary<Market, OrderBook>()

    member x.SendOrder (limitOrder: LimitOrder, market: Market) =
        ()

    member x.Item
        with get (market: Market) =
            new OrderBook()
        