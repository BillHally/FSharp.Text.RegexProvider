// --------------------------------------------------------------------------------------
// FAKE build script 
// --------------------------------------------------------------------------------------

#if FAKE
#r """paket:
    source https://api.nuget.org/v3/index.json
    storage: none

    nuget Fake.Core.ReleaseNotes
    nuget Fake.Core.Target
    nuget Fake.Tools.Git
    nuget Fake.IO.FileSystem
    nuget Fake.DotNet.AssemblyInfoFile
    nuget Fake.DotNet.Cli
    nuget Fake.DotNet.MSBuild
    nuget Fake.DotNet.Testing.NUnit
    nuget Fake.DotNet.NuGet
    nuget Fake.DotNet.Fsi

    //"""
#endif

#if INTERACTIVE
// Load the intellisense generated by Fake for this script
#load "./.fake/build.fsx/intellisense.fsx"

#if !FAKE
    // Help ionide find netstandard
    #I @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\Facades"  
    // Make Visual Studio (+Code) intellisense work
    #r "netstandard"
#endif
#endif

open System
open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Tools
open Fake.DotNet
open Fake.DotNet.NuGet

let projects = [|"RegexProvider"|]

let projectName = "FSharp.Text.RegexProvider"
let summary = "A type provider for regular expressions."
let description = "A type provider for regular expressions."
let authors = ["Steffen Forkmann"; "David Tchepak"; "Sergey Tihon"; "Daniel Mohl"; "Tomas Petricek"; "Ryan Riley"; "Mauricio Scheffer"; "Phil Trelford"; "Vasily Kirichenko" ]
let tags = "F# fsharp typeproviders regex"

let solutionFile  = "RegexProvider"

let gitHome = "https://github.com/fsprojects"
let gitName = "FSharp.Text.RegexProvider"
let cloneUrl = "git@github.com:fsprojects/FSharp.Text.RegexProvider.git"
let nugetDir = "./nuget/"

// Read additional information from the release notes document
let release = ReleaseNotes.parse (IO.File.ReadAllLines "RELEASE_NOTES.md")

// Generate assembly info files with the right version & up-to-date information
Target.create "AssemblyInfo" (fun _ ->
  for project in projects do
    let fileName = "src/" + project + "/AssemblyInfo.fs"
    AssemblyInfoFile.createFSharp fileName
        [ AssemblyInfo.Title projectName
          AssemblyInfo.Product projectName
          AssemblyInfo.Description summary
          AssemblyInfo.Version release.AssemblyVersion
          AssemblyInfo.FileVersion release.AssemblyVersion ] 
)

// --------------------------------------------------------------------------------------
// Clean build results 

Target.create "Clean" (fun _ ->
    Shell.cleanDirs ["bin"; "temp"; nugetDir]
)

