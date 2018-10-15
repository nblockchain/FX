//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard

open FsharpExchangeDotNetStandard.Redis

open System

open StackExchange.Redis
open Newtonsoft.Json

type MatchLeftOver =
    | NoMatch
    | UnmatchedLimitOrderLeftOverAfterPartialMatch of LimitOrder
    | SideLeftAfterFullMatch of IOrderBookSide

module OrderBookSideMemoryManager =
    let rec AppendOrder (order: LimitOrder) (orderBookSide: IOrderBookSide): IOrderBookSide =
        orderBookSide.IfEmptyElse
            (fun _ -> MemoryOrderBookSide([ order ]):>IOrderBookSide)
            (fun head tail ->
                if (head.OrderInfo.Side <> order.OrderInfo.Side) then
                    failwith "Assertion failed, should not mix different sides in same OrderBookSide structure"

                // FIXME: when order is same price, we should let the oldest order be in the tip...? test this
                let canAdd =
                    match order.OrderInfo.Side with
                    | Side.Buy -> order.Price > head.Price
                    | Side.Sell -> order.Price < head.Price
                if (canAdd) then
                    orderBookSide.Prepend order
                else
                    let newTail = AppendOrder order tail
                    newTail.Prepend head
            )

type OrderBook(bidSide: IOrderBookSide, askSide: IOrderBookSide) =

    let rec MatchMarket (quantityLeftToMatch: decimal) (orderBookSide: IOrderBookSide): IOrderBookSide =
        orderBookSide.IfEmptyElse
            (fun _ -> raise LiquidityProblem)
            (fun firstLimitOrder tail ->
                if (quantityLeftToMatch > firstLimitOrder.OrderInfo.Quantity) then
                    MatchMarket (quantityLeftToMatch - firstLimitOrder.OrderInfo.Quantity) tail
                elif (quantityLeftToMatch = firstLimitOrder.OrderInfo.Quantity) then
                    tail
                else //if (quantityLeftToMatch < firstLimitOrder.Quantity)
                    let newPartialLimitOrder = { Price = firstLimitOrder.Price;
                                                 OrderInfo =
                                                 { Id = firstLimitOrder.OrderInfo.Id;
                                                   Side = firstLimitOrder.OrderInfo.Side;
                                                   Quantity = firstLimitOrder.OrderInfo.Quantity - quantityLeftToMatch }
                                               }
                    OrderBookSideMemoryManager.AppendOrder newPartialLimitOrder tail
            )

    let rec MatchLimitOrders (orderInBook: LimitOrder)
                             (incomingOrderRequest: LimitOrderRequest)
                             (restOfBookSide: IOrderBookSide)
                             : MatchLeftOver =
        let incomingOrder = incomingOrderRequest.Order
        if (orderInBook.OrderInfo.Side = incomingOrder.OrderInfo.Side) then
            failwith "Failed assertion: MatchLimitOrders() should not receive orders of same side"

        let matches: bool =
            match incomingOrder.OrderInfo.Side with
            | Side.Sell -> orderInBook.Price >= incomingOrder.Price
            | Side.Buy -> orderInBook.Price <= incomingOrder.Price

        if (matches) then
            if (incomingOrderRequest.RequestType = LimitOrderRequestType.MakerOnly) then
                raise MatchExpectationsUnmet

            if (orderInBook.OrderInfo.Quantity = incomingOrder.OrderInfo.Quantity) then
                SideLeftAfterFullMatch(restOfBookSide)
            elif (orderInBook.OrderInfo.Quantity > incomingOrder.OrderInfo.Quantity) then
                let partialRemainingQuantity = orderInBook.OrderInfo.Quantity - incomingOrder.OrderInfo.Quantity
                let partialRemainingLimitOrder = { Price = orderInBook.Price;
                                                   OrderInfo =
                                                   { Id = orderInBook.OrderInfo.Id;
                                                     Side = orderInBook.OrderInfo.Side;
                                                     Quantity = partialRemainingQuantity }
                                                 }
                SideLeftAfterFullMatch(OrderBookSideMemoryManager.AppendOrder partialRemainingLimitOrder restOfBookSide)
            else //if (orderInBook.Quantity < incomingOrder.Quantity)
                let partialRemainingIncomingLimitOrder =
                    { Price = incomingOrder.Price;
                      OrderInfo = { Id = orderInBook.OrderInfo.Id;
                                    Side = incomingOrder.OrderInfo.Side;
                                    Quantity = incomingOrder.OrderInfo.Quantity - orderInBook.OrderInfo.Quantity }
                    }
                let partialRemainingIncomingOrderRequest =
                    { Order = partialRemainingIncomingLimitOrder;
                      RequestType = incomingOrderRequest.RequestType }
                restOfBookSide.IfEmptyElse
                    (fun _ -> UnmatchedLimitOrderLeftOverAfterPartialMatch(partialRemainingIncomingLimitOrder))
                    (fun secondLimitOrder secondTail ->
                        MatchLimitOrders secondLimitOrder partialRemainingIncomingOrderRequest secondTail)
        else
            NoMatch

    let EmptyOrderBookSide () =
        MemoryOrderBookSide([]):>IOrderBookSide

    let rec MatchLimit (incomingOrderRequest: LimitOrderRequest) (orderBookSide: OrderBook)
                     : OrderBook =
        let incomingOrder = incomingOrderRequest.Order
        match incomingOrder.OrderInfo.Side with
        | Side.Buy ->
            askSide.IfEmptyElse
                (fun _ -> OrderBook(OrderBookSideMemoryManager.AppendOrder incomingOrder bidSide, askSide))
                (fun firstSellLimitOrder restOfAskSide ->
                    let maybeMatchingResultSide = MatchLimitOrders firstSellLimitOrder incomingOrderRequest restOfAskSide
                    match maybeMatchingResultSide with
                    | NoMatch -> OrderBook(OrderBookSideMemoryManager.AppendOrder incomingOrder bidSide, askSide)
                    | SideLeftAfterFullMatch(newAskSide) -> OrderBook(bidSide, newAskSide)
                    | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                        OrderBook(OrderBookSideMemoryManager.AppendOrder leftOverOrder bidSide, EmptyOrderBookSide ())
                )
        | Side.Sell ->
            bidSide.IfEmptyElse
                (fun _ -> OrderBook(bidSide, OrderBookSideMemoryManager.AppendOrder incomingOrder askSide))
                (fun firstBuyLimitOrder restOfBidSide ->
                    let maybeMatchingResultSide = MatchLimitOrders firstBuyLimitOrder incomingOrderRequest restOfBidSide
                    match maybeMatchingResultSide with
                    | NoMatch -> OrderBook(bidSide, OrderBookSideMemoryManager.AppendOrder incomingOrder askSide)
                    | SideLeftAfterFullMatch(newBidSide) -> OrderBook(newBidSide, askSide)
                    | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                        OrderBook(EmptyOrderBookSide (), OrderBookSideMemoryManager.AppendOrder leftOverOrder askSide)
                )

    new() = OrderBook(MemoryOrderBookSide([]):>IOrderBookSide, MemoryOrderBookSide([]):>IOrderBookSide)

    member internal this.InsertOrder (order: OrderRequest): OrderBook =
        match order with
        | Limit(limitOrder) ->
            MatchLimit limitOrder this
        | Market(marketOrder) ->
            match marketOrder.Side with
            | Side.Buy ->
                OrderBook(bidSide, MatchMarket marketOrder.Quantity askSide)
            | Side.Sell ->
                OrderBook(MatchMarket marketOrder.Quantity bidSide, askSide)

    member x.Item
        with get (side: Side) =
            match side with
            | Side.Buy -> bidSide
            | Side.Sell -> askSide

    static member internal InsertOrderRedis (order: OrderRequest) (market: Market) (tipOrderId: Guid): unit =
        // TODO: dispose
        let redis = ConnectionMultiplexer.Connect "localhost"
        let db = redis.GetDatabase()

        let nonTipQuery = { Market = market; Tip = false; Side = order.Side }
        let nonTipQueryStr = JsonConvert.SerializeObject nonTipQuery
        let value = db.StringGet (RedisKey.op_Implicit nonTipQueryStr)
        let newTail =
            if not value.HasValue then
                order.Id.ToString()::List.empty
            else
                let currentTail = JsonConvert.DeserializeObject<List<string>>(value.ToString())
                List.append currentTail (order.Id.ToString()::[])

        OrderRedisManager.SetTail newTail nonTipQueryStr

        match order with
        | Limit limitOrder ->
            match limitOrder.RequestType with
            | LimitOrderRequestType.Normal ->
                OrderRedisManager.InsertOrder limitOrder.Order
            | _ -> failwith "makerOnly not supported yet for redis"
        | _ -> failwith "market not supported yet for redis"
