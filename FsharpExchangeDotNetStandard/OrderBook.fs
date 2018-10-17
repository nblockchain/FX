//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard

type MatchLeftOver =
    | NoMatch
    | UnmatchedLimitOrderLeftOverAfterPartialMatch of LimitOrder
    | SideLeftAfterFullMatch of IOrderBookSideFragment

type OrderBook(bidSide: IOrderBookSideFragment, askSide: IOrderBookSideFragment,
               emptySide: Side -> IOrderBookSideFragment) =

    let CanPrependOrderBeforeOrder (incomingOrder: LimitOrder) (existingOrder: LimitOrder): bool =
        match incomingOrder.OrderInfo.Side with
        | Side.Buy -> incomingOrder.Price > existingOrder.Price
        | Side.Sell -> incomingOrder.Price < existingOrder.Price

    let rec InsertLimitOrder (incomingOrder: LimitOrder) (orderBookSide: IOrderBookSideFragment)
                                 : IOrderBookSideFragment =
        orderBookSide.Insert incomingOrder CanPrependOrderBeforeOrder

    let rec MatchMarket (quantityLeftToMatch: decimal) (orderBookSide: IOrderBookSideFragment): IOrderBookSideFragment =
        match orderBookSide.Analyze() with
        | EmptyList ->
            raise LiquidityProblem
        | NonEmpty headTail ->
            let firstLimitOrder = headTail.Head
            let tail = headTail.Tail()
            if (quantityLeftToMatch > firstLimitOrder.OrderInfo.Quantity) then
                MatchMarket (quantityLeftToMatch - firstLimitOrder.OrderInfo.Quantity) tail
            elif (quantityLeftToMatch = firstLimitOrder.OrderInfo.Quantity) then
                tail
            else //if (quantityLeftToMatch < firstLimitOrder.Quantity)
                let side = firstLimitOrder.OrderInfo.Side
                let newPartialLimitOrder = { Price = firstLimitOrder.Price;
                                             OrderInfo =
                                             { Id = firstLimitOrder.OrderInfo.Id;
                                               Side = side;
                                               Quantity = firstLimitOrder.OrderInfo.Quantity - quantityLeftToMatch }
                                           }
                InsertLimitOrder newPartialLimitOrder tail

    let rec MatchLimitOrders (orderInBook: LimitOrder)
                             (incomingOrderRequest: LimitOrderRequest)
                             (restOfBookSide: IOrderBookSideFragment)
                             : MatchLeftOver =
        let incomingOrder = incomingOrderRequest.Order
        if (orderInBook.OrderInfo.Side = incomingOrder.OrderInfo.Side) then
            failwith "Failed assertion: MatchLimitOrders() should not receive orders of same side"

        let matches: bool =
            match incomingOrder.OrderInfo.Side with
            | Side.Sell -> orderInBook.Price >= incomingOrder.Price
            | Side.Buy -> orderInBook.Price <= incomingOrder.Price

        if not matches then
            NoMatch
        else
            if (incomingOrderRequest.RequestType = LimitOrderRequestType.MakerOnly) then
                raise MatchExpectationsUnmet

            if (orderInBook.OrderInfo.Quantity = incomingOrder.OrderInfo.Quantity) then
                SideLeftAfterFullMatch(restOfBookSide)
            elif (orderInBook.OrderInfo.Quantity > incomingOrder.OrderInfo.Quantity) then
                let partialRemainingQuantity = orderInBook.OrderInfo.Quantity - incomingOrder.OrderInfo.Quantity
                let side = orderInBook.OrderInfo.Side
                let partialRemainingLimitOrder = { Price = orderInBook.Price;
                                                   OrderInfo =
                                                   { Id = orderInBook.OrderInfo.Id;
                                                     Side = side;
                                                     Quantity = partialRemainingQuantity }
                                                 }
                SideLeftAfterFullMatch(InsertLimitOrder partialRemainingLimitOrder restOfBookSide)
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

                match restOfBookSide.Analyze() with
                | EmptyList ->
                    UnmatchedLimitOrderLeftOverAfterPartialMatch(partialRemainingIncomingLimitOrder)
                | NonEmpty headTail ->
                    let secondLimitOrder = headTail.Head
                    let secondTail = headTail.Tail()
                    MatchLimitOrders secondLimitOrder partialRemainingIncomingOrderRequest secondTail

    let rec MatchLimit (incomingOrderRequest: LimitOrderRequest) (orderBookSide: OrderBook)
                     : OrderBook =
        let incomingOrder = incomingOrderRequest.Order
        match incomingOrder.OrderInfo.Side with
        | Side.Buy ->
            match askSide.Analyze() with
            | EmptyList ->
                OrderBook(InsertLimitOrder incomingOrder bidSide, askSide, emptySide)
            | NonEmpty headTail ->
                let firstSellLimitOrder = headTail.Head
                let restOfAskSide = headTail.Tail()
                let maybeMatchingResultSide = MatchLimitOrders firstSellLimitOrder incomingOrderRequest restOfAskSide
                match maybeMatchingResultSide with
                | NoMatch -> OrderBook(InsertLimitOrder incomingOrder bidSide, askSide, emptySide)
                | SideLeftAfterFullMatch(newAskSide) -> OrderBook(bidSide, newAskSide, emptySide)
                | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                    OrderBook(InsertLimitOrder leftOverOrder bidSide,
                              emptySide Side.Sell,
                              emptySide)

        | Side.Sell ->
            match bidSide.Analyze() with
            | EmptyList -> OrderBook(bidSide, InsertLimitOrder incomingOrder askSide, emptySide)
            | NonEmpty headTail ->
                let firstBuyLimitOrder = headTail.Head
                let restOfBidSide = headTail.Tail()
                let maybeMatchingResultSide = MatchLimitOrders firstBuyLimitOrder incomingOrderRequest restOfBidSide
                match maybeMatchingResultSide with
                | NoMatch -> OrderBook(bidSide, InsertLimitOrder incomingOrder askSide, emptySide)
                | SideLeftAfterFullMatch(newBidSide) -> OrderBook(newBidSide, askSide, emptySide)
                | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                    OrderBook(emptySide Side.Buy,
                              InsertLimitOrder leftOverOrder askSide,
                              emptySide)

    member internal this.InsertOrder (order: OrderRequest): OrderBook =
        match order with
        | Limit(limitOrder) ->
            MatchLimit limitOrder this
        | Market(marketOrder) ->
            match marketOrder.Side with
            | Side.Buy ->
                OrderBook(bidSide, MatchMarket marketOrder.Quantity askSide, emptySide)
            | Side.Sell ->
                OrderBook(MatchMarket marketOrder.Quantity bidSide, askSide, emptySide)

    member x.Item
        with get (side: Side) =
            match side with
            | Side.Buy -> bidSide
            | Side.Sell -> askSide

type IMarketStore =
    abstract member GetOrderBook: Market -> OrderBook
    abstract member ReceiveOrder: OrderRequest -> Market -> unit
