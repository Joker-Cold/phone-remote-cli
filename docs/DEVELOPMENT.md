# 组成、构建和测试

[← 回到首页](../README.md)

## 组成

```
手机 App  ──Tailscale 内的 HTTP──▶  本机服务 relay（电脑端启动，只监听 Tailscale 地址）  ◀──同一地址──  电脑端程序（运行终端）
```

| 目录 | 内容 |
| --- | --- |
| `agent-windows/` | 电脑端：`RemoteCliApp.cs`（窗口、托盘、启动本机服务）、`Tailnet.cs`（找出本机的 Tailscale 地址）、`RemoteCliAgent.cs`（终端与对话扫描）、`Remote.cs`（手机可以读取和更改的几项设置）、`Controls.cs`（外观和控件）、`QrCode.cs`。C#，用 Windows 自带的编译器构建 |
| `relay/` | 本机服务：`server.py`（登录、HTTP 和 WebSocket）、`relay.py`（终端状态与输出的转发：一台电脑一个 `Relay` 对象）。只用 Python 标准库，安装包内置官方嵌入式 Python |
| `android/` | 安卓 App：`MainActivity`（各界面之间的跳转、扫码回调、语音识别）、`Workbench`（工作台）、`Kit`（配色和界面部件）、`AggregateSessions`（任务分组与排序，不依赖 Android，可单独检查）、`Watcher`（任务提醒） |
| `web/` | App 里一台电脑的列表页、终端页（xterm.js）、文件查看和电脑设置页，由本机服务提供、嵌在 App 内显示；随电脑端一起更新 |
| `tools/` | `package_windows.py`（便携版）、`bump_version.py`（一次递增所有版本号）、`window_pictures.py`（文档里的电脑端截图）、`app_pictures.py`（文档里的 App 页面截图；其余的 `.jpg` 在手机上截取）、`make_icons.py`（从 `icon-source.png` 生成电脑端的 `app.ico`、App 的启动图标和 README 的标志，生成结果随仓库保存，构建时不需要 Pillow） |
| `docs/PROTOCOL.md` | 三方之间的接口 |

电脑端程序启动时找出 Tailscale 网卡上的 `100.64.0.0/10` 地址，在这个地址上启动本机服务，电脑端的终端代理也连这个地址。Tailscale 未连接时不启动服务；每 5 秒检查一次，地址出现、消失或变化时重新启动服务。

## 自己构建

```powershell
# 电脑端（两个 exe），只需要 Windows 自带的 .NET Framework 4.x
powershell -ExecutionPolicy Bypass -File agent-windows\build.ps1

# 便携版：使用 .cache\python-3.12.10-embed-amd64.zip（python.org 官方的 Windows 嵌入式 Python，需自行放入）并核对哈希，生成 dist\phone-remote-cli\ 文件夹和 dist\phone-remote-cli.zip
python tools\package_windows.py

# 安卓：JDK 11+ 和 Android SDK，不需要 Gradle；需自行把 Maven Central 上的 com.google.zxing:core:3.5.3 放到 .cache\zxing-core-3.5.3.jar；生成 dist\phone-remote-cli.apk
# 默认用 D:\java17、D:\Android\Sdk、build-tools 36.1.0、platforms\android-36.1；可用 -Jdk -Sdk -BuildTools -Platform 指定
powershell -ExecutionPolicy Bypass -File android\build.ps1
```

安卓的签名密钥在第一次构建时生成，保存在仓库之外的 `~/.remote-cli-signing`（`signing.p12` 和 `signing-password.txt`）。请备份这个文件夹：换一把密钥签名的 App 不能覆盖安装原来的。

测试时可设置环境变量 `REMOTECLI_ADDRESS=127.0.0.1`，电脑端改在本机地址上启动服务，不需要 Tailscale。`REMOTECLI_DATA` 指定另一个数据文件夹，不影响正在使用的设置。

## 分支

