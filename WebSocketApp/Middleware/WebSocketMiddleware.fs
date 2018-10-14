namespace WebSocketApp

module Middleware =

    open System
    open System.Text
    open System.Threading
    open System.Threading.Tasks
    open System.Net.WebSockets
    open Microsoft.AspNetCore.Http

    open FSharp.Control.Tasks.ContextInsensitive

    open FsharpExchangeDotNetStandard

    let mutable sockets = list<WebSocket>.Empty

    let private addSocket sockets socket = socket :: sockets

    let private removeSocket sockets socket =
        sockets
        |> List.choose (fun s -> if s <> socket then Some s else None)

    let exchange = Exchange()

    let private sendMessage =
        fun (socket : WebSocket) (message : string) ->
            task {
                let buffer = Encoding.UTF8.GetBytes(message)
                let segment = new ArraySegment<byte>(buffer)

                if socket.State = WebSocketState.Open then
                    do! socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None)
                else
                    sockets <- removeSocket sockets socket
            }

    let sendLimitOrderToEngine =
        fun (limitOrder: WebSocketApp.Models.LimitOrder) ->
            task {
                let orderInfo = { Id = Guid.NewGuid();
                                  Side = FsharpExchangeDotNetStandard.Side.Parse limitOrder.Side;
                                  Quantity = limitOrder.Quantity; }
                let limitOrder = { OrderInfo = orderInfo; Price = limitOrder.Price; }
                let limitOrderReq = { Order = limitOrder; RequestType = LimitOrderRequestType.Normal; }
                let market = { BuyCurrency = Currency.BTC; SellCurrency = Currency.USD; }

                // TODO: make async
                exchange.SendLimitOrder(limitOrderReq, market)
                ()
            }
    
    let sendMessageToSockets =
        fun message ->
            task {
                for socket in sockets do
                    try
                        do! sendMessage socket message
                    with
                        | _ -> sockets <- removeSocket sockets socket
            }
    
    type WebSocketMiddleware(next : RequestDelegate) =
        member __.Invoke(ctx : HttpContext) =
            async {
                if ctx.Request.Path = PathString("/ws") then
                    match ctx.WebSockets.IsWebSocketRequest with
                    | true ->
                        let! webSocket = ctx.WebSockets.AcceptWebSocketAsync() |> Async.AwaitTask
                        sockets <- addSocket sockets webSocket

                        let buffer : byte[] = Array.zeroCreate 4096
                        let! ct = Async.CancellationToken
                        
                        let task = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                                       |> Async.AwaitTask
                        let! _ = task
                        return ()

                    | false ->
                        ctx.Response.StatusCode <- 400
                        return ()
                else
                    let task = next.Invoke(ctx)
                                    |> Async.AwaitTask
                    do! task
                    return ()
            } |> Async.StartAsTask :> Task