@echo off
rem Builds the Lordran loader as dinput8.dll with llvm-mingw (output: <this folder>\build\dinput8.dll).
rem SRC is this script's folder and MinHook is ..\..\tools\minhook from it (the path trace.c and travel.c include), so the
rem same script builds the main tree (src\loader) and an agent sandbox copy (data\sandbox\NAME\src\loader, session 8).
setlocal
set W=C:\EchoesOfLordran
set CC=%W%\tools\llvm-mingw-20260922-ucrt-x86_64\bin\x86_64-w64-mingw32-clang.exe
set SRC=%~dp0
if "%SRC:~-1%"=="\" set SRC=%SRC:~0,-1%
for %%I in ("%SRC%\..\..\tools\minhook") do set MH=%%~fI
if not exist "%SRC%\build" mkdir "%SRC%\build"
"%CC%" -O2 -Wall -Wextra -shared -DLORDRAN_DEV -I"%MH%\include" -o "%SRC%\build\dinput8.dll" "%SRC%\main.c" "%SRC%\proxy.c" "%SRC%\vfs.c" "%SRC%\winfs.c" "%SRC%\regulation.c" "%SRC%\autopilot.c" "%SRC%\trace.c" "%SRC%\entry.c" "%SRC%\devkit.c" "%SRC%\travel.c" "%SRC%\gesture.c" "%SRC%\mapcap.c" "%SRC%\stream.c" "%SRC%\dummyguard.c" "%SRC%\nmbpool.c" "%SRC%\log.c" "%MH%\src\hook.c" "%MH%\src\buffer.c" "%MH%\src\trampoline.c" "%MH%\src\hde\hde64.c" "%SRC%\dinput8.def" -Wl,--no-insert-timestamp
exit /b %ERRORLEVEL%
