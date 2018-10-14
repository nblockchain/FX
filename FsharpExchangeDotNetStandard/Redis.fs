//
// Copyright (C) 2017-2018 Gate Digital Services Ltd. (Gatecoin)
//

namespace FsharpExchangeDotNetStandard.Redis

open FsharpExchangeDotNetStandard

type OrderQuery =
    {
        OrderId: string;
    }

type MarketQuery =
    {
        Market: Market;
        Side: Side;
        Tip: bool;
    }

type Query =
    | MarketSide of MarketQuery
    | Order of OrderQuery
