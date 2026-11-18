# Explorer2Dialog

Explorer2Dialog 让 Windows 标准文件对话框跟随资源管理器目录。打开文件或另存为时，它会切到最近使用的目录。已打开的对话框也能在后台跟随本地目录切换，焦点留在资源管理器。

适用环境为 Windows 11 x64 和 .NET Framework 4.8.1。

## 安装和使用

- 双击 `Explorer2Dialog-0.1.0-x64.msi` 为当前用户安装。完成后可从开始菜单打开 **Explorer2Dialog**，也可在 Windows 的「已安装的应用」中卸载。
- v0.1.0 的 MSI 未进行代码签名。
- 从源码构建的 `bin\Explorer2Dialog.exe` 可直接运行。
- 在任务栏通知区域右键点击图标，可以暂停、查看当前缓存目录或退出。升级或卸载前，请先从托盘退出程序。

1. 在资源管理器打开目标文件夹，再到目标应用打开文件或另存为。
2. 对话框打开后，可以保留它并继续在资源管理器换目录。后台跟随使用最近活动的标准对话框。目录树无法定位目标时，切回对话框后会尝试前台跳转。
3. 自行选择文件或确认保存。资源管理器的目标保持相同目录时，对话框里的手动选择会保留。

记事本、Word 和 Edge 的实测入口见[测试报告](docs/测试报告.md)。不同应用的自定义文件选择器可能有自己的行为。[已知限制](docs/已知限制.md)

## 从源码构建

在源码根目录打开 PowerShell 并运行以下命令。

~~~powershell
.\build.ps1
.\build.ps1 -Checks
~~~

使用 Windows 自带的 x64 .NET Framework 编译器和 C# 5。构建产物位于 `bin`。联合测试用 `DialogTestApp.exe`，无需额外的第三方运行库。

### 构建安装包

使用 WiX Toolset 3.14.1 的 `candle.exe` 和 `light.exe` 打包。将它们加入 `PATH` 后，从源码根目录运行以下命令。[WiX 官方发行页](https://github.com/wixtoolset/wix3/releases/tag/wix3141rtm)

~~~powershell
.\build-msi.ps1
~~~

也可通过 `-WixBin` 指定包含两个工具的目录。脚本在 `dist` 生成 MSI，中间构建位于 `dist\msi-build`。

### 联合复测

先从托盘退出当前实例，再从源码根目录运行以下命令。

~~~powershell
$root = (Get-Location).Path
$fixture = Start-Process -FilePath (Join-Path $root 'bin\DialogTestApp.exe') -ArgumentList '--label', 'MVP' -PassThru
Start-Process -FilePath (Join-Path $root 'bin\Explorer2Dialog.exe') -ArgumentList '--pid', $fixture.Id
~~~

在资源管理器打开 `tests\fixtures\A`，随后在测试窗口点击 **Open File**，核对 `only-a.txt`。保持对话框打开，在同一个资源管理器窗口进入 `tests\fixtures\B 中文 + #100%\层级 2\层级 3`，核对原对话框显示 `only-b.txt`。再切到 `tests\fixtures\C` 复核后台跟随。复测实际软件时，直接启动主程序即可。

## 日志和隐私

普通日志位于 `%LOCALAPPDATA%\Explorer2Dialog\Logs\events.log`，记录事件结果、错误码和耗时。显式传入 `--diagnostics` 会生成含完整缓存和目标路径的诊断日志。对外分享诊断信息前，应先移除个人路径及标识。

[技术架构](docs/技术架构.md) · [测试报告](docs/测试报告.md) · [已知限制](docs/已知限制.md)

## 许可证

项目采用 [MIT 许可证](LICENSE)。
