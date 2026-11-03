@echo off
chcp 936 >nul
pushd "%~dp0"
title DesktopDock - 同步到 GitHub

set "REMOTE=https://github.com/Mdfh213/DesktopDock.git"
set "GIT=git -c http.sslBackend=openssl"

echo ==========================================================
echo    DesktopDock  ^- 同步到 GitHub
echo ==========================================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [错误] 没找到 git.exe，请先安装 Git for Windows。
    goto :end
)

echo [1/4] 配置 GitHub 远端...
git remote remove github >nul 2>nul
git remote add github "%REMOTE%"
echo        %REMOTE%
echo.

echo [2/4] 连一下 GitHub（顺便检查仓库是否已创建）...
%GIT% ls-remote "%REMOTE%" >"%TEMP%\dd_gh.txt" 2>&1
if not errorlevel 1 goto :push

findstr /i /c:"could not resolve" /c:"connection was reset" /c:"failed to connect" /c:"timed out" /c:"unable to access" "%TEMP%\dd_gh.txt" >nul 2>nul
if not errorlevel 1 (
    echo.
    echo [网络不通] 连不上 GitHub，这不是仓库的问题。
    echo   国内直连 GitHub 经常被重置，需要走代理后再试：
    echo     1. 打开你的代理软件，记下本地端口（例如 7890）
    echo     2. 在本窗口执行一次：
    echo          git config --global http.proxy http://127.0.0.1:7890
    echo     3. 再双击本脚本
    echo   （以后想取消代理：git config --global --unset http.proxy）
    echo.
    goto :end
)

echo.
echo [仓库不存在] 远端仓库还没创建，请先：
echo    1. 浏览器打开  https://github.com/new
echo    2. Repository name 填  DesktopDock
echo    3. Public / Private 随意
echo    4. 不要勾选 Add a README / .gitignore / license
echo    5. 创建完成后重新双击本脚本
echo.
goto :end

:push
echo [3/4] 推送全部分支...
%GIT% push github --all
if errorlevel 1 (
    echo.
    echo [失败] 推送没有成功，常见原因：
    echo    - 弹窗时没有登录 GitHub 账号
    echo    - GitHub 账号名 Mdfh213 或仓库名 DesktopDock 写错
    echo    - 代理没开 / 网络中断
    echo.
    goto :end
)
%GIT% push github --tags >nul 2>nul

echo [4/4] 核对远端分支...
%GIT% ls-remote --heads "%REMOTE%"
echo.
echo [完成] https://github.com/Mdfh213/DesktopDock
echo.
echo 以后每次更新：先双击 push-to-gitee.bat，再双击本脚本，两个平台就同步了。
echo.

:end
del "%TEMP%\dd_gh.txt" >nul 2>nul
popd
pause