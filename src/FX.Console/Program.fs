open System

open FsharpExchangeDotNetStandard

[<EntryPoint>]
let main argv =
    let ex = new Exchange(Persistence.Redis)
    Console.WriteLine("exchange started!: " + ex.GetHashCode().ToString())
    0 // return an integer exit code
