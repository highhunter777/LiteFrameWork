@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\codegen\gen-luban.ps1" %*
exit /b %errorlevel%
