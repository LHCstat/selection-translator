# 划译（SelectionTranslator）

Windows 系统级划词翻译工具：在任意程序里选中英文，点击悬浮「译」图标或按 `Alt+D`，立刻看到流式中文译文。

不依赖浏览器，不做全页翻译，不抢焦点，不污染剪贴板。

## 功能

- 任意可选中文本的程序：Chrome、Edge、Word、WPS、记事本、PDF 阅读器等
- 取词两级降级：UI Automation 优先，失败回退模拟复制
- 流式译文、原文对照、复制、重试、固定
- 剪贴板自动保存与恢复
- 密码框不取词；终端类程序禁用模拟 Ctrl+C
- 默认智谱 GLM-4-Flash（免费），支持任意 OpenAI 兼容接口与本地 Ollama
- API Key 使用 Windows DPAPI 加密保存

## 使用

1. 运行 `SelectionTranslator.App.exe`，托盘出现图标
2. 托盘右键 → 设置，选择翻译服务并填入 API Key
3. 在任意程序中选中英文：
   - 点击鼠标旁出现的「译」图标，或
   - 按 `Alt+D`
4. 译文卡片出现，逐字显示结果

## 已知限制

- 管理员权限的窗口中无法取词，除非本程序也以管理员身份运行
- 全屏独占游戏、DRM 视频无法取词
- 终端类程序只使用 UIA 取词，可能取不到
- 首次使用需要自行申请免费 API Key（智谱开放平台）

## 构建

需要 .NET 8 SDK：

```bash
dotnet build SelectionTranslator.sln
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj
```

发布：

```bash
dotnet publish src/SelectionTranslator.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/framework-dependent
```

## 目录

- `src/SelectionTranslator.Core`：取词、翻译、配置，无 UI 代码
- `src/SelectionTranslator.App`：WPF 宿主、托盘、悬浮窗
- `src/SelectionTranslator.Tests`：单元测试
- `docs/superpowers`：设计文档与实现计划