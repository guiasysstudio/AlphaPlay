@echo off
chcp 65001 >nul
setlocal

REM ============================================================
REM AlphaPlay - Gerador do instalador final
REM Uso:
REM   gerar_instalador.bat
REM   gerar_instalador.bat 1.0.2
REM ============================================================

set APP_VERSION=1.0.0
if not "%~1"=="" set APP_VERSION=%~1

set APP_NAME=AlphaPlay

cd /d "%~dp0"

echo.
echo ============================================================
echo Gerando instalador final do %APP_NAME% %APP_VERSION%
echo ============================================================
echo.

if not exist "installer\AlphaPlay.iss" (
    echo ERRO: installer\AlphaPlay.iss nao foi encontrado.
    pause
    exit /b 1
)

REM 1) Publica o programa primeiro, ja com a versao correta.
call "%~dp0publicar_alpha.bat" /nopause %APP_VERSION%
if errorlevel 1 goto erro

REM 2) Localiza o compilador do Inno Setup.
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if "%ISCC%"=="" (
    echo.
    echo ERRO: Inno Setup 6 nao encontrado.
    echo Instale o Inno Setup 6 e execute este arquivo novamente.
    echo Link: https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

if not exist "installer\output" mkdir "installer\output"

REM Limpa instaladores antigos da mesma versao para evitar confusao.
if exist "installer\output\AlphaPlay_Setup_%APP_VERSION%.exe" del /q "installer\output\AlphaPlay_Setup_%APP_VERSION%.exe"

"%ISCC%" /DAppVersion=%APP_VERSION% "installer\AlphaPlay.iss"
if errorlevel 1 goto erro

if not exist "installer\output\AlphaPlay_Setup_%APP_VERSION%.exe" (
    echo.
    echo ERRO: O Inno Setup terminou, mas o instalador final nao foi encontrado.
    echo Esperado: installer\output\AlphaPlay_Setup_%APP_VERSION%.exe
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo INSTALADOR GERADO COM SUCESSO:
echo %CD%\installer\output\AlphaPlay_Setup_%APP_VERSION%.exe
echo ============================================================
echo.
explorer "%CD%\installer\output"
pause
exit /b 0

:erro
echo.
echo ERRO: Falha ao gerar o instalador.
echo Verifique as mensagens acima.
echo.
pause
exit /b 1
