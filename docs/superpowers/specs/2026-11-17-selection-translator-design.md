# 系统级划词翻译工具 设计文档

- 日期：2026-11-17
- 平台：Windows 10/11（x64）
- 状态：待评审

## 1. 背景与目标

做一个常驻托盘的 Windows 小工具：用户在**任意程序**里选中英文，通过悬浮图标或全局快捷键，立刻看到流式中文译文。不绑定浏览器，不做全页翻译。

对标产品：沉浸式翻译的划词体验、STranslate 的系统级取词。

### 目标

1. 任意可选中文本的程序里都能取词（浏览器、Office、WPS、PDF、聊天软件、终端等）。
2. 从按键到首字译文 P95 ≤ 1.5 秒。
3. 默认零配置可用：内置免费模型（智谱 GLM-4-Flash），也支持任意 OpenAI 兼容接口与本地 Ollama。
4. 常驻资源占用低：空闲 CPU ≈ 0，内存 < 120MB。
5. 不打断用户：默认只弹小图标，不自动翻译、不抢焦点、不污染剪贴板。

### 非目标（本期不做）

- 全页面/整篇翻译
- OCR、截图翻译
- 视频语音字幕
- macOS / Linux
- 账号、云同步、历史记录云端存储
- 术语库、生词本

## 2. 用户场景与验收标准

| 场景 | 预期 |
|---|---|
| Chrome 里选中英文段落，按 `Alt+D` | 鼠标旁出现译文卡片，原文在上、译文流式追加 |
| 在 Word 里选中单词，划词后松开鼠标 | 光标旁出现小图标，点击后展开卡片 |
| 在记事本里选中文本，剪贴板原本有内容 | 取词完成后剪贴板内容恢复原样 |
| 在密码输入框里选中/双击 | 不取词、不弹窗 |
| 断网时触发 | 卡片显示错误与「重试」按钮，不崩溃 |
| 程序以普通权限运行，焦点在管理员窗口 | 取词失败，给出明确提示，不静默 |
| 空闲放置 10 分钟 | CPU 占用接近 0，无定时轮询 |

## 3. 总体架构

```
┌──────────────────────── SelectionTranslator.exe (WPF, .NET 8) ────────────────────────┐
│                                                                                       │
│  Interop 层                    Selection 层                  Translation 层            │
│  ├ MouseHook (WH_MOUSE_LL) ──► SelectionPipeline ──────────► ProviderRouter            │
│  ├ HotkeyManager (RegisterHotKey)   ├ UiaTextAcquirer        ├ OpenAiCompatibleProvider│
│  ├ SendInputHelper (Ctrl+C)         ├ ClipboardTextAcquirer  └ OllamaProvider          │
│  ├ ClipboardHelper (保存/恢复)      └ TextValidator            │                        │
│  └ ForegroundWindow (进程/DPI)                                 ▼                        │
│                                                          TranslationCache (LRU+磁盘)    │
│  UI 层                                                                                 │
│  ├ SelectionIconWindow (无焦点小图标)                                                   │
│  ├ TranslationPopup    (跟随鼠标的译文卡片)                                             │
│  ├ SettingsWindow + TrayIcon                                                           │
│  └ Toast / 错误提示                                                                     │
└───────────────────────────────────────────────────────────────────────────────────────┘
                                   │ HTTPS / SSE
                                   ▼
                     智谱 GLM / OpenAI 兼容 / 本地 Ollama
```

### 设计原则

- **取词与翻译解耦**：`SelectionPipeline` 只负责拿到一段可信文本，翻译层只接收文本。
- **取词多级降级**：UIA → 模拟复制 → 放弃。每级独立超时，不相互阻塞。
- **单任务**：同一时刻只允许一个取词/翻译任务，新任务取消旧任务，避免窗口打架。
- **可观测**：每个阶段记录耗时，便于定位"慢在哪一段"。

## 4. 取词子系统

### 4.1 触发方式

| 方式 | 默认 | 说明 |
|---|---|---|
| 划词悬浮图标 | 开 | 鼠标拖选结束后在光标旁显示 16×16 图标，点击才翻译 |
| 划词自动翻译 | 关 | 选中即翻译，适合重度用户，容易误触 |
| 全局快捷键 | `Alt+D` | 对当前选区直接翻译，可配置 |
| 手动复制双击 | 关 | 连续两次 Ctrl+C 触发，作为兼容兜底 |

### 4.2 鼠标钩子

- 全局低级鼠标钩子 `WH_MOUSE_LL`，安装在**独立线程**并跑消息循环，委托必须保持强引用防止被 GC。
- 只处理 `WM_LBUTTONDOWN` / `WM_LBUTTONUP`，回调内**不做任何耗时操作**，只记录起点、终点、时间戳后投递到 UI 线程。
- 判定为"拖选"的条件：位移 ≥ 4px 且按住时长 ≥ 50ms。单击、双击、拖拽窗口都不触发。
- 触发前排除：
  - 前台窗口属于本进程（防止自己选自己的译文）
  - 前台进程在内置保护列表（密码管理器类）或用户自定义排除列表
  - 光标位于任务栏/桌面壳层

终端类程序（WindowsTerminal、conhost、cmd、PowerShell）默认**只走 UIA，不走模拟复制**：向终端发送 Ctrl+C 可能中断正在运行的命令，风险高于收益。

### 4.3 文本获取流水线

`SelectionPipeline.AcquireAsync(pointerPos, ct)` 严格按顺序执行：

```
1. UIA 读取（预算 250ms）
   ├ 取光标处 AutomationElement，向上找支持 TextPattern 的祖先
   ├ 同时尝试 FocusedElement 的 TextPattern.GetSelection()
   ├ IsPassword == true → 直接返回 null（不取密码）
   ├ 只接受可编辑/文档控件的选区
   └ 失败 → 进入 2

2. 模拟复制（预算 500ms）
   ├ 记录 GetClipboardSequenceNumber()
   ├ 若剪贴板当前是文本，保存原文本
   ├ SendInput 清修饰键 + Ctrl+C（一个批次，避免用户按键插入）
   ├ 每 10ms 轮询序列号变化，最多 500ms
   ├ 读到文本后立即恢复原剪贴板（最多重试 5 次）
   └ 失败 → 返回 null

3. 结束：没有文本 → 图标模式下静默消失；快捷键模式下提示"未取到选中文字"
```

### 4.4 文本校验（TextValidator）

统一过滤后再进翻译：

- 去首尾空白后长度 1–5000 字符
- 必须包含至少一个字母（纯数字/纯符号不翻译）
- 排除本程序自己的窗口内容
- 与上一次成功取词完全相同则跳过（防重复触发）
- 记录来源进程名，便于排除列表与统计

### 4.5 剪贴板安全

- 只保存/恢复**文本**；图片、文件等二进制内容不读不写，避免破坏用户数据。
- 取词期间用进程内互斥量串行化，避免并发复制。
- 恢复失败时记录日志但不重试无限次，避免卡死。

### 4.6 权限与兼容性

- 普通权限程序无法向管理员窗口发送输入（UIPI），此时 UIA 往往也不可用。检测到失败且前台窗口已提权时，提示用户"目标程序以管理员身份运行，请以管理员身份启动本工具"。
- 全屏独占游戏、DRM 视频、部分自绘界面不可取词，属于系统限制。

## 5. 翻译子系统

### 5.1 Provider 抽象

```csharp
public interface ITranslationProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task TranslateAsync(TranslationRequest request,
                        IProgress<TranslationChunk> onChunk,
                        CancellationToken ct);
}

public sealed record TranslationRequest(
    string Text,
    string? SourceLang,   // null = 自动判断
    string TargetLang,
    string? PageContext); // 可选：窗口标题，帮助理解语境

public sealed record TranslationChunk(string Delta, bool IsFinal, string? Error);
```

实现：

| Provider | Id | 说明 |
|---|---|---|
| 智谱 GLM | `glm` | 默认。`https://open.bigmodel.cn/api/paas/v4/chat/completions`，默认 `glm-4-flash`（免费） |
| OpenAI 兼容 | `openai-compatible` | 自定义 baseUrl / model / key，兼容硅基流动、DeepSeek、OpenRouter 等 |
| Ollama | `ollama` | 复用 OpenAI 兼容实现，baseUrl 默认 `http://127.0.0.1:11434/v1` |

三者共用 SSE 流式解析逻辑，不重复实现。

### 5.2 提示词

```
system:
你是专业英中翻译引擎。将用户提供的文本翻译成{目标语言}。
规则：
1. 只输出译文，不要解释、不要加引号、不要重复原文。
2. 保持原文的段落与标点结构。
3. 专有名词、代码、变量名保留原文。
4. 如果原文是单词或短语，给出最常用的 1–2 个义项，用「；」分隔。
5. 如果原文已经是目标语言，原样返回。

user:
{选中的文本}
```

- 目标语言默认 `简体中文`，可在设置中改为英语/日语等。
- 不单独做语言检测：由模型按规则自行判断，原文若已是目标语言则原样返回；用户可随时手动切换目标语言。

### 5.3 流式

