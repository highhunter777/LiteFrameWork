@echo off
rem Tester entry point: double-click this file.
rem ASCII-only on purpose: cmd.exe parses .cmd in the system OEM code page, so non-ASCII
rem text here would be mojibake. Chinese prompts come from Join-LiteGameTest.ps1; this
rem launcher only switches the console to UTF-8 first so they render correctly.
chcp 65001 >nul
setlocal
cd /d "%~dp0"
echo.
echo Preparing the online test environment. Please wait...
echo A Windows permission prompt may appear -- click "Yes".
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Join-LiteGameTest.ps1" %*
set EXITCODE=%ERRORLEVEL%
echo.
echo Exit code: %EXITCODE%
pause
