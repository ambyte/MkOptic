@echo off
setlocal
cd /d "%~dp0"

echo === Restore ===
dotnet restore MkOptic.sln || goto :error

echo === Build (Debug) ===
dotnet build MkOptic.sln -c Debug --no-restore || goto :error

echo === Run (Debug) ===
set MKOPTIC_ENVIRONMENT=Development
dotnet run --project src\MkOptic\MkOptic.csproj -c Debug --no-build -- %*
set EXITCODE=%ERRORLEVEL%
echo === App exited with code %EXITCODE% ===
pause
exit /b %EXITCODE%

:error
echo === Build FAILED ===
pause
exit /b 1
