open Argu
open FSharp.Control
open Agents
open Serilog
open System
open System.IO
open Typewriter

type Arguments =
    | [<AltCommandLine("-r")>] Rate_Limit of int
    | [<AltCommandLine("-p")>] Parallel_Limit of int
    | [<AltCommandLine("-b")>] Buffer_Size of int
    | [<AltCommandLine("-f")>] File_Path of string
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Rate_Limit _ -> "specify the limit on the number of keys pressed per second"
            | Parallel_Limit _ -> "specify the number of keys to process in parallel"
            | Buffer_Size _ -> "specify the number of keys to process before writing to console"
            | File_Path _ -> "specify the path of the file in which to write buffer"

type Typewriter(rateLimit:int<1/second>, parallelLimit:int, bufferSize:int, filePath:string) =
    let processBuffer (buffer:char list) =
        async {
            do! Async.Sleep(5000)
            let line =
                buffer 
                |> List.rev
                |> List.toArray
                |> String
            File.AppendAllLines(filePath, [line])
        }
    
    let rateAgent = RateAgent("Type", rateLimit)
    let parallelAgent = ParallelAgent("Type", parallelLimit)
    let bufferAgent = BufferAgent("Print", bufferSize, processBuffer)

    member __.Write(keyInfo:ConsoleKeyInfo) =
        match keyInfo.Key, keyInfo.Modifiers with
        | ConsoleKey.Enter, ConsoleModifiers.Control ->
            printfn "received wait"
            rateAgent.Wait()
            parallelAgent.Wait()
            bufferAgent.Wait()
            exit 0
        | ConsoleKey.Enter, _ ->
            rateAgent.LogStatus()
            parallelAgent.LogStatus()
            bufferAgent.LogStatus()
        | _ ->
            let work = async {
                do! Async.Sleep(1000)
                bufferAgent.Post(keyInfo.KeyChar)
            }
            rateAgent.Post(fun () ->
                Console.Write(keyInfo.KeyChar)
                parallelAgent.Post(work)
            )

let readKeys () =
    printfn """
Begin typing.
Press <Enter> to log the status of agents.
Press <Ctrl-Enter> to wait for the typewriter to complete.
Press <Esc> to exit without waiting.
Press <Ctrl-C> to force exit.
"""
    fun _ -> Console.ReadKey(true)
    |> Seq.initInfinite
    |> Seq.takeWhile (fun keyInfo -> keyInfo.Key <> ConsoleKey.Escape)

[<EntryPoint>]
let main argv =
    Log.Logger <- LoggerConfiguration()
        .WriteTo.Console()
        .CreateLogger()
    let parser = ArgumentParser.Create<Arguments>()
    let args = parser.ParseCommandLine(argv)
    let rateLimit = args.GetResult Rate_Limit
    let rateLimitPerSecond = rateLimit * 1</second>
    let parallelLimit = args.GetResult Parallel_Limit
    let bufferSize = args.GetResult Buffer_Size
    let filePath = args.GetResult File_Path
    let typewriter = Typewriter(rateLimitPerSecond, parallelLimit, bufferSize, filePath)
    readKeys() |> Seq.iter typewriter.Write
    0
