namespace Agents

[<Measure>] type second
type Seconds = int<second>
type PerSecond = int<1/second>

module Seconds =
    let fromInt (i:int) : Seconds =
        i * 1<second>
