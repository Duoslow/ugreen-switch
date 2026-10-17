@echo off
rem Toggle UGREEN DP switch via NodeMCU. Optional argument: 1, 2 or state
set HOST=192.168.4.107
set CMD=press
if not "%~1"=="" set CMD=%~1

curl -s -m 5 http://%HOST%/%CMD% >nul
if errorlevel 1 (
    echo Switch not reachable at %HOST%
    pause
)
