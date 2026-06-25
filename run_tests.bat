@echo off
setlocal
cd /d "%~dp0"
echo === GrokoEngine Tests ===
dotnet run --project GrokoEngine.Tests/GrokoEngine.Tests.csproj -c Debug
exit /b %errorlevel%
