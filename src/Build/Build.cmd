@echo off

REM  █████╗ ████████╗ ██████╗ ███╗   ███╗██╗██╗  ██╗     ██████╗ ███████╗
REM ██╔══██╗╚══██╔══╝██╔═══██╗████╗ ████║██║╚██╗██╔╝    ██╔═══██╗██╔════╝
REM ███████║   ██║   ██║   ██║██╔████╔██║██║ ╚███╔╝     ██║   ██║███████╗
REM ██╔══██║   ██║   ██║   ██║██║╚██╔╝██║██║ ██╔██╗     ██║   ██║╚════██║
REM ██║  ██║   ██║   ╚██████╔╝██║ ╚═╝ ██║██║██╔╝ ██╗    ╚██████╔╝███████║
REM ╚═╝  ╚═╝   ╚═╝    ╚═════╝ ╚═╝     ╚═╝╚═╝╚═╝  ╚═╝     ╚═════╝ ╚══════╝

REM CONFIGURATION
SET BUILD_CPU=x86
SET BUILD_NAME=AtomOS-Alfa
SET ATOMIX_APPS=.\Bin\Apps
SET OUTPUT_DIR=.\Output
SET ATOMIX_COMPILER_FLAGS=-v -optimize
SET ATOMIX_COMPILER=.\Bin\Atomixilc.exe
SET ATOMIX_ISO_DIR=.\ISO
SET ATOMIX_RD=.\ramdisk
SET ATOMIX_LIB=.\Bin\Atomix.Core.dll
SET ATOMIX_RamFS=.\Bin\Atomix.RamFS.exe
SET GCC_LIB=.\Local\lib

REM CONSTANTS
SET /A EXIT_CODE=0
SET /A ERROR_COMPILER_MISSING=1
SET /A ERROR_KERNEL_MISSING=2
SET /A ERROR_LINKER_SCRIPT_MISSING=4
SET /A ERROR_NASM_NOT_FOUND=8
SET /A ERROR_GEN_ISO_NOT_FOUND=16
SET /A ERROR_ELF_LINKER_NOT_FOUND=32
SET /A ERROR_READELF_NOT_FOUND=64
SET /A ERROR_COMPILER_NZEC=128
SET /A ERROR_NASM_NZEC=256
SET /A ERROR_LINKER_NZEC=512
SET /A ERROR_RAMDISK_NZEC=1024
SET /A ERROR_GENISO_NZEC=2048

REM Kernel_H CONFIGURATION
SET ATOMIX_KERNEL_H=.\Bin\Atomix.Kernel_H.dll
SET ATOMIX_KERNEL_H_LINKER=.\..\Kernel\Atomix.Kernel_H\linker.ld

REM Kernel_alpha CONFIGURATION
SET ATOMIX_KERNEL_alpha=.\Bin\Kernel_alpha.dll;.\Bin\Atomix.mscorlib.dll
SET ATOMIX_KERNEL_alpha_LINKER=.\..\Kernel\Kernel_alpha\linker.ld

IF "%1"=="Kernel_H" GOTO BUILD_KERNEL_H
IF "%1"=="Kernel_alpha" GOTO BUILD_KERNEL_alpha

:BUILD_KERNEL_H
SET ATOMIX_KERNEL=%ATOMIX_KERNEL_H%
SET ATOMIX_KERNEL_LINKER=%ATOMIX_KERNEL_H_LINKER%
GOTO BUILDER_START

:BUILD_KERNEL_alpha
SET ATOMIX_KERNEL=%ATOMIX_KERNEL_alpha%
SET ATOMIX_KERNEL_LINKER=%ATOMIX_KERNEL_alpha_LINKER%
GOTO BUILDER_START

:BUILDER_START

REM BASIC CHECKING
IF NOT EXIST %ATOMIX_COMPILER% SET /A EXIT_CODE^|=%ERROR_COMPILER_MISSING%
IF NOT EXIST %ATOMIX_KERNEL% SET /A EXIT_CODE^|=%ERROR_KERNEL_MISSING%
IF NOT EXIST %ATOMIX_KERNEL_LINKER% SET /A EXIT_CODE^|=%ERROR_LINKER_SCRIPT_MISSING%

