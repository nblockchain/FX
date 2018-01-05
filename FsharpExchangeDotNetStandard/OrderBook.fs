namespace FsharpExchangeDotNetStandard

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
                newPartialLimitOrder::tail

    let MatchLimitOrders (orderInBook: LimitOrder) (incomingOrder: LimitOrder) (restOfBookSide: OrderBookSide)
                       : Option<OrderBookSide> =
        if (orderInBook.Side = incomingOrder.Side) then
            failwith "Failed assertion: MatchLimitOrders() should not receive orders of same side"

        let matches: bool =
            match incomingOrder.Side with
            | Side.Sell -> orderInBook.Price >= incomingOrder.Price
            | Side.Buy -> orderInBook.Price <= incomingOrder.Price

        if (matches) then
            if (orderInBook.Quantity = incomingOrder.Quantity) then
                Some(restOfBookSide)
            elif (orderInBook.Quantity > incomingOrder.Quantity) then
                let partialRemainingLimitOrder = { Side = orderInBook.Side;
                                                   Price = orderInBook.Price;
                                                   Quantity = orderInBook.Quantity - incomingOrder.Quantity }
                Some(partialRemainingLimitOrder::restOfBookSide)
            else //if (firstBuyLimitOrder.Quantity < limitOrder.Quantity) <- FIXME!: write test for this case
                failwith "Not implemented yet"
        else
            None

    let rec MatchLimits (limitOrder: LimitOrder) (orderBookSide: OrderBook): OrderBook =
        match limitOrder.Side with
        | Side.Buy ->
            match askSide with
            | [] -> OrderBook(limitOrder::bidSide, askSide)
            | firstSellLimitOrder::restOfAskSide ->
                let maybeMatchingResultSide = MatchLimitOrders firstSellLimitOrder limitOrder restOfAskSide
                match maybeMatchingResultSide with
                | None -> OrderBook(limitOrder::bidSide, askSide)
                | Some(newAskSide) -> OrderBook(bidSide, newAskSide)
        | Side.Sell ->
            match bidSide with
            | [] -> OrderBook(bidSide, limitOrder::askSide)
            | firstBuyLimitOrder::restOfBidSide ->
                let maybeMatchingResultSide = MatchLimitOrders firstBuyLimitOrder limitOrder restOfBidSide
                match maybeMatchingResultSide with
                | None -> OrderBook(bidSide, limitOrder::askSide)
                | Some(newBidSide) -> OrderBook(newBidSide, askSide)

    new() = OrderBook([], [])

    member internal this.InsertOrder (order: Order): OrderBook =
        match order with
        | Limit(limitOrder) ->
            MatchLimits limitOrder this
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
