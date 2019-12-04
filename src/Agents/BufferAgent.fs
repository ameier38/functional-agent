namespace Agents

open FSharp.Control
open Serilog

type BufferAgentRunningState =
    { IsWaiting: bool
      BufferSize: int
      ProcessBufferRequestedCount: int
      ProcessBufferCompletedCount: int }

type BufferAgentStatus =
    | Running of BufferAgentRunningState
    | Done

type BufferAgentMessage<'I> =
    | ItemReceived of 'I
    | WaitRequested
    | ProcessBufferCompleted
    | StatusRequested of AsyncReplyChannel<BufferAgentStatus>

type BufferAgentState<'I> =
    { IsWaiting: bool
      Buffer: 'I list 
      ProcessBufferRequestedCount: int
      ProcessBufferCompletedCount: int }

type BufferAgentMailbox<'I> = MailboxProcessor<BufferAgentMessage<'I>>

type BufferAgent<'I>(name:string, bufferSize:int,processBuffer:'I list -> Async<unit>) =
    let initialState =
        { IsWaiting = false
          Buffer = []
          ProcessBufferRequestedCount = 0
          ProcessBufferCompletedCount = 0 }

    let tryProcessBuffer (inbox:BufferAgentMailbox<'I>) (state:BufferAgentState<'I>) =
        match state.Buffer with
        | [] -> state
        | buffer when ((buffer |> List.length) >= bufferSize) || state.IsWaiting ->
            Async.Start(async {
                do! processBuffer buffer
                inbox.Post(ProcessBufferCompleted)
            })
            { state with
                Buffer = []
                ProcessBufferRequestedCount = state.ProcessBufferRequestedCount + 1 }
        | _ -> state
    
    let evolve (inbox:BufferAgentMailbox<'I>)
        : BufferAgentState<'I> -> BufferAgentMessage<'I> -> BufferAgentState<'I> =
        fun (state:BufferAgentState<'I>) (msg:BufferAgentMessage<'I>) ->
            match msg with
            | ItemReceived item -> 
                { state with
                    Buffer = item :: state.Buffer }
            | WaitRequested -> 
                { state with
                    IsWaiting = true }
            | ProcessBufferCompleted -> 
                { state with
                    ProcessBufferCompletedCount = state.ProcessBufferCompletedCount + 1 }
            | StatusRequested replyChannel ->
                let isComplete = state.ProcessBufferRequestedCount = state.ProcessBufferCompletedCount
                if state.IsWaiting && isComplete then
                    replyChannel.Reply(Done)
                else
                    let status =
                        { IsWaiting = state.IsWaiting
                          BufferSize = state.Buffer |> List.length
                          ProcessBufferRequestedCount = state.ProcessBufferRequestedCount
                          ProcessBufferCompletedCount = state.ProcessBufferCompletedCount }
                    replyChannel.Reply(Running(status))
                state
            |> tryProcessBuffer inbox

    let agent = BufferAgentMailbox.Start(fun inbox ->
        AsyncSeq.initInfiniteAsync(fun _ -> inbox.Receive())
        |> AsyncSeq.fold (evolve inbox) initialState
        |> Async.Ignore
    )

    let rec wait () =
        async {
            match agent.PostAndReply(StatusRequested) with
            | Done -> ()
            | Running status ->
                Log.Information("[BufferAgent {Name}] Status: {@Status}", name, status)
                do! Async.Sleep(1000)
                return! wait()
        }

    member __.Post(item:'I) =
        agent.Post(ItemReceived(item))

    member __.LogStatus() =
        let status = agent.PostAndReply(StatusRequested)
        Log.Information("[BufferAgent {Name}] Status: {@Status}", name, status)

    member __.Wait() =
        agent.Post(WaitRequested)
        wait()
        |> Async.RunSynchronously
