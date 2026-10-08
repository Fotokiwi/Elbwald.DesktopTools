$ErrorActionPreference = "Stop"

$Version = "0.1.0-alpha.1"
$Configuration = "Release"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")

$Solution = Join-Path $RepoRoot "Elbwald.DesktopTools.sln"
$AppProject = Join-Path $RepoRoot "src/Elbwald.DesktopTools.App/Elbwald.DesktopTools.App.csproj"

$ArtifactRoot = Join-Path $RepoRoot "artifacts/alpha/$Version"
$PublishRoot = Join-Path $ArtifactRoot "publish"

$LinuxDir = Join-Path $PublishRoot "linux-x64"
$WindowsDir = Join-Path $PublishRoot "win-x64"

Write-Host "== Elbwald Digital – Desktop Tools $Version =="
Write-Host "Repository: $RepoRoot"
Write-Host

if (Test-Path $ArtifactRoot) {
    Remove-Item $ArtifactRoot -Recurse -Force
}

New-Item $PublishRoot -ItemType Directory -Force | Out-Null

Write-Host "== Clean =="
dotnet clean $Solution -c $Configuration

Write-Host
Write-Host "== Restore =="
dotnet restore $Solution

Write-Host
Write-Host "== Tests =="
dotnet test $Solution `
    -c $Configuration `
    --no-restore

function Publish-ElbwaldRuntime {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Runtime,

        [Parameter(Mandatory = $true)]
        [string] $Output
    )

    Write-Host
    Write-Host "== Publish $Runtime =="

    dotnet publish $AppProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $Output

    $RequiredFiles = @(
        "Modules/PhotoSort/module.json",
        "Modules/PhotoSort/Elbwald.DesktopTools.PhotoSort.dll",
        "Modules/MediaAnalyzer/module.json",
        "Modules/MediaAnalyzer/Elbwald.DesktopTools.MediaAnalyzer.dll"
    )

    foreach ($RelativePath in $RequiredFiles) {
        $FullPath = Join-Path $Output $RelativePath

        if (-not (Test-Path $FullPath)) {
            throw "Publish unvollständig. Datei fehlt: $FullPath"
        }
    }

    @"
Elbwald Digital – Desktop Tools
Version: $Version
Configuration: $Configuration
Runtime: $Runtime
Self-contained: yes
Single-file: no
Trimmed: no

Original media files are not part of this build directory.
Application caches and recovery data remain in the operating system's local app-data location.
"@ | Set-Content `
    -Path (Join-Path $Output "BUILD-INFO.txt") `
    -Encoding UTF8
}

Publish-ElbwaldRuntime -Runtime "linux-x64" -Output $LinuxDir
Publish-ElbwaldRuntime -Runtime "win-x64" -Output $WindowsDir

$LinuxExecutable = Join-Path $LinuxDir "Elbwald.DesktopTools.App"
$WindowsExecutable = Join-Path $WindowsDir "Elbwald.DesktopTools.App.exe"

if (-not (Test-Path $LinuxExecutable)) {
    throw "Linux-Executable fehlt: $LinuxExecutable"
}

if (-not (Test-Path $WindowsExecutable)) {
    throw "Windows-Executable fehlt: $WindowsExecutable"
}

Write-Host
Write-Host "== Archive =="

Compress-Archive `
    -Path (Join-Path $LinuxDir "*") `
    -DestinationPath (Join-Path $ArtifactRoot "Elbwald.DesktopTools-$Version-linux-x64.zip") `
    -Force

Compress-Archive `
    -Path (Join-Path $WindowsDir "*") `
    -DestinationPath (Join-Path $ArtifactRoot "Elbwald.DesktopTools-$Version-win-x64.zip") `
    -Force

Write-Host
Write-Host "Alpha-Build fertig:"
Write-Host "  $ArtifactRoot"
