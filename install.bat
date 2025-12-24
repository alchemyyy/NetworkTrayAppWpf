@echo off
setlocal enabledelayedexpansion

:: Check for admin privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo This installer requires administrator privileges.
    echo Please run as administrator.
    pause
    exit /b 1
)

set "INSTALL_DIR=C:\Program Files\NetworkTrayAppWpf"
set "APP_NAME=NetworkTrayAppWpf"
set "EXE_NAME=NetworkTrayAppWpf.exe"
set "UNINSTALL_KEY=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\%APP_NAME%"

echo.
echo ========================================
echo  NetworkTrayAppWpf Installer
echo ========================================
echo.

:: Get the directory where this script is located (source files)
set "SOURCE_DIR=%~dp0"
set "SOURCE_DIR=%SOURCE_DIR:~0,-1%"

:: Check if source exe exists (script should be alongside the exe)
if exist "%SOURCE_DIR%\%EXE_NAME%" (
    set "BUILD_DIR=%SOURCE_DIR%"
) else (
    echo ERROR: Cannot find %EXE_NAME%
    echo The installer must be in the same folder as %EXE_NAME%.
    pause
    exit /b 1
)

echo Source: %BUILD_DIR%
echo Destination: %INSTALL_DIR%
echo.

:: Stop the application if running
echo Stopping running instances...
taskkill /f /im "%EXE_NAME%" >nul 2>&1
timeout /t 1 /nobreak >nul

:: Create installation directory
echo Creating installation directory...
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%"
    if %errorlevel% neq 0 (
        echo ERROR: Failed to create installation directory.
        pause
        exit /b 1
    )
)

:: Copy application files
echo Copying application files...
copy /y "%BUILD_DIR%\%EXE_NAME%" "%INSTALL_DIR%\" >nul
if %errorlevel% neq 0 (
    echo ERROR: Failed to copy main executable.
    pause
    exit /b 1
)

:: Copy icon file if it exists
if exist "%BUILD_DIR%\app.ico" (
    copy /y "%BUILD_DIR%\app.ico" "%INSTALL_DIR%\" >nul
)

:: Create uninstaller in install directory
echo Creating uninstaller...
copy /y "%SOURCE_DIR%\uninstall.bat" "%INSTALL_DIR%\" >nul 2>&1

:: Register in Add/Remove Programs
echo Registering application...

:: Get file version (use PowerShell for reliability)
for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "(Get-Item '%INSTALL_DIR%\%EXE_NAME%').VersionInfo.FileVersion"`) do set "VERSION=%%v"
if "%VERSION%"=="" set "VERSION=1.0.0.0"

:: Get current date for InstallDate
for /f "tokens=1-3 delims=/" %%a in ('echo %date%') do set "INSTALL_DATE=%%c%%a%%b"

:: Calculate estimated size in KB
for %%A in ("%INSTALL_DIR%\%EXE_NAME%") do set /a "SIZE_KB=%%~zA / 1024"

:: Add registry entries
reg add "%UNINSTALL_KEY%" /v "DisplayName" /t REG_SZ /d "Network Tray App" /f >nul
reg add "%UNINSTALL_KEY%" /v "DisplayVersion" /t REG_SZ /d "%VERSION%" /f >nul
reg add "%UNINSTALL_KEY%" /v "Publisher" /t REG_SZ /d "NetworkTrayAppWpf" /f >nul
reg add "%UNINSTALL_KEY%" /v "InstallLocation" /t REG_SZ /d "%INSTALL_DIR%" /f >nul
reg add "%UNINSTALL_KEY%" /v "InstallDate" /t REG_SZ /d "%INSTALL_DATE%" /f >nul
reg add "%UNINSTALL_KEY%" /v "UninstallString" /t REG_SZ /d "\"%INSTALL_DIR%\uninstall.bat\"" /f >nul
reg add "%UNINSTALL_KEY%" /v "QuietUninstallString" /t REG_SZ /d "\"%INSTALL_DIR%\uninstall.bat\" /quiet" /f >nul
reg add "%UNINSTALL_KEY%" /v "DisplayIcon" /t REG_SZ /d "%INSTALL_DIR%\%EXE_NAME%" /f >nul
reg add "%UNINSTALL_KEY%" /v "EstimatedSize" /t REG_DWORD /d %SIZE_KB% /f >nul
reg add "%UNINSTALL_KEY%" /v "NoModify" /t REG_DWORD /d 1 /f >nul
reg add "%UNINSTALL_KEY%" /v "NoRepair" /t REG_DWORD /d 1 /f >nul

echo.
echo ========================================
echo  Installation Complete!
echo ========================================
echo.
echo Installed to: %INSTALL_DIR%
echo.
echo To start the application, run:
echo   "%INSTALL_DIR%\%EXE_NAME%"
echo.
echo To uninstall, use Add/Remove Programs or run:
echo   "%INSTALL_DIR%\uninstall.bat"
echo.

:: Ask if user wants to start the app
choice /c YN /m "Start NetworkTrayAppWpf now"
if %errorlevel%==1 (
    echo Starting application...
    start "" "%INSTALL_DIR%\%EXE_NAME%"
)

echo.
pause
exit /b 0
