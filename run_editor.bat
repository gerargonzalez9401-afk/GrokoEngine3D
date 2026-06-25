@echo off
setlocal
cd /d "%~dp0"
echo === GrokoEngine ImGui Editor ===
dotnet run --project GrokoEngine.ImGuiEditor/GrokoEngine.ImGuiEditor.csproj -c Debug
exit /b %errorlevel%
