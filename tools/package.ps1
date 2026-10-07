$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$dist = [IO.Path]::GetFullPath((Join-Path $root "dist"))
$packageRoot = [IO.Path]::GetFullPath((Join-Path $dist "package-content"))
$backendPublish = [IO.Path]::GetFullPath((Join-Path $dist "backend-publish"))
$trayPublish = [IO.Path]::GetFullPath((Join-Path $dist "tray-publish"))
$zip = Join-Path $dist "windows-pcaudio-uplink-win-x64.zip"
$backendProject = Join-Path $root "windows-pcaudio-uplink.csproj"
$trayProject = Join-Path $root "WindowsPcAudioUplink.Tray\windows-pcaudio-uplink-tray.csproj"

if (-not $dist.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Diretorio de distribuicao invalido."
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
Get-ChildItem -LiteralPath $dist -Force | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}

Write-Host "Publicando executavel self-contained win-x64..."
& dotnet publish $backendProject -p:PublishProfile=win-x64 -p:PublishDir=$backendPublish
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& dotnet publish $trayProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=false `
    -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:UseAppHost=true -p:DebugType=none -p:DebugSymbols=false -p:PublishDir=$trayPublish
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item (Join-Path $backendPublish "windows-pcaudio-uplink.exe") $packageRoot
Copy-Item (Join-Path $trayPublish "windows-pcaudio-uplink-tray.exe") $packageRoot
Copy-Item (Join-Path $root "configs") (Join-Path $packageRoot "configs") -Recurse

Write-Host "Gerando $zip..."
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zip -CompressionLevel Optimal
Remove-Item -LiteralPath $packageRoot -Recurse -Force
Remove-Item -LiteralPath $backendPublish -Recurse -Force
Remove-Item -LiteralPath $trayPublish -Recurse -Force

Write-Host "Pacote criado: $zip"
