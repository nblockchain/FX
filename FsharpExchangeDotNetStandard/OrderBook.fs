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

    let rec AppendOrder (order: LimitOrder) (orderBookSide: IOrderBookSideFragment): IOrderBookSideFragment =
        match orderBookSide.Analyze() with
        | EmptyList ->
            (emptySide order.OrderInfo.Side).Prepend order
        | NonEmpty headTail ->
            let head = headTail.Head
            if (head.OrderInfo.Side <> order.OrderInfo.Side) then
                failwith "Assertion failed, should not mix different sides in same OrderBookSide structure"

            // FIXME: when order is same price, we should let the oldest order be in the tip...? test this
            let canAdd =
                match order.OrderInfo.Side with
                | Side.Buy -> order.Price > head.Price
                | Side.Sell -> order.Price < head.Price
            if canAdd then
                orderBookSide.Prepend order
            else
                let tail = headTail.Tail()
                let newTail = AppendOrder order tail
                newTail.Prepend head

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
                AppendOrder newPartialLimitOrder tail

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
                SideLeftAfterFullMatch(AppendOrder partialRemainingLimitOrder restOfBookSide)
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
                OrderBook(AppendOrder incomingOrder bidSide, askSide, emptySide)
            | NonEmpty headTail ->
                let firstSellLimitOrder = headTail.Head
                let restOfAskSide = headTail.Tail()
                let maybeMatchingResultSide = MatchLimitOrders firstSellLimitOrder incomingOrderRequest restOfAskSide
                match maybeMatchingResultSide with
                | NoMatch -> OrderBook(AppendOrder incomingOrder bidSide, askSide, emptySide)
                | SideLeftAfterFullMatch(newAskSide) -> OrderBook(bidSide, newAskSide, emptySide)
                | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                    OrderBook(AppendOrder leftOverOrder bidSide,
                              emptySide Side.Sell,
                              emptySide)

        | Side.Sell ->
            match bidSide.Analyze() with
            | EmptyList -> OrderBook(bidSide, AppendOrder incomingOrder askSide, emptySide)
            | NonEmpty headTail ->
                let firstBuyLimitOrder = headTail.Head
                let restOfBidSide = headTail.Tail()
                let maybeMatchingResultSide = MatchLimitOrders firstBuyLimitOrder incomingOrderRequest restOfBidSide
                match maybeMatchingResultSide with
                | NoMatch -> OrderBook(bidSide, AppendOrder incomingOrder askSide, emptySide)
                | SideLeftAfterFullMatch(newBidSide) -> OrderBook(newBidSide, askSide, emptySide)
                | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                    OrderBook(emptySide Side.Buy,
                              AppendOrder leftOverOrder askSide,
                              emptySide)

    member __.SyncAsRoot() =
        bidSide.SyncAsRoot()
        askSide.SyncAsRoot()

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

