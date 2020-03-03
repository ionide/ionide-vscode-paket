[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.PaketService

open System
open System.Text.RegularExpressions

open Fable.Core
open Fable.Import
open Fable.Import.Node
open Fable.Import.Node.ChildProcess
open Fable.Core.JsInterop

open Ionide.VSCode
open Fable.Import.vscode
open Ionide.VSCode.Helpers
let (</>) a b =
    if Process.isWin ()
    then a + @"\" + b
    else a + "/" + b


let isProject (fileName:string) = fileName.EndsWith(".fsproj") || fileName.EndsWith(".csproj") || fileName.EndsWith(".vbproj")


// See https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools#install-a-global-tool for Windows and Linux/macOS values
let globalToolPath  =
    let getGlobalToolSubPath root =
        root </> ".dotnet" </> "tools"
    let pathOption  =
        match Process.isWin() with
        | true  ->
            Globals.``process``.env?``USERPROFILE``
            |> unbox<string>
            |> Option.ofObj
            |> Option.map getGlobalToolSubPath
        | false  ->
            Globals.``process``.env?``HOME``
            |> unbox<string>
            |> Option.ofObj
            |> Option.map getGlobalToolSubPath
    pathOption

let potentialDirectories =
    [
        vscode.workspace.rootPath
        vscode.workspace.rootPath </> ".paket"
    ]

let findBinary name =
    potentialDirectories
    |> List.map (fun dir -> dir </> name)
    |> List.tryFind (U2.Case1 >> fs.existsSync)

type PaketType =
    | Old of string
    | Global
    | LocalTool

let getPaketPath () =
    let checkGlobal () =
        match globalToolPath with
        | Some toolPath ->
            let fn = if Process.isWin () then "paket.exe" else "paket"
            let path = toolPath </> fn
            if fs.existsSync !^ path then
                Some Global
            else
                None
        | None ->
            None

    match findBinary "paket.exe" with
    | Some s -> Some (Old s) // The paket.exe in workspace
    | None ->
        let configPath = vscode.workspace.rootPath </> ".config" </> "dotnet-tools.json"
        if fs.existsSync !^ configPath then
            let file = (fs.readFileSync configPath).toString()
            if file.Contains "paket" then
                Some LocalTool
            else
                checkGlobal ()
        else
            checkGlobal ()

let getBootstrapperPath () = findBinary "paket.bootstrapper.exe"

let outputChannel = vscode.window.createOutputChannel "Paket"

let getConfig () =
    let cfg = vscode.workspace.getConfiguration()
    cfg.get ("Paket.autoshow", true)

let UpdatePaketIfNeeded () =
    match getBootstrapperPath (), getPaketPath () with
    | Some bootstrapperPath, _ ->
        outputChannel.appendLine ("Paket bootstrapper exists at " + bootstrapperPath)
        Process.spawnWithNotification bootstrapperPath "mono" "" outputChannel
        |> Process.toPromise
    | None, Some (Old paketPath) ->
        outputChannel.appendLine ("Paket is in magic mode. Location: " + paketPath)
        Promise.empty
    | None, Some (Global) ->
        outputChannel.appendLine ("Paket is used as global tool.")
        Promise.empty
    | None, Some (LocalTool) ->
        outputChannel.appendLine ("Paket is used as local tool.")
        Promise.empty
    | None, None ->
        window.showErrorMessage ("Neither Paket nor its bootstrapper were found. Install paket as local or global tool with `dotnet` CLI")
        |> Promise.bind (fun _ -> Promise.empty)

let runWithPaketLocation oldHandler globalHandler localHandler =
    match getPaketPath () with
    | Some (Old location) ->
        oldHandler location
    | Some LocalTool ->
        localHandler ()
    | Some Global ->
        globalHandler ()
    | None ->
        vscode.window.showErrorMessage "Unable to find Paket"
        |> Promise.bind (fun _ -> Promise.reject "Unable to find Paket")

let private spawnPaket cmd =
    if isNull workspace.rootPath then
        window.showErrorMessage("Paket can be run only if folder is open")
        |> ignore
    else
        UpdatePaketIfNeeded ()
        |> Promise.bind (fun _ ->
            runWithPaketLocation
                (fun location ->
                    outputChannel.clear ()
                    outputChannel.appendLine (location)
                    let startedMessage = vscode.window.setStatusBarMessage "Paket started"
                    if getConfig () then outputChannel.show ()

                    Process.spawnWithNotification location "mono" cmd outputChannel
                    |> Process.onExit(fun code _ ->
                        startedMessage.dispose() |> ignore
                        if code.ToString() ="0" then
                            vscode.window.setStatusBarMessage ("Paket completed", 10000.0) |> ignore
                        else
                            vscode.window.showErrorMessage("Paket failed", "Show")
                            |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                            |> ignore)
                    |> ignore
                    Promise.empty)
                (fun _ ->
                    outputChannel.clear ()
                    outputChannel.appendLine ("Running paket as global tool")
                    let startedMessage = vscode.window.setStatusBarMessage "Paket started"
                    if getConfig () then outputChannel.show ()

                    Process.spawnWithNotification "paket" "" cmd outputChannel
                    |> Process.onExit(fun code _ ->
                        startedMessage.dispose() |> ignore
                        if code.ToString() ="0" then
                            vscode.window.setStatusBarMessage ("Paket completed", 10000.0) |> ignore
                        else
                            vscode.window.showErrorMessage("Paket failed", "Show")
                            |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                            |> ignore)
                    |> ignore
                    Promise.empty
                )
                (fun _ ->
                    outputChannel.clear ()
                    outputChannel.appendLine ("Running paket as local tool")
                    let startedMessage = vscode.window.setStatusBarMessage "Paket started"
                    if getConfig () then outputChannel.show ()

                    Process.spawnWithNotification "dotnet" "" ("paket " + cmd) outputChannel
                    |> Process.onExit(fun code _ ->
                        startedMessage.dispose() |> ignore
                        if code.ToString() ="0" then
                            vscode.window.setStatusBarMessage ("Paket completed", 10000.0) |> ignore
                        else
                            vscode.window.showErrorMessage("Paket failed", "Show")
                            |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                            |> ignore)
                    |> ignore
                    Promise.empty)
        ) |> ignore

let private execPaket cmd = promise {
    if isNull workspace.rootPath |> not then
        let! _ = UpdatePaketIfNeeded ()
        return! runWithPaketLocation
                    (fun location -> Process.exec location "mono" cmd)
                    (fun _ -> Process.exec "paket" "" cmd)
                    (fun _ -> Process.exec "dotnet" "" ("paket " + cmd))
    else
        window.showErrorMessage("Paket can be run only if folder is open")
        |> ignore
        return! Promise.reject "Paket can be run only if folder is open"
}

let private handlePaketList (error : ChildProcess.ExecError option, stdout : string, stderr : string) =
    if(stdout = "") then
        [||]
    else
        stdout.Split('\n')
        |> Array.filter((<>) "" )

let Init () = "init" |> spawnPaket
let GenerateLoadScripts () = "generate-load-scripts" |> spawnPaket
let Update () = "update" |> spawnPaket
let Install () = "install" |> spawnPaket
let Outdated () = "outdated" |> spawnPaket
let Restore () = "restore" |> spawnPaket
let AutoRestoreOn () = "auto-restore on" |> spawnPaket
let AutoRestoreOff () = "auto-restore off" |> spawnPaket
let ConvertFromNuget () = "convert-from-nuget" |> spawnPaket
let Simplify () = "simplify" |> spawnPaket

let inputOptions = createEmpty<vscode.InputBoxOptions>

let Add () =
    (vscode.window.showInputBox inputOptions)
    |> Promise.map (fun n ->
        if JS.isDefined n then sprintf "add nuget %s" n  |> spawnPaket)
    |> ignore

let Why () =
    (vscode.window.showInputBox inputOptions)
    |> Promise.map (fun n ->
        if JS.isDefined n then sprintf "why %s" n  |> spawnPaket)
    |> ignore

let AddToCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if isProject fn then
        (vscode.window.showInputBox inputOptions)
        |> Promise.map (fun n ->
            if JS.isDefined n then sprintf "add nuget %s project \"%s\"" n fn |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "project file needs to be opened" |> ignore

let UpdateGroup () =
    "show-groups -s"
    |> execPaket
    |> Promise.map (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Promise.map (fun n ->
        if JS.isDefined n then sprintf "update group %s" n |> spawnPaket)
    |> ignore

let UpdatePackage () =
    "show-installed-packages -s"
    |> execPaket
    |> Promise.map (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Promise.map (fun n ->
        if JS.isDefined n then
            let group = n.Split(' ').[0].Trim()
            let name = n.Split(' ').[1].Trim()
            sprintf "update nuget %s group %s" name group |> spawnPaket)
    |> ignore

let UpdatePackageCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if isProject fn then
        "show-installed-packages -s"
        |> execPaket
        |> Promise.map (handlePaketList)
        |> (unbox >> vscode.window.showQuickPick)
        |> Promise.map (fun n ->
            if JS.isDefined n then
                let group = n.Split(' ').[0].Trim()
                let name = n.Split(' ').[1].Trim()
                sprintf "update nuget %s project \"%s\" group %s" name fn group |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "project file needs to be opened" |> ignore

let RemovePackage () =
    "show-installed-packages -s"
    |> execPaket
    |> Promise.map (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Promise.map (fun (n :string) ->
        if JS.isDefined n then
            let group = n.Split(' ').[0].Trim()
            let name = n.Split(' ').[1].Trim()
            sprintf "remove nuget %s group %s" name group |> spawnPaket)
    |> ignore

let RemovePackageCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if isProject fn then
        "show-installed-packages -s"
        |> execPaket
        |> Promise.map (handlePaketList)
        |> (unbox >> vscode.window.showQuickPick)
        |> Promise.map (fun n ->
            if JS.isDefined n then
                let group = n.Split(' ').[0].Trim()
                let name = n.Split(' ').[1].Trim()
                sprintf "remove nuget %s project \"%s\" group %s" name fn group |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "project file needs to be opened" |> ignore


let private createDependenciesProvider () =
    {   new CompletionItemProvider
        with
            member this.provideCompletionItems(doc, pos, ct) =
                promise {
                    let range = doc.getWordRangeAtPosition pos
                    let line = doc.getText( Range(pos.line,0.,pos.line,1000.) )
                    let tags = line.Split(' ') |> Array.filter ((<>) "") |> Array.toList
                    let word = doc.getText range
                    let! response =
                        let isRangeDefined = JS.isDefined range
                        let (|PaketTag|_|) items =
                            match items, isRangeDefined with
                            | [ word; _ ], true -> Some(PaketTag word)
                            | _ -> None
                        let concatAndLift = String.concat "\n" >> Promise.lift
                        match tags with
                        | [ _ ] ->
                            ["nuget"; "git"; "github"; "http"; "gist"; "clitool"; "versions"; "source"; "version"; "group"
                             "references: strict"; "framework:"; "content: none"; "copy_content_to_output_dir: always"
                             "import_targets:"; "copy_local:"; "redirects:"; "strategy:"; "storage:"; "lowest_matching:"; "generate_load_scripts"
                             "restriction:" ]
                            |> concatAndLift
                        // | PaketTag "nuget" -> send word
                        | PaketTag "source" -> [ "https://api.nuget.org/v3/index.json"; "https://nuget.org/api/v2" ] |> concatAndLift
                        | PaketTag "framework:" ->
                            [
                                "net35"
                                "net40"
                                "net472"
                                "netstandard2.0"
                                "netstandard2.1"
                                "netcoreapp2.1"
                                "netcoreapp3.0"
                                "netcoreapp3.1"
                                "uap10.0"
                                "auto-detect" ]
                                |> concatAndLift
                        | PaketTag "redirects:" -> [ "on"; "off"; "force" ] |> concatAndLift
                        | PaketTag "strategy:" -> [ "min"; "max" ] |> concatAndLift
                        | PaketTag "storage:" -> [ "none"; "symlink"; "packages" ] |> concatAndLift
                        | PaketTag "generate_load_scripts:" -> [ "true"; "false" ] |> concatAndLift
                        | PaketTag "lowest_matching:" -> [ "true"; "false" ] |> concatAndLift
                        | PaketTag "import_targets:" -> [ "true"; "false" ] |> concatAndLift
                        | PaketTag "copy_local:" -> [ "true"; "false" ] |> concatAndLift
                        | _ -> Promise.lift ""
                    return
                        response.Split '\n'
                        |> Seq.map(fun n -> CompletionItem <| n.Trim())
                        |> ResizeArray
                } |> U2.Case2

            member this.resolveCompletionItem(sug, ct) =
                promise {
                    return sug
                } |> U2.Case2
    }

type InstalledPackage = {
    group: string
    name: string
    version: string
}

let parsePaketList (lines: string[]) =
    seq {
        for line in lines do
            let parts = line.Trim().Split(' ')
            if parts.Length = 4 then
                yield {
                    group = parts.[0]
                    name = parts.[1]
                    version = parts.[3]
                }
    }

let private createReferencesProvider () =
    {   new CompletionItemProvider
        with
            member this.provideCompletionItems(doc, pos, ct) =
                promise {
                    let! executionResult = "show-installed-packages -s" |> execPaket
                    let installedPackages = handlePaketList executionResult |> parsePaketList
                    return seq {
                        for (name, inGroups) in Seq.groupBy (fun p -> p.name) installedPackages do
                            let groups = String.Join(",", Seq.map (fun p -> p.group) inGroups)
                            let item = CompletionItem name
                            item.detail <- groups
                            yield item
                    } |> ResizeArray
                } |> U2.Case2

            member this.resolveCompletionItem(sug, ct) =
                promise {
                    return sug
                } |> U2.Case2
    }

let private saveHandler (doc : TextDocument) =
    let config =
        let cfg = vscode.workspace.getConfiguration()
        cfg.get ("Paket.autoInstall", false)
    if (doc.fileName.EndsWith "paket.references" || doc.fileName.EndsWith "paket.dependencies" ) && config then
        Install ()

let activate (context: vscode.ExtensionContext) =
    let registerCommand com (f: unit->unit) =
        vscode.commands.registerCommand(com, unbox<Func<obj,obj>> f)
        |> context.subscriptions.Add

    let df = createEmpty<DocumentFilter>
    df.language <- Some "paket-dependencies"
    let selector : DocumentSelector = df |> U3.Case2

    let df = createEmpty<DocumentFilter>
    df.language <- Some "paket-references"
    let referencesSelector : DocumentSelector = df |> U3.Case2


    languages.registerCompletionItemProvider(selector, createDependenciesProvider())
    |> ignore

    languages.registerCompletionItemProvider(referencesSelector, createReferencesProvider())
    |> ignore

    workspace.onDidSaveTextDocument.Invoke(unbox saveHandler, null, unbox context.subscriptions)
    |> ignore


    registerCommand "paket.Init" Init
    registerCommand "paket.Install" Install
    registerCommand "paket.Update" Update
    registerCommand "paket.Outdated" Outdated
    registerCommand "paket.Restore" Restore
    registerCommand "paket.AutoRestoreOn" AutoRestoreOn
    registerCommand "paket.AutoRestoreOff" AutoRestoreOff
    registerCommand "paket.ConvertFromNuget" ConvertFromNuget
    registerCommand "paket.Simplify" Simplify
    registerCommand "paket.Add" Add
    registerCommand "paket.Why" Why
    registerCommand "paket.AddToCurrent" AddToCurrent
    registerCommand "paket.UpdateGroup" UpdateGroup
    registerCommand "paket.UpdatePackage" UpdatePackage
    registerCommand "paket.UpdatePackageCurrent" UpdatePackageCurrent
    registerCommand "paket.RemovePackage" RemovePackage
    registerCommand "paket.RemovePackageCurrent" RemovePackageCurrent
    registerCommand "paket.GenerateLoadScripts" GenerateLoadScripts

