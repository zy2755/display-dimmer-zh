# Display Dimmer 中文补丁（非官方）

把 [Display Dimmer](https://apps.microsoft.com/detail/9nbwhfcln6cm)（一款 Windows 外接显示器亮度调节工具）的界面完整翻译成简体中文。
共 **654 条**界面文案，覆盖主窗口、设置页、托盘菜单、计划任务、应用规则、热键、专业版弹窗和更新说明。

> **这是一个工具仓库，不是软件仓库。**
> 这里**不包含** Display Dimmer 的任何可执行文件。你需要自己从 Microsoft Store 安装官方版，
> 然后由本工具读取你本机的官方文件、就地替换界面字符串，产物也只留在你自己的电脑上。
> 详见 [NOTICE.md](NOTICE.md)。

---

## 快速上手（三步）

### 第 0 步：准备环境

- Windows 10/11
- 已在 Microsoft Store 安装 Display Dimmer（免费版即可）
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（只用来编译本仓库自带的工具，不需要你写 C#）

### 第 1 步：取回官方文件

以**管理员身份**打开 PowerShell，在仓库目录下运行：

```powershell
.\fetch_original.ps1
```

它会自动找到商店安装目录，把程序复制到两个地方：

| 目录 | 作用 |
| --- | --- |
| `orig\` | 原版基准。构建时要靠它加载程序集来解析 BAML 结构 |
| `app\`  | 绿色版运行目录。打完补丁后从这里的 `Display Dimmer.exe` 启动 |

> 为什么要做成"绿色版"？因为商店应用是 MSIX 签名包，直接往里换 DLL 会因校验失败而打不开。
> 所以只能把整个程序目录复制出来、脱离商店的加载器单独运行。

### 第 2 步：打补丁

```powershell
.\build.ps1 -InPlace
```

看到 `完成！` 就说明 `app\Display Dimmer.dll` 已经被换成中文版了（原版自动备份为 `.bak`）。
双击 `app\Display Dimmer.exe` 即可。

---

## 注意事项（动手前请看）

- **版本绑定**：这份词典针对 **Display Dimmer 2.2.12** 制作。软件升级后界面文案会变，
  需要重新提取、补译，再跑一次本工具。
- **专业版（Pro）授权**：绿色版脱离了 Microsoft Store 的运行环境，
  **商店内购授权可能无法正常验证**。如果你是 Pro 用户，建议先保留原商店版做对比，确认无误再切换。
- **自动更新**：绿色版没有商店更新通道。如果程序提示更新，更新后你的中文 DLL 会被覆盖，需要重新打补丁。
- **界面图标**：MSIX 转绿色版后，exe 本身不带图标资源，资源管理器里会显示空白。
  可以用 `scripts\gen_ico.py` + `scripts\embed_icon.py` 补一个（可选）。
- **旧设置迁移**：如果你之前用的是商店版，设置里的部分界面文案已经被写进配置文件了，
  可以用 `scripts\migrate_settings.py` 一并转成中文。

---

## 目录结构

```
display-dimmer-zh/
├── build.ps1              # 一键构建（主脚本）
├── fetch_original.ps1     # 从商店安装目录取回官方文件（需管理员）
├── dict/                  # 翻译词典
│   ├── dict.tsv           #   BAML 界面资源用（253 条）
│   ├── us_dict.tsv        #   代码字符串 · 第一遍：完整句子与标签（470 条）
│   ├── us_dict2.tsv       #   第二遍：拼接碎片（135 条）
│   ├── us_dict3.tsv       #   第三遍：漏掉的界面类（49 条）
│   └── us_scope.tsv       #   按方法限定的替换规则（66 条）
├── scripts/               # Python 辅助脚本（仅标准库，图标处理部分需要 Pillow）
│   ├── mkus2.py / mkus3.py    # 重新生成第二、三遍词典（词典就写在脚本里）
│   ├── gen_ico.py             # 合成多尺寸 .ico
│   ├── embed_icon.py          # 把图标写回 exe 的 PE 资源
│   └── migrate_settings.py    # 迁移已汉化的设置项
├── tools/BamlTool/        # 自研 C# 工具（改 BAML 与 IL 字符串）
├── orig/                  # 你放官方原版 DLL 的地方（不进仓库）
├── app/                   # 绿色版程序目录（不进仓库）
└── out/                   # 构建产物（不进仓库）
```

---

## 原理：为什么需要两遍替换

WPF 程序的界面文字藏在两个完全不同的地方，只改一处会中英混杂。

**第一处：BAML 资源**（`localize` 子命令）
XAML 编译后变成二进制的 BAML，嵌在 `g.resources` 里。这里放着窗口、按钮、菜单上的静态文字。
难点在于 BAML 结构复杂（延迟加载、连接 ID、类型转换器……），手写解析器极易损坏。
本工具走的是 WPF 自带的 `BamlLocalizer` 管线（内部 `BamlResourceDeserializer` / `BamlResourceSerializer`），
所以能完整保留全部结构，只替换字符串——这也是它比"暴力字节替换"可靠的原因。

**第二处：IL 里的字符串常量**（`usx` 子命令）
状态提示、动态拼出来的句子都写在 C# 代码里，编译后落在元数据 `#US` 流中，靠 Mono.Cecil 改写。

**为什么代码串要分三遍**
第一遍按"完整句子/标签"的形态过滤，效果最好但会漏掉 `"Connected to: "` 这类**带首尾空格的拼接碎片**——
它们要和变量名拼起来才成句。第二遍专门补这些。第三遍补第一遍类白名单没覆盖到的界面类。
三遍按顺序合并，后者覆盖前者。

**为什么要"按方法限定"**
有些字符串极度短小且到处复用，比如 `"."`、`" ("`、`" at "`。全局替换会连诊断日志和命令行输出一起改坏。
`us_scope.tsv` 允许指定"只在方法名包含某子串时才替换"，把作用域卡死在界面方法里。

---

## 常见问题

**Q：能不能直接给我一个中文版安装包？**
不能，那样等于分发原作者的软件，是侵权。请理解——这也是本仓库只放工具的原因。

**Q：我没装 .NET SDK 怎么办？**
装一个（链接在上面），或者用免安装版解压到任意位置，然后：
`.\build.ps1 -Dotnet "D:\dotnet8sdk\dotnet.exe"`

**Q：软件升级到新版本了，词典还能用吗？**
大部分能——词典按"原文 → 译文"匹配，原文没变的条目照样命中。
新增的界面文案会保持英文，你可以提 PR 补上。

---

## 想帮忙改进

最欢迎的是**补译**：把新版里漏掉的英文条目加到 `dict/` 下的对应文件里，发个 PR。
格式很简单，一行一条，原文和译文用 Tab 分隔：

```
No brightness rules set up for 	尚无亮度规则：
```

改完后跑一次 `.\build.ps1`，确认界面显示正常再提交。

---

## 许可

- 本仓库的工具源码与翻译：**MIT**（见 [LICENSE](LICENSE)）
- Display Dimmer 软件本身：权利归原作者 **Vantari Works** 所有，本仓库不授予任何相关权利
- 如果你觉得这个软件好用，请通过 Microsoft Store 购买 Pro 版支持作者

**本仓库是非官方的社区项目，与 Vantari Works 没有任何关联。**
