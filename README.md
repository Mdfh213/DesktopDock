# DesktopDock · 桌面侧边栏

[![Version](https://img.shields.io/badge/version-1.1.0-blue.svg)](#)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078D4.svg)](#)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Size](https://img.shields.io/badge/size-62%20KB-lightgrey.svg)](#)
[![Dependencies](https://img.shields.io/badge/dependencies-none-success.svg)](#)
[![Build](https://img.shields.io/badge/build-csc.exe%20only-orange.svg)](#重新编译)

轻量、简约的桌面侧边栏启动器。把图标收进屏幕左右两侧的抽屉里，鼠标贴边滑出，点一下就启动。

**单文件 · 零依赖 · 约 62 KB · 免安装 · 只用系统自带的 `csc.exe` 就能编译**

![DesktopDock 侧边栏面板](docs/panel.jpg)

> **面向使用者的完整说明：[使用说明.md](使用说明.md)**（安装、图标操作、滚动、托盘菜单、常见问题）
> 本文档偏开发者视角：功能、架构、配置字段、已知限制。

---

## 截图

把几个图标长按拖到一起，就会合并成这样的**分组格子**（2×2 迷你预览）：

![分组格子](docs/group.jpg)

---


## 功能

| 功能 | 说明 |
|---|---|
| **边缘唤出** | 鼠标移动到屏幕**左/右边缘**（6px 热区）即可唤出侧边栏，移开自动收起 |
| **隐藏桌面图标** | 启动时隐藏桌面图标，退出时自动恢复；托盘菜单可随时开关 |
| **全屏自动挂起** | 检测到全屏应用（游戏/视频）时自动隐藏自身，全屏结束后自动恢复 |
| **手机式分组** | 拖到另一个图标上**长按 0.6 秒**松手，即合并成"文件夹格子"，点开显示组内图标 |
| **拖拽排序** | 直接拖到另一个图标上松手（不长按）＝两者**交换位置**，顺序存入配置 |
| **滚动** | 三种方式：滚轮 / 鼠标贴住面板上边缘或下边缘自动滚 / 点右上 ▲、右下 ▼ 翻页 |
| **托盘常驻** | 托盘菜单可重扫桌面、编辑配置、开机自启、退出 |
| **高 DPI 适配** | 按系统缩放自动放大（你这里是 2560×1600 @150%，实测正确） |

## 使用

1. 双击 `dist\DesktopDock.exe`
2. 首次运行会自动扫描桌面（用户桌面 + 公共桌面）里所有 `.lnk` / `.url` / `.exe`，生成 `dist\config.json`
3. 鼠标移到屏幕左边缘 → 侧边栏滑出 → 点击图标即可启动程序
4. **排序 / 合并图标**：按住图标拖到另一个图标上**直接松手 = 交换位置**；按住**停留约 0.6 秒**（target 出现蓝色高亮边框）再松手 = 合并为新分组；拖到已有分组格子上 = 加入该分组
5. **右键格子**：打开 / 打开所在位置 / 重命名 / 移出分组 / 删除；分组格子还有重命名、解散

## 配置（`config.json`，与 exe 同目录）

```jsonc
{
  "HideDesktopIcons": true,   // 是否隐藏桌面图标
  "HotEdgeWidth": 6,          // 边缘热区宽度（像素，缩放后）
  "PanelWidth": 330,          // 面板宽度
  "CellSize": 96,             // 格子大小（图标大小 = 格子的 56%）
  "Gap": 12,                  // 格子间距
  "PanelOpacity": 0.97,       // 面板不透明度
  "Items": [
    { "Name": "微信", "Path": "C:\\Users\\Public\\Desktop\\微信.lnk", "Group": "" },
    { "Name": "Edge", "Path": "C:\\...\\Microsoft Edge.lnk", "Group": "开发工具" }
  ]
}
```

`Group` 为空表示顶层；相同 `Group` 的名字会自动合并成一个分组格子。

改完配置后在托盘菜单点「重新加载配置」即可，无需重启。

## 开机自启

托盘菜单 → 勾选「开机自启」（写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限）。

## 重新编译

双击 `build.bat` 即可（只用 Windows 自带的 `csc.exe`，无需安装 .NET SDK）。

## 架构

```
src/
  Program.cs      入口、DPI 适配、单实例、--selftest 自检
  Native.cs       Win32 调用、桌面图标控制、全屏检测
  Config.cs       配置模型、JSON 读写、桌面扫描
  Ui.cs           缩放/配色/圆角/图标缓存
  EdgeZone.cs     边缘触发热区窗口
  DockPanel.cs    滑出面板 + 格子画布（绘制/命中/拖拽合并/右键菜单）
  DockManager.cs  托盘、定时器、全屏挂起、多屏编排
```

## 已知限制 / 注意

- **隐藏桌面图标依赖与 explorer 同权限运行**。以管理员或受限令牌（低完整性）运行时，`ShowWindow`/`WM_COMMAND` 会被 UIPI 拒绝，图标不会隐藏；正常双击运行即可。
- 图标隐藏是通过 Explorer 的显示状态切换实现的；如果中途手动右键桌面「查看 → 显示桌面图标」，下次程序会按 `HideDesktopIcons` 的设定再纠正。
- 内容超出屏幕高度时可以滚动（滚轮 / 鼠标贴边自动滚动），右侧有细滚动条；想一屏放下全部图标就把 `CellSize` 调小。
- 全屏检测采用「前台窗口矩形覆盖整个显示器」的判定，最大化窗口（露出任务栏）不算全屏。
- 关机/强杀进程不会执行退出清理，桌面图标会保持隐藏状态，重新打开程序或在托盘取消勾选即可恢复。

## 日志

出错信息写入 `dist\dock.log`，可直接发给我排查。
