@echo off
rem Builds a fresh DynamicIsland.exe and packs it with fonts\ into dist\DynamicIsland-v<version>.zip,
rem ready to attach to a GitHub release tagged v<version>. The version comes from AppInfo.Version in Updater.cs.
setlocal
cd /d "%~dp0"

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Select-String -Path Updater.cs -Pattern 'Version = .([0-9.]+).').Matches[0].Groups[1].Value"`) do set VER=%%v
if "%VER%"=="" (echo Could not read AppInfo.Version from Updater.cs & exit /b 1)
echo Releasing v%VER%

rem the exe can't be rebuilt while it runs: ask a running island to quit
tasklist /FI "IMAGENAME eq DynamicIsland.exe" | find /I "DynamicIsland.exe" >nul
if not errorlevel 1 (
  echo Closing the running island...
  if exist "%~dp0DynamicIsland.exe" start "" /wait "%~dp0DynamicIsland.exe" --ipc quit
  ping -n 4 127.0.0.1 >nul
  taskkill /IM DynamicIsland.exe /F >nul 2>&1
  ping -n 2 127.0.0.1 >nul
)

call "%~dp0build.bat" || exit /b 1

powershell -NoProfile -Command ^
  "$v=(Get-Item DynamicIsland.exe).VersionInfo.ProductVersion;" ^
  "if ($v -ne '%VER%') { Write-Host ('Built exe reports version ' + $v + ', expected %VER%. Not packing.'); exit 1 };" ^
  "New-Item -ItemType Directory -Force dist | Out-Null;" ^
  "$z='dist\DynamicIsland-v%VER%.zip'; if (Test-Path $z) { Remove-Item $z };" ^
  "Compress-Archive -Path DynamicIsland.exe, fonts -DestinationPath $z;" ^
  "Write-Host ('OK: ' + $z + '  (' + [math]::Round((Get-Item $z).Length/1MB,1) + ' MB)')"
if errorlevel 1 exit /b 1

echo.
echo Next: on GitHub create a release with tag v%VER% and attach dist\DynamicIsland-v%VER%.zip
