namespace Agents

open FSharp.Control
open Serilog

type BlockingAgentRunningState =
    { IsWaiting: bool
      WorkRequestedCount: int
      WorkRunningCount: int
      WorkQueuedCount: int
      WorkCompletedCount: int }

type BlockingAgentStatus =
    | Running of BlockingAgentRunningState
    | Done

type BlockingAgentMessage =
    | WorkRequested of Async<unit>
    | WorkCompleted
    | WaitRequested
    | StatusRequested of AsyncReplyChannel<BlockingAgentStatus>

type BlockingAgentState =
    { IsWaiting: bool
      WorkRequestedCount: int
      WorkRunningCount: int
      WorkCompletedCount: int
      WorkQueued: Async<unit> list }

type BlockingAgentMailbox = MailboxProcessor<BlockingAgentMessage>

type BlockingAgent(name: string, limit:int) =
    let initialState =
        { IsWaiting = false
          WorkRequestedCount = 0
          WorkRunningCount = 0
          WorkCompletedCount = 0
          WorkQueued = [] }

    let tryWork (inbox:BlockingAgentMailbox) (state:BlockingAgentState) =
        match state.WorkQueued with
        | [] -> state
        | work :: remainingQueue when state.WorkRunningCount < limit ->
            Async.Start(async {
                do! work
                inbox.Post(WorkCompleted)
            })
            { state with
                WorkRunningCount = state.WorkRunningCount + 1
                WorkQueued = remainingQueue }
        | _ -> state

    let folder (inbox:BlockingAgentMailbox) (state:BlockingAgentState) (msg:BlockingAgentMessage) =
        match msg with
        | WorkRequested work ->
            { state with
                WorkRequestedCount = state.WorkRequestedCount + 1
                WorkQueued = work :: state.WorkQueued }
        | WaitRequested -> 
            { state with
                IsWaiting = true }
        | WorkCompleted ->
            { state with
                WorkRunningCount = state.WorkRunningCount - 1
                WorkCompletedCount = state.WorkCompletedCount + 1 }
        | StatusRequested replyChannel ->
            let isComplete = state.WorkRequestedCount = state.WorkCompletedCount
            if state.IsWaiting && isComplete then 
                replyChannel.Reply(Done)
            else
                let status =
                    { IsWaiting = state.IsWaiting
                      WorkRequestedCount = state.WorkRequestedCount
                      WorkRunningCount = state.WorkRunningCount
                      WorkQueuedCount = state.WorkQueued |> List.length
                      WorkCompletedCount = state.WorkCompletedCount }
                replyChannel.Reply(Running(status))
            state
        |> tryWork inbox

    let agent = BlockingAgentMailbox.Start(fun inbox ->
        AsyncSeq.initInfiniteAsync (fun _ -> inbox.Receive())
        |> AsyncSeq.fold (folder inbox) initialState
        |> Async.Ignore
    )

    let rec wait () =
        async {
            match agent.PostAndReply(StatusRequested) with
            | Done -> ()
            | Running status ->
                Log.Information("[BlockingAgent {Name}] Status: {@Status}", name, status)
                do! Async.Sleep(1000)
                return! wait()
        }

    member __.Post(work:Async<unit>) =
        agent.Post(WorkRequested(work))

    member __.LogStatus() =
        let status = agent.PostAndReply(StatusRequested)
        Log.Information("[BlockingAgent {Name}] Status: {@Status}", name, status)

    member __.Wait() =
        agent.Post(WaitRequested)
        wait()
        |> Async.RunSynchronously
