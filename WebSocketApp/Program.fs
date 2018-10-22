module WebSocketApp.App

open System
open System.IO

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection

open Microsoft.AspNetCore.Http

open Giraffe
open Giraffe.Razor

open FSharp.Control.Tasks.ContextInsensitive

open WebSocketApp.Models
open WebSocketApp.Middleware

// ---------------------------------
// Web app
// ---------------------------------

let indexHandler (name : string) =
    let greetings = sprintf "Hello %s, from Giraffe!" name
    let model     = { Text = greetings }
    razorHtmlView "Index" model

let handlePostMessage =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! message = ctx.BindJsonAsync<Message>()

            do! sendMessageToSockets message.Text
            return! next ctx
        }

let handlePostLimitOrder =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! limitOrder = ctx.BindJsonAsync<LimitOrder>()
            do! sendLimitOrderToEngine limitOrder
            return! next ctx
        }

let handlePostMarketOrder =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! marketOrder = ctx.BindJsonAsync<MarketOrder>()
            do! sendMarketOrderToEngine marketOrder
            return! next ctx
        }

let webApp =
    choose [
        GET >=>
            choose [
                route "/" >=> indexHandler "world"
                routef "/hello/%s" indexHandler
            ]
        POST >=>
            choose [
                route "/message" >=> handlePostMessage
                route "/limitOrder" >=> handlePostLimitOrder
                route "/marketOrder" >=> handlePostMarketOrder
            ]
        setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder : CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080")
           .AllowAnyMethod()
           .AllowAnyHeader()
           |> ignore

let configureApp (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IHostingEnvironment>()
    (match env.IsDevelopment() with
    | true  -> app.UseDeveloperExceptionPage()
    | false -> app.UseGiraffeErrorHandler errorHandler)
        .UseWebSockets()
        .UseMiddleware<WebSocketMiddleware>()
        .UseHttpsRedirection()
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(webApp)

let configureServices (services : IServiceCollection) =
    let sp  = services.BuildServiceProvider()
    let env = sp.GetService<IHostingEnvironment>()
    let viewsFolderPath = Path.Combine(env.ContentRootPath, "Views")
    services.AddRazorEngine viewsFolderPath |> ignore
    services.AddCors() |> ignore
    services.AddGiraffe() |> ignore

let configureLogging (builder : ILoggingBuilder) =
    let filter (l : LogLevel) = l.Equals LogLevel.Error
    builder.AddFilter(filter).AddConsole().AddDebug() |> ignore

[<EntryPoint>]
let main _ =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    WebHostBuilder()
        .UseKestrel()
        .UseContentRoot(contentRoot)
        .UseIISIntegration()
        .UseWebRoot(webRoot)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
        .UseUrls("http://0.0.0.0:5000")
        .Build()
        .Run()
    0