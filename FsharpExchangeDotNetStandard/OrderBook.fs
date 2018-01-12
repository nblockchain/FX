namespace FsharpExchangeDotNetStandard

type MatchLeftOver =
    | NoMatch
    | UnmatchedLimitOrderLeftOverAfterPartialMatch of LimitOrder
    | SideLeftAfterFullMatch of OrderBookSide

module OrderBookSideMemoryManager =
    let rec AppendOrder (order: LimitOrder) (orderBookSide: OrderBookSide): OrderBookSide =
        match orderBookSide with
        | [] -> [ order ]
        | head::tail ->
            if (head.OrderInfo.Side <> order.OrderInfo.Side) then
                failwith "Assertion failed, should not mix different sides in same OrderBookSide structure"

            // FIXME: when order is same price, we should let the oldest order be in the tip...? test this
            let canAdd =
                match order.OrderInfo.Side with
                | Side.Buy -> order.Price > head.Price
                | Side.Sell -> order.Price < head.Price
            if (canAdd) then
                order::orderBookSide
            else
                head::(AppendOrder order tail)

type OrderBook(bidSide: OrderBookSide, askSide: OrderBookSide) =

    let rec MatchMarket (quantityLeftToMatch: decimal) (orderBookSide: OrderBookSide): OrderBookSide =
        match orderBookSide with
        | [] -> raise LiquidityProblem
        | firstLimitOrder::tail ->
            if (quantityLeftToMatch > firstLimitOrder.OrderInfo.Quantity) then
                MatchMarket (quantityLeftToMatch - firstLimitOrder.OrderInfo.Quantity) tail
            elif (quantityLeftToMatch = firstLimitOrder.OrderInfo.Quantity) then
                tail
            else //if (quantityLeftToMatch < firstLimitOrder.Quantity)
                let newPartialLimitOrder = { Price = firstLimitOrder.Price;
                                             OrderInfo =
                                             { Side = firstLimitOrder.OrderInfo.Side;
                                               Quantity = firstLimitOrder.OrderInfo.Quantity - quantityLeftToMatch }
                                           }
                OrderBookSideMemoryManager.AppendOrder newPartialLimitOrder tail

    let rec MatchLimitOrders (orderInBook: LimitOrder)
                             (incomingOrderRequest: LimitOrderRequest)
                             (restOfBookSide: OrderBookSide)
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
                                                   { Side = orderInBook.OrderInfo.Side;
                                                     Quantity = partialRemainingQuantity }
                                                 }
                SideLeftAfterFullMatch(OrderBookSideMemoryManager.AppendOrder partialRemainingLimitOrder restOfBookSide)
            else //if (orderInBook.Quantity < incomingOrder.Quantity)
                let partialRemainingIncomingLimitOrder =
                    { Price = incomingOrder.Price;
                      OrderInfo = { Side = incomingOrder.OrderInfo.Side;
                                    Quantity = incomingOrder.OrderInfo.Quantity - orderInBook.OrderInfo.Quantity }
                    }
                let partialRemainingIncomingOrderRequest =
                    { Order = partialRemainingIncomingLimitOrder;
                      RequestType = incomingOrderRequest.RequestType }
                match restOfBookSide with
                | [] ->
                    UnmatchedLimitOrderLeftOverAfterPartialMatch(partialRemainingIncomingLimitOrder)
                | secondLimitOrder::secondTail ->
                    MatchLimitOrders secondLimitOrder partialRemainingIncomingOrderRequest secondTail
        else
            NoMatch

    let rec MatchLimit (incomingOrderRequest: LimitOrderRequest) (orderBookSide: OrderBook)
                     : OrderBook =
        let incomingOrder = incomingOrderRequest.Order
        match incomingOrder.OrderInfo.Side with
        | Side.Buy ->
            match askSide with
            | [] -> OrderBook(OrderBookSideMemoryManager.AppendOrder incomingOrder bidSide, askSide)
            | firstSellLimitOrder::restOfAskSide ->
                let maybeMatchingResultSide = MatchLimitOrders firstSellLimitOrder incomingOrderRequest restOfAskSide
                match maybeMatchingResultSide with
                | NoMatch -> OrderBook(OrderBookSideMemoryManager.AppendOrder incomingOrder bidSide, askSide)
                | SideLeftAfterFullMatch(newAskSide) -> OrderBook(bidSide, newAskSide)
                | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                    OrderBook(OrderBookSideMemoryManager.AppendOrder leftOverOrder bidSide, [])
        | Side.Sell ->
            match bidSide with
            | [] -> OrderBook(bidSide, OrderBookSideMemoryManager.AppendOrder incomingOrder askSide)
            | firstBuyLimitOrder::restOfBidSide ->
                let maybeMatchingResultSide = MatchLimitOrders firstBuyLimitOrder incomingOrderRequest restOfBidSide
                match maybeMatchingResultSide with
                | NoMatch -> OrderBook(bidSide, OrderBookSideMemoryManager.AppendOrder incomingOrder askSide)
                | SideLeftAfterFullMatch(newBidSide) -> OrderBook(newBidSide, askSide)
                | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                    OrderBook([], OrderBookSideMemoryManager.AppendOrder leftOverOrder askSide)

    new() = OrderBook([], [])

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
