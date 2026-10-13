# Phone Remote CLI 项目规则

- 本项目基于 KangWang42/remote-cli 修改，公开仓库为 `github.com/Joker-Cold/phone-remote-cli`（远端 `origin`）。不恢复任何从 GitHub 检查或下载更新的功能。
- 只有 `main` 一个分支，所有改动直接在 `main` 上做、构建、提交。推送到 `origin` 前须经用户确认。
- 产品由 Android App 和 Windows 电脑端组成；电脑端以便携版交付：解压即用的文件夹，不做安装程序。不再维护 Linux 电脑端。
- 连接只经过 Tailscale：电脑端只在 Tailscale 网卡的 `100.64.0.0/10` 地址上启动本机服务，不监听 `0.0.0.0` 或其它网卡；不加回局域网直连、公网隧道、公共或自建中转。测试用 `REMOTECLI_ADDRESS=127.0.0.1` 代替 Tailscale 地址。
- 提交前运行 `python tests/quick.py`，全部通过才提交；改到需要浏览器的部分（终端页、文件查看、截图）时再跑 `docs/DEVELOPMENT.md` 里对应的检查。
- 改哪端构建哪端，只改源码而不重新构建不算完成：改了 `agent-windows/`、`relay/`、`web/` 就运行 `python tools/package_windows.py`，生成 `dist/phone-remote-cli/` 和 `dist/phone-remote-cli.zip`；改了 `android/` 就运行 `android/build.ps1`，生成 `dist/phone-remote-cli.apk`。名称固定，不带版本号。`dist/phone-remote-cli/` 和 `dist/phone-remote-cli.apk` 随源码一起提交，提交前两端都要是用当前源码构建的；`dist/phone-remote-cli.zip` 不进仓库，电脑端文件夹里也不放 zip（打包时把 Python 标准库解压成 `python/Lib`）。不发布 Release。
- 日常使用的电脑端副本放在仓库下的 `remote_cli/`（不进仓库）。需要用上新构建时，先在托盘退出程序，再把 `dist/phone-remote-cli/` 的内容复制过去；不直接运行 `dist/` 里的程序，以免挡住下一次构建。
- 版本号只在要装到手机或发布 Release 时递增，用 `python tools/bump_version.py` 一次改好电脑端、本机服务、打包脚本和 Android 的 `versionName`，并把 `versionCode` 加一；不手工改其中某一处。
- APK 用 `~/.remote-cli-signing` 里的本地密钥签名，不另生成新密钥；换密钥后已安装的 App 无法覆盖更新。本机构建环境：JDK `D:\java17`，Android SDK `D:\Android\Sdk`（build-tools 36.1.0、platforms/android-36.1），`android/build.ps1` 已按此设好默认值。
- 项目保持清晰、可扩展的结构，不做无意义的堆砌。新能力以职责单一、接口明确的模块加入（例如 `agent-windows/Tailnet.cs`、`relay.Relay`）；状态放在可以有多个实例的对象里，不靠全局变量或重复加载模块，并去掉因改动而不再需要的代码。
- 测试和文档：改到手机、电脑端、本机服务之间接口的，同时更新 `docs/PROTOCOL.md` 并加测试；修 bug 时能写回归测试就写；纯界面和文案调整不强制。用户能看到的功能变化写进 README 或使用说明。
- README 和文档直接描述功能、变化及必要的使用限制，使用自然、客观的中文；不加入助手自述或制作过程说明，不虚构测试证据。
