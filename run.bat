@echo off
CALL build.bat
IF ERRORLEVEL 1 (
    GOTO :eof
)
pushd bin
fracture %*
popd
:eof