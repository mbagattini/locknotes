@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM  Configurazione - modifica il percorso di destinazione qui
REM ============================================================
set "DEST=C:\Users\mbagattini\OneDrive - Revorg Srl\Home\Utilities\LockNotes"
set "RID=win-x64"
set "CONFIG=Release"
REM ============================================================

set "PROJECT=%~dp0LockNotes\LockNotes.csproj"

echo.
echo Pubblicazione di LockNotes
echo   Progetto:     %PROJECT%
echo   Destinazione: %DEST%
echo   RID:          %RID%
echo   Configurazione: %CONFIG%
echo.

REM Pulisce la destinazione per evitare file residui da publish precedenti
if exist "%DEST%" (
    echo Pulizia della cartella di destinazione...
    rmdir /s /q "%DEST%"
)

dotnet publish "%PROJECT%" -c %CONFIG% -r %RID% -o "%DEST%"

if errorlevel 1 (
    echo.
    echo Pubblicazione FALLITA.
    pause
    exit /b 1
)

echo.
echo Pubblicazione completata in: %DEST%
pause
endlocal
