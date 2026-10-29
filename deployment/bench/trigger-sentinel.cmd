@echo off
rem Double-clickable wrapper: Windows 10 refuses to run a .ps1 by default (execution policy
rem Restricted). Bypass applies to this one PowerShell process only, as install.ps1's command does.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0trigger-sentinel.ps1"
