@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
if errorlevel 1 (
  echo vcvars64.bat not found - install the Visual Studio C++ build tools
  exit /b 1
)
set SRC=%~dp0
set OUT=%SRC%..\..\RageLightEditor\Assets\fivem
if not exist "%SRC%obj" mkdir "%SRC%obj"
pushd "%SRC%obj"
cl /nologo /O2 /W3 /std:c++17 /EHa /MT /D_CRT_SECURE_NO_WARNINGS "%SRC%RageToolsLive.cpp" /link /DLL /OUT:"%OUT%\RageToolsLive.asi" kernel32.lib
set RC=%errorlevel%
popd
if %RC% neq 0 (
  echo build failed
  exit /b %RC%
)
echo built "%OUT%\RageToolsLive.asi"