- `HttpClient` + `HttpCompletionOption.ResponseHeadersRead`，逐行读 `data:`，解析 `choices[0].delta.content`。
- 首字到达即渲染，不等完整响应。
- 15 秒无任何数据视为超时；总时长上限 60 秒。
- 429 / 5xx：指数退避重试 1 次，并在卡片上提示"请求过于频繁"。

### 5.4 缓存

- 内存 LRU 200 条 + 磁盘 `%APPDATA%\SelectionTranslator\cache.json`。
- Key = SHA256(providerId + model + targetLang + 原文)，Value = 译文 + 时间戳。
- 命中直接展示并标注（可选显示"缓存"）。
- TTL 30 天，启动时异步清理。

## 6. UI 设计

### 6.1 悬浮图标 `SelectionIconWindow`

- 16×16 圆角图标，位于光标右下 12px，屏幕边缘自动翻转。
- 窗口样式：`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`，`ShowActivated=false`，不出现在 Alt+Tab。
- 关闭时机：点击图标（进入翻译）、点击其他位置、滚轮、按键、Esc、3 秒后鼠标远离。
- 图标点击**不抢焦点**，保证目标程序选区不被清空。

### 6.2 译文卡片 `TranslationPopup`

- 宽度默认 420px（可拖拽调整），最大高度 60% 屏幕，超出滚动。
- 结构：
  ```
  [原文（最多 4 行，超出折叠）]
  ────────────────────────
  [流式译文]
  ────────────────────────
  [复制] [重新翻译] [切换模型] [固定] [关闭]   延迟 0.7s
  ```
- 位置：光标右下，屏幕边缘自动翻转并夹在屏幕内；跟随多显示器 DPI。
- 交互：Esc 关闭；拖动可移动；"固定"后不随新翻译更新；点击外部关闭（可配置）。
- 首次显示不激活窗口，用户点击卡片内按钮时才激活。
- 跟随系统深色/浅色主题。

### 6.3 托盘与设置

- 托盘菜单：翻译剪贴板 / 显示设置 / 暂停划词 / 开机自启 / 退出。
- 设置页分区：触发方式、翻译服务、外观、排除应用、关于。
- 设置即时生效，无需重启。

## 7. 配置与密钥

路径：`%APPDATA%\SelectionTranslator\config.json`

```json
{
  "hotkey": "Alt+D",
  "trigger": { "showIcon": true, "autoTranslate": false, "autoStart": true },
  "translate": {
    "provider": "glm",
    "model": "glm-4-flash",
    "baseUrl": "https://open.bigmodel.cn/api/paas/v4",
    "apiKeyProtected": "<DPAPI base64>",
    "targetLang": "简体中文",
    "timeoutSeconds": 60
  },
  "ui": { "theme": "system", "fontSize": 14, "popupWidth": 420 },
  "exclude": { "processes": [] }
}
```

- API Key 使用 Windows DPAPI（`ProtectedData`, CurrentUser）加密后存储，绝不写入日志。
- 配置写入采用临时文件 + 原子替换，防止崩溃损坏。
- 不收集任何遥测。

## 8. 错误处理与降级

| 故障 | 行为 |
|---|---|
| UIA 拿不到 | 静默降级到模拟复制 |
| 模拟复制失败 | 图标消失；快捷键触发时提示"未取到选中文字" |
| 剪贴板被占用 | 20ms 间隔重试 5 次，失败则提示 |
| 无网络 / DNS 失败 | 卡片显示错误 + 重试按钮 |
| 401 / 403 | 提示检查 API Key，提供"打开设置"入口 |
| 429 | 退避 1 次，仍失败则提示稍后再试 |
| 流中断 | 保留已显示的部分，追加"（已中断）"，可重试 |
| 译文为空 | 视为失败，保留原文并给出重试 |
| 快捷键被其他程序占用 | 启动时提示冲突，引导在设置中改键，不静默失效 |
| 连续失败 3 次 | 托盘气泡建议检查配置 |

## 9. 性能预算

| 指标 | 目标 |
|---|---|
| 冷启动到托盘可用 | < 1.0s |
| 空闲内存 | < 120MB |
| 空闲 CPU | < 0.5% |
| UIA 取词 | P50 40ms / P95 250ms |
| 剪贴板取词 | P50 120ms / P95 400ms |
| 首字译文 | P95 1.5s |
| 译文完成（100 字英文） | P95 3.0s |

## 10. 安全与隐私

- 选中文本只发送到用户自己配置的翻译端点，默认不发往任何第三方服务器。
- 日志默认不记录原文与译文，只记录长度、耗时、错误码；调试模式需手动开启。
- 密码框、排除进程不取词。
- 剪贴板用完即恢复。
- 不加载任何远程脚本，不自动更新。

## 11. 技术栈

| 依赖 | 版本 | 许可 | 用途 |
|---|---|---|---|
| .NET 8 + WPF | 8.0 | MIT | 主框架 |
| CommunityToolkit.Mvvm | 8.x | MIT | MVVM |
| Microsoft.Windows.CsWin32 | 最新 | MIT | Win32 互操作源码生成 |
| H.NotifyIcon.Wpf | 2.x | MIT | 托盘图标 |
| WPF-UI（可选） | 4.x | MIT | Fluent 风格控件 |
| xUnit | 最新 | Apache-2.0 | 单元测试与断言 |

环境要求：Windows 10/11 x64，**.NET 8 SDK**（用于编译；本机当前只有 8.0.30 运行时，需要安装 SDK）。

## 12. 目录结构

```
SelectionTranslator/
├─ SelectionTranslator.sln
├─ src/
│  ├─ SelectionTranslator.App/          WPF 宿主、托盘、窗口
│  │  ├─ App.xaml(.cs)                  单实例、依赖装配、全局异常
│  │  ├─ Views/                         IconWindow, Popup, Settings
│  │  ├─ ViewModels/
│  │  └─ Assets/
│  ├─ SelectionTranslator.Core/         无 UI 依赖，可单测
│  │  ├─ Interop/                       MouseHook, Hotkey, SendInput, Clipboard
│  │  ├─ Selection/                     Acquirers, Pipeline, Validator
│  │  ├─ Translation/                   Providers, Prompt, Cache, SSE
│  │  └─ Settings/                      Config, DPAPI, Migration
│  └─ SelectionTranslator.Tests/
└─ docs/
```

## 13. 测试策略

**单元测试（自动化）**
- `TextValidator`：长度边界、纯数字、密码、重复
- `PromptBuilder`：目标语言、上下文拼接
- SSE 解析：多行、`[DONE]`、异常 JSON、分片跨包
- 缓存：命中/过期/容量淘汰
- 配置：默认值、损坏文件回退、密钥加解密
- 剪贴板保存恢复：用假的剪贴板接口

**手动测试矩阵**

| 程序 | 取词 | 说明 |
|---|---|---|
| Chrome / Edge | UIA | 主流场景 |
| Word / WPS | UIA 或复制 | 富文本 |
| 记事本 / VS Code | UIA | 基础验证 |
| Adobe Acrobat（文本 PDF） | 复制 | 选区行为特殊 |
| 微信 / QQ | 复制 | 自绘界面 |
| Windows Terminal | 复制 | Ctrl+C 语义冲突，需验证 |
| 管理员权限窗口 | 失败提示 | 权限边界 |
| 多显示器 + 150% 缩放 | 位置正确 | DPI |

**验收**：第 2 节表格全部通过。

## 14. 里程碑

| 阶段 | 内容 | 可验证结果 |
|---|---|---|
| M1 骨架 | 托盘 + 全局快捷键 + 剪贴板取词 + GLM 流式 + 基础卡片 | 任意程序按 Alt+D 能出译文 |
| M2 取词增强 | UIA 快速路径 + 划词图标 + 排除规则 | Chrome/Word 无剪贴板污染 |
| M3 完整 | 设置页 + 多 Provider + 缓存 + DPAPI + 深色模式 | 可日常使用 |
| M4 打磨 | DPI、多屏、单实例、开机自启、错误提示、安装包 | 可分发 |
| M5（以后） | OCR 兜底 | 图片/扫描件可翻译 |

## 15. 风险与未决问题

| 风险 | 影响 | 应对 |
|---|---|---|
| UIA 在不同应用行为不一致 | 取词变慢或失败 | 统一降级到复制；建立兼容性测试矩阵 |
| 全局钩子被杀软误报 | 无法安装/启动 | 代码签名；安装包说明；避免注入行为 |
| 模拟 Ctrl+C 影响终端等程序 | 副作用 | 排除列表 + 默认关闭终端类自动触发 |
| WPF 无焦点窗口在多屏 DPI 下漂移 | 图标位置错 | 用 `MonitorFromPoint` + PerMonitorV2 清单 |
| 免费模型额度/政策变化 | 默认不可用 | Provider 可切换，设置页给一键切换预设 |
| 未签名 exe 的 SmartScreen 警告 | 分发受阻 | 后续购买代码签名证书 |

**已定决策**
- 语言方向：默认自动识别 → 简体中文；目标语言可改。
- 不做浏览器扩展，桌面端覆盖网页场景。
- 默认不自动翻译，只弹图标；快捷键常备。

**待确认**
- 是否需要开机自启默认开启（当前设计为默认开启，可在设置关闭）。
- 设置界面是否需要中英双语（当前先做简体中文）。