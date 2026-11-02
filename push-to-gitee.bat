@echo off
chcp 936 >nul
pushd "%~dp0"
title DesktopDock - 推送到 Gitee

set "REMOTE=https://gitee.com/Dusk_Luminance/DesktopDock.git"
set "GIT=git -c http.sslBackend=openssl"

echo ==========================================================
echo    DesktopDock  ^- 推送到 Gitee
echo ==========================================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [错误] 没找到 git.exe，请先安装 Git for Windows。
    goto :end
)

echo [1/4] 配置远端仓库...
git remote remove origin >nul 2>nul
git remote add origin "%REMOTE%"
echo        %REMOTE%
echo.

echo [2/4] 检查远端仓库是否已创建...
%GIT% ls-remote "%REMOTE%" >nul 2>nul
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

echo [3/4] 同步远端提交...
%GIT% fetch origin >nul 2>nul
echo.

echo [4/4] 推送 main 分支...
%GIT% push -u origin main
if not errorlevel 1 goto :ok

echo.
echo [提示] 直接推送被拒绝，可能远端有你本地没有的提交。
echo        正在尝试「先合并远端、再重推」，稍等...
%GIT% pull --rebase origin main
if errorlevel 1 (
    echo.
    echo [冲突] 本地与远端的历史已分叉，自动合并失败。
    echo        请把这段输出发给开发者处理。
    echo.
    goto :end
)
%GIT% push -u origin main
if not errorlevel 1 goto :ok
goto :fail

:ok
echo.
echo [完成] https://gitee.com/Dusk_Luminance/DesktopDock
echo.
goto :end

:fail
echo.
echo [失败] 推送没有成功，常见原因：
echo    - 弹窗时没有登录 Gitee 账号
echo    - 仓库路径或账号名写错
echo    - 网络/代理问题
echo.

:end
popd
pause