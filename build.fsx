#load ".fake/build.fsx/intellisense.fsx"
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
open BlackFox.Fake

let solution = "FunctionalAgent.sln"
let printer = "src" </> "Printer" </> "Printer.fsproj"

let clean = BuildTask.create "Clean" [] {
    !! "src/**/bin"
    ++ "src/**/obj"
    ++ "src/**/out"
    |> Shell.cleanDirs 
}

BuildTask.create "Restore" [] {
    DotNet.restore id solution
}

BuildTask.create "Build" [clean] {
    DotNet.build id solution
}

BuildTask.create "Publish" [clean] {
    DotNet.publish 
        (fun opts -> 
            { opts with 
                OutputPath = Some ("src" </> "Printer" </> "out")
                Configuration = DotNet.BuildConfiguration.Release })
        printer
}

let _default = BuildTask.createEmpty "Default" []

BuildTask.runOrDefault _default