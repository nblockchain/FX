//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
// Copyright (C) 2018 Diginex Ltd. (www.diginex.com)
//

namespace FsharpExchangeDotNetStandard

open System
open System.Linq

exception LiquidityProblem
exception MatchExpectationsUnmet
exception OrderAlreadyExists

type public Currency =
    | BTC
    | USD

type Side =
    | Buy
    | Sell
    static member Parse (side: string): Side =
        match side.ToLower() with
        | "buy" -> Buy
        | "sell" -> Sell
        | invalidSide -> invalidArg "side" (sprintf "Unknown side %s" side)
    member self.Other() =
        match self with
        | Side.Buy -> Side.Sell
        | Side.Sell -> Side.Buy

type LimitOrderRequestType =
    | Normal
    | MakerOnly

type OrderInfo =
    { Id: Guid; Side: Side; Quantity: decimal; }

type LimitOrder =
    { OrderInfo: OrderInfo; Price: decimal }

type LimitOrderRequest =
    { Order: LimitOrder; RequestType: LimitOrderRequestType; }

type OrderRequest =
    | Market of OrderInfo
    | Limit of LimitOrderRequest

    member this.Side
        with get() =
            match this with
            | Market item ->
                item.Side
            | Limit item ->
                item.Order.OrderInfo.Side

    member this.Id
        with get() =
            match this with
            | Market item ->
                item.Id
            | Limit item ->
                item.Order.OrderInfo.Id


type HeadTail<'TElement,'TContainer> =
    {
        Head: 'TElement;
        Tail: unit -> 'TContainer;
    }
type ListAnalysis<'TElement,'TContainer> =
    | EmptyList
    | NonEmpty of HeadTail<'TElement,'TContainer>

type IOrderBookSideFragment =
    abstract member Analyze: unit -> ListAnalysis<LimitOrder,IOrderBookSideFragment>
    abstract member Insert: LimitOrder -> (LimitOrder -> LimitOrder -> bool) -> IOrderBookSideFragment
    abstract member Tip: Option<LimitOrder>
    abstract member Tail: Option<IOrderBookSideFragment>
    abstract member Count: unit -> int

type public Market =
    { BuyCurrency: Currency; SellCurrency: Currency }

type Persistence =
    | Memory
    | Redis
