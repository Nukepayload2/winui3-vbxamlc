@echo off
rem Packs into ..\..\PackageStore. Releases pass the version explicitly, e.g. -p:PackageVersion=3.0.0-dev.260913.1
setlocal
pushd "%~dp0"
dotnet pack XamlCompilerPackage.vbproj -c Release -o ..\..\PackageStore %*
set _rc=%errorlevel%
popd
exit /b %_rc%
