$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$dist = [IO.Path]::GetFullPath((Join-Path $root "dist"))
$publish = [IO.Path]::GetFullPath((Join-Path $dist "windows-x64"))
$zip = Join-Path $dist "windows-pcaudio-uplink-win-x64.zip"
$project = Join-Path $root "windows-pcaudio-uplink.csproj"

if (-not $dist.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Diretorio de distribuicao invalido."
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
Get-ChildItem -LiteralPath $dist -Force | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}

Write-Host "Publicando executavel self-contained win-x64..."
& dotnet publish $project -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Gerando $zip..."
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal
Remove-Item -LiteralPath $publish -Recurse -Force

Write-Host "Pacote criado: $zip"
