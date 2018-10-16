//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard

open System

exception LiquidityProblem
exception MatchExpectationsUnmet

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
   abstract member SyncAsRoot: unit -> unit

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
        member this.SyncAsRoot () =
            () // NOP, no need for memory

type public Market =
    { BuyCurrency: Currency; SellCurrency: Currency }

type Persistence =
    | Memory
    | Redis
