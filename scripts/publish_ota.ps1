# ========================================================
# Script de Release OTA 100% Automatizado - APAC Kiosk Guardian
# Baseado no padrao de excelencia do EscalaIBAI
# ========================================================
param (
    [string]$Changelog = "Correcao de compatibilidade de sites e melhorias no atualizador OTA.",
    [string]$CustomVersion = "",
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = "Stop"

try {
    $Host.UI.RawUI.WindowTitle = "🚀 Publicador OTA - APAC Kiosk Guardian"
} catch {}

trap {
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Red
    Write-Host "[PROCESSO: ERRO ❌] Diagnóstico da falha: $_" -ForegroundColor Red
    Write-Host "==========================================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Pressione ENTER para sair..." -ForegroundColor Yellow
    Read-Host
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   🚀 Publicação de Atualização OTA - APAC Kiosk Guardian " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Ler versão atual do ApacKiosk.csproj
Write-Host "[PROCESSO: 10%] Lendo versão atual do ApacKiosk.csproj..." -ForegroundColor Cyan
$csprojPath = "ApacKiosk\ApacKiosk.csproj"
if (-not (Test-Path $csprojPath)) {
    Write-Error "[PROCESSO: ERRO ❌] Arquivo $csprojPath não encontrado. Execute a partir da raiz do repositório."
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
Write-Host "   Nome da Versão : $nextVersion" -ForegroundColor White
Write-Host "   Tag Git        : $tagName" -ForegroundColor White
Write-Host "   Changelog      : $Changelog" -ForegroundColor White
Write-Host "----------------------------------------------------------" -ForegroundColor Yellow
Write-Host ""

# 3. Atualizar ApacKiosk.csproj
Write-Host "[PROCESSO: 25%] Atualizando ApacKiosk.csproj para $nextVersion..." -ForegroundColor Cyan
$newCsproj = [regex]::Replace($csprojContent, '<Version>[0-9\.]+</Version>', "<Version>$nextVersion</Version>")
$newCsproj = [regex]::Replace($newCsproj, '<FileVersion>[0-9\.]+</FileVersion>', "<FileVersion>$nextVersion</FileVersion>")
$newCsproj = [regex]::Replace($newCsproj, '<AssemblyVersion>[0-9\.]+</AssemblyVersion>', "<AssemblyVersion>$nextVersion</AssemblyVersion>")
Set-Content -Path $csprojPath -Value $newCsproj -NoNewline
Write-Host "[PROCESSO: 30%] ApacKiosk.csproj atualizado com sucesso." -ForegroundColor Green

# 4. Compilar binário Release single-file
$publishDir = "publish"
$exePath = Join-Path $publishDir "ApacKiosk.exe"

if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "[PROCESSO: 40%] Compilando binário Release (dotnet publish)..." -ForegroundColor Cyan
    dotnet publish ApacKiosk/ApacKiosk.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output $publishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[PROCESSO: ERRO ❌] Falha na compilação do executável com dotnet publish!"
    }
}

if (-not (Test-Path $exePath)) {
    Write-Error "[PROCESSO: ERRO ❌] Executável não encontrado em $exePath!"
}

$exeSizeMB = [math]::Round(((Get-Item $exePath).Length / 1MB), 2)
Write-Host "[PROCESSO: 70%] Executável Release gerado com sucesso ($exeSizeMB MB)!" -ForegroundColor Green

# 5. Gerar hash SHA-256 para integridade
Write-Host "[PROCESSO: 75%] Gerando checksum SHA-256..." -ForegroundColor Cyan
$sha256 = (Get-FileHash $exePath -Algorithm SHA256).Hash
$shaFile = Join-Path $publishDir "ApacKiosk.exe.sha256"
Set-Content -Path $shaFile -Value "$sha256 *ApacKiosk.exe"
Write-Host "SHA-256: $sha256" -ForegroundColor Gray

# 6. Git commit e tag
Write-Host ""
Write-Host "[PROCESSO: 80%] Fazendo commit e tag Git..." -ForegroundColor Cyan
git add ApacKiosk/
git commit -m "chore(release): versao $nextVersion - $Changelog"
git push origin main

git tag -f -a $tagName -m "Release $tagName"
git push origin $tagName --force

# 7. Publicar Release no GitHub via CLI
Write-Host ""
Write-Host "[PROCESSO: 90%] Publicando Release no GitHub ($tagName)..." -ForegroundColor Cyan
gh release create $tagName $exePath $shaFile --title "APAC Kiosk Guardian $tagName" --notes "$Changelog"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[PROCESSO: AVISO ⚠️] Se a release já existia, atualizando assets..." -ForegroundColor Yellow
    gh release upload $tagName $exePath $shaFile --clobber
}

# 8. Obter URL do asset
$downloadUrl = "https://github.com/joemcestrategia/apac/releases/download/$tagName/ApacKiosk.exe"

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "🎉 PROCESSO CONCLUÍDO COM SUCESSO (100%)! Versão $tagName no ar!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "URL de Download: $downloadUrl" -ForegroundColor Cyan
Write-Host "As máquinas do Kiosk agora detectarão essa atualização pela internet." -ForegroundColor Green
Write-Host ""
