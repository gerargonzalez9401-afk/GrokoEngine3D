@echo off
setlocal
cd /d "%~dp0"
echo === GrokoEngine Release Build ===
dotnet restore GrokoEngine.sln
if errorlevel 1 goto fail

dotnet build GrokoEngine.sln -c Release --no-restore
if errorlevel 1 goto fail

echo.
echo Build Release OK.
exit /b 0
:fail
echo.
echo Build Release FALLÓ. Revisa el primer error arriba.
exit /b 1
