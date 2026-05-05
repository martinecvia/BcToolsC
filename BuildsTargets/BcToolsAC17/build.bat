@echo off
setlocal

REM ================== PATHS ==================
set ROOT=C:\Users\coplak\source\repos\martinecvia\BcToolsC
set TOOLSDIR=%ROOT%\packages\ILRepack.2.0.44\tools
set BUILDDIR=%ROOT%\BuildsTargets\BcToolsAC17
set OUTPUTDIR=%BUILDDIR%\bin\Debug
set LIBDIR=%ROOT%\CadReferences\2017

set ILREPACK=%TOOLSDIR%\ILRepack.exe
set ASSEMBLY=BcToolsAC17.0

REM ================== FILES ==================
set PRIMARY=%OUTPUTDIR%\%ASSEMBLY%.dll
set TEMP=%OUTPUTDIR%\%ASSEMBLY%.ilrepack.dll
set FINAL=%OUTPUTDIR%\%ASSEMBLY%.dll

set DEP1=%OUTPUTDIR%\System.Buffers.dll
set DEP2=%OUTPUTDIR%\System.Memory.dll
set DEP3=%OUTPUTDIR%\System.Numerics.Vectors.dll
set DEP4=%OUTPUTDIR%\System.Runtime.CompilerServices.Unsafe.dll

REM ================== START ==================
echo === Listing Debug directory ===
dir "%OUTPUTDIR%"

echo.
echo === Running ILRepack ===

"%ILREPACK%" ^
 /out:"%TEMP%" ^
 "%PRIMARY%" ^
 "%DEP1%" ^
 "%DEP2%" ^
 "%DEP3%" ^
 "%DEP4%" ^
 /internalize ^
 /union ^
 /parallel ^
 /lib:"%LIBDIR%" ^
 /lib:"%OUTPUTDIR%"

IF ERRORLEVEL 1 (
    echo.
    echo !!! ILRepack FAILED !!!
    exit /b 1
)

echo.
echo === Replacing original DLL ===
del "%FINAL%"
move "%TEMP%" "%FINAL%"

echo.
echo === Cleaning merged dependencies ===
del "%DEP1%"
del "%DEP1:.dll=.xml%"
del "%DEP2%"
del "%DEP2:.dll=.xml%"
del "%DEP3%"
del "%DEP3:.dll=.xml%"
del "%DEP4%"
del "%DEP4:.dll=.xml%"

echo.
echo === ILRepack completed successfully ===
endlocal
pause