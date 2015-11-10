[<ReflectedDefinition>]
module Ionide.VSCode

open System
open System.Text.RegularExpressions
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.fs
open FunScript.TypeScript.child_process
open FunScript.TypeScript.vscode

open Ionide
open Ionide.VSCode

module PaketService =
    let private location =
        if Process.isWin () then
            (VSCode.getPluginPath "ionide-paket") + @"\bin\paket.exe"
        else
            (VSCode.getPluginPath "ionide-paket") + @"/bin/paket.exe"


    let private bootstrapperLocation =
        if Globals._process.platform.StartsWith("win") then
            (VSCode.getPluginPath "ionide-paket") + @"\bin\paket.bootstrapper.exe"
        else
            (VSCode.getPluginPath "ionide-paket")+ @"/bin/paket.bootstrapper.exe"

    let private spawnPaket cmd =
        let outputChannel = window.Globals.createOutputChannel "Paket"
        outputChannel.clear ()
        window.Globals.showInformationMessageOverload2 ("Paket started", "Open")
        |> Promise.toPromise
        |> Promise.success(fun n -> if n = "Open" then outputChannel.show (2 |> unbox) )
        |> ignore
        let proc = Process.spawnWithNotification location "mono" cmd outputChannel
        proc.on("exit",unbox<Function>(fun (code : string) ->
            if code ="0" then
                window.Globals.showInformationMessage "Paket completed" |> ignore
            else
                window.Globals.showErrorMessage "Paket failed" |> ignore
        )) |> ignore
        proc

    let UpdatePaketSilent () = Process.spawn bootstrapperLocation "mono" ""
    let Init () = "init" |> spawnPaket
    let Install () = "install" |> spawnPaket
    let Update () = "update" |> spawnPaket
    let Outdated () = "outdated" |> spawnPaket
    let Restore () = "restore" |> spawnPaket
    let AutoRestoreOn () = "auto-restore on" |> spawnPaket
    let AutoRestoreOff () = "auto-restore off" |> spawnPaket
    let ConvertFromNuget () = "convert-from-nuget" |> spawnPaket
    let Simplify () = "simplify" |> spawnPaket


type Paket() =
    member x.activate(state:obj) =
        PaketService.UpdatePaketSilent () |> ignore
        commands.Globals.registerCommand("paket.Init", PaketService.Init |> unbox) |> ignore
        commands.Globals.registerCommand("paket.Install", PaketService.Install |> unbox) |> ignore
        commands.Globals.registerCommand("paket.Update", PaketService.Update |> unbox) |> ignore
        commands.Globals.registerCommand("paket.Outdated", PaketService.Outdated |> unbox) |> ignore
        commands.Globals.registerCommand("paket.Restore", PaketService.Restore |> unbox) |> ignore
        commands.Globals.registerCommand("paket.AutoRestoreOn", PaketService.AutoRestoreOn |> unbox) |> ignore
        commands.Globals.registerCommand("paket.AutoRestoreOff", PaketService.AutoRestoreOff |> unbox) |> ignore
        commands.Globals.registerCommand("paket.ConvertFromNuget", PaketService.ConvertFromNuget |> unbox) |> ignore
        commands.Globals.registerCommand("paket.Simplify", PaketService.Simplify |> unbox) |> ignore
        ()
