param(
    [string]$Versao = "1.0.1",
    [string]$Repo = "guiasysstudio/AlphaPlay"
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================"
Write-Host " AlphaPlay - Publicar versao $Versao"
Write-Host "============================================================"
Write-Host ""

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectDir

$Tag = "v$Versao"
$InstallerName = "AlphaPlay_Setup_$Versao.exe"
$LocalReleaseDir = Join-Path $ProjectDir "release-final"

Write-Host "1) Criando/atualizando .gitignore..."
@"
bin/
obj/
publish/
installer/output/
release-final/
.vs/
*.user
*.suo
*.log
"@ | Set-Content ".gitignore" -Encoding UTF8

Write-Host "OK"
Write-Host ""

Write-Host "2) Verificando ferramentas..."
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git nao encontrado. Instale o Git antes de continuar."
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI 'gh' nao encontrado. Instale o GitHub CLI e rode: gh auth login"
}

if (-not (Test-Path ".\gerar_instalador.bat")) {
    throw "Arquivo gerar_instalador.bat nao encontrado na pasta do projeto."
}

Write-Host "Git OK"
Write-Host "GitHub CLI OK"
Write-Host ""

Write-Host "3) Gerando instalador..."
& ".\gerar_instalador.bat"

if ($LASTEXITCODE -ne 0) {
    throw "gerar_instalador.bat terminou com erro."
}

Write-Host ""
Write-Host "4) Procurando instalador gerado..."

$Installer = Get-ChildItem -Path $ProjectDir -Recurse -Filter "*.exe" |
    Where-Object {
        $_.FullName -like "*installer*output*" -or
        $_.FullName -like "*Output*"
    } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $Installer) {
    throw "Nenhum instalador .exe encontrado depois da geracao."
}

Write-Host "Instalador encontrado:"
Write-Host $Installer.FullName
Write-Host ""

Write-Host "5) Copiando instalador para pasta release-final..."

if (-not (Test-Path $LocalReleaseDir)) {
    New-Item -ItemType Directory -Path $LocalReleaseDir | Out-Null
}

$FinalInstallerPath = Join-Path $LocalReleaseDir $InstallerName
Copy-Item $Installer.FullName $FinalInstallerPath -Force

Write-Host "Instalador final:"
Write-Host $FinalInstallerPath
Write-Host ""

Write-Host "6) Publicando Release no GitHub..."

$ReleaseTitle = "AlphaPlay $Tag"
$NotesFile = Join-Path $env:TEMP "alphaplay_release_notes_$Versao.md"

@"
# AlphaPlay $Tag

Atualizacao do AlphaPlay.

## Alteracoes principais

- Melhorias e correcoes gerais do AlphaPlay.
- Atualizacao dos arquivos do projeto no GitHub.
- Instalador anexado automaticamente nesta Release.

## Instalacao

Baixe e execute o arquivo:

$InstallerName
"@ | Set-Content $NotesFile -Encoding UTF8

$ReleaseExists = $false

try {
    gh release view $Tag --repo $Repo | Out-Null
    $ReleaseExists = $true
} catch {
    $ReleaseExists = $false
}

if ($ReleaseExists) {
    Write-Host "Release $Tag ja existe. Atualizando arquivo anexado..."
    gh release upload $Tag $FinalInstallerPath --repo $Repo --clobber
} else {
    Write-Host "Criando Release $Tag..."
    gh release create $Tag $FinalInstallerPath `
        --repo $Repo `
        --title $ReleaseTitle `
        --notes-file $NotesFile `
        --target main
}

Write-Host ""
Write-Host "7) Atualizando arquivos do projeto no GitHub..."

if (-not (Test-Path ".git")) {
    git init
    git branch -M main
}

$RemoteUrl = git remote get-url origin 2>$null
if (-not $RemoteUrl) {
    git remote add origin "https://github.com/$Repo.git"
}

git add -A
git status

$HasChanges = git status --porcelain

if ($HasChanges) {
    git commit -m "Atualizar AlphaPlay $Tag"
    git push origin main
} else {
    Write-Host "Nenhuma alteracao de codigo para enviar."
}

Write-Host ""
Write-Host "============================================================"
Write-Host " Publicacao concluida com sucesso!"
Write-Host " Versao: $Tag"
Write-Host " Instalador: $FinalInstallerPath"
Write-Host " Release: https://github.com/$Repo/releases/tag/$Tag"
Write-Host "============================================================"
