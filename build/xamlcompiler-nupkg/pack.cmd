@echo off
rem Packs into ..\..\PackageStore. Extra args are forwarded, e.g. -p:PackageVersion=3.0.0-dev.1
setlocal
pushd "%~dp0"
dotnet pack XamlCompilerPackage.vbproj -c Release -o ..\..\PackageStore %*
set _rc=%errorlevel%
popd
exit /b %_rc%
