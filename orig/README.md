# orig/ —— 放官方原版文件的地方

这个目录用来放**你自己从 Microsoft Store 安装**的 Display Dimmer 的原版文件。
仓库里不会自带这些文件（那是原作者的著作权物，不归我们分发）。

只需要放两个文件：

```
orig/
├── Display Dimmer.dll                  # 原版主程序集（必需）
└── Display Dimmer.runtimeconfig.json   # 运行时配置（推荐一起放）
```

## 为什么需要原版 DLL

`BamlTool` 在解析 BAML（WPF 编译后的界面资源）时，需要把程序集真正加载起来
才能解析里面引用的类型（比如 `local:SettingsWindow`）。所以必须有一份**原版**
DLL 作为加载基准 —— 脚本通过 `DD_REFDIR` 环境变量告诉工具去哪儿找。

## 怎么拿到它

见仓库根目录的 `fetch_original.ps1`（以管理员身份运行，会自动从商店安装目录
复制出来）。也可以手动复制：

商店版默认安装在（目录名里的版本号会变）：

```
C:\Program Files\WindowsApps\NathanLabs.DisplayDimmer_<一长串>\
```

这个目录默认没有访问权限，需要用管理员权限，或者先取得所有权。
