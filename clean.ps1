$ErrorActionPreference = "SilentlyContinue"
if (Test-Path node_modules) {
    Remove-Item -Path node_modules -Recurse -Force
    Start-Sleep -Seconds 2
}
Write-Host "Clean complete"
