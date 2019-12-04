namespace Agents

open FSharp.Control
open FSharpx.Collections
open Serilog

type ParallelAgentRunningState =
    { IsWaiting: bool
      WorkRequestedCount: int
      WorkRunningCount: int
      WorkQueuedCount: int
      WorkCompletedCount: int }

type ParallelAgentStatus =
    | Running of ParallelAgentRunningState
    | Done

type ParallelAgentMessage =
    | WorkRequested of Async<unit>
    | WorkCompleted
    | WaitRequested
    | StatusRequested of AsyncReplyChannel<ParallelAgentStatus>

type ParallelAgentState =
    { IsWaiting: bool
      WorkRequestedCount: int
      WorkRunningCount: int
      WorkCompletedCount: int
      WorkQueued: Queue<Async<unit>> }

type ParallelAgentMailbox = MailboxProcessor<ParallelAgentMessage>

type ParallelAgent(name: string, limit:int) =
    let initialState =
        { IsWaiting = false
          WorkRequestedCount = 0
          WorkRunningCount = 0
          WorkCompletedCount = 0
          WorkQueued = Queue.empty<Async<unit>> }

    let tryWork (inbox:ParallelAgentMailbox) (state:ParallelAgentState) =
        match state.WorkQueued with
        | Queue.Nil -> state
        | Queue.Cons (work, remainingQueue) when state.WorkRunningCount < limit ->
            Async.Start(async {
                do! work
                inbox.Post(WorkCompleted)
            })
            { state with
                WorkRunningCount = state.WorkRunningCount + 1
                WorkQueued = remainingQueue }
        | _ -> state

    let evolve (inbox:ParallelAgentMailbox) 
        : ParallelAgentState -> ParallelAgentMessage -> ParallelAgentState =
        fun (state:ParallelAgentState) (msg:ParallelAgentMessage) ->
            match msg with
            | WorkRequested work ->
                { state with
                    WorkRequestedCount = state.WorkRequestedCount + 1
                    WorkQueued = state.WorkQueued.Conj(work) }
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
                          WorkQueuedCount = state.WorkQueued.Length
                          WorkCompletedCount = state.WorkCompletedCount }
                    replyChannel.Reply(Running(status))
                state
            |> tryWork inbox

    let agent = ParallelAgentMailbox.Start(fun inbox ->
        AsyncSeq.initInfiniteAsync (fun _ -> inbox.Receive())
        |> AsyncSeq.fold (evolve inbox) initialState
        |> Async.Ignore
    )

    let rec wait () =
        async {
            match agent.PostAndReply(StatusRequested) with
            | Done -> ()
            | Running status ->
                Log.Information("[ParallelAgent {Name}] Status: {@Status}", name, status)
                do! Async.Sleep(1000)
                return! wait()
        }

    member __.Post(work:Async<unit>) =
        agent.Post(WorkRequested(work))

    member __.LogStatus() =
        let status = agent.PostAndReply(StatusRequested)
        Log.Information("[ParallelAgent {Name}] Status: {@Status}", name, status)

    member __.Wait() =
        agent.Post(WaitRequested)
        wait()
        |> Async.RunSynchronously
