
namespace FsharpExchangeDotNetStandard

open System

type MatchLeftOver =
    | NoMatch
    | UnmatchedLimitOrderLeftOverAfterPartialMatch of LimitOrder
    | SideLeftAfterFullMatch of OrderBookSideFragmentModification

type OrderBook(bidSide: IOrderBookSideFragment, askSide: IOrderBookSideFragment,
               emptySide: Side -> IOrderBookSideFragment) =

    let CanPrependOrderBeforeOrder (incomingOrder: LimitOrder) (existingOrder: LimitOrder): bool =
        match incomingOrder.OrderInfo.Side with
        | Side.Bid -> incomingOrder.Price > existingOrder.Price
        | Side.Ask -> incomingOrder.Price < existingOrder.Price

    let rec InsertLimitOrder (incomingOrder: LimitOrder) (orderBookSide: IOrderBookSideFragment)
                                 : OrderBookSideFragmentModification =
        orderBookSide.Insert incomingOrder CanPrependOrderBeforeOrder

    let rec MatchMarket (quantityLeftToMatch: decimal) (orderBookSide: IOrderBookSideFragment)
                        : OrderBookSideFragmentModification =
        match orderBookSide.Analyze() with
        | EmptyList ->
            raise LiquidityProblem
        | NonEmpty headTail ->

            let firstLimitOrder = headTail.Head
            let tail = headTail.Tail()
            if (quantityLeftToMatch > firstLimitOrder.OrderInfo.Quantity) then
                MatchMarket (quantityLeftToMatch - firstLimitOrder.OrderInfo.Quantity) tail
            elif (quantityLeftToMatch = firstLimitOrder.OrderInfo.Quantity) then
                (fun _ -> tail)
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
            | Side.Ask -> orderInBook.Price >= incomingOrder.Price
            | Side.Bid -> orderInBook.Price <= incomingOrder.Price

        if not matches then
            NoMatch
        else
            if (incomingOrderRequest.RequestType = LimitOrderRequestType.MakerOnly) then
                raise MatchExpectationsUnmet

            if (orderInBook.OrderInfo.Quantity = incomingOrder.OrderInfo.Quantity) then
                SideLeftAfterFullMatch(fun _ -> restOfBookSide)
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
                     : OrderBookModification =
        let incomingOrder = incomingOrderRequest.Order
        let incomingSide = incomingOrder.OrderInfo.Side
        let matchingSide,nonMatchingSide =
            match incomingSide with
            | Side.Bid -> askSide,bidSide
            | Side.Ask -> bidSide,askSide

        let resultingSideFunc,otherSideFunc,limitMatch
            : OrderBookSideFragmentModification*OrderBookSideFragmentModification*Option<Match> =
            match matchingSide.Analyze() with
            | EmptyList ->
                (InsertLimitOrder incomingOrder nonMatchingSide),(fun _ -> matchingSide),None
            | NonEmpty headTail ->
                let firstLimitOrder = headTail.Head
                let restOfSide = headTail.Tail()
                let maybeMatchingResultSide = MatchLimitOrders firstLimitOrder incomingOrderRequest restOfSide

                match maybeMatchingResultSide with
                | SideLeftAfterFullMatch newSide ->
                    (fun _ -> nonMatchingSide), newSide, Some Match.Full
                | NoMatch ->
                    (InsertLimitOrder incomingOrder nonMatchingSide),(fun _ -> matchingSide), None
                | UnmatchedLimitOrderLeftOverAfterPartialMatch leftOverOrder ->
                    let matchAmount =
                        incomingOrderRequest.Order.OrderInfo.Quantity - leftOverOrder.OrderInfo.Quantity

                    (InsertLimitOrder leftOverOrder nonMatchingSide),
                    (fun _ -> (emptySide (incomingSide.Other()))),
                    Some (Match.Partial matchAmount)
        match incomingSide with
        | Side.Bid ->
            (fun transaction ->
                let resultingSide = resultingSideFunc transaction
                let otherSide = otherSideFunc transaction
                OrderBook(resultingSide, otherSide, emptySide), limitMatch)
        | Side.Ask ->
            (fun transaction  ->
                let resultingSide = resultingSideFunc transaction
                let otherSide = otherSideFunc transaction
                OrderBook(otherSide, resultingSide, emptySide), limitMatch)

    member internal this.InsertOrder (order: OrderRequest): OrderBookModification =
        match order with
        | Limit(limitOrder) ->
            MatchLimit limitOrder this
        | Market(marketOrder) ->
            // a market order always matches in a full way unless an exception is thrown:
            let marketMatch = Some Match.Full

            match marketOrder.Side with
            | Side.Bid ->
                (fun transaction ->
                    let newAskSideFunc = MatchMarket marketOrder.Quantity askSide
                    let newAskSide = newAskSideFunc transaction
                    OrderBook(bidSide, newAskSide, emptySide),marketMatch)
            | Side.Ask ->
                (fun transaction ->
                    let newBidSideFunc = MatchMarket marketOrder.Quantity bidSide
                    let newBidSide = newBidSideFunc transaction
                    OrderBook(newBidSide, askSide, emptySide),marketMatch)

    member x.Item
        with get (side: Side) =
            match side with
            | Side.Bid -> bidSide
            | Side.Ask -> askSide

and OrderBookModification =
    ITransaction->OrderBook*Option<Match>

type IMarketStore =
    abstract member GetOrderBook: Market -> OrderBook
    abstract member ReceiveOrder: OrderRequest -> Market -> Option<Match>
    abstract member CancelOrder: Guid -> unit