只有 `main` 一个分支，所有改动直接在上面完成、构建和提交。

## 日常使用的副本

`dist/phone-remote-cli/` 每次构建都会整个替换，程序在里面运行时构建会失败。日常使用的电脑端放在仓库下的 `remote_cli\`（已在 `.gitignore` 里）：要换上新构建时，先在托盘图标上右键“退出”，再复制过去：

```powershell
robocopy dist\phone-remote-cli remote_cli /MIR /XD __pycache__
```

## 版本号

以下几处的版本号保持一致，只在要装到手机或发布 Release 时递增；Android 的 `versionCode` 同时加一。用脚本一次改好：

```powershell
python toolsump_version.py          # 最后一位加一，例如 1.0.6 → 1.0.7
python toolsump_version.py 1.1.0    # 指定版本
```

脚本改的是：

- `agent-windows/RemoteCliApp.cs` 的 `Version`
- `relay/server.py` 的 `VERSION`
- `tools/package_windows.py` 的 `VERSION`
- `android/AndroidManifest.xml` 的 `versionName` 和 `versionCode`

## 测试

提交前运行快速检查：不需要浏览器和打包结果，约一分钟，遇到第一个失败就停下并显示输出。

```powershell
python tests\quick.py [--jdk D:\java17]
```

下面是全部检查，单独运行时用；`quick.py` 包含其中不需要浏览器、不需要打包结果的那些，`smooth_check` 测的是延迟，耗时较长，不在其中。

```powershell
python -m unittest discover -s relay -p "test_*.py"     # 本机服务
python tests\tailnet_check.py                            # 只选 Tailscale 网卡上的 100.64.0.0/10 地址
python tests\e2e_windows.py
node tests\bridge_check.js                                # WebSocket 连接状态、延迟测量、连续输入、断线重连和连不上时的原因
node tests\terminal_scroll_check.js                       # 拖动、惯性、网络停顿时画面的跟随、Codex 普通/全屏模式、Claude Code 的滚轮配速、鼠标编码和回到最新
node tests\session_state_check.js                         # 运行中的会话与保留的写入锁分开显示
python tests\android_aggregate_check.py --jdk D:\java17   # 工作台的分组、排序、已完成标记、刷新判定和任务提醒
python tests\native_owner_check.py                        # 临时进程验证归属，不操作真实 Codex 会话
python tests\session_copies_check.py                      # 同一段对话只列一次，用虚构的对话文件
python tests\said_check.py                                # 手机上看到的对话内容：问了什么、答了什么、做了什么，用虚构的对话文件
python tests\history_check.py                             # 电脑端保留已交给本机服务的输出，缺少时从缺的地方再给一遍
python tests\look_check.py                                # 先看对话再接手、票据登录、手机和电脑共用一个终端
python tests\files_check.py [chrome.exe <输出文件夹>]     # 查看文件：列目录、读文件、不出项目文件夹；带浏览器时给每种查看器截图
python tests\smooth_check.py                              # 历史很长时按键回显和连续输出的间隔
python tests\move_check.py                                # 电脑端换到另一个服务地址再换回：终端、名字、画面和已结束的终端都跟着
python tests\fullscreen_check.py <chrome.exe>             # 占满屏幕的程序：触摸拖动逐行滚动，回到最新；长时间运行后重新打开只读最近的输出
python tests\pages_check.py <chrome.exe> <输出文件夹>     # 截取列表页和终端页
python tests\e2e_packaged.py                              # 便携版 dist\phone-remote-cli：内置 Python 启动本机服务并运行 PowerShell 终端；先运行 tools\package_windows.py
chrome --headless=new --window-size=1480,520 --screenshot=skins.png tests\skin_code_check.html   # 各外观下代码、diff 和 16 色的显示
java -cp .cache\zxing-core-3.5.3.jar tests\QrDecodeCheck.java <二维码.png> <内容>   # 手机端的二维码识别
```
