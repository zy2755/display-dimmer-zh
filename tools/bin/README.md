# tools/bin/ —— 放预编译 BamlTool 的地方

如果你**不想装 .NET 8 SDK**，可以从本仓库的 Releases 页面下载 `BamlTool-v1.zip`，
把里面的文件全部解压到这个目录，然后 `build.ps1` 会直接用它，跳过编译。

解压完应该长这样：

```
tools/bin/
├── BamlTool.dll                 # 主程序（本仓库源码编译所得）
├── BamlTool.runtimeconfig.json
├── BamlTool.deps.json
├── Mono.Cecil.dll               # 第三方库，MIT 许可
├── Mono.Cecil.Mdb.dll
├── Mono.Cecil.Pdb.dll
└── Mono.Cecil.Rocks.dll
```

注意：即便用预编译包，运行它**仍然需要 .NET 8 运行时**
（只装 Runtime 就行，不用装完整 SDK）：
https://dotnet.microsoft.com/download/dotnet/8.0

这个目录里的文件都不会进仓库（已在 .gitignore 中排除）。
