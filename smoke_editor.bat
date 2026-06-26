@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "EXE=%~dp0publish\Editor\GrokoEngine.ImGuiEditor.exe"
set "SMOKE_PROJECT=%~dp0publish\SmokeProject"

echo === GrokoEngine Editor Smoke Test ===

if not exist "%EXE%" (
    echo No existe el editor publicado. Ejecutando publish_editor.bat primero...
    call "%~dp0publish_editor.bat"
    if errorlevel 1 goto fail
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$exe = '%EXE%';" ^
  "$project = '%SMOKE_PROJECT%';" ^
  "New-Item -ItemType Directory -Force -Path (Join-Path $project 'Assets') | Out-Null;" ^
  "$work = Split-Path $exe;" ^
  "$p = Start-Process -FilePath $exe -ArgumentList @('\"' + $project + '\"') -WorkingDirectory $work -PassThru;" ^
  "Start-Sleep -Seconds 5;" ^
  "$alive = Get-Process -Id $p.Id -ErrorAction SilentlyContinue;" ^
  "if (-not $alive) { $alive = Get-Process GrokoEngine.ImGuiEditor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -First 1 }" ^
  "if ($alive) { Write-Host ('Smoke OK: editor arranco. Id=' + $alive.Id + ' Title=' + $alive.MainWindowTitle); Stop-Process -Id $alive.Id -Force; exit 0 }" ^
  "else { Write-Error 'Smoke FALLO: el editor se cerro antes de 5 segundos.'; exit 1 }"
if errorlevel 1 goto fail

echo.
echo Smoke editor OK.
exit /b 0

:fail
echo.
echo Smoke editor FALLO.
exit /b 1
