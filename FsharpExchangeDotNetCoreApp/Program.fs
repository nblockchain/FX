open System

open FsharpExchangeDotNetStandard

[<EntryPoint>]
let main argv =
    let ex = new Exchange()
    Console.WriteLine("exchange started!: " + ex.GetHashCode().ToString())
    0 // return an integer exit code
