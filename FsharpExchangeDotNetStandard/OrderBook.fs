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

    let rec MatchLimitOrders (orderInBook: LimitOrder) (incomingOrder: LimitOrder) (restOfBookSide: OrderBookSide)
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
            else //if (orderInBook.Quantity < incomingOrder.Quantity)
                let partialRemainingIncomingLimitOrder =
                    { Side = incomingOrder.Side;
                      Price = incomingOrder.Price;
                      Quantity = incomingOrder.Quantity - orderInBook.Quantity }
                match restOfBookSide with
                | [] -> failwith "not implemented yet!" // <- FIXME!: write test for this case
                | secondLimitOrder::secondTail ->
                    MatchLimitOrders secondLimitOrder partialRemainingIncomingLimitOrder secondTail
        else
            None

    let rec MatchLimit (incomingOrder: LimitOrder) (orderBookSide: OrderBook): OrderBook =
        match incomingOrder.Side with
        | Side.Buy ->
            match askSide with
            | [] -> OrderBook(incomingOrder::bidSide, askSide)
            | firstSellLimitOrder::restOfAskSide ->
                let maybeMatchingResultSide = MatchLimitOrders firstSellLimitOrder incomingOrder restOfAskSide
                match maybeMatchingResultSide with
                | None -> OrderBook(incomingOrder::bidSide, askSide)
                | Some(newAskSide) -> OrderBook(bidSide, newAskSide)
        | Side.Sell ->
            match bidSide with
            | [] -> OrderBook(bidSide, incomingOrder::askSide)
            | firstBuyLimitOrder::restOfBidSide ->
                let maybeMatchingResultSide = MatchLimitOrders firstBuyLimitOrder incomingOrder restOfBidSide
                match maybeMatchingResultSide with
                | None -> OrderBook(bidSide, incomingOrder::askSide)
                | Some(newBidSide) -> OrderBook(newBidSide, askSide)

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
