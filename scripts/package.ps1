param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $root "artifacts\publish"
$artifactsDirectory = Join-Path $root "artifacts"

$resolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedPublish = [IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublish.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish directory must remain inside the repository."
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

$bundledDotnet = Join-Path $root ".tools\dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $bundledDotnet) {
    $bundledDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

& $dotnet publish (Join-Path $root "src\CodexRemaining.App\CodexRemaining.App.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $iscc) {
    $iscc = (Get-Command ISCC.exe -ErrorAction Stop).Source
}

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
& $iscc (Join-Path $root "installer\CodexRemaining.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Get-Item (Join-Path $artifactsDirectory "CodexRemaining-Setup.exe")
