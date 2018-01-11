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
            if (head.Side <> order.Side) then
                failwith "Assertion failed, should not mix different sides in same OrderBookSide structure"

            // FIXME: when order is same price, we should let the oldest order be in the tip...? test this
            let canAdd =
                match order.Side with
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
            if (quantityLeftToMatch > firstLimitOrder.Quantity) then
                MatchMarket (quantityLeftToMatch - firstLimitOrder.Quantity) tail
            elif (quantityLeftToMatch = firstLimitOrder.Quantity) then
                tail
            else //if (quantityLeftToMatch < firstLimitOrder.Quantity)
                let newPartialLimitOrder = { Side = firstLimitOrder.Side;
                                             Price = firstLimitOrder.Price;
                                             Quantity = firstLimitOrder.Quantity - quantityLeftToMatch }
                OrderBookSideMemoryManager.AppendOrder newPartialLimitOrder tail

    let rec MatchLimitOrders (orderInBook: LimitOrder) (incomingOrder: LimitOrder) (restOfBookSide: OrderBookSide)
                       : MatchLeftOver =
        if (orderInBook.Side = incomingOrder.Side) then
            failwith "Failed assertion: MatchLimitOrders() should not receive orders of same side"

        let matches: bool =
            match incomingOrder.Side with
            | Side.Sell -> orderInBook.Price >= incomingOrder.Price
            | Side.Buy -> orderInBook.Price <= incomingOrder.Price

        if (matches) then
            if (orderInBook.Quantity = incomingOrder.Quantity) then
                SideLeftAfterFullMatch(restOfBookSide)
            elif (orderInBook.Quantity > incomingOrder.Quantity) then
                let partialRemainingLimitOrder = { Side = orderInBook.Side;
                                                   Price = orderInBook.Price;
                                                   Quantity = orderInBook.Quantity - incomingOrder.Quantity }
                SideLeftAfterFullMatch(OrderBookSideMemoryManager.AppendOrder partialRemainingLimitOrder restOfBookSide)
            else //if (orderInBook.Quantity < incomingOrder.Quantity)
                let partialRemainingIncomingLimitOrder =
                    { Side = incomingOrder.Side;
                      Price = incomingOrder.Price;
                      Quantity = incomingOrder.Quantity - orderInBook.Quantity }
                match restOfBookSide with
                | [] ->
                    UnmatchedLimitOrderLeftOverAfterPartialMatch(partialRemainingIncomingLimitOrder)
                | secondLimitOrder::secondTail ->
                    MatchLimitOrders secondLimitOrder partialRemainingIncomingLimitOrder secondTail
        else
            NoMatch

    let rec MatchLimit (incomingOrder: LimitOrder) (orderBookSide: OrderBook): OrderBook =
        match incomingOrder.Side with
        | Side.Buy ->
            match askSide with
            | [] -> OrderBook(OrderBookSideMemoryManager.AppendOrder incomingOrder bidSide, askSide)
            | firstSellLimitOrder::restOfAskSide ->
                let maybeMatchingResultSide = MatchLimitOrders firstSellLimitOrder incomingOrder restOfAskSide
                match maybeMatchingResultSide with
                | NoMatch -> OrderBook(OrderBookSideMemoryManager.AppendOrder incomingOrder bidSide, askSide)
                | SideLeftAfterFullMatch(newAskSide) -> OrderBook(bidSide, newAskSide)
                | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                    OrderBook(OrderBookSideMemoryManager.AppendOrder leftOverOrder bidSide, [])
        | Side.Sell ->
            match bidSide with
            | [] -> OrderBook(bidSide, OrderBookSideMemoryManager.AppendOrder incomingOrder askSide)
            | firstBuyLimitOrder::restOfBidSide ->
                let maybeMatchingResultSide = MatchLimitOrders firstBuyLimitOrder incomingOrder restOfBidSide
                match maybeMatchingResultSide with
                | NoMatch -> OrderBook(bidSide, OrderBookSideMemoryManager.AppendOrder incomingOrder askSide)
                | SideLeftAfterFullMatch(newBidSide) -> OrderBook(newBidSide, askSide)
                | UnmatchedLimitOrderLeftOverAfterPartialMatch(leftOverOrder) ->
                    OrderBook([], OrderBookSideMemoryManager.AppendOrder leftOverOrder askSide)

    new() = OrderBook([], [])

    member internal this.InsertOrder (order: Order): OrderBook =
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
