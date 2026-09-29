param(
    [ValidateSet('Test', 'Build', 'Publish')][string]$Task = 'Test',
    [string]$OutputDirectory = 'artifacts/current',
    [string]$TestResultsDirectory = 'artifacts/test-results'
)
$ErrorActionPreference = 'Stop'
$previousDotnetRoot = $env:DOTNET_ROOT
$previousDotnetRootX64 = $env:DOTNET_ROOT_X64
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $localSdk = Join-Path $env:LOCALAPPDATA 'TulliusTranslator.BuildTools\dotnet10\dotnet.exe'
    $dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $sdkVersion = & $dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^10\.') { throw '.NET 10 SDK is required. Install it, then retry.' }
    if ($Task -eq 'Test') {
        & $dotnet build tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
        & $dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=results.trx' --results-directory $TestResultsDirectory
    } elseif ($Task -eq 'Publish') {
        & $dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o $OutputDirectory -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:EnableWindowsTargeting=true
    } else {
        & $dotnet build XTranslatorAi.sln -c Release
    }
    if ($LASTEXITCODE -ne 0) { throw "$Task failed." }
} finally {
    $env:DOTNET_ROOT = $previousDotnetRoot
    $env:DOTNET_ROOT_X64 = $previousDotnetRootX64
    Pop-Location
}
