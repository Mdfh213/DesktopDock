@echo off
chcp 936 >nul
pushd "%~dp0"
title DesktopDock - 同步到 GitHub

set "REMOTE=git@github.com:Mdfh213/DesktopDock.git"

echo ==========================================================
echo    DesktopDock  ^- 同步到 GitHub（走 SSH 443 端口）
echo ==========================================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [错误] 没找到 git.exe，请先安装 Git for Windows。
    goto :end
)

echo [1/3] 测试 GitHub SSH 通道...
ssh -T -o StrictHostKeyChecking=accept-new -o BatchMode=yes -o ConnectTimeout=12 git@github.com 2>"%TEMP%\dd_ssh.txt"
chcp 936 >nul 2>nul
findstr /c:"successfully authenticated" "%TEMP%\dd_ssh.txt" >nul 2>nul
if not errorlevel 1 goto :linked

echo.
echo [需要一次操作] 这台电脑的 SSH 公钥还没加到 GitHub。
echo   1. 复制下面这行公钥：
echo.
type "%USERPROFILE%\.ssh\id_ed25519_github.pub"
echo.
echo   2. 浏览器打开  https://github.com/settings/keys
echo      点 New SSH key，Title 随便填（例如 PC），Key 粘贴上面那行，保存
echo   3. 保存后重新双击本脚本
echo.
goto :end

:linked
echo        SSH 通道正常，账号已绑定。
echo.

echo [2/3] 配置远端仓库...
git remote remove github >nul 2>nul
git remote add github "%REMOTE%"
echo        %REMOTE%
echo.

echo [3/3] 推送全部分支...
git push github --all
if errorlevel 1 (
    echo.
    echo [失败] 推送没有成功，常见原因：
    echo    - GitHub 上还没有创建 DesktopDock 仓库（先去 https://github.com/new 建一个空仓库）
    echo    - 仓库名或账号名写错
    echo.
    goto :end
)
git push github --tags >nul 2>nul
echo.
echo [完成] https://github.com/Mdfh213/DesktopDock
echo.

:end
del "%TEMP%\dd_ssh.txt" >nul 2>nul
popd
pause
