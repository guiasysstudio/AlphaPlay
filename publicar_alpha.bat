@echo off
chcp 65001 >nul
setlocal

REM ============================================================
REM AlphaPlay - Publicacao do programa
REM Altere a versao abaixo quando for gerar uma nova versao.
REM Exemplo: 1.0.0, 1.0.1, 1.1.0
REM ============================================================
set APP_VERSION=1.0.0
set APP_NAME=AlphaPlay
set RUNTIME=win-x64
set NO_PAUSE=%~1

cd /d "%~dp0"

echo.
echo ============================================================
echo Publicando %APP_NAME% %APP_VERSION% para %RUNTIME%
echo ============================================================
echo.

if not exist "AlphaPlay.csproj" (
    echo ERRO: AlphaPlay.csproj nao foi encontrado.
    echo Execute este arquivo dentro da pasta raiz do projeto AlphaPlay.
    if /I not "%NO_PAUSE%"=="/nopause" pause
    exit /b 1
)

if exist "publish" rmdir /s /q "publish"
mkdir "publish" >nul 2>nul

dotnet restore
if errorlevel 1 goto erro

dotnet publish "AlphaPlay.csproj" ^
  -c Release ^
  -r %RUNTIME% ^
  --self-contained true ^
  /p:PublishSingleFile=false ^
  /p:IncludeNativeLibrariesForSelfExtract=false ^
  /p:Version=%APP_VERSION% ^
  /p:AssemblyVersion=%APP_VERSION% ^
  /p:FileVersion=%APP_VERSION% ^
  /p:InformationalVersion=%APP_VERSION% ^
  -o "publish"

if errorlevel 1 goto erro

if not exist "publish\AlphaPlay.exe" (
    echo ERRO: publish\AlphaPlay.exe nao foi gerado.
    if /I not "%NO_PAUSE%"=="/nopause" pause
    exit /b 1
)

echo %APP_VERSION% > "publish\version.txt"

echo.
echo ============================================================
echo Publicacao concluida com sucesso.
echo Saida: %CD%\publish
echo ATENCAO: publish\AlphaPlay.exe nao e o instalador.
echo O instalador final e gerado por gerar_instalador.bat.
echo ============================================================
echo.
if /I not "%NO_PAUSE%"=="/nopause" pause
exit /b 0

:erro
echo.
echo ERRO: Falha ao publicar o AlphaPlay.
echo Verifique as mensagens acima.
echo.
if /I not "%NO_PAUSE%"=="/nopause" pause
exit /b 1
