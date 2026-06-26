@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo === GrokoEngine PRO Verify ===
echo Este script hace clean, restore, build, tests, publish y smoke test.
echo.

echo [1/8] .NET SDK
dotnet --info
if errorlevel 1 goto fail

echo.
echo [2/8] Clean
dotnet clean GrokoEngine.sln -c Debug
if errorlevel 1 goto fail
dotnet clean GrokoEngine.sln -c Release
if errorlevel 1 goto fail

echo.
echo [3/8] Restore
dotnet restore GrokoEngine.sln
if errorlevel 1 goto fail

echo.
echo [4/8] Build Debug
dotnet build GrokoEngine.sln -c Debug --no-restore
if errorlevel 1 goto fail

echo.
echo [5/8] Tests
dotnet run --project GrokoEngine.Tests/GrokoEngine.Tests.csproj -c Debug --no-build
if errorlevel 1 goto fail

echo.
echo [6/8] Build Release
dotnet build GrokoEngine.sln -c Release --no-restore
if errorlevel 1 goto fail

echo.
echo [7/8] Publish Editor
call "%~dp0publish_editor.bat"
if errorlevel 1 goto fail

echo.
echo [8/8] Smoke Editor
call "%~dp0smoke_editor.bat"
if errorlevel 1 goto fail

echo.
echo VERIFY PRO OK: clean + restore + builds + tests + publish + smoke pasaron.
exit /b 0

:fail
echo.
echo VERIFY PRO FALLO. Corrige el primer error mostrado arriba y vuelve a ejecutar verify_pro.bat.
exit /b 1
