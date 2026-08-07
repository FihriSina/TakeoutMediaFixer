param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\src\TakeoutMediaFixer\tools"),
    [string]$Version = "13.59"
)

$ErrorActionPreference = "Stop"

$checksums = @{
    "13.59" = "44b512b25af500724ba579d0a53c8fc5851628b692dd5e5d94ae4a15c2cba9ec"
}

if (-not $checksums.ContainsKey($Version)) {
    throw "ExifTool $Version için doğrulanmış SHA-256 değeri tanımlı değil."
}

$expectedHash = $checksums[$Version]
$archiveName = "exiftool-${Version}_64.zip"
$urls = @(
    "https://sourceforge.net/projects/exiftool/files/$archiveName/download",
    "https://exiftool.org/$archiveName"
)

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("TakeoutMediaFixer-ExifTool-" + [guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempRoot $archiveName
$extractPath = Join-Path $tempRoot "extract"

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null

    $downloaded = $false
    foreach ($url in $urls) {
        try {
            Write-Host "ExifTool indiriliyor: $url"
            Invoke-WebRequest -Uri $url -OutFile $archivePath -UseBasicParsing -Headers @{ "User-Agent" = "TakeoutMediaFixer build script" }
            $downloaded = $true
            break
        }
        catch {
            Write-Warning "İndirme başarısız: $($_.Exception.Message)"
        }
    }

    if (-not $downloaded) {
        throw "ExifTool indirilemedi."
    }

    $actualHash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "ExifTool SHA-256 doğrulaması başarısız. Beklenen: $expectedHash, bulunan: $actualHash"
    }

    Expand-Archive -Path $archivePath -DestinationPath $extractPath -Force

    $exe = Get-ChildItem -Path $extractPath -Recurse -File | Where-Object { $_.Name -eq "exiftool(-k).exe" } | Select-Object -First 1
    $filesDirectory = Get-ChildItem -Path $extractPath -Recurse -Directory | Where-Object { $_.Name -eq "exiftool_files" } | Select-Object -First 1

    if (-not $exe -or -not $filesDirectory) {
        throw "ExifTool arşiv yapısı beklenen biçimde değil."
    }

    if (Test-Path $Destination) {
        Remove-Item -Path $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    Copy-Item -Path $exe.FullName -Destination (Join-Path $Destination "exiftool.exe") -Force
    Copy-Item -Path $filesDirectory.FullName -Destination (Join-Path $Destination "exiftool_files") -Recurse -Force

    $readme = Get-ChildItem -Path $extractPath -Recurse -File | Where-Object { $_.Name -match "^README" } | Select-Object -First 1
    if ($readme) {
        Copy-Item -Path $readme.FullName -Destination (Join-Path $Destination "EXIFTOOL-README.txt") -Force
    }

    Write-Host "ExifTool $Version doğrulandı ve $Destination klasörüne yerleştirildi."
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
