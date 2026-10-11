@echo off
setlocal
python "%~dp0..\scripts\codegen\gen_luban.py" %*
exit /b %errorlevel%
