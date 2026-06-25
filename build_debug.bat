@echo off
setlocal
cd /d "%~dp0"
echo === GrokoEngine Debug Build ===
dotnet --info
if errorlevel 1 goto fail

dotnet restore GrokoEngine.sln
if errorlevel 1 goto fail

dotnet build GrokoEngine.sln -c Debug --no-restore
if errorlevel 1 goto fail

echo.
echo Build Debug OK.
exit /b 0
:fail
echo.
echo Build Debug FALLÓ. Revisa el primer error arriba.
exit /b 1
