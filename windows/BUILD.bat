@echo off
title Album Cover Screen Saver - build and run

cd /d "%~dp0"

echo.
echo  Album Cover Screen Saver, Windows port
echo  Building the screen saver and offering to run it.
echo.
echo  This window will stay open when it finishes. Read what it says.
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"

echo.
echo ============================================================
echo  Finished.
echo  A copy of everything above was saved as build.log
echo  in this same folder, next to saver.log if one was written.
echo.

echo.
echo  This window will not close on a stray keypress.
echo  Drag over any text with the mouse and press Enter to copy it.
echo.

:askclose
set "answer="
set /p "answer=Close this window? Type y and press Enter: "
if /i "%answer%"=="y" goto :eof
echo.
echo  Still open. Copy what you need, then type y and press Enter.
goto askclose
