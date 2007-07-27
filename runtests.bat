@echo off
CALL build.bat /target:Tests
IF ERRORLEVEL 1 (
    GOTO :eof
)
pushd bin
tests %*
popd
:eof