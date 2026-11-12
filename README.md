# NBA2K Lab

NBA2K Lab 是 NBA 2K27 PC 中文编辑器，首次正式发布版本 **1.0.0**。
使用 C#、React 和 WebView2，提供单文件 Windows 程序。

## 下载与运行

在本仓库 Releases 中下载 `NBA2K-Lab-1.0.0-win-x64.zip`，解压后运行
`NBA2K-Lab-1.0.0.exe`。也可直接下载独立 EXE。

运行环境：

- Windows x64。
- Microsoft Edge WebView2 Runtime。
- 已启动并加载名单或联盟的 NBA2K27.exe；编辑器与游戏使用相同的权限级别。

本版对应的游戏文件：

```text
File:   NBA2K27.exe
Size:   880513304 bytes
SHA256: 0ca3734d217d62197a2a88ea7485fc8f761c8b6862d3bce44616f5bf75e97c89
```

连接时会核对游戏构建和进程身份。进入游戏后点击“连接游戏”，默认以浏览模式打开，
需要修改时打开编辑开关。修改先保留为草稿，点击应用后写入游戏；需要持久保存时，
使用游戏自身的存档功能。

## 功能

- 球员资料、能力、倾向、动作、合同、外观及已定义字段的编辑、批量修改、导入导出。
- 赛季累计、场均、单场最高和荣誉查看与编辑。
- 当前联盟名单、球队资料、阵容调整、交易预览和选秀权展示。
- 通用 GS 和场内 GS，各 70 项，用户与电脑独立设置，支持预设、应用和撤销。
- 场内比分及球员统计编辑，联动相关球队与节次计数器。
- 联盟与球队历史、NBA 纪录、球队单场纪录及得分纪录。
- 赛程、比赛日志、排行榜、统计分析、球员对比和建模工具。
- 球员库、修改前后预览、逐项还原和本地操作记录。

场内 GS 使用比赛中的独立数组。尚未加载时显示提示，进入比赛后重新读取即可。
场内统计应用和撤销需要打开游戏暂停菜单；程序会重新核对暂停状态和预览数据。
有未应用的球员草稿时，关闭窗口、切换对象、刷新和重新连接会先询问是否放弃。

## 本地数据

操作记录、预设、导出和球员库位于：

```text
%USERPROFILE%\Documents\NBA2K27PlayerEditor\
```

WebView2 缓存位于：

```text
%LOCALAPPDATA%\Nba2k27Editor\webview\
```

替换 EXE 不会删除这些数据。撤销仅针对当前连接中对应的修改；
游戏已经自行改变相关数值时，程序会拒绝覆盖新值。

## 从源码构建

需要 .NET 8 SDK、Node.js 24 和 pnpm。

```powershell
cd web
pnpm install --frozen-lockfile
pnpm run build
cd ..
dotnet run --project tests/Nba2k27Editor.Tests -c Release
dotnet publish src/Nba2k27Editor.WebHost -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o artifacts/1.0.0/build
```

完整发布流程：

```powershell
.\scripts\release.ps1 -FrontendTestConcurrency 1
```

前端测试包含可选的 Playwright 浏览器回归。完整验收时需让 `playwright`
可被 Node.js 导入，并安装对应平台的浏览器；Windows 检查使用 Microsoft Edge。
也可将 `DISPLAY_READ_PLAYWRIGHT` 指向现有 Playwright 的绝对模块路径。
没有浏览器运行环境时，相关测试会显示为跳过，不能计入完整界面验收。

最终 EXE 提供只读检查入口：

```powershell
.\artifacts\1.0.0\build\Nba2k27Editor.exe --assets-check --out artifacts/1.0.0/assets.json
.\artifacts\1.0.0\build\Nba2k27Editor.exe --workspace-usability-ui-check --out artifacts/1.0.0/workspace-ui.json
```

第二项需要正在运行且已加载联盟的受支持游戏。其他带有 write、apply、
roundtrip 等语义的诊断入口可能实际修改游戏数据，不属于只读检查。

## 目录

| 路径 | 内容 |
| --- | --- |
| `src/Nba2k27Editor.Core` | 字段、内存读写、事务、名单、GS 与比赛逻辑 |
| `src/Nba2k27Editor.WebHost` | Windows 桌面宿主、内嵌前端与桥接接口 |
| `web` | React 界面、资源和前端测试 |
| `tests/Nba2k27Editor.Tests` | C# 离线回归 |
| `tools/Nba2k27Editor.Cli` | 命令行诊断工具 |
| `scripts/release.ps1` | 构建和打包 |

此项目为独立工具，与 NBA、2K 或其关联公司无隶属关系。
