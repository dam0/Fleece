﻿module Fleece

open System
open System.Globalization
open System.Json
open System.Collections.Generic
open ReadOnlyCollectionsExtensions

let (|JArray|JObject|JNumber|JBool|JString|JNull|) (o: JsonValue) =
    match o with
    | :? JsonArray as x ->
        let values = (x :> JsonValue IList).AsReadOnlyList()
        JArray values
    | :? JsonObject as x ->
        let values = (x :> IDictionary<string, JsonValue>).AsReadOnlyDictionary()
        JObject values
    | :? JsonPrimitive as x ->
        match x.JsonType with
        | JsonType.Number -> JNumber (x.ReadAs<decimal>())
        | JsonType.Boolean -> JBool (x.ReadAs<bool>())
        | JsonType.String -> JString (x.ReadAs<string>())
        | JsonType.Default -> JNull
        | _ -> failwithf "Invalid JsonType %A for primitive %A" x.JsonType x
    | null -> JNull
    | _ -> failwithf "Invalid JsonValue %A" o


let inline JArray (x: JsonValue IReadOnlyList) = JsonArray x :> JsonValue
let inline JObject (x: IReadOnlyDictionary<string, JsonValue>) = JsonObject x :> JsonValue
let inline JNumber (x: decimal) = JsonPrimitive x :> JsonValue
let inline JBool (x: bool) = JsonPrimitive x :> JsonValue
let inline JString (x: string) = JsonPrimitive x :> JsonValue
let JNull = JsonPrimitive("").ValueOrDefault(0)


open FsControl
open FsControl.Core
open FSharpPlus

let inline Success x = Choice1Of2 x
let inline Failure x = Choice2Of2 x

type 'a ChoiceS = Choice<'a, string>

type FromJSON = FromJSON with
    static member instance (FromJSON, _: bool, _: bool ChoiceS) = fun (x: JsonValue) -> 
        match x with
        | JBool b -> Success b
        | a -> Failure (sprintf "Expected bool, actual %A" a)

    static member instance (FromJSON, _: string, _: string ChoiceS) = fun (x: JsonValue) -> 
        match x with
        | JString b -> Success b
        | JNull -> Success null
        | a -> Failure (sprintf "Expected string, actual %A" a)

    static member instance (FromJSON, _: decimal, _: decimal ChoiceS) = fun (x: JsonValue) ->
        match x with
        | JNumber b -> Success b
        | a -> Failure (sprintf "Expected decimal, actual %A" a)

    static member instance (FromJSON, _: int, _: int ChoiceS) = fun (x: JsonValue) -> 
        match x with
        | JNumber b ->
            if Decimal.Truncate b = b then
                try
                    Success (int b)
                with
                | :? OverflowException -> Failure ("Int overflow: " + b.ToString(CultureInfo.InvariantCulture))
            else
                Failure ("Invalid int " + b.ToString(CultureInfo.InvariantCulture))
        | a -> Failure (sprintf "Expected int, actual %A" a)

let inline fromJSON (x: JsonValue) : 'a ChoiceS = Inline.instance (FromJSON, Unchecked.defaultof<'a>) x

let inline parseJSON (x: string) : 'a ChoiceS =
    try
        let json = JsonValue.Parse x
        fromJSON json
    with e -> Failure (e.ToString())

let inline (.>) (o: IReadOnlyDictionary<string, JsonValue>) key = 
    match o.TryGetValue key with
    | true, value -> fromJSON value
    | _ -> Failure ("Key '" + key + "' not found in " + JObject(o).ToString())

let inline private tuple2 x y = x,y
let inline private tuple3 x y z = x,y,z

type FromJSON with
    static member inline instance (FromJSON,  _: 'a array, _: 'a array ChoiceS) = fun (x: JsonValue) ->
        match x with
        | JArray a -> 
            let xx : 'a ChoiceS seq = Seq.map fromJSON a
            sequenceA xx |> map Seq.toArray
        | a -> Failure (sprintf "Expected array, found %A" a)

    static member inline instance (FromJSON,  _: 'a list, _: 'a list ChoiceS) = fun (x: JsonValue) ->
        match x with
        | JArray a -> 
            let xx : 'a ChoiceS seq = Seq.map fromJSON a
            sequenceA xx |> map Seq.toList
        | a -> Failure (sprintf "Expected array, found %A" a)

    static member inline instance (FromJSON, _: 'a * 'b, _: ('a * 'b) ChoiceS) = fun (x: JsonValue) ->
        match x with
        | JArray a ->
            if a.Count <> 2 then
                Failure ("Expected array with 2 items, was: " + x.ToString())
            else
                tuple2 <!> (fromJSON a.[0]) <*> (fromJSON a.[1])
        | a -> Failure (sprintf "Expected array, found %A" a)

    static member inline instance (FromJSON, _: 'a * 'b * 'c, _: ('a * 'b * 'c) ChoiceS) = fun (x: JsonValue) ->
        match x with
        | JArray a ->
            if a.Count <> 3 then
                Failure ("Expected array with 3 items, was: " + x.ToString())
            else
                tuple3 <!> (fromJSON a.[0]) <*> (fromJSON a.[1]) <*> (fromJSON a.[2])
        | a -> Failure (sprintf "Expected array, found %A" a)