Target.create "CleanDocs" (fun _ ->
    Shell.cleanDirs ["docs/output"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target.create "BuildProvider" ( fun _ ->
    !! (solutionFile + ".sln")
    |> MSBuild.runRelease id "" "Restore;Rebuild"
    |> ignore
)    

Target.create "BuildTests" (fun _ ->
    !! (solutionFile + ".Tests.sln")
    |> MSBuild.runRelease id "" "Restore;Rebuild"
    |> ignore
)

Target.create "Build" ignore

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner & kill test runner when complete

Target.create "RunTests" (fun _ ->
    Target.activateFinal "CloseTestRunner"

    DotNet.test
        (
            fun p ->
            {
                p with
                    NoBuild       = true
                    NoRestore     = true
                    Configuration = DotNet.BuildConfiguration.Release 
            }
        )
        "tests/RegexProvider.tests/RegexProvider.tests.fsproj"
)

Target.createFinal "CloseTestRunner" (fun _ ->  
    Process.killAllByName "nunit-agent.exe"
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "NuGet" (fun _ ->
    // Format the description to fit on a single line (remove \r\n and double-spaces)
    let description = description.Replace("\r", "")
                                 .Replace("\n", "")
                                 .Replace("  ", " ")
    let project = projects.[0]

    let nugetDocsDir = nugetDir @@ "docs"
    let nugetlibDir = nugetDir @@ "lib"

    Shell.cleanDir nugetDocsDir
    Shell.cleanDir nugetlibDir
        
    Shell.copyDir nugetlibDir @"src/RegexProvider/bin/Release" (fun file -> file.Contains "FSharp.Core." |> not)
    Shell.copyDir nugetDocsDir "./docs/output" FileFilter.allFiles
    
    NuGet.NuGet (fun p -> 
        { p with   
            Authors = authors
            Project = projectName
            Summary = summary
            Description = description
            Version = release.NugetVersion
            ReleaseNotes = release.Notes |> String.toLines
            Tags = tags
            OutputPath = nugetDir
            AccessKey = Environment.environVarOrDefault "nugetkey" ""
            Publish = Environment.hasEnvironVar "nugetkey"
            Dependencies = [] })
        (project + ".nuspec")
)

// --------------------------------------------------------------------------------------
// Generate the documentation

let executeFSIWithArgs relativeWorkingDir scriptFile defines scriptArgs =
    let (ret, _) =
        Fsi.exec (fun p -> 
            { p with 
                Definitions = defines
                WorkingDirectory = __SOURCE_DIRECTORY__ @@ relativeWorkingDir}) scriptFile scriptArgs
    ret = 0

Target.create "GenerateReferenceDocs" (fun _ ->
    if not <| executeFSIWithArgs "docs/tools" "generate.fsx" [ "RELEASE"; "REFERENCE" ] [] then
      failwith "generating reference documentation failed"
)

let generateHelp' fail debug =
    let args =
        if debug then ["HELP"]
        else ["RELEASE"; "HELP"]
    if executeFSIWithArgs "docs/tools" "generate.fsx" args [] then
        Trace.traceImportant "Help generated"
    else
        if fail then
            failwith "generating help documentation failed"
        else
            Trace.traceImportant "generating help documentation failed"

let generateHelp fail =
    generateHelp' fail false

Target.create "GenerateHelp" (fun _ ->
    File.delete "docs/content/release-notes.md"
    Shell.copyFile "docs/content/" "RELEASE_NOTES.md"
    Shell.rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    File.delete "docs/content/license.md"
    Shell.copyFile "docs/content/" "LICENSE.txt"
    Shell.rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp true
)

Target.create "GenerateHelpDebug" (fun _ ->
    File.delete "docs/content/release-notes.md"
    Shell.copyFile "docs/content/" "RELEASE_NOTES.md"
    Shell.rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    File.delete "docs/content/license.md"
    Shell.copyFile "docs/content/" "LICENSE.txt"
    Shell.rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp' true true
)

Target.create "KeepRunning" (fun _ ->    
    use watcher = !! (__SOURCE_DIRECTORY__ @@ "docs/content/*.*") |> ChangeWatcher.run (Seq.iter (fun e -> generateHelp false))

    Trace.traceImportant "Waiting for help edits. Press any key to stop."

    System.Console.ReadKey() |> ignore

    watcher.Dispose()
)

Target.create "GenerateDocs" ignore

// --------------------------------------------------------------------------------------
// Release Scripts

Target.create "ReleaseDocs" (fun _ ->
    let tempDocsDir = "temp/gh-pages"
    Shell.cleanDir tempDocsDir
    Git.Repository.cloneSingleBranch "" cloneUrl "gh-pages" tempDocsDir

    Git.Repository.fullclean tempDocsDir
    Shell.copyRecursive "docs/output" tempDocsDir true |> Seq.iter (fun f -> Trace.logfn "%A" f)
    Git.Staging.stageAll tempDocsDir
    Git.Commit.exec tempDocsDir (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Git.Branches.push tempDocsDir
)

Target.create "Release" ignore

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" ignore

"Clean"
  ==> "AssemblyInfo"
  ==> "BuildProvider" ==> "BuildTests" ==> "Build"
  ==> "RunTests"
  =?> ("GenerateReferenceDocs", BuildServer.isLocalBuild)
  =?> ("GenerateDocs", BuildServer.isLocalBuild)
  ==> "All"
  =?> ("ReleaseDocs", BuildServer.isLocalBuild)

"All"
  ==> "NuGet"

"CleanDocs"
  ==> "GenerateHelp"
  ==> "GenerateReferenceDocs"
  ==> "GenerateDocs"

"CleanDocs"
  ==> "GenerateHelpDebug"

"GenerateHelp"
  ==> "KeepRunning"
    
"ReleaseDocs"
  ==> "Release"

"Nuget"
  ==> "Release"

Target.runOrDefault "All"
