@echo off
chcp 936 >nul
pushd "%~dp0"
title DesktopDock - 推送到 Gitee

set "REMOTE=https://gitee.com/Dusk_Luminance/DesktopDock.git"

echo ==========================================================
echo    DesktopDock  ^- 推送到 Gitee
echo ==========================================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [错误] 没找到 git.exe，请先安装 Git for Windows。
    goto :end
)

echo [1/3] 配置远端仓库...
git remote remove origin >nul 2>nul
git remote add origin "%REMOTE%"
echo        %REMOTE%
echo.

echo [2/3] 检查远端仓库是否已创建...
git -c http.sslBackend=openssl ls-remote "%REMOTE%" >nul 2>nul
if errorlevel 1 (
    echo.
    echo [需要你操作] 远端仓库还不存在，请先创建：
    echo    1. 浏览器打开  https://gitee.com/projects/new
    echo    2. 仓库名填     DesktopDock
    echo    3. 路径填       Dusk_Luminance/DesktopDock
    echo    4. 不要勾选任何「使用 Readme 初始化」之类的选项
    echo    5. 创建完成后再双击本脚本
    echo.
    goto :end
)

echo [3/3] 推送 main 分支...
git -c http.sslBackend=openssl push -u origin main
if errorlevel 1 (
    echo.
    echo [失败] 推送没有成功，常见原因：
    echo    - 弹窗时没有登录 Gitee 账号
    echo    - 仓库路径或账号名写错
    echo    - 网络/代理问题
    echo.
    goto :end
)

echo.
echo [完成] https://gitee.com/Dusk_Luminance/DesktopDock
echo.

:end
popd
pause