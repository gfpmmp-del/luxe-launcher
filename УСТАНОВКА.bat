@echo off
title Setup - NO install needed
cd /d "%~dp0"
echo ==========================================
echo  NOTHING TO INSTALL!
echo  Python NOT needed. Java NOT needed to install.
echo  Just run ZAPUSK.bat:
echo   - portable Java downloads itself to .jdk\
echo   - Minecraft downloads itself to .minecraft\
echo   - no admin, no reboot, no winget
echo ==========================================
echo.
choice /C YN /M "Start launcher now"
if errorlevel 2 goto :end
if errorlevel 1 call "%~dp0ZAPUSK.bat"
:end
pause
