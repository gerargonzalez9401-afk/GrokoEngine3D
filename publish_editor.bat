@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PUBLISH_DIR=%~dp0publish\Editor"
set "PROJECT=GrokoEngine.ImGuiEditor\GrokoEngine.ImGuiEditor.csproj"
set "EXE=%PUBLISH_DIR%\GrokoEngine.ImGuiEditor.exe"

echo === GrokoEngine Editor Publish ===
echo Output: %PUBLISH_DIR%
echo.

echo [1/5] Restore
dotnet restore GrokoEngine.sln
if errorlevel 1 goto fail

echo.
echo [2/5] Build Release
dotnet build GrokoEngine.sln -c Release --no-restore
if errorlevel 1 goto fail

echo.
echo [3/5] Clean publish folder
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
mkdir "%PUBLISH_DIR%"
if errorlevel 1 goto fail

echo.
echo [4/5] Publish editor
dotnet publish "%PROJECT%" -c Release --no-build --self-contained false -o "%PUBLISH_DIR%"
if errorlevel 1 goto fail

echo.
echo [5/5] Validate publish output
if not exist "%EXE%" goto missing_exe
if not exist "%PUBLISH_DIR%\assimp.dll" goto missing_assimp
if not exist "%PUBLISH_DIR%\glfw3.dll" goto missing_glfw
if not exist "%PUBLISH_DIR%\cimgui.dll" goto missing_cimgui
if not exist "%PUBLISH_DIR%\Mimotor.Math.dll" goto missing_math

echo.
echo Publish OK.
echo Editor listo:
echo "%EXE%"
exit /b 0

:missing_exe
echo ERROR: falta GrokoEngine.ImGuiEditor.exe en "%PUBLISH_DIR%".
goto fail

:missing_assimp
echo ERROR: falta assimp.dll en "%PUBLISH_DIR%".
goto fail

:missing_glfw
echo ERROR: falta glfw3.dll en "%PUBLISH_DIR%".
goto fail

:missing_cimgui
echo ERROR: falta cimgui.dll en "%PUBLISH_DIR%".
goto fail

:missing_math
echo ERROR: falta Mimotor.Math.dll en "%PUBLISH_DIR%".
goto fail

:fail
echo.
echo Publish FALLO. Revisa el primer error arriba.
exit /b 1
