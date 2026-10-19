@echo off
rem Runs Lordran.Tools. Build first: tools\dotnet\dotnet.exe build src\Lordran.Tools -c Release
set DOTNET_ROOT=C:\EchoesOfLordran\tools\dotnet
"%DOTNET_ROOT%\dotnet.exe" "C:\EchoesOfLordran\src\Lordran.Tools\bin\Release\net10.0\Lordran.Tools.dll" %*
