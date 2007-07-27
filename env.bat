@echo off
REM set the root to the path of env.bat, regardless of where it was run from
SET FRACTURE_ROOT=%~dp0

REM set up the roots of various third party libraries
SET BOOST_ROOT=%FRACTURE_ROOT%\ext\boost
SET LUA_ROOT=%FRACTURE_ROOT%\ext\lua
SET LUABIND_ROOT=%FRACTURE_ROOT%\ext\luabind
SET SDL_ROOT=%FRACTURE_ROOT%\ext\sdl