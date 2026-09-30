cd /d %~dp0
set LUBAN_EXE=%~dp0LubanGenerater\Luban\Luban.exe

rem Pass 1: C# typed code + binary data (M2 config pipeline).
rem 2026-09-28: 服务端同读这份 .bytes（RoomServer 源链接 Generated + Luban Runtime——
rem              原 Pass 1b 服务端 json 已废弃，RoomServer/Data 随之删除）。
%LUBAN_EXE% ^
    -t client ^
    -c cs-bin ^
    -d bin ^
    --conf %~dp0luban.conf ^
    -x outputCodeDir=E:\unityProject\Test\Assets\GameData\Generated ^
    -x outputDataDir=E:\unityProject\Test\Assets\GameData\Config

rem Pass 2: Lua data tables (consumed by Bridge.data in M3)
%LUBAN_EXE% ^
    -t client ^
    -d lua ^
    --conf %~dp0luban.conf ^
    -x outputDataDir=E:\unityProject\Test\Assets\LiteGame\Lua\cfg

rem Pass 3: registry Lua paths -> LuaKeys.g.cs (M3, fail on unknown root)
python gen_lua_keys.py
if %errorlevel% neq 0 exit /b %errorlevel%

rem Pass 4: restore asmdef under outputCodeDir (Luban clears outputCodeDir, which deletes
rem         Assets/GameData/Generated/Luban.Tables.asmdef every run -> 'cfg' types silently vanish).
rem         Long-term fix for a recurring pitfall; safe no-op when nothing was deleted.
rem         NOTE: git must run from the repo root (-C), since this script cd's into Luban/.
git -C "%~dp0.." checkout -- Assets/GameData/Generated/Luban.Tables.asmdef Assets/GameData/Generated/Luban.Tables.asmdef.meta 2>nul
exit /b 0
