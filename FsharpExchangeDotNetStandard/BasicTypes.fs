namespace FsharpExchangeDotNetStandard

open System

exception LiquidityProblem

type public Currency =
    | BTC
    | USD

type Side =
    | Buy
    | Sell
    member self.Other() =
        match self with
        | Side.Buy -> Side.Sell
        | Side.Sell -> Side.Buy

type LimitOrderRequestType =
    | Normal
    | MakerOnly

type OrderInfo =
    { Side: Side; Quantity: decimal; }

type LimitOrder =
    { OrderInfo: OrderInfo; Price: decimal }

type LimitOrderRequest =
    { Order: LimitOrder; RequestType: LimitOrderRequestType; }

type OrderRequest =
    | Market of OrderInfo
    | Limit of LimitOrderRequest

type OrderBookSide =
    list<LimitOrder>

type public Market =
    { BuyCurrency: Currency; SellCurrency: Currency }
