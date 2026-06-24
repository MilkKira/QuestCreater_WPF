@echo off
setlocal

set "PROJECT_DIR=%~dp0"
set "OUTPUT_DIR=%PROJECT_DIR%publish-single"

dotnet publish "%PROJECT_DIR%QuestCreater_WPF.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "%OUTPUT_DIR%"

if errorlevel 1 (
  echo.
  echo Publish failed.
  exit /b 1
)

echo.
echo Published single-file app to:
echo %OUTPUT_DIR%\QuestCreater_WPF.exe

endlocal
