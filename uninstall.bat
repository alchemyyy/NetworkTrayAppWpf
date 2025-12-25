@echo off
setlocal enabledelayedexpansion

:: Check for admin privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo This uninstaller requires administrator privileges.
    echo Please run as administrator.
    pause
    exit /b 1
)

set "INSTALL_DIR=C:\Program Files\NetworkTrayAppWpf"
set "APP_NAME=NetworkTrayAppWpf"
set "EXE_NAME=NetworkTrayAppWpf.exe"
set "UNINSTALL_KEY=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\%APP_NAME%"
set "QUIET_MODE=0"

:: Check for quiet mode
if /i "%~1"=="/quiet" set "QUIET_MODE=1"
if /i "%~1"=="/q" set "QUIET_MODE=1"
if /i "%~1"=="/silent" set "QUIET_MODE=1"

if %QUIET_MODE%==0 (
    echo.
    echo ========================================
    echo  NetworkTrayAppWpf Uninstaller
    echo ========================================
    echo.
    echo This will completely remove NetworkTrayAppWpf from your system.
    echo.
    choice /c YN /m "Are you sure you want to uninstall"
    if !errorlevel! neq 1 (
        echo Uninstall cancelled.
        pause
        exit /b 0
    )
    echo.
)

:: Stop the application if running
echo Stopping running instances...
taskkill /f /im "%EXE_NAME%" >nul 2>&1
timeout /t 2 /nobreak >nul

:: Remove from startup (registry)
echo Removing startup entries...
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "%APP_NAME%" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "%APP_NAME%" /f >nul 2>&1

:: Remove registry entries (Add/Remove Programs)
echo Removing registry entries...
reg delete "%UNINSTALL_KEY%" /f >nul 2>&1

:: Remove settings from AppData
echo Removing application data...
set "APPDATA_DIR=%LOCALAPPDATA%\NetworkTrayAppWpf"
if exist "%APPDATA_DIR%" (
    rmdir /s /q "%APPDATA_DIR%" >nul 2>&1
)

:: Also check for settings in the same location as exe (legacy)
if exist "%INSTALL_DIR%\settings.json" (
    del /f /q "%INSTALL_DIR%\settings.json" >nul 2>&1
)

:: Remove installation directory
echo Removing application files...
if exist "%INSTALL_DIR%" (
    :: First try to remove all known files
    del /f /q "%INSTALL_DIR%\%EXE_NAME%" >nul 2>&1
    del /f /q "%INSTALL_DIR%\app.ico" >nul 2>&1
    del /f /q "%INSTALL_DIR%\*.dll" >nul 2>&1
    del /f /q "%INSTALL_DIR%\*.json" >nul 2>&1
    del /f /q "%INSTALL_DIR%\*.pdb" >nul 2>&1

    :: Try to remove the directory itself
    :: Note: Can't delete uninstall.bat while it's running, so schedule it
    rmdir /s /q "%INSTALL_DIR%" >nul 2>&1

    :: If directory still exists (because uninstall.bat is in it), schedule cleanup
    if exist "%INSTALL_DIR%" (
        :: Create a self-deleting cleanup script
        (
            echo @echo off
            echo timeout /t 3 /nobreak ^>nul
            echo rmdir /s /q "%INSTALL_DIR%"
            echo del /f /q "%%~f0"
        ) > "%TEMP%\cleanup_networktray.bat" 2>nul
        if exist "%TEMP%\cleanup_networktray.bat" (
            start "" /min cmd /c "%TEMP%\cleanup_networktray.bat" >nul 2>&1
        )
    )
) else (
    :: Installation directory doesn't exist, nothing to remove
    echo Installation directory not found, skipping...
)

if %QUIET_MODE%==0 (
    echo.
    echo ========================================
    echo  Uninstallation Complete!
    echo ========================================
    echo.
    echo NetworkTrayAppWpf has been removed from your system.
    echo.
    echo This window will close in 5 seconds, press any key to keep open...
    timeout /t 5 >nul
    if !errorlevel!==1 pause >nul
)

exit /b 0
