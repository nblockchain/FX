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

type LimitOrder =
    { Side: Side; Quantity: decimal; Price: decimal }

type MarketOrder =
    { Side: Side; Quantity: decimal; }

type internal Order =
    | Limit of LimitOrder
    | Market of MarketOrder

type OrderBookSide =
    list<LimitOrder>

type public Market =
    { BuyCurrency: Currency; SellCurrency: Currency }
