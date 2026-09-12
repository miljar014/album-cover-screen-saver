@echo off
title Album Cover Screen Saver - run it

cd /d "%~dp0"

set "SCR=%~dp0src\AlbumCoverScreenSaver.Saver\bin\Debug\net8.0-windows\AlbumCoverScreenSaver.scr"

if not exist "%SCR%" (
  echo.
  echo  It has not been built yet. Run BUILD.bat first, then come back here.
  echo.
  pause
  exit /b 1
)

echo.
echo  Starting the screen saver full screen.
echo.
echo  Move the mouse or press a key to quit it and come back here.
echo.

start /wait "" "%SCR%" /s

echo.
echo ============================================================
echo  Back. Here is what the saver wrote about the run:
echo.

copy /y "%LOCALAPPDATA%\AlbumCoverScreenSaver\saver.log" "%~dp0saver.log" >nul 2>&1

if exist "%~dp0saver.log" (
  type "%~dp0saver.log"
) else (
  echo  Nothing was written, which is itself a clue. Tell the Claude session.
)

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
