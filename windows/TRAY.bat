@echo off
title Album Cover Screen Saver - tray app

cd /d "%~dp0"

set "EXE=%~dp0src\AlbumCoverScreenSaver.Tray\bin\Debug\net8.0-windows10.0.19041.0\AlbumCoverScreenSaver.Tray.exe"

if not exist "%EXE%" (
  echo.
  echo  It has not been built yet. Run BUILD.bat first, then come back here.
  echo.
  pause
  exit /b 1
)

echo.
echo  Starting the tray app.
echo.
echo  Look for the record icon near the clock, at the right of the taskbar.
echo  You may have to click the little arrow to show hidden icons.
echo  Right-click it to see what it is doing.
echo.
echo  It keeps running after this window closes. Quit it from its own menu.
echo.

start "" "%EXE%"

echo  Play some music for a minute, then press a key here to collect the log.
echo.
pause

copy /y "%LOCALAPPDATA%\AlbumCoverScreenSaver\tray.log" "%~dp0tray.log" >nul 2>&1

echo.
echo ============================================================
echo  What the tray app wrote:
echo.

if exist "%~dp0tray.log" (
  type "%~dp0tray.log"
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
