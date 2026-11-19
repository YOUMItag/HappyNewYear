@echo off
rem ============================================================
rem  Run a Unity/Tuanjie batchmode method, with the log ALWAYS
rem  written inside  <project>\Logs\batchmode\  - never outside
rem  the project folder.
rem
rem  Usage:
rem    build-batchmode.bat                  -> build Windows player
rem    build-batchmode.bat <executeMethod>  -> run any editor method
rem    build-batchmode.bat --check          -> print resolved paths only
rem ============================================================
setlocal EnableExtensions

rem Project root = one level up from this script's folder (Tools\)
pushd "%~dp0.."
set "PROJ=%CD%"
popd

set "LOGDIR=%PROJ%\Logs\batchmode"
set "EDITOR=D:\Unity\2022.3.62t14\Editor\Tuanjie.exe"
set "METHOD=%~1"
if "%METHOD%"=="" set "METHOD=Game.EditorTools.BuildScript.BuildWindowsFromCommandLine"

for /f "tokens=1-4 delims=/-. " %%a in ("%date%") do set "D=%%a%%b%%c"
set "T=%time: =0%"
set "STAMP=%D%-%T:~0,2%%T:~3,2%"
set "LOG=%LOGDIR%\batch_%STAMP%.log"

if /i "%METHOD%"=="--check" (
  echo project : %PROJ%
  echo editor  : %EDITOR%
  echo log dir : %LOGDIR%
  echo log file: %LOG%
  exit /b 0
)

if not exist "%LOGDIR%" mkdir "%LOGDIR%"
if not exist "%EDITOR%" (
  echo [ERROR] editor not found: %EDITOR%
  echo         edit the EDITOR= line in this file if the path changed.
  exit /b 1
)

echo project: %PROJ%
echo method : %METHOD%
echo log    : %LOG%
echo.

"%EDITOR%" -batchmode -quit -nographics -projectPath "%PROJ%" -executeMethod %METHOD% -logFile "%LOG%"

echo.
echo exit code: %errorlevel%
echo log written to: %LOG%
