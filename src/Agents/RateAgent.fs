namespace Agents

open FSharp.Control
open FSharpx.Collections
open Serilog

type RateAgentRunningState =
    { IsWaiting: bool
      TokenCount: int
      WorkRequestedCount: int
      WorkCompletedCount: int
      WorkQueuedCount: int }

type RateAgentStatus =
    | Running of RateAgentRunningState
    | Done

type RateAgentMessage =
    | WorkRequested of Async<unit>
    | WorkCompleted
    | RefillRequested
    | WaitRequested
    | StatusRequested of AsyncReplyChannel<RateAgentStatus>

type RateAgentState =
    { IsWaiting: bool
      TokenCount: int
      WorkRequestedCount: int
      WorkCompletedCount: int
      WorkQueued: Queue<Async<unit>> }

type RateAgentMailbox = MailboxProcessor<RateAgentMessage>

type RateAgent(name:string, rateLimit:PerSecond) =

    let initialState =
        { IsWaiting = false
          TokenCount = 1<second> * rateLimit
          WorkRequestedCount = 0
          WorkCompletedCount = 0
          WorkQueued = Queue.empty<Async<unit>> }

    let tryWork (inbox:RateAgentMailbox) (state:RateAgentState) =
        let rec recurse (s:RateAgentState) =
            match s.WorkQueued, s.TokenCount with
            | Queue.Nil, _ -> s
            | _, 0 -> s
            | Queue.Cons (work, remainingQueue), tokenCount ->
                Async.Start(async {
                    do! work
                    inbox.Post(WorkCompleted)
                })
                let newState =
                    { s with
                        TokenCount = tokenCount - 1
                        WorkQueued = remainingQueue }
                recurse newState
        recurse state

    let folder (inbox:RateAgentMailbox) (state:RateAgentState) (msg:RateAgentMessage) =
        match msg with
        | WorkRequested work -> 
            { state with
                WorkRequestedCount = state.WorkRequestedCount + 1
                WorkQueued = state.WorkQueued.Conj(work) }
        | WorkCompleted -> 
            { state with
                WorkCompletedCount = state.WorkCompletedCount + 1 }
        | RefillRequested -> 
            { state with
                TokenCount = 1<second> * rateLimit }
        | WaitRequested -> 
            { state with
                IsWaiting = true }
        | StatusRequested replyChannel ->
            let isComplete = state.WorkRequestedCount = state.WorkCompletedCount
            if state.IsWaiting && isComplete then 
                replyChannel.Reply(Done)
            else
                let status =
                    { IsWaiting = state.IsWaiting
                      TokenCount = state.TokenCount
                      WorkRequestedCount = state.WorkRequestedCount
                      WorkCompletedCount = state.WorkCompletedCount
                      WorkQueuedCount = state.WorkQueued.Length }
                replyChannel.Reply(Running(status))
            state
        |> tryWork inbox

    let agent = RateAgentMailbox.Start(fun inbox ->
        AsyncSeq.initInfiniteAsync (fun _ -> inbox.Receive())
        |> AsyncSeq.fold (folder inbox) initialState
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

    member __.Post(work:Async<unit>) =
        agent.Post(WorkRequested(work))

    member __.Wait() =
        agent.Post(WaitRequested)
        wait()
        |> Async.RunSynchronously
