namespace GrpcModels

open System.Text.Json
open System.Text.Json.Serialization

open FsharpExchangeDotNetStandard

module ModelSerialization =
    type MatchTypeConverter() =
        inherit JsonConverter<Match>()

        override this.Read(reader, _typeToConvert, _options) =
            if reader.TokenType <> JsonTokenType.StartObject then
                raise <| JsonException()
            
            // "Type" key
            reader.Read() |> ignore
            if reader.TokenType <> JsonTokenType.PropertyName || reader.GetString() <> "Type" then
                raise <| JsonException()
            // "Type" value
            reader.Read() |> ignore
            match reader.GetString() with
            | "Full" ->
                reader.Read() |> ignore
                if reader.TokenType <> JsonTokenType.EndObject then
                    raise <| JsonException()
                Match.Full
            | "Partial" -> 
                // "Amount" key
                reader.Read() |> ignore
                if reader.TokenType <> JsonTokenType.PropertyName || reader.GetString() <> "Amount" then
                    raise <| JsonException()
                // "Amount" value
                reader.Read() |> ignore
                if reader.TokenType <> JsonTokenType.Number then
                    raise <| JsonException()
                let amount = reader.GetDecimal()
                reader.Read() |> ignore
                if reader.TokenType <> JsonTokenType.EndObject then
                    raise <| JsonException()
                Match.Partial amount
            | typeName -> raise <| JsonException("Unknown Match type: " + typeName)

        override this.Write(writer, value, _options ) =
            writer.WriteStartObject()
            match value with
            | Full ->
                writer.WriteString("Type", "Full")
            | Partial amount ->
                writer.WriteString("Type", "Partial")
                writer.WriteNumber("Amount", amount)
            writer.WriteEndObject()

    let serializationOptions = 
        let options = JsonSerializerOptions(Redis.Serialization.serializationOptions)
        options.Converters.Add(MatchTypeConverter())
        options
