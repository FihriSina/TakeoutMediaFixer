param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\TakeoutMediaFixer\TakeoutMediaFixer.csproj"
$tools = Join-Path $repoRoot "src\TakeoutMediaFixer\tools"
$artifactRoot = Join-Path $repoRoot "artifacts"
$publishDirectory = Join-Path $artifactRoot "TakeoutMediaFixer-$Runtime"
$zipPath = Join-Path $artifactRoot "TakeoutMediaFixer-$Runtime.zip"
$checksumPath = Join-Path $artifactRoot "SHA256SUMS.txt"

& (Join-Path $PSScriptRoot "Get-ExifTool.ps1") -Destination $tools

if (Test-Path $publishDirectory) {
    Remove-Item $publishDirectory -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
if (Test-Path $checksumPath) {
    Remove-Item $checksumPath -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Push-Location $repoRoot
try {
    dotnet restore "TakeoutMediaFixer.sln"
    dotnet build "TakeoutMediaFixer.sln" --configuration $Configuration --no-restore
    dotnet run --project "tests\TakeoutMediaFixer.SmokeTests\TakeoutMediaFixer.SmokeTests.csproj" --configuration $Configuration --no-build
    dotnet publish $project `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        --output $publishDirectory `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true

    Copy-Item "README.md" $publishDirectory
    Copy-Item "LICENSE" $publishDirectory
    Copy-Item "THIRD_PARTY_NOTICES.md" $publishDirectory

    Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $zipPath -Leaf)" | Set-Content -Path $checksumPath -Encoding utf8
    Write-Host "Paket hazır: $zipPath"
    Write-Host "SHA-256: $hash"
}
finally {
    Pop-Location
}