REM ENVIRONMENT CHECKING
WHERE nasm.exe >nul 2>nul
IF %ERRORLEVEL% NEQ 0 SET /A EXIT_CODE^|=%ERROR_NASM_NOT_FOUND%
WHERE genisoimage.exe >nul 2>nul
IF %ERRORLEVEL% NEQ 0 SET /A EXIT_CODE^|=%ERROR_GEN_ISO_NOT_FOUND%
WHERE i386-atomos-ld.exe >nul 2>nul
IF %ERRORLEVEL% NEQ 0 SET /A EXIT_CODE^|=%ERROR_ELF_LINKER_NOT_FOUND%
WHERE readelf.exe >nul 2>nul
IF %ERRORLEVEL% NEQ 0 SET /A EXIT_CODE^|=%ERROR_READELF_NOT_FOUND%

IF /I "%EXIT_CODE%" NEQ "0" GOTO BUILDER_EXIT

REM BUILD KERNEL FIRST
%ATOMIX_COMPILER% -cpu %BUILD_CPU% -i %ATOMIX_KERNEL% -o %OUTPUT_DIR%\Kernel.asm %ATOMIX_COMPILER_FLAGS%
IF NOT EXIST %OUTPUT_DIR%\Kernel.asm SET /A EXIT_CODE^|=ERROR_COMPILER_NZEC & GOTO BUILDER_EXIT

nasm.exe -felf %OUTPUT_DIR%\Kernel.asm -o %ATOMIX_ISO_DIR%\Kernel.o
IF %ERRORLEVEL% NEQ 0 SET /A EXIT_CODE^|=ERROR_NASM_NZEC & GOTO BUILDER_EXIT

i386-atomos-ld %ATOMIX_ISO_DIR%\Kernel.o Local\i386-atomos\lib\crti.o %GCC_LIB%\gcc\i386-atomos\5.3.0\crtbegin.o %GCC_LIB%\libcairo.a %GCC_LIB%\libpng15.a %GCC_LIB%\libpixman-1.a %GCC_LIB%\libz.a %GCC_LIB%\libfreetype.a %GCC_LIB%\gcc\i386-atomos\5.3.0\crtend.o Local\i386-atomos\lib\crtn.o Local\i386-atomos\lib\libm.a %GCC_LIB%\gcc\i386-atomos\5.3.0\libgcc.a Local\i386-atomos\lib\libc.a -T %ATOMIX_KERNEL_LINKER% -o %ATOMIX_ISO_DIR%\Kernel.bin
IF %ERRORLEVEL% NEQ 0 SET /A EXIT_CODE^|=ERROR_LINKER_NZEC & GOTO BUILDER_EXIT

del %ATOMIX_ISO_DIR%\Kernel.o
readelf --wide --symbols %ATOMIX_ISO_DIR%\Kernel.bin > Kernel.map

REM BUILD APP ONE BY ONE
FOR %%I IN (%ATOMIX_APPS%\*.dll) DO (
    ECHO [APP] %%~nI
    %ATOMIX_COMPILER% -cpu %BUILD_CPU% -i %%I;%ATOMIX_LIB% -o %OUTPUT_DIR%\Apps\%%~nI.asm %ATOMIX_COMPILER_FLAGS%
    IF NOT EXIST %OUTPUT_DIR%\Apps\%%~nI.asm SET /A EXIT_CODE^|=ERROR_COMPILER_NZEC & GOTO BUILDER_EXIT

    nasm.exe -felf %OUTPUT_DIR%\Apps\%%~nI.asm -o %ATOMIX_RD%\%%~nI.o
    IF %ERRORLEVEL% NEQ 0 SET /A EXIT_CODE^|=ERROR_NASM_NZEC & GOTO BUILDER_EXIT
)

REM CREATE RAM DISK
%ATOMIX_RamFS% %ATOMIX_RD% -o %ATOMIX_ISO_DIR%\Initrd.bin
IF %ERRORLEVEL% NEQ 0 SET /A EXIT_CODE^|=ERROR_RAMDISK_NZEC & GOTO BUILDER_EXIT

REM CREATE ISO IMAGE
genisoimage.exe -o %OUTPUT_DIR%\%BUILD_NAME%.iso -b isolinux/isolinux.bin -c isolinux/boot.cat -no-emul-boot -boot-load-size 4 -input-charset utf-8 -boot-info-table %ATOMIX_ISO_DIR%
IF %ERRORLEVEL% NEQ 0 SET /A EXIT_CODE^|=ERROR_GENISO_NZEC & GOTO BUILDER_EXIT

:BUILDER_EXIT
IF /I "%EXIT_CODE%" NEQ "0" ECHO BUILD FAILED (%EXIT_CODE%)
IF /I "%EXIT_CODE%" EQU "0" ECHO BUILD SUCCESSFULLY
EXIT /B %EXIT_CODE%
