namespace Typewriter

open System

module Env =
    let getEnv (key:string) (defaultValueOpt:string option) =
        let (|Exists|Empty|) value = 
            if String.IsNullOrEmpty(value) |> not then Exists value
            else Empty
        match Environment.GetEnvironmentVariable(key), defaultValueOpt with
        | Exists value, _ -> value
        | Empty, Some defaultValue -> defaultValue
        | Empty, None -> failwithf "could not find %s" key
