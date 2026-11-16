# ========================================================
# Script de Release OTA + Instalador Offline - APAC Kiosk Guardian
# ========================================================
param (
    [string]$Changelog = "Atualizador OTA à prova de falhas, janela de status em tempo real, rollback automático e instalador offline.",
    [string]$CustomVersion = "",
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = "Stop"

try {
    $Host.UI.RawUI.WindowTitle = "Publicador OTA + Instalador Offline - APAC Kiosk Guardian"
} catch {}

trap {
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Red
    Write-Host "[PROCESSO: ERRO] Diagnóstico da falha: $_" -ForegroundColor Red
    Write-Host "==========================================================" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Publicação OTA + Instalador Offline - APAC Kiosk Guardian " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Ler versão atual do ApacKiosk.csproj
Write-Host "[PROCESSO: 5%] Lendo versão atual do ApacKiosk.csproj..." -ForegroundColor Cyan
$csprojPath = "ApacKiosk\ApacKiosk.csproj"
$installerCsprojPath = "ApacKiosk.Installer\ApacKiosk.Installer.csproj"
$installerManifestPath = "ApacKiosk.Installer\app.manifest"
$payloadZipPath = "ApacKiosk.Installer\Payload.zip"

if (-not (Test-Path $csprojPath)) {
    throw "Arquivo $csprojPath não encontrado. Execute a partir da raiz da solução."
}
if (-not (Test-Path $installerCsprojPath)) {
    throw "Arquivo $installerCsprojPath não encontrado."
}

$csprojContent = Get-Content $csprojPath -Raw
$versionMatch = [regex]::Match($csprojContent, '<Version>([0-9\.]+)</Version>')
$currentVersion = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { "1.0.0.0" }
Write-Host "Versão atual no csproj: $currentVersion" -ForegroundColor Gray

# 2. Determinar próxima versão
if ([string]::IsNullOrWhiteSpace($CustomVersion)) {
    $parts = $currentVersion.Split('.')
    if ($parts.Count -eq 4) {
        $build = [int]$parts[2]
        $rev = [int]$parts[3] + 1
        $nextVersion = "$($parts[0]).$($parts[1]).$build.$rev"
    } elseif ($parts.Count -eq 3) {
        $patch = [int]$parts[2] + 1
        $nextVersion = "$($parts[0]).$($parts[1]).$patch.0"
    } else {
        $nextVersion = "$currentVersion.1"
    }
} else {
    $nextVersion = $CustomVersion.TrimStart('v')
}

$tagName = "v$nextVersion"

Write-Host ""
Write-Host "----------------------------------------------------------" -ForegroundColor Yellow
Write-Host " NOVA VERSÃO A SER GERADA:" -ForegroundColor Yellow
Write-Host "   Versão     : $nextVersion" -ForegroundColor White
Write-Host "   Tag Git    : $tagName" -ForegroundColor White
Write-Host "   Instalador : Instalador_ApacKiosk_v$nextVersion.exe" -ForegroundColor White
Write-Host "   Changelog  : $Changelog" -ForegroundColor White
Write-Host "----------------------------------------------------------" -ForegroundColor Yellow
Write-Host ""

# 3. Atualizar versões nos arquivos .csproj e app.manifest
Write-Host "[PROCESSO: 15%] Atualizando ApacKiosk.csproj e ApacKiosk.Installer.csproj para $nextVersion..." -ForegroundColor Cyan
$newCsproj = [regex]::Replace($csprojContent, '<Version>[0-9\.]+</Version>', "<Version>$nextVersion</Version>")
$newCsproj = [regex]::Replace($newCsproj, '<FileVersion>[0-9\.]+</FileVersion>', "<FileVersion>$nextVersion</FileVersion>")
$newCsproj = [regex]::Replace($newCsproj, '<AssemblyVersion>[0-9\.]+</AssemblyVersion>', "<AssemblyVersion>$nextVersion</AssemblyVersion>")
Set-Content -Path $csprojPath -Value $newCsproj -NoNewline

$installerCsprojContent = Get-Content $installerCsprojPath -Raw
$newInstCsproj = [regex]::Replace($installerCsprojContent, '<AssemblyName>.*?</AssemblyName>', "<AssemblyName>Instalador_ApacKiosk_v$nextVersion</AssemblyName>")
$newInstCsproj = [regex]::Replace($newInstCsproj, '<Version>[0-9\.]+</Version>', "<Version>$nextVersion</Version>")
$newInstCsproj = [regex]::Replace($newInstCsproj, '<FileVersion>[0-9\.]+</FileVersion>', "<FileVersion>$nextVersion</FileVersion>")
$newInstCsproj = [regex]::Replace($newInstCsproj, '<AssemblyVersion>[0-9\.]+</AssemblyVersion>', "<AssemblyVersion>$nextVersion</AssemblyVersion>")
Set-Content -Path $installerCsprojPath -Value $newInstCsproj -NoNewline

if (Test-Path $installerManifestPath) {
    $manifestContent = Get-Content $installerManifestPath -Raw
    $newManifest = [regex]::Replace($manifestContent, '<assemblyIdentity\s+version="[0-9\.]+"\s+name="ApacKiosk\.Installer"/>', "<assemblyIdentity version=`"$nextVersion`" name=`"ApacKiosk.Installer`"/>")
    Set-Content -Path $installerManifestPath -Value $newManifest -NoNewline
}

# 4. Compilar executável OTA principal
$publishDir = "publish"
$exePath = Join-Path $publishDir "ApacKiosk.exe"
$installerExeName = "Instalador_ApacKiosk_v$nextVersion.exe"
$installerExePath = Join-Path $publishDir $installerExeName

if (-not $SkipBuild) {
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    Write-Host "[PROCESSO: 30%] Compilando binário OTA Release (ApacKiosk.exe)..." -ForegroundColor Cyan
    dotnet publish ApacKiosk/ApacKiosk.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "Falha na compilação do executável principal!"
    }
}

if (-not (Test-Path $exePath)) {
    throw "Executável não encontrado em $exePath!"
}

$exeSizeMB = [math]::Round(((Get-Item $exePath).Length / 1MB), 2)
Write-Host "[PROCESSO: 45%] ApacKiosk.exe gerado com sucesso ($exeSizeMB MB)!" -ForegroundColor Green

# 5. Gerar SHA-256 do executável OTA
Write-Host "[PROCESSO: 50%] Gerando checksum SHA-256..." -ForegroundColor Cyan
$sha256 = (Get-FileHash $exePath -Algorithm SHA256).Hash
$shaFile = Join-Path $publishDir "ApacKiosk.exe.sha256"
Set-Content -Path $shaFile -Value "$sha256 *ApacKiosk.exe"
Write-Host "SHA-256 ApacKiosk.exe: $sha256" -ForegroundColor Gray

# 6. Empacotar Payload.zip e compilar Instalador Offline
Write-Host "[PROCESSO: 55%] Empacotando Payload.zip do instalador offline..." -ForegroundColor Cyan
$payloadStage = Join-Path $env:TEMP ("ApacPayloadStage_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $payloadStage -Force | Out-Null

try {
    Copy-Item $exePath (Join-Path $payloadStage "ApacKiosk.exe") -Force
    if (Test-Path "ApacKiosk.Installer\icon.ico") {
        Copy-Item "ApacKiosk.Installer\icon.ico" (Join-Path $payloadStage "icon.ico") -Force
    }

    $resourcesSrc = Join-Path $publishDir "Resources"
    if (-not (Test-Path $resourcesSrc)) {
        $resourcesSrc = "ApacKiosk\Resources"
    }
    if (Test-Path $resourcesSrc) {
        Copy-Item $resourcesSrc (Join-Path $payloadStage "Resources") -Recurse -Force
    }

    if (Test-Path $payloadZipPath) {
        Remove-Item $payloadZipPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($payloadStage, (Resolve-Path ".").Path + "\" + $payloadZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}
finally {
    Remove-Item $payloadStage -Recurse -Force -ErrorAction SilentlyContinue
}

$payloadMB = [math]::Round(((Get-Item $payloadZipPath).Length / 1MB), 2)
Write-Host "Payload.zip atualizado ($payloadMB MB)." -ForegroundColor Gray
if ($payloadMB -ge 95) {
    Write-Warning "Payload.zip ($payloadMB MB) está próximo do limite de 100 MB do GitHub!"
}

Write-Host "[PROCESSO: 65%] Compilando Instalador Offline ($installerExeName)..." -ForegroundColor Cyan
$installerStage = Join-Path $env:TEMP ("ApacInstallerOut_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $installerStage -Force | Out-Null

try {
    dotnet publish $installerCsprojPath --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output $installerStage
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao compilar o Instalador Offline!"
    }

    $builtInstaller = Join-Path $installerStage $installerExeName
    if (-not (Test-Path $builtInstaller)) {
        throw "Instalador compilado não encontrado em $builtInstaller!"
    }
    Copy-Item $builtInstaller $installerExePath -Force
}
finally {
    Remove-Item $installerStage -Recurse -Force -ErrorAction SilentlyContinue
}

# Limpar arquivos de depuração/XML da pasta publish para manter apenas os artefatos de distribuição
Get-ChildItem $publishDir -Include "*.pdb", "*.xml" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

$installerSha256 = (Get-FileHash $installerExePath -Algorithm SHA256).Hash
$installerShaTxt = Join-Path $publishDir "Instalador_ApacKiosk_v$nextVersion.sha256.txt"
Set-Content -Path $installerShaTxt -Value "$installerSha256 *$installerExeName"
$installerSizeMB = [math]::Round(((Get-Item $installerExePath).Length / 1MB), 2)
Write-Host "[PROCESSO: 75%] Instalador Offline gerado: $installerExePath ($installerSizeMB MB)" -ForegroundColor Green
Write-Host "SHA-256 Instalador: $installerSha256" -ForegroundColor Gray

# 7. Git commit e tag
Write-Host ""
Write-Host "[PROCESSO: 82%] Fazendo commit e tag Git..." -ForegroundColor Cyan
git add ApacKiosk/ ApacKiosk.Installer/ scripts/publish_ota.ps1
git commit -m "chore(release): versao $nextVersion - $Changelog"
if ($LASTEXITCODE -ne 0) { throw "Falha no git commit!" }

git push origin main
if ($LASTEXITCODE -ne 0) { throw "Falha no git push main!" }

git tag -f -a $tagName -m "Release $tagName"
git push origin $tagName --force
if ($LASTEXITCODE -ne 0) { throw "Falha no git push tag!" }

# 8. Publicar Release no GitHub (OTA exe + sha256 + Instalador Offline)
Write-Host ""
Write-Host "[PROCESSO: 92%] Publicando Release no GitHub ($tagName)..." -ForegroundColor Cyan
$releaseNotes = @"
$Changelog

### Artefatos desta versão ($tagName)
- **OTA Automático:** ``ApacKiosk.exe`` ($exeSizeMB MB) — SHA-256: ``$sha256``
- **Instalador Offline:** ``$installerExeName`` ($installerSizeMB MB) — SHA-256: ``$installerSha256``
"@

gh release create $tagName $exePath $shaFile $installerExePath --title "APAC Kiosk Guardian $tagName" --notes "$releaseNotes"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[PROCESSO: AVISO] Release já existia, atualizando assets..." -ForegroundColor Yellow
    gh release upload $tagName $exePath $shaFile $installerExePath --clobber
    if ($LASTEXITCODE -ne 0) { throw "Falha ao enviar assets para a Release no GitHub!" }
}

$downloadUrl = "https://github.com/joemcestrategia/apac/releases/download/$tagName/ApacKiosk.exe"
$installerUrl = "https://github.com/joemcestrategia/apac/releases/download/$tagName/$installerExeName"

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "PROCESSO CONCLUÍDO COM SUCESSO (100%)! Versão $tagName no ar!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "OTA Download       : $downloadUrl" -ForegroundColor Cyan
Write-Host "Instalador Offline : $installerUrl" -ForegroundColor Cyan
Write-Host "Local Instalador   : $installerExePath" -ForegroundColor Cyan
Write-Host ""
