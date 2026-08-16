# DeepSeek Harness 免安装便携版（Windows）构建工程

DeepSeek Harness 的 Windows 免安装便携版：内置官方 Node.js 与全部程序包，解压后双击启动器即可使用，无需安装任何软件。

本仓库提供启动器源码（C#）、一键构建脚本与使用文档。

## 快速使用

从 [Releases](https://github.com/sqs404/dsh-portable/releases) 下载最新压缩包，解压后双击「启动 DeepSeek Harness.exe」即可使用（默认端口 3081）。

## 仓库内容

```
dsh-portable/
├── launcher.cs                 启动器源码（C#，约 6KB）
├── build.ps1                   一键构建脚本（下载 Node → 安装依赖 → 编译 → 组装）
├── docs/使用说明.txt            成品内置的中文使用说明
├── LICENSE / THIRD_PARTY_NOTICES.md
└── README.md                   本说明
```

## 从源码构建

### 环境要求

- **64 位 Windows 10 / 11**（构建与运行均需）
- 网络连接（下载 Node.js 与 npm 依赖）

无需安装任何开发工具：Node.js 由脚本自动下载，C# 编译器使用 Windows 自带的 `csc`。

### 一键构建

```powershell
# 默认使用官方源
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Zip
```

国内网络加速：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Zip `
    -Registry "https://registry.npmmirror.com/" `
    -NodeMirror "https://npmmirror.com/mirrors/node/"
```

脚本执行：

1. 下载官方 **Node.js**（默认 v24.19.0）并取出 `node.exe`；
2. 用 npm **扁平安装**官方发布包 `@deepseek-ai/dsh@0.1.0-rc.6`（`--ignore-scripts`，产物为纯真实目录、零链接）；
3. 用系统自带 `csc` 编译 `launcher.cs` 生成 `启动 DeepSeek Harness.exe`；
4. 复制文档，组装出 `dist\` 便携目录（可选 `-Zip` 打包）。

### 手动构建（等价步骤）

```bat
:: 1. Node.js 运行时
::    从 https://nodejs.org/dist/v24.19.0/node-v24.19.0-win-x64.zip 解压出 node.exe

:: 2. 官方运行时依赖
mkdir dist && cd dist
echo {"name":"dsh-portable-runtime","private":true,"dependencies":{"@deepseek-ai/dsh":"0.1.0-rc.6"}}> package.json
npm install --ignore-scripts --no-audit --no-fund

:: 3. 编译启动器（Windows 自带 .NET Framework 编译器）
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe ^
    /r:System.Windows.Forms.dll /out:"启动 DeepSeek Harness.exe" ..\launcher.cs

:: 4. 把 node.exe 复制进 dist 即完成；双击启动器即可运行
```

## 使用方法

1. 把构建产物（或 Releases 下载的压缩包）解压到任意位置（路径可含中文）；
2. 双击 **`启动 DeepSeek Harness.exe`**；
3. 浏览器自动打开 `http://127.0.0.1:3081`，按页面提示配置模型服务（如 DeepSeek API Key）后即可使用；
4. 关闭弹出的黑色命令行窗口即停止服务。

- **自定义端口**：目录下新建 `port.txt` 写入端口号即可（默认 3081）；
- **命令行启动**：`node.exe node_modules\@deepseek-ai\dsh\lib\bin.js web`。

## 工作原理

- 启动器（`launcher.cs`）通过 `DSH_HOME` 环境变量把用户数据根指向自身 `data\` 目录，实现数据完全隔离（不读写系统用户目录）；
- 首次启动由官方 `dsh web` 自动初始化配置（`data\profiles\web`）并重建内部链接，全程无需联网；
- node_modules 采用 npm 扁平安装（零 junction/symlink），因此 git clone、ZIP 解压、文件夹拷贝三种方式均不会产生损坏链接，这是"拷贝即用"的关键（pnpm 默认布局含数万个链接，分发后必然失效）。

## 注意事项

- 官方标注 DeepSeek Harness 为**开发者预览版**，迭代快，可能存在破坏性变更；
- Agent 可能修改文件、执行命令，请从测试目录开始使用；
- 模型 API 与联网搜索需要网络；离线时可启动程序但无法调用模型；
- 仅支持 **64 位 Windows 10 / 11**；
- 本工程为官方 npm 发布包的 Windows 分发封装，非官方制品；许可证 MIT（见 `LICENSE` 与 `THIRD_PARTY_NOTICES.md`），版权归 DeepSeek AI 及其贡献者所有。

## 相关链接

- 官方项目：[deepseek-ai/deepseek-harness](https://github.com/deepseek-ai/deepseek-harness)
- 官方 npm 包：[`@deepseek-ai/dsh`](https://www.npmjs.com/package/@deepseek-ai/dsh)
