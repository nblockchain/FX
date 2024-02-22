namespace GrpcModels

open System

type LimitOrder =
    {
        Price: decimal
        Side: string
        Quantity: decimal
    }

type MarketOrder =
    {
        Side: string
        Quantity: decimal
    }

type CancelOrderRequest =
    {
        OrderId: Guid
        // TODO: add Market
    }
