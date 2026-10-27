@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\Tools\VsDevCmd.bat" -no_logo -arch=x64 -host_arch=x64
if errorlevel 1 exit /b 1
cd /d "%~dp0build"
cl /nologo /EHsc /std:c++17 /I . ..\api_offsets.cpp /Fe:api_offsets.exe
if errorlevel 1 exit /b 1
api_offsets.exe
