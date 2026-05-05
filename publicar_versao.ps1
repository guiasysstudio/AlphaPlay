param(
    [string]$Versao = "1.0.2",
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
$ReleaseTitle = "AlphaPlay $Tag"
$LocalReleaseDir = Join-Path $ProjectDir "release-final"
$FinalInstallerPath = Join-Path $LocalReleaseDir $InstallerName
$IssPath = Join-Path $ProjectDir "installer\AlphaPlay.iss"
$CsprojPath = Join-Path $ProjectDir "AlphaPlay.csproj"
$GerarBatPath = Join-Path $ProjectDir "gerar_instalador.bat"
$PublicarBatPath = Join-Path $ProjectDir "publicar_alpha.bat"

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

Write-Host "2) Verificando ferramentas e arquivos obrigatorios..."
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git nao encontrado."
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI 'gh' nao encontrado."
}
if (-not (Test-Path $GerarBatPath)) {
    throw "Arquivo gerar_instalador.bat nao encontrado."
}
if (-not (Test-Path $PublicarBatPath)) {
    throw "Arquivo publicar_alpha.bat nao encontrado."
}
if (-not (Test-Path $IssPath)) {
    throw "Arquivo installer\AlphaPlay.iss nao encontrado."
}
if (-not (Test-Path $CsprojPath)) {
    throw "Arquivo AlphaPlay.csproj nao encontrado."
}
Write-Host "Tudo OK"
Write-Host ""

Write-Host "3) Atualizando versao real do projeto..."
$CsprojText = Get-Content $CsprojPath -Raw -Encoding UTF8

$CsprojText = $CsprojText -replace '<Version>.*?</Version>', "<Version>$Versao</Version>"
$CsprojText = $CsprojText -replace '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$Versao</AssemblyVersion>"
$CsprojText = $CsprojText -replace '<FileVersion>.*?</FileVersion>', "<FileVersion>$Versao</FileVersion>"

if ($CsprojText -notmatch '<Version>') {
    $CsprojText = $CsprojText -replace '<RootNamespace>AlphaPlay</RootNamespace>', "<RootNamespace>AlphaPlay</RootNamespace>`n    <Version>$Versao</Version>`n    <AssemblyVersion>$Versao</AssemblyVersion>`n    <FileVersion>$Versao</FileVersion>"
}

Set-Content $CsprojPath $CsprojText -Encoding UTF8

$IssText = Get-Content $IssPath -Raw -Encoding UTF8
$IssText = $IssText -replace '#define\s+AppVersion\s+".*"', "#define AppVersion `"$Versao`""
$IssText = $IssText -replace '#define\s+MyAppVersion\s+".*"', "#define MyAppVersion `"$Versao`""
$IssText = $IssText -replace 'AppVersion=.*', "AppVersion={#AppVersion}"
$IssText = $IssText -replace 'OutputBaseFilename=.*', "OutputBaseFilename=AlphaPlay_Setup_{#AppVersion}"
Set-Content $IssPath $IssText -Encoding UTF8

Write-Host "Versao atualizada no AlphaPlay.csproj e AlphaPlay.iss: $Versao"
Write-Host ""

Write-Host "4) Gerando instalador real da versao $Versao..."
& ".\gerar_instalador.bat" $Versao

if ($LASTEXITCODE -ne 0) {
    throw "gerar_instalador.bat terminou com erro."
}

Write-Host ""
Write-Host "5) Conferindo instalador gerado..."

$ExpectedInstaller = Join-Path $ProjectDir "installer\output\$InstallerName"

if (-not (Test-Path $ExpectedInstaller)) {
    throw "O instalador da versao correta nao foi encontrado. Esperado: $ExpectedInstaller"
}

Write-Host "Instalador correto encontrado:"
Write-Host $ExpectedInstaller
Write-Host ""

Write-Host "6) Copiando instalador para release-final..."
if (-not (Test-Path $LocalReleaseDir)) {
    New-Item -ItemType Directory -Path $LocalReleaseDir | Out-Null
}

Copy-Item $ExpectedInstaller $FinalInstallerPath -Force

if (-not (Test-Path $FinalInstallerPath)) {
    throw "Falha ao copiar instalador para release-final."
}

Write-Host "Instalador final:"
Write-Host $FinalInstallerPath
Write-Host ""

Write-Host "7) Criando/atualizando Release no GitHub..."
$NotesFile = Join-Path $env:TEMP "alphaplay_release_notes_$Versao.md"

@"
# AlphaPlay $Tag

Atualizacao do AlphaPlay $Tag.

## Alteracoes principais

- Melhorias e correcoes gerais do AlphaPlay.
- Versao real do programa atualizada para $Versao.
- Instalador anexado automaticamente nesta Release.

## Instalacao

Baixe e execute o arquivo:

$InstallerName
"@ | Set-Content $NotesFile -Encoding UTF8

# Verifica se a Release existe usando cmd.exe para evitar erro NativeCommandError
# do PowerShell quando o gh retorna "release not found".
cmd /c "gh release view $Tag --repo $Repo >nul 2>nul"
$ReleaseViewExitCode = $LASTEXITCODE

if ($ReleaseViewExitCode -eq 0) {
    Write-Host "Release $Tag ja existe. Atualizando instalador..."
    gh release upload $Tag "$FinalInstallerPath" --repo $Repo --clobber

    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao anexar/atualizar o instalador na Release $Tag."
    }
} else {
    Write-Host "Release $Tag nao existe. Criando Release..."
    gh release create $Tag "$FinalInstallerPath" --repo $Repo --title "$ReleaseTitle" --notes-file "$NotesFile"

    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao criar a Release $Tag no GitHub."
    }
}

Write-Host "Release publicada/atualizada com sucesso."
Write-Host ""

Write-Host "8) Atualizando arquivos do projeto no GitHub..."
if (-not (Test-Path ".git")) {
    git init
    git branch -M main
}

$RemoteUrl = git remote get-url origin 2>$null
if (-not $RemoteUrl) {
    git remote add origin "https://github.com/$Repo.git"
}

git add -A
$HasChanges = git status --porcelain

if ($HasChanges) {
    git commit -m "Atualizar AlphaPlay $Tag"
    git push origin main

    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao enviar arquivos do projeto para o GitHub."
    }
} else {
    Write-Host "Nenhuma alteracao de codigo para enviar."
}

Write-Host ""
Write-Host "9) Conferindo Releases no GitHub..."
gh release list --repo $Repo --limit 5

Write-Host ""
Write-Host "============================================================"
Write-Host " Publicacao concluida com sucesso!"
Write-Host " Versao real do programa: $Versao"
Write-Host " Instalador: $FinalInstallerPath"
Write-Host " Release: https://github.com/$Repo/releases/tag/$Tag"
Write-Host "============================================================"
