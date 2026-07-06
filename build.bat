@echo off
REM ============================================================
REM Build both projects with one command
REM Requires: Visual Studio Build Tools or dotnet CLI
REM ============================================================
setlocal enabledelayedexpansion

echo.
echo === Building KeyLogger.Service (setup.exe) ===
dotnet build src\KeyLogger.Service\KeyLogger.Service.csproj -c Release --nologo -v q
if %ERRORLEVEL% NEQ 0 (
    echo [!] Service build FAILED
    pause
    exit /b 1
)

echo.
echo === Building KeyLogger.Collect (collect.exe) ===
dotnet build src\KeyLogger.Collect\KeyLogger.Collect.csproj -c Release --nologo -v q
if %ERRORLEVEL% NEQ 0 (
    echo [!] Collect build FAILED
    pause
    exit /b 1
)

echo.
echo ========================================
echo  BUILD COMPLETE
echo ========================================
echo  setup.exe   : src\KeyLogger.Service\bin\Release\net472\setup.exe
echo  collect.exe : src\KeyLogger.Collect\bin\Release\net472\collect.exe
echo ========================================
echo.
pause