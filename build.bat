@echo off
CALL env.bat
msbuild "%FRACTURE_ROOT%\proj\fracture.sln" /v:m /nologo %*
echo __________________________________________________
