@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo === GrokoEngine PRO Verify ===
echo Este script hace clean, restore, build y tests.
echo.

echo [1/5] .NET SDK
dotnet --info
if errorlevel 1 goto fail

echo.
echo [2/5] Clean
dotnet clean GrokoEngine.sln -c Debug
if errorlevel 1 goto fail

echo.
echo [3/5] Restore
dotnet restore GrokoEngine.sln
if errorlevel 1 goto fail

echo.
echo [4/5] Build Debug
dotnet build GrokoEngine.sln -c Debug --no-restore
if errorlevel 1 goto fail

echo.
echo [5/5] Tests
dotnet run --project GrokoEngine.Tests/GrokoEngine.Tests.csproj -c Debug --no-build
if errorlevel 1 goto fail

echo.
echo VERIFY PRO OK: clean + restore + build + tests pasaron.
exit /b 0

:fail
echo.
echo VERIFY PRO FALLO. Corrige el primer error mostrado arriba y vuelve a ejecutar verify_pro.bat.
exit /b 1
