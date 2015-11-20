// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#I "packages/FAKE/tools"
#r "packages/FAKE/tools/FakeLib.dll"
open System
open System.Diagnostics
open System.IO
open Fake
open Fake.Git
open Fake.ProcessHelper
open Fake.ReleaseNotesHelper
open Fake.ZipHelper

#if MONO
#else
#load "src/vscode-bindings.fsx"
#load "src/paket.fs"
#load "src/main.fs"
#endif


// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "ionide"
let gitHome = "https://github.com/" + gitOwner


// The name of the project on GitHub
let gitName = "ionide-vscode-paket"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/ionide"


// Read additional information from the release notes document
let releaseNotesData =
    File.ReadAllLines "RELEASE_NOTES.md"
    |> parseAllReleaseNotes

let release = List.head releaseNotesData

let msg =  release.Notes |> List.fold (fun r s -> r + s + "\n") ""
let releaseMsg = (sprintf "Release %s\n" release.NugetVersion) + msg


let run cmd args dir =
    if execProcess( fun info ->
        info.FileName <- cmd
        if not( String.IsNullOrWhiteSpace dir) then
            info.WorkingDirectory <- dir
        info.Arguments <- args
    ) System.TimeSpan.MaxValue = false then
        traceError <| sprintf "Error while running '%s' with args: %s" cmd args

let npmTool =
    match isUnix with
    | true -> "/usr/local/bin/npm"
    | _ -> __SOURCE_DIRECTORY__ </> "packages/Npm.js/tools/npm.cmd"
    
let vsceTool =
    #if MONO
        "vsce"
    #else
        // TODO: Detect where vsce lives
        @"C:\Users\Steffen\AppData\Roaming\npm" </> "vsce.cmd"
    #endif
    

// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

Target "Clean" (fun _ ->
    CleanDir "./temp"
    CopyFiles "release" ["README.md"; "LICENSE.md"; "RELEASE_NOTES.md"]
)

#if MONO
Target "BuildGenerator" (fun () ->
    [ __SOURCE_DIRECTORY__ </> "src" </> "Ionide.Paket.fsproj" ]
    |> MSBuildDebug "" "Rebuild"
    |> Log "AppBuild-Output: "
)

Target "RunGenerator" (fun () ->
    (TimeSpan.FromMinutes 5.0)
    |> ProcessHelper.ExecProcess (fun p ->
        p.FileName <- __SOURCE_DIRECTORY__ </> "src" </> "bin" </> "Debug" </> "Ionide.Paket.exe" )
    |> ignore
)
#else
Target "RunScript" (fun () ->
    Ionide.VSCode.Generator.translateModules typeof<Ionide.VSCode.Paket> (".." </> "release" </> "paket.js")
)
#endif

Target "InstallVSCE" ( fun _ ->
    killProcess "npm"
    run npmTool "install -g vsce" ""
)

Target "BuildPackage" ( fun _ ->
    killProcess "vsce"
    run vsceTool "package" "release"
    !! "release/*.vsix"
    |> Seq.iter(MoveFile "./temp/")
)

Target "LoginToVSCE" ( fun _ ->
    let publisher =
        match getBuildParam "vsce-publisher" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "VSCE Publisher: "
        
    let token =
        match getBuildParam "vsce-token" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "VSCE Token: "
        
    killProcess "vsce"
    run vsceTool (sprintf "login %s -pat %s" publisher token) "release"
)

Target "PublishToGallery" ( fun _ -> 
    killProcess "vsce"
    run vsceTool "publish" "release"
)

// --------------------------------------------------------------------------------------
// Run generator by default. Invoke 'build <Target>' to override
// --------------------------------------------------------------------------------------

Target "Default" DoNothing
Target "Release" DoNothing

#if MONO
"Clean"
    ==> "BuildGenerator"
    ==> "RunGenerator"
    ==> "Default"
#else
"Clean"
    ==> "RunScript"
    ==> "Default"
#endif

"Default"
  ==> "InstallVSCE"
  ==> "BuildPackage"
  // ==> "LoginToVSCE"
  ==> "PublishToGallery"
  ==> "Release"
RunTargetOrDefault "Default"
