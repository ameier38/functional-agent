namespace Agents

open FSharp.Control
open FSharpx.Collections
open Serilog

type RateAgentWork = unit -> unit

type RateAgentRunningState =
    { IsWaiting: bool
      TokenCount: int
      WorkQueuedCount: int }

type RateAgentStatus =
    | Running of RateAgentRunningState
    | Done

type RateAgentMessage =
    | WorkRequested of RateAgentWork
    | RefillRequested
    | WaitRequested
    | StatusRequested of AsyncReplyChannel<RateAgentStatus>

type RateAgentState =
    { IsWaiting: bool
      TokenCount: int
      WorkQueued: Queue<RateAgentWork> }

type RateAgentMailbox = MailboxProcessor<RateAgentMessage>

type RateAgent(name:string, rateLimit:PerSecond) =

    let initialState =
        { IsWaiting = false
          TokenCount = 1<second> * rateLimit
          WorkQueued = Queue.empty<RateAgentWork> }

    let tryWork (state:RateAgentState) =
        let rec recurse (s:RateAgentState) =
            match s.WorkQueued, s.TokenCount with
            | Queue.Nil, _ -> s
            | _, 0 -> s
            | Queue.Cons (work, remainingQueue), tokenCount ->
                work ()
                let newState =
                    { s with
                        TokenCount = tokenCount - 1
                        WorkQueued = remainingQueue }
                recurse newState
        if state.TokenCount > 0 then recurse state
        else state

    let evolve
        : RateAgentState -> RateAgentMessage -> RateAgentState = 
        fun (state:RateAgentState) (msg:RateAgentMessage) ->
            match msg with
            | WorkRequested work -> 
                { state with
                    WorkQueued = state.WorkQueued.Conj(work) }
            | RefillRequested -> 
                { state with
                    TokenCount = 1<second> * rateLimit }
            | WaitRequested -> 
                { state with
                    IsWaiting = true }
            | StatusRequested replyChannel ->
                if state.IsWaiting && state.WorkQueued.IsEmpty then 
                    replyChannel.Reply(Done)
                else
                    let status =
                        { IsWaiting = state.IsWaiting
                          TokenCount = state.TokenCount
                          WorkQueuedCount = state.WorkQueued.Length }
                    replyChannel.Reply(Running(status))
                state
            |> tryWork

    let agent = RateAgentMailbox.Start(fun inbox ->
        AsyncSeq.initInfiniteAsync (fun _ -> inbox.Receive())
        |> AsyncSeq.fold evolve initialState
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
    
    let rec refill () =
        async {
            do! Async.Sleep(1000)
            agent.Post(RefillRequested)
            return! refill()
        }

    do Async.Start(refill())

    member __.LogStatus() =
        let status = agent.PostAndReply(StatusRequested)
        Log.Information("[RateAgent {Name}] Status: {@Status}", name, status)

    member __.Post(work:RateAgentWork) =
        agent.Post(WorkRequested(work))

    member __.Wait() =
        agent.Post(WaitRequested)
        wait()
        |> Async.RunSynchronously
