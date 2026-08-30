@echo off
setlocal
chcp 65001 >nul
title 构建路遥电脑控制器
set "BUILD_SCRIPT=%~dp0scripts\build-windows.ps1"
set "BUILD_LOG=%~dp0build.log"

echo ============================================================
echo               路遥电脑控制器 Windows 构建工具
echo ============================================================
echo.

if not exist "%BUILD_SCRIPT%" (
  echo [错误] 没有找到 scripts\build-windows.ps1
  echo.
  echo 你很可能是在压缩包预览窗口里直接双击了本文件。
  echo 请先右键压缩包，选择“全部解压”，再进入解压后的文件夹运行。
  echo.
  pause
  exit /b 2
)

echo 构建日志将保存到：%BUILD_LOG%
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%BUILD_SCRIPT%"
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%EXIT_CODE%"=="0" (
  echo [失败] 构建没有完成，错误代码：%EXIT_CODE%
  echo 请把同一文件夹中的 build.log 发给我，我可以继续定位。
) else (
  echo [成功] Windows 程序已经生成。
  echo 请查看 dist\win-x64\LooyWindowsController.exe
)
echo.
echo 按任意键关闭此窗口……
pause >nul
exit /b %EXIT_CODE%
