namespace WebSocketApp.Models

[<CLIMutable>]
type Message =
    {
        Text : string
    }

[<CLIMutable>]
type LimitOrder =
    {
        Price: decimal;
        Side: string;
        Quantity: decimal;
    }

[<CLIMutable>]
type MarketOrder =
    {
        Side: string;
        Quantity: decimal;
    }
