namespace Typewriter

type SeqConfig =
    { Url: string } with
    static member Load() =
        let protocol = Some "http" |> Env.getEnv "SEQ_PROTOCOL"
        let host = Some "localhost" |> Env.getEnv "SEQ_HOST"
        let port = Some "5341" |> Env.getEnv "SEQ_PORT"
        { Url = sprintf "%s://%s:%s" protocol host port }

type Config =
    { SeqConfig: SeqConfig } with
    static member Load() =
        { SeqConfig = SeqConfig.Load() }
