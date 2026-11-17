# 系统级划词翻译工具 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Windows 上做一个常驻托盘的划词翻译工具，任意程序里选中英文后，通过悬浮图标或 `Alt+D` 看到流式中文译文。

**Architecture:** 单个 WPF 进程，内部分为 Core（取词、翻译、配置，无 UI 代码）与 App（窗口、托盘、装配）。取词走 UIA 优先、模拟 Ctrl+C 兜底的两级流水线；翻译走 OpenAI 兼容的 SSE 流式接口，默认智谱 GLM-4-Flash。

**Tech Stack:** .NET 8 (`net8.0-windows`)、WPF、WinForms（仅托盘 `NotifyIcon`）、CommunityToolkit.Mvvm、System.Text.Json、手写 Win32 P/Invoke、xUnit。

**Spec:** `docs/superpowers/specs/2026-11-17-selection-translator-design.md`

## Global Constraints

- 平台：Windows 10/11 x64，目标框架 `net8.0-windows`，需要 .NET 8 SDK。
- 默认翻译服务：`glm` / `glm-4-flash`，BaseUrl `https://open.bigmodel.cn/api/paas/v4`。
- 默认快捷键 `Alt+D`；默认目标语言 `简体中文`；默认不自动翻译，只弹图标。
- 取词上限 5000 字符；UIA 预算 250ms；模拟复制预算 500ms。
- API Key 必须 DPAPI 加密后落盘；日志不得记录原文或译文。
- 不引入遥测、不加载远程脚本、不做自动更新。
- 所有依赖许可证必须是 MIT / Apache-2.0 / 公共域。
- 终端类进程只允许 UIA 取词，禁止模拟 Ctrl+C。
- 每次 git 提交信息末尾追加：`Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`。
- 测试命令统一使用：`dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj`。

---

## 文件结构

```
SelectionTranslator.sln
Directory.Build.props
.gitignore
src/
├─ SelectionTranslator.Core/
│  ├─ SelectionTranslator.Core.csproj      net8.0-windows, UseWPF=true
│  ├─ Selection/
│  │  ├─ TextValidator.cs                  文本校验与归一化
│  │  ├─ ITextAcquirer.cs                  取词接口
│  │  ├─ DragTracker.cs                    拖选判定（纯逻辑）
│  │  ├─ SelectionPipeline.cs              取词流水线
│  │  ├─ SelectionContext.cs               取词上下文
│  │  ├─ AcquisitionPolicy.cs              进程排除策略
│  │  ├─ ClipboardTextAcquirer.cs          模拟复制取词
│  │  └─ UiaTextAcquirer.cs                UI Automation 取词
│  ├─ Translation/
│  │  ├─ TranslationModels.cs              请求/分片/接口/异常
│  │  ├─ PromptBuilder.cs                  提示词
│  │  ├─ SseReader.cs                      SSE 事件解析
│  │  ├─ OpenAiStream.cs                   OpenAI chunk 解析
│  │  ├─ OpenAiCompatibleProvider.cs       Provider 实现
│  │  ├─ ProviderFactory.cs                按配置创建 Provider
│  │  ├─ TranslationCache.cs               LRU + 磁盘缓存
│  │  └─ CacheKey.cs                       缓存键
│  ├─ Settings/
│  │  ├─ AppSettings.cs                    配置模型
│  │  ├─ SettingsStore.cs                  读写配置
│  │  └─ DpapiSecretProtector.cs           DPAPI 密钥保护
│  ├─ Interop/
│  │  ├─ NativeMethods.cs                  user32 P/Invoke
│  │  ├─ ClipboardService.cs               剪贴板读写
│  │  ├─ InputSimulator.cs                 SendInput Ctrl+C
│  │  ├─ HotkeyParser.cs                   快捷键字符串解析
│  │  └─ StaRunner.cs                      STA 线程执行
│  ├─ Hotkey/
│  │  └─ HotkeyManager.cs                  RegisterHotKey 消息窗口
│  └─ Diagnostics/
│     └─ AppLog.cs                         文件日志
├─ SelectionTranslator.App/
│  ├─ SelectionTranslator.App.csproj       WPF + WinForms 托盘
│  ├─ app.manifest                         PerMonitorV2
│  ├─ App.xaml / App.xaml.cs               单实例、DI、启动
│  ├─ AppPaths.cs                          数据目录
│  ├─ Views/
│  │  ├─ TranslationPopupWindow.xaml(.cs)  译文卡片
│  │  ├─ SelectionIconWindow.xaml(.cs)     悬浮图标
│  │  └─ SettingsWindow.xaml(.cs)          设置页
│  ├─ ViewModels/
│  │  └─ TranslationPopupViewModel.cs
│  ├─ Services/
│  │  ├─ TrayIconService.cs                托盘
│  │  ├─ TranslationCoordinator.cs         取词→翻译→展示
│  │  ├─ SelectionIconController.cs        划词图标控制
│  │  ├─ LowLevelMouseHook.cs              全局鼠标钩子
│  │  ├─ WindowPlacer.cs                   多屏定位
│  │  └─ AutoStartService.cs               开机自启
│  └─ Assets/                              预留图标
└─ SelectionTranslator.Tests/
   ├─ SelectionTranslator.Tests.csproj
   ├─ Selection/TextValidatorTests.cs
   ├─ Selection/PromptBuilderTests.cs
   ├─ Selection/SseReaderTests.cs
   ├─ Selection/TranslationCacheTests.cs
   ├─ Selection/SettingsStoreTests.cs
   ├─ Selection/HotkeyParserTests.cs
   ├─ Selection/DragTrackerTests.cs
   ├─ Selection/ClipboardTextAcquirerTests.cs
   ├─ Selection/SelectionPipelineTests.cs
   └─ Translation/OpenAiProviderTests.cs
```

---

## Task 1: 解决方案骨架与文本校验

**Files:**
- Create: `.gitignore`, `Directory.Build.props`, `SelectionTranslator.sln`
- Create: `src/SelectionTranslator.Core/SelectionTranslator.Core.csproj`
- Create: `src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj`
- Create: `src/SelectionTranslator.App/SelectionTranslator.App.csproj`
- Create: `src/SelectionTranslator.Core/Selection/TextValidator.cs`
- Test: `src/SelectionTranslator.Tests/Selection/TextValidatorTests.cs`

**Interfaces:**
- Produces: `SelectionTranslator.Core.Selection.TextValidator.TryNormalize(string? raw, out string text, out string? reason) : bool`
- Produces: 常量 `TextValidator.MaxLength = 5000`

- [ ] **Step 1: 安装 .NET 8 SDK（仅当缺失时）**

```bash
dotnet --list-sdks
```

若输出为空：

```bash
winget install --id Microsoft.DotNet.SDK.8 -e --source winget
```

安装后重开终端并再次执行 `dotnet --list-sdks`，必须看到 `8.0.x`。此步骤会修改系统，执行前先向用户确认。

- [ ] **Step 2: 创建解决方案与三个项目**

```bash
git init
dotnet new sln -n SelectionTranslator
dotnet new classlib -n SelectionTranslator.Core -o src/SelectionTranslator.Core -f net8.0
dotnet new xunit -n SelectionTranslator.Tests -o src/SelectionTranslator.Tests -f net8.0
dotnet new wpf -n SelectionTranslator.App -o src/SelectionTranslator.App -f net8.0
dotnet sln add src/SelectionTranslator.Core src/SelectionTranslator.Tests src/SelectionTranslator.App
dotnet add src/SelectionTranslator.Tests reference src/SelectionTranslator.Core
dotnet add src/SelectionTranslator.App reference src/SelectionTranslator.Core
rm src/SelectionTranslator.Core/Class1.cs
```

- [ ] **Step 3: 配置项目文件**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

`src/SelectionTranslator.Core/SelectionTranslator.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <RootNamespace>SelectionTranslator.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
  </ItemGroup>
</Project>
```

`src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj`：把 `<TargetFramework>` 改为 `net8.0-windows`，并在首个 `PropertyGroup` 内加入：

```xml
<UseWPF>true</UseWPF>
```

`src/SelectionTranslator.App/SelectionTranslator.App.csproj` 的 `PropertyGroup` 内加入：

```xml
<UseWindowsForms>true</UseWindowsForms>
<ApplicationManifest>app.manifest</ApplicationManifest>
```

并在 `ItemGroup` 内加入：

```xml
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
```

`.gitignore`:

```gitignore
bin/
obj/
.vs/
*.user
*.suo
publish/
```

- [ ] **Step 4: 写失败的测试**

`src/SelectionTranslator.Tests/Selection/TextValidatorTests.cs`:

```csharp
using SelectionTranslator.Core.Selection;
using Xunit;

namespace SelectionTranslator.Tests.Selection;

public class TextValidatorTests
{
    [Fact]
    public void Trims_and_accepts_english()
    {
        Assert.True(TextValidator.TryNormalize("  hello world \n", out var text, out var reason));
        Assert.Equal("hello world", text);
        Assert.Null(reason);
    }

    [Fact]
    public void Rejects_empty_or_whitespace()
    {
        Assert.False(TextValidator.TryNormalize("   ", out _, out var reason));
        Assert.Equal("empty", reason);
        Assert.False(TextValidator.TryNormalize(null, out _, out _));
    }

    [Fact]
    public void Rejects_text_over_5000_chars()
    {
        var raw = new string('a', TextValidator.MaxLength + 1);
        Assert.False(TextValidator.TryNormalize(raw, out _, out var reason));
        Assert.Equal("too-long", reason);
    }

    [Fact]
    public void Rejects_digits_and_symbols_without_letters()
    {
        Assert.False(TextValidator.TryNormalize("12345 !!!", out _, out var reason));
        Assert.Equal("no-letter", reason);
    }

    [Fact]
    public void Accepts_chinese_and_mixed_text()
    {
        Assert.True(TextValidator.TryNormalize("你好 hello", out var text, out _));
        Assert.Equal("你好 hello", text);
    }
}
```

- [ ] **Step 5: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~TextValidatorTests
```

预期：编译失败，提示 `TextValidator` 不存在。

- [ ] **Step 6: 写最小实现**

`src/SelectionTranslator.Core/Selection/TextValidator.cs`:

```csharp
namespace SelectionTranslator.Core.Selection;

public static class TextValidator
{
    public const int MaxLength = 5000;

    public static bool TryNormalize(string? raw, out string text, out string? reason)
    {
        text = (raw ?? string.Empty).Trim();
        reason = null;

        if (text.Length == 0)
        {
            reason = "empty";
            return false;
        }

        if (text.Length > MaxLength)
        {
            reason = "too-long";
            return false;
        }

        if (!text.Any(char.IsLetter))
        {
            reason = "no-letter";
            return false;
        }

        return true;
    }
}
```

- [ ] **Step 7: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~TextValidatorTests
```

预期：5 个测试全部 PASS。

- [ ] **Step 8: 提交**

```bash
git add .gitignore Directory.Build.props SelectionTranslator.sln src
git commit -m "chore: scaffold solution and add text validator"
```

---

## Task 2: 翻译模型与提示词

**Files:**
- Create: `src/SelectionTranslator.Core/Translation/TranslationModels.cs`
- Create: `src/SelectionTranslator.Core/Translation/PromptBuilder.cs`
- Test: `src/SelectionTranslator.Tests/Selection/PromptBuilderTests.cs`

**Interfaces:**
- Produces: `TranslationRequest(string Text, string? SourceLang, string TargetLang, string? PageContext)`
- Produces: `TranslationChunk(string Delta, bool IsFinal, string? Error)`
- Produces: `ITranslationProvider { string Id; string DisplayName; Task TranslateAsync(TranslationRequest, IProgress<TranslationChunk>, CancellationToken); }`
- Produces: `TranslationException : Exception` 带 `int? StatusCode`
- Produces: `PromptBuilder.Build(TranslationRequest) : IReadOnlyList<ChatMessage>`
- Produces: `ChatMessage(string Role, string Content)`

- [ ] **Step 1: 写失败的测试**

`src/SelectionTranslator.Tests/Selection/PromptBuilderTests.cs`:

```csharp
using SelectionTranslator.Core.Translation;
using Xunit;

namespace SelectionTranslator.Tests.Selection;

public class PromptBuilderTests
{
    [Fact]
    public void System_prompt_contains_target_language()
    {
        var messages = PromptBuilder.Build(new TranslationRequest("hello", null, "简体中文", null));
        Assert.Equal("system", messages[0].Role);
        Assert.Contains("简体中文", messages[0].Content);
    }

    [Fact]
    public void User_prompt_is_raw_text_without_context()
    {
        var messages = PromptBuilder.Build(new TranslationRequest("hello", null, "简体中文", null));
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("hello", messages[1].Content);
    }

    [Fact]
    public void Context_is_prefixed_to_user_prompt()
    {
        var messages = PromptBuilder.Build(new TranslationRequest("hello", null, "简体中文", "BBC News"));
        Assert.Contains("BBC News", messages[1].Content);
        Assert.EndsWith("hello", messages[1].Content);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~PromptBuilderTests
```

预期：编译失败，类型不存在。

- [ ] **Step 3: 写实现**

`src/SelectionTranslator.Core/Translation/TranslationModels.cs`:

```csharp
namespace SelectionTranslator.Core.Translation;

public sealed record TranslationRequest(
    string Text,
    string? SourceLang,
    string TargetLang,
    string? PageContext = null);

public sealed record TranslationChunk(string Delta, bool IsFinal, string? Error = null);

public interface ITranslationProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task TranslateAsync(
        TranslationRequest request,
        IProgress<TranslationChunk> onChunk,
        CancellationToken cancellationToken);
}

public sealed class TranslationException : Exception
{
    public int? StatusCode { get; }

    public TranslationException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
```

`src/SelectionTranslator.Core/Translation/PromptBuilder.cs`:

```csharp
namespace SelectionTranslator.Core.Translation;

public sealed record ChatMessage(string Role, string Content);

public static class PromptBuilder
{
    public static IReadOnlyList<ChatMessage> Build(TranslationRequest request)
    {
        var system = $"""
            你是专业翻译引擎。将用户提供的文本翻译成{request.TargetLang}。
            规则：
            1. 只输出译文，不要解释、不要加引号、不要重复原文。
            2. 保持原文的段落与标点结构。
            3. 专有名词、代码、变量名保留原文。
            4. 如果原文是单词或短语，给出最常用的 1-2 个义项，用「；」分隔。
            5. 如果原文已经是{request.TargetLang}，原样返回。
            """;

        var user = string.IsNullOrWhiteSpace(request.PageContext)
            ? request.Text
            : $"窗口标题：{request.PageContext}\n\n{request.Text}";

        return [new ChatMessage("system", system), new ChatMessage("user", user)];
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~PromptBuilderTests
```

预期：3 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/SelectionTranslator.Core/Translation src/SelectionTranslator.Tests/Selection/PromptBuilderTests.cs
git commit -m "feat: add translation models and prompt builder"
```

---

## Task 3: SSE 流解析

**Files:**
- Create: `src/SelectionTranslator.Core/Translation/SseReader.cs`
- Create: `src/SelectionTranslator.Core/Translation/OpenAiStream.cs`
- Test: `src/SelectionTranslator.Tests/Selection/SseReaderTests.cs`

**Interfaces:**
- Produces: `SseReader.ReadDataEventsAsync(Stream, CancellationToken) : IAsyncEnumerable<string>`
- Produces: `OpenAiStream.ReadDeltasAsync(Stream, CancellationToken) : IAsyncEnumerable<string>`

- [ ] **Step 1: 写失败的测试**

`src/SelectionTranslator.Tests/Selection/SseReaderTests.cs`:

```csharp
using System.Text;
using SelectionTranslator.Core.Translation;
using Xunit;

namespace SelectionTranslator.Tests.Selection;

public class SseReaderTests
{
    private static async Task<List<string>> ReadAsync(string raw)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var result = new List<string>();
        await foreach (var item in SseReader.ReadDataEventsAsync(stream, CancellationToken.None))
            result.Add(item);
        return result;
    }

    [Fact]
    public async Task Reads_single_data_line()
    {
        var events = await ReadAsync("data: hello\n\n");
        Assert.Equal(new[] { "hello" }, events);
    }

    [Fact]
    public async Task Skips_comments_and_ignores_non_data_fields()
    {
        var events = await ReadAsync(": ping\nevent: message\ndata: hi\n\n");
        Assert.Equal(new[] { "hi" }, events);
    }

    [Fact]
    public async Task Joins_multiline_data()
    {
        var events = await ReadAsync("data: a\ndata: b\n\n");
        Assert.Equal(new[] { "a\nb" }, events);
    }

    [Fact]
    public async Task Flushes_last_event_without_trailing_blank_line()
    {
        var events = await ReadAsync("data: tail");
        Assert.Equal(new[] { "tail" }, events);
    }

    [Fact]
    public async Task Extracts_openai_deltas_and_stops_at_done()
    {
        const string raw =
            "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\n" +
            "data: [DONE]\n\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var deltas = new List<string>();
        await foreach (var d in OpenAiStream.ReadDeltasAsync(stream, CancellationToken.None))
            deltas.Add(d);
        Assert.Equal(new[] { "你", "好" }, deltas);
    }

    [Fact]
    public async Task Skips_malformed_json()
    {
        const string raw = "data: {bad json}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var deltas = new List<string>();
        await foreach (var d in OpenAiStream.ReadDeltasAsync(stream, CancellationToken.None))
            deltas.Add(d);
        Assert.Equal(new[] { "ok" }, deltas);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~SseReaderTests
```

预期：编译失败。

- [ ] **Step 3: 写实现**

`src/SelectionTranslator.Core/Translation/SseReader.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text;

namespace SelectionTranslator.Core.Translation;

public static class SseReader
{
    public static async IAsyncEnumerable<string> ReadDataEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        var data = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
            {
                if (data.Length > 0)
                    yield return data.ToString();
                yield break;
            }

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return data.ToString();
                    data.Clear();
                }
                continue;
            }

            if (line.StartsWith(':'))
                continue;

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(line[5..].TrimStart());
            }
        }
    }
}
```

`src/SelectionTranslator.Core/Translation/OpenAiStream.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SelectionTranslator.Core.Translation;

public static class OpenAiStream
{
    public static async IAsyncEnumerable<string> ReadDeltasAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var payload in SseReader.ReadDataEventsAsync(stream, cancellationToken))
        {
            if (payload == "[DONE]")
                yield break;

            string? delta = null;

            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;

                if (root.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var m)
                        ? m.GetString()
                        : null;
                    throw new TranslationException(message ?? "翻译服务返回错误");
                }

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];

                if (choice.TryGetProperty("delta", out var deltaElement) &&
                    deltaElement.TryGetProperty("content", out var content))
                {
                    delta = content.GetString();
                }
                else if (choice.TryGetProperty("text", out var text))
                {
                    delta = text.GetString();
                }
            }
            catch (JsonException)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~SseReaderTests
```

预期：6 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/SelectionTranslator.Core/Translation/SseReader.cs src/SelectionTranslator.Core/Translation/OpenAiStream.cs src/SelectionTranslator.Tests/Selection/SseReaderTests.cs
git commit -m "feat: parse SSE streams from OpenAI-compatible APIs"
```

---

## Task 4: 翻译缓存

**Files:**
- Create: `src/SelectionTranslator.Core/Translation/CacheKey.cs`
- Create: `src/SelectionTranslator.Core/Translation/TranslationCache.cs`
- Test: `src/SelectionTranslator.Tests/Selection/TranslationCacheTests.cs`

**Interfaces:**
- Produces: `CacheKey.For(string providerId, string model, string targetLang, string text) : string`
- Produces: `TranslationCache(string path, int capacity = 200, TimeSpan? ttl = null, TimeProvider? timeProvider = null)`
- Produces: `TryGet(string key, out string value) : bool`、`Set(string key, string value)`、`Load()`、`Save()`

- [ ] **Step 1: 写失败的测试**

`src/SelectionTranslator.Tests/Selection/TranslationCacheTests.cs`:

```csharp
using SelectionTranslator.Core.Translation;
using Xunit;

namespace SelectionTranslator.Tests.Selection;

public class TranslationCacheTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cache-{Guid.NewGuid():N}.json");

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Set_then_get_returns_value()
    {
        using var cache = new TranslationCache(_path);
        cache.Set("k", "v");
        Assert.True(cache.TryGet("k", out var value));
        Assert.Equal("v", value);
    }

    [Fact]
    public void Missing_key_returns_false()
    {
        using var cache = new TranslationCache(_path);
        Assert.False(cache.TryGet("missing", out _));
    }

    [Fact]
    public void Evicts_least_recently_used()
    {
        using var cache = new TranslationCache(_path, capacity: 2);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.TryGet("a", out _);
        cache.Set("c", "3");

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Expires_after_ttl()
    {
        var time = new FakeTime();
        using var cache = new TranslationCache(_path, ttl: TimeSpan.FromDays(30), timeProvider: time);
        cache.Set("k", "v");
        time.Now = time.Now.AddDays(31);
        Assert.False(cache.TryGet("k", out _));
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        using (var cache = new TranslationCache(_path))
        {
            cache.Set("k", "v");
            cache.Save();
        }

        using var reloaded = new TranslationCache(_path);
        reloaded.Load();
        Assert.True(reloaded.TryGet("k", out var value));
        Assert.Equal("v", value);
    }

    [Fact]
    public void Same_input_produces_same_key()
    {
        var a = CacheKey.For("glm", "glm-4-flash", "简体中文", "hello");
        var b = CacheKey.For("glm", "glm-4-flash", "简体中文", "hello");
        var c = CacheKey.For("glm", "glm-4-flash", "简体中文", "hello!");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~TranslationCacheTests
```

预期：编译失败。

- [ ] **Step 3: 写实现**

`src/SelectionTranslator.Core/Translation/CacheKey.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace SelectionTranslator.Core.Translation;

public static class CacheKey
{
    public static string For(string providerId, string model, string targetLang, string text)
    {
        var raw = $"{providerId}\u0000{model}\u0000{targetLang}\u0000{text}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
```

`src/SelectionTranslator.Core/Translation/TranslationCache.cs`:

```csharp
using System.Text.Json;

namespace SelectionTranslator.Core.Translation;

public sealed class CacheEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public CacheEntry()
    {
    }

    public CacheEntry(string key, string value, DateTimeOffset createdAt)
    {
        Key = key;
        Value = value;
        CreatedAt = createdAt;
    }
}

public sealed class TranslationCache : IDisposable
{
    private readonly int _capacity;
    private readonly string _path;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();

    public TranslationCache(
        string path,
        int capacity = 200,
        TimeSpan? ttl = null,
        TimeProvider? timeProvider = null)
    {
        _path = path;
        _capacity = capacity;
        _ttl = ttl ?? TimeSpan.FromDays(30);
        _time = timeProvider ?? TimeProvider.System;
    }

    public bool TryGet(string key, out string value)
    {
        lock (_lock)
        {
            value = string.Empty;
            if (!_map.TryGetValue(key, out var node))
                return false;

            if (IsExpired(node.Value))
            {
                _lru.Remove(node);
                _map.Remove(key);
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(string key, string value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _map.Remove(key);
            }

            var node = _lru.AddFirst(new CacheEntry(key, value, _time.GetUtcNow()));
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var last = _lru.Last;
                if (last is null) break;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }

    public void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            var entries = JsonSerializer.Deserialize<List<CacheEntry>>(File.ReadAllText(_path));
            if (entries is null)
                return;

            lock (_lock)
            {
                _map.Clear();
                _lru.Clear();

                foreach (var entry in entries.OrderByDescending(e => e.CreatedAt))
                {
                    if (IsExpired(entry)) continue;
                    var node = _lru.AddFirst(entry);
                    _map[entry.Key] = node;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 缓存损坏不影响主流程，直接当作空缓存。
        }
    }

    public void Save()
    {
        try
        {
            List<CacheEntry> snapshot;
            lock (_lock)
            {
                snapshot = _lru.ToList();
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
    }

    private bool IsExpired(CacheEntry entry) => _time.GetUtcNow() - entry.CreatedAt > _ttl;

    public void Dispose() => Save();
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~TranslationCacheTests
```

预期：6 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/SelectionTranslator.Core/Translation/CacheKey.cs src/SelectionTranslator.Core/Translation/TranslationCache.cs src/SelectionTranslator.Tests/Selection/TranslationCacheTests.cs
git commit -m "feat: add LRU translation cache with disk persistence"
```

---

## Task 5: 配置与 DPAPI 密钥

**Files:**
- Create: `src/SelectionTranslator.Core/Settings/AppSettings.cs`
- Create: `src/SelectionTranslator.Core/Settings/SettingsStore.cs`
- Create: `src/SelectionTranslator.Core/Settings/DpapiSecretProtector.cs`
- Test: `src/SelectionTranslator.Tests/Selection/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `AppSettings`（`Hotkey`, `Trigger`, `Translate`, `Ui`, `Exclude`）
- Produces: `SettingsStore(string path) { AppSettings Current; void Load(); void Save(); }`
- Produces: `ISecretProtector { string Protect(string); string Unprotect(string); }`
- Produces: `DpapiSecretProtector`

- [ ] **Step 1: 写失败的测试**

`src/SelectionTranslator.Tests/Selection/SettingsStoreTests.cs`:

```csharp
using SelectionTranslator.Core.Settings;
using Xunit;

namespace SelectionTranslator.Tests.Selection;

public class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"config-{Guid.NewGuid():N}.json");

    [Fact]
    public void Creates_defaults_when_file_missing()
    {
        var store = new SettingsStore(_path);
        store.Load();
        Assert.Equal("Alt+D", store.Current.Hotkey);
        Assert.Equal("glm", store.Current.Translate.Provider);
        Assert.True(store.Current.Trigger.ShowIcon);
        Assert.False(store.Current.Trigger.AutoTranslate);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        var store = new SettingsStore(_path);
        store.Load();
        store.Current.Hotkey = "Ctrl+Shift+T";
        store.Current.Translate.TargetLang = "English";
        store.Save();

        var reloaded = new SettingsStore(_path);
        reloaded.Load();
        Assert.Equal("Ctrl+Shift+T", reloaded.Current.Hotkey);
        Assert.Equal("English", reloaded.Current.Translate.TargetLang);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        File.WriteAllText(_path, "{ not json");
        var store = new SettingsStore(_path);
        store.Load();
        Assert.Equal("Alt+D", store.Current.Hotkey);
    }

    [Fact]
    public void Dpapi_roundtrip()
    {
        var protector = new DpapiSecretProtector();
        var encrypted = protector.Protect("sk-test-123");
        Assert.NotEqual("sk-test-123", encrypted);
        Assert.Equal("sk-test-123", protector.Unprotect(encrypted));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~SettingsStoreTests
```

预期：编译失败。

- [ ] **Step 3: 写实现**

`src/SelectionTranslator.Core/Settings/AppSettings.cs`:

```csharp
namespace SelectionTranslator.Core.Settings;

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Alt+D";
    public TriggerSettings Trigger { get; set; } = new();
    public TranslateSettings Translate { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public ExcludeSettings Exclude { get; set; } = new();
}

public sealed class TriggerSettings
{
    public bool ShowIcon { get; set; } = true;
    public bool AutoTranslate { get; set; }
    public bool AutoStart { get; set; } = true;
}

public sealed class TranslateSettings
{
    public string Provider { get; set; } = "glm";
    public string Model { get; set; } = "glm-4-flash";
    public string BaseUrl { get; set; } = "https://open.bigmodel.cn/api/paas/v4";
    public string ApiKeyProtected { get; set; } = string.Empty;
    public string TargetLang { get; set; } = "简体中文";
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class UiSettings
{
    public string Theme { get; set; } = "system";
    public double FontSize { get; set; } = 14;
    public double PopupWidth { get; set; } = 420;
}

public sealed class ExcludeSettings
{
    public List<string> Processes { get; set; } = new();
}
```

`src/SelectionTranslator.Core/Settings/SettingsStore.cs`:

```csharp
using System.Text.Json;

namespace SelectionTranslator.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(_path))
        {
            Current = new AppSettings();
            Save();
            return;
        }

        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options)
                      ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
```

`src/SelectionTranslator.Core/Settings/DpapiSecretProtector.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace SelectionTranslator.Core.Settings;

public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SelectionTranslator.v1");

    public string Protect(string plainText) =>
        Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedText) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser));
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~SettingsStoreTests
```

预期：4 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/SelectionTranslator.Core/Settings src/SelectionTranslator.Tests/Selection/SettingsStoreTests.cs
git commit -m "feat: add settings store and DPAPI secret protection"
```

---

## Task 6: 快捷键解析

**Files:**
- Create: `src/SelectionTranslator.Core/Interop/HotkeyParser.cs`
- Test: `src/SelectionTranslator.Tests/Selection/HotkeyParserTests.cs`

**Interfaces:**
- Produces: `readonly record struct Hotkey(uint Modifiers, uint VirtualKey)`
- Produces: `HotkeyParser.TryParse(string?, out Hotkey) : bool`
- Produces: 常量 `HotkeyParser.ModAlt/ModControl/ModShift/ModWin`

- [ ] **Step 1: 写失败的测试**

`src/SelectionTranslator.Tests/Selection/HotkeyParserTests.cs`:

```csharp
using SelectionTranslator.Core.Interop;
using Xunit;

namespace SelectionTranslator.Tests.Selection;

public class HotkeyParserTests
{
    [Fact]
    public void Parses_alt_d()
    {
        Assert.True(HotkeyParser.TryParse("Alt+D", out var hotkey));
        Assert.Equal(HotkeyParser.ModAlt, hotkey.Modifiers);
        Assert.Equal((uint)'D', hotkey.VirtualKey);
    }

    [Fact]
    public void Parses_multiple_modifiers_and_function_key()
    {
        Assert.True(HotkeyParser.TryParse("ctrl + shift + F5", out var hotkey));
        Assert.Equal(HotkeyParser.ModControl | HotkeyParser.ModShift, hotkey.Modifiers);
        Assert.Equal(0x74u, hotkey.VirtualKey);
    }

    [Fact]
    public void Rejects_missing_modifier()
    {
        Assert.False(HotkeyParser.TryParse("D", out _));
    }

    [Fact]
    public void Rejects_unknown_key()
    {
        Assert.False(HotkeyParser.TryParse("Alt+Banana", out _));
    }

    [Fact]
    public void Rejects_garbage()
    {
        Assert.False(HotkeyParser.TryParse(null, out _));
        Assert.False(HotkeyParser.TryParse("", out _));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~HotkeyParserTests
```

预期：编译失败。

- [ ] **Step 3: 写实现**

`src/SelectionTranslator.Core/Interop/HotkeyParser.cs`:

```csharp
namespace SelectionTranslator.Core.Interop;

public readonly record struct Hotkey(uint Modifiers, uint VirtualKey);

public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        uint modifiers = 0;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "alt": modifiers |= ModAlt; break;
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "shift": modifiers |= ModShift; break;
                case "win" or "meta": modifiers |= ModWin; break;
                default: return false;
            }
        }

        var key = parts[^1].ToUpperInvariant();
        uint virtualKey;

        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
            virtualKey = key[0];
        else if (key.Length == 1 && key[0] is >= '0' and <= '9')
            virtualKey = key[0];
        else if (key.StartsWith('F') && int.TryParse(key[1..], out var function) && function is >= 1 and <= 24)
            virtualKey = (uint)(0x70 + function - 1);
        else if (key == "SPACE")
            virtualKey = 0x20;
        else
            return false;

        if (modifiers == 0)
            return false;

        hotkey = new Hotkey(modifiers, virtualKey);
        return true;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~HotkeyParserTests
```

预期：5 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/SelectionTranslator.Core/Interop/HotkeyParser.cs src/SelectionTranslator.Tests/Selection/HotkeyParserTests.cs
git commit -m "feat: parse configurable global hotkeys"
```

---

## Task 7: 拖选判定

**Files:**
- Create: `src/SelectionTranslator.Core/Selection/DragTracker.cs`
- Test: `src/SelectionTranslator.Tests/Selection/DragTrackerTests.cs`

**Interfaces:**
- Produces: `DragTracker(int minDistance = 4, TimeSpan? minDuration = null)`
- Produces: `OnDown(int x, int y, long timestampMs)`、`OnUp(int x, int y, long timestampMs) : bool`

- [ ] **Step 1: 写失败的测试**

`src/SelectionTranslator.Tests/Selection/DragTrackerTests.cs`:

```csharp
using SelectionTranslator.Core.Selection;
using Xunit;

namespace SelectionTranslator.Tests.Selection;

public class DragTrackerTests
{
    [Fact]
    public void Click_is_not_drag()
    {
        var tracker = new DragTracker();
        tracker.OnDown(100, 100, 0);
        Assert.False(tracker.OnUp(101, 101, 200));
    }

    [Fact]
    public void Move_beyond_distance_is_drag()
    {
        var tracker = new DragTracker();
        tracker.OnDown(100, 100, 0);
        Assert.True(tracker.OnUp(120, 130, 200));
    }

    [Fact]
    public void Too_short_duration_is_not_drag()
    {
        var tracker = new DragTracker(minDuration: TimeSpan.FromMilliseconds(50));
        tracker.OnDown(100, 100, 0);
        Assert.False(tracker.OnUp(200, 200, 10));
    }

    [Fact]
    public void Up_without_down_is_ignored()
    {
        var tracker = new DragTracker();
        Assert.False(tracker.OnUp(200, 200, 100));
    }

    [Fact]
    public void Second_up_after_drag_is_ignored()
    {
        var tracker = new DragTracker();
        tracker.OnDown(0, 0, 0);
        Assert.True(tracker.OnUp(50, 50, 200));
        Assert.False(tracker.OnUp(60, 60, 300));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~DragTrackerTests
```

预期：编译失败。

- [ ] **Step 3: 写实现**

`src/SelectionTranslator.Core/Selection/DragTracker.cs`:

```csharp
namespace SelectionTranslator.Core.Selection;

public sealed class DragTracker
{
    private readonly int _minDistance;
    private readonly TimeSpan _minDuration;
    private (int X, int Y, long Ms)? _down;

    public DragTracker(int minDistance = 4, TimeSpan? minDuration = null)
    {
        _minDistance = minDistance;
        _minDuration = minDuration ?? TimeSpan.FromMilliseconds(50);
    }

    public void OnDown(int x, int y, long timestampMs) => _down = (x, y, timestampMs);

    public bool OnUp(int x, int y, long timestampMs)
    {
        if (_down is not { } down)
            return false;

        _down = null;

        var dx = x - down.X;
        var dy = y - down.Y;
        var farEnough = dx * dx + dy * dy >= _minDistance * _minDistance;
        var longEnough = timestampMs - down.Ms >= _minDuration.TotalMilliseconds;

        return farEnough && longEnough;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~DragTrackerTests
```

预期：5 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/SelectionTranslator.Core/Selection/DragTracker.cs src/SelectionTranslator.Tests/Selection/DragTrackerTests.cs
git commit -m "feat: detect drag selections from mouse events"
```

---

## Task 8: 剪贴板服务与剪贴板取词

**Files:**
- Create: `src/SelectionTranslator.Core/Interop/NativeMethods.cs`（本任务先放 `GetClipboardSequenceNumber` 与 `GetCursorPos`）
- Create: `src/SelectionTranslator.Core/Interop/StaRunner.cs`
- Create: `src/SelectionTranslator.Core/Interop/ClipboardService.cs`
- Create: `src/SelectionTranslator.Core/Interop/InputSimulator.cs`
- Create: `src/SelectionTranslator.Core/Selection/ITextAcquirer.cs`
- Create: `src/SelectionTranslator.Core/Selection/ClipboardTextAcquirer.cs`
- Test: `src/SelectionTranslator.Tests/Selection/ClipboardTextAcquirerTests.cs`

**Interfaces:**
- Produces: `IClipboardService { uint GetSequenceNumber(); bool TryGetText(out string); object? Snapshot(); void Restore(object?); bool TrySetText(string); }`
- Produces: `IInputSimulator { void SendCopy(); }`
- Produces: `ITextAcquirer { string Name; Task<string?> TryAcquireAsync(CancellationToken); }`
- Produces: `ClipboardTextAcquirer(IClipboardService, IInputSimulator, TimeSpan? pollInterval = null, TimeSpan? timeout = null)`

- [ ] **Step 1: 写失败的测试**

`src/SelectionTranslator.Tests/Selection/ClipboardTextAcquirerTests.cs`:

```csharp
using SelectionTranslator.Core.Interop;
using SelectionTranslator.Core.Selection;
using Xunit;

namespace SelectionTranslator.Tests.Selection;

public class ClipboardTextAcquirerTests
{
    private sealed class FakeClipboard : IClipboardService
    {
        public uint Sequence { get; set; } = 1;
        public string Text { get; set; } = string.Empty;
        public object? SnapshotValue { get; set; } = "previous";
        public bool Restored { get; private set; }
        public bool CopyHappens { get; set; } = true;
        public IInputSimulator? Input { get; set; }

        public uint GetSequenceNumber() => Sequence;
        public bool TryGetText(out string text) { text = Text; return !string.IsNullOrEmpty(Text); }
        public object? Snapshot() => SnapshotValue;
        public void Restore(object? snapshot) => Restored = true;
        public bool TrySetText(string text) { Text = text; return true; }
    }

    private sealed class FakeInput : IInputSimulator
    {
        private readonly FakeClipboard _clipboard;
        public FakeInput(FakeClipboard clipboard) => _clipboard = clipboard;
        public void SendCopy()
        {
            if (!_clipboard.CopyHappens) return;
            _clipboard.Sequence++;
            _clipboard.Text = "selected text";
        }
    }

    [Fact]
    public async Task Returns_text_and_restores_clipboard()
    {
        var clipboard = new FakeClipboard { Text = "original" };
        var acquirer = new ClipboardTextAcquirer(
            clipboard,
            new FakeInput(clipboard),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(50));

        var text = await acquirer.TryAcquireAsync(CancellationToken.None);

        Assert.Equal("selected text", text);
        Assert.True(clipboard.Restored);
    }

    [Fact]
    public async Task Returns_null_when_nothing_is_copied()
    {
        var clipboard = new FakeClipboard { Text = "original", CopyHappens = false };
        var acquirer = new ClipboardTextAcquirer(
            clipboard,
            new FakeInput(clipboard),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(20));

        Assert.Null(await acquirer.TryAcquireAsync(CancellationToken.None));
        Assert.True(clipboard.Restored);
    }

    [Fact]
    public async Task Returns_null_when_copied_text_is_empty()
    {
        var fake = new SequenceOnlyClipboard();
        var acquirer = new ClipboardTextAcquirer(
            fake,
            new FakeInput2(fake),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(20));

        Assert.Null(await acquirer.TryAcquireAsync(CancellationToken.None));
    }

    private sealed class SequenceOnlyClipboard : IClipboardService
    {
        public uint GetSequenceNumber() => 2;
        public bool TryGetText(out string text) { text = ""; return false; }
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public bool TrySetText(string text) => true;
    }

    private sealed class FakeInput2 : IInputSimulator
    {
        private readonly SequenceOnlyClipboard _clipboard;
        public FakeInput2(SequenceOnlyClipboard clipboard) => _clipboard = clipboard;
        public void SendCopy() { }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~ClipboardTextAcquirerTests
```

预期：编译失败。

- [ ] **Step 3: 写接口与纯逻辑**

`src/SelectionTranslator.Core/Selection/ITextAcquirer.cs`:

```csharp
namespace SelectionTranslator.Core.Selection;

public interface ITextAcquirer
{
    string Name { get; }
    Task<string?> TryAcquireAsync(CancellationToken cancellationToken);
}
```

`src/SelectionTranslator.Core/Interop/NativeMethods.cs`（后续任务继续追加）：

```csharp
using System.Runtime.InteropServices;

namespace SelectionTranslator.Core.Interop;

public static partial class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
}
```

`src/SelectionTranslator.Core/Interop/StaRunner.cs`:

```csharp
namespace SelectionTranslator.Core.Interop;

public static class StaRunner
{
    public static Task<T> RunAsync<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(action()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static T Run<T>(Func<T> action) => RunAsync(action).GetAwaiter().GetResult();
}
```

`src/SelectionTranslator.Core/Interop/ClipboardService.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;

namespace SelectionTranslator.Core.Interop;

public interface IClipboardService
{
    uint GetSequenceNumber();
    bool TryGetText(out string text);
    object? Snapshot();
    void Restore(object? snapshot);
    bool TrySetText(string text);
}

public sealed class ClipboardService : IClipboardService
{
    private const int Retries = 5;
    private const int RetryDelayMs = 20;

    public uint GetSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();

    public bool TryGetText(out string text)
    {
        var result = StaRunner.Run(() =>
        {
            for (var i = 0; i < Retries; i++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                        return (true, Clipboard.GetText());
                    return (false, string.Empty);
                }
                catch (COMException)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
            return (false, string.Empty);
        });

        text = result.Item2;
        return result.Item1;
    }

    public object? Snapshot() => StaRunner.Run<object?>(() =>
    {
        for (var i = 0; i < Retries; i++)
        {
            try { return Clipboard.GetDataObject(); }
            catch (COMException) { Thread.Sleep(RetryDelayMs); }
        }
        return null;
    });

    public void Restore(object? snapshot)
    {
        if (snapshot is null)
            return;

        StaRunner.Run(() =>
        {
            for (var i = 0; i < Retries; i++)
            {
                try
                {
                    Clipboard.SetDataObject(snapshot, true);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
            return false;
        });
    }

    public bool TrySetText(string text) => StaRunner.Run(() =>
    {
        for (var i = 0; i < Retries; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }
        return false;
    });
}
```

`src/SelectionTranslator.Core/Interop/InputSimulator.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SelectionTranslator.Core.Interop;

public interface IInputSimulator
{
    void SendCopy();
}

public sealed class InputSimulator : IInputSimulator
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkShift = 0x10;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;
    private const ushort VkC = 0x43;

    public void SendCopy()
    {
        var inputs = new[]
        {
            Key(VkControl, up: true),
            Key(VkShift, up: true),
            Key(VkMenu, up: true),
            Key(VkLWin, up: true),
            Key(VkRWin, up: true),
            Key(VkControl),
            Key(VkC),
            Key(VkC, up: true),
            Key(VkControl, up: true)
        };

        if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "模拟复制失败，目标程序可能以管理员权限运行");
    }

    private static INPUT Key(ushort virtualKey, bool up = false) => new()
    {
        type = InputKeyboard,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = (ushort)(NativeMethods.MapVirtualKey(virtualKey, 0) & 0xFF),
                dwFlags = up ? KeyEventKeyUp : 0
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
```

同时给 `NativeMethods` 追加：

```csharp
[DllImport("user32.dll", SetLastError = true)]
public static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInput);

[DllImport("user32.dll")]
public static extern uint MapVirtualKey(uint code, uint mapType);

[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}
```

注意：`InputSimulator` 里的私有结构体改为使用 `NativeMethods.INPUT` / `NativeMethods.InputUnion` / `NativeMethods.KEYBDINPUT`，避免重复定义：

```csharp
private static NativeMethods.INPUT Key(ushort virtualKey, bool up = false) => new()
{
    type = InputKeyboard,
    U = new NativeMethods.InputUnion
    {
        ki = new NativeMethods.KEYBDINPUT
        {
            wVk = virtualKey,
            wScan = (ushort)(NativeMethods.MapVirtualKey(virtualKey, 0) & 0xFF),
            dwFlags = up ? KeyEventKeyUp : 0
        }
    }
};
```

并删除 `InputSimulator` 中重复的三个结构体定义。

`src/SelectionTranslator.Core/Selection/ClipboardTextAcquirer.cs`:

```csharp
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Core.Selection;

public sealed class ClipboardTextAcquirer : ITextAcquirer
{
    private readonly IClipboardService _clipboard;
    private readonly IInputSimulator _input;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ClipboardTextAcquirer(
        IClipboardService clipboard,
        IInputSimulator input,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null)
    {
        _clipboard = clipboard;
        _input = input;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(10);
        _timeout = timeout ?? TimeSpan.FromMilliseconds(500);
    }

    public string Name => "clipboard";

    public async Task<string?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return null;

        var snapshot = _clipboard.Snapshot();

        try
        {
            var sequence = _clipboard.GetSequenceNumber();
            _input.SendCopy();

            var attempts = Math.Max(1, (int)Math.Ceiling(_timeout.TotalMilliseconds / _pollInterval.TotalMilliseconds));

            for (var i = 0; i < attempts; i++)
            {
                await Task.Delay(_pollInterval, cancellationToken);

                if (_clipboard.GetSequenceNumber() == sequence)
                    continue;

                if (_clipboard.TryGetText(out var text) && !string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }
        finally
        {
            _clipboard.Restore(snapshot);
            _gate.Release();
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~ClipboardTextAcquirerTests
```

预期：3 个测试 PASS。

- [ ] **Step 5: 手动验证真实剪贴板**

运行 `dotnet build SelectionTranslator.sln` 确认编译通过。真实剪贴板行为留到 Task 13 集成后手动验证（打开记事本选中文字，按热键，检查剪贴板是否恢复）。

- [ ] **Step 6: 提交**

```bash
git add src/SelectionTranslator.Core/Interop src/SelectionTranslator.Core/Selection/ITextAcquirer.cs src/SelectionTranslator.Core/Selection/ClipboardTextAcquirer.cs src/SelectionTranslator.Tests/Selection/ClipboardTextAcquirerTests.cs
git commit -m "feat: acquire selected text through clipboard with restore"
```

---

## Task 9: 取词流水线与进程策略

**Files:**
- Create: `src/SelectionTranslator.Core/Selection/SelectionContext.cs`
- Create: `src/SelectionTranslator.Core/Selection/AcquisitionPolicy.cs`
- Create: `src/SelectionTranslator.Core/Selection/SelectionPipeline.cs`
- Test: `src/SelectionTranslator.Tests/Selection/SelectionPipelineTests.cs`

**Interfaces:**
- Produces: `SelectionContext(int X, int Y, string? ProcessName, nint Hwnd)`
- Produces: `SelectionResult(string Text, string AcquirerName)`
- Produces: `IAcquisitionPolicy { bool IsAllowed(string acquirerName, SelectionContext context); }`
- Produces: `DefaultAcquisitionPolicy(IReadOnlyCollection<string> excludedProcesses)`
- Produces: `SelectionPipeline(IReadOnlyList<ITextAcquirer>, IAcquisitionPolicy, TimeProvider? timeProvider = null)`
- Produces: `SelectionPipeline.AcquireAsync(SelectionContext, CancellationToken) : Task<SelectionResult?>`

- [ ] **Step 1: 写失败的测试**

`src/SelectionTranslator.Tests/Selection/SelectionPipelineTests.cs`:

```csharp
using SelectionTranslator.Core.Selection;
using Xunit;

namespace SelectionTranslator.Tests.Selection;

public class SelectionPipelineTests
{
    private sealed class FakeAcquirer : ITextAcquirer
    {
        private readonly string? _text;
        public FakeAcquirer(string name, string? text) { Name = name; _text = text; }
        public string Name { get; }
        public bool Called { get; private set; }
        public Task<string?> TryAcquireAsync(CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(_text);
        }
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Returns_first_valid_result()
    {
        var uia = new FakeAcquirer("uia", "hello");
        var clipboard = new FakeAcquirer("clipboard", "world");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { uia, clipboard },
            new DefaultAcquisitionPolicy(Array.Empty<string>()));

        var result = await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("hello", result!.Text);
        Assert.False(clipboard.Called);
    }

    [Fact]
    public async Task Falls_back_when_first_returns_invalid_text()
    {
        var uia = new FakeAcquirer("uia", "   ");
        var clipboard = new FakeAcquirer("clipboard", "world");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { uia, clipboard },
            new DefaultAcquisitionPolicy(Array.Empty<string>()));

        var result = await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None);

        Assert.Equal("world", result!.Text);
        Assert.Equal("clipboard", result.AcquirerName);
    }

    [Fact]
    public async Task Skips_clipboard_in_terminal()
    {
        var uia = new FakeAcquirer("uia", null);
        var clipboard = new FakeAcquirer("clipboard", "danger");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { uia, clipboard },
            new DefaultAcquisitionPolicy(Array.Empty<string>()));

        var result = await pipeline.AcquireAsync(new SelectionContext(0, 0, "WindowsTerminal", 0), CancellationToken.None);

        Assert.Null(result);
        Assert.False(clipboard.Called);
    }

    [Fact]
    public async Task Skips_excluded_process()
    {
        var uia = new FakeAcquirer("uia", "secret");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { uia },
            new DefaultAcquisitionPolicy(new[] { "KeePass" }));

        var result = await pipeline.AcquireAsync(new SelectionContext(0, 0, "KeePass", 0), CancellationToken.None);

        Assert.Null(result);
        Assert.False(uia.Called);
    }

    [Fact]
    public async Task Suppresses_identical_text_within_800ms()
    {
        var time = new FakeTime();
        var acquirer = new FakeAcquirer("uia", "hello");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { acquirer },
            new DefaultAcquisitionPolicy(Array.Empty<string>()),
            time);

        Assert.NotNull(await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None));
        Assert.Null(await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None));

        time.Now = time.Now.AddSeconds(1);
        Assert.NotNull(await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None));
    }

    [Fact]
    public async Task Returns_null_when_all_acquirers_fail()
    {
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { new FakeAcquirer("uia", null), new FakeAcquirer("clipboard", null) },
            new DefaultAcquisitionPolicy(Array.Empty<string>()));

        Assert.Null(await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~SelectionPipelineTests
```

预期：编译失败。

- [ ] **Step 3: 写实现**

`src/SelectionTranslator.Core/Selection/SelectionContext.cs`:

```csharp
namespace SelectionTranslator.Core.Selection;

public sealed record SelectionContext(int X, int Y, string? ProcessName, nint Hwnd);

public sealed record SelectionResult(string Text, string AcquirerName);
```

`src/SelectionTranslator.Core/Selection/AcquisitionPolicy.cs`:

```csharp
namespace SelectionTranslator.Core.Selection;

public interface IAcquisitionPolicy
{
    bool IsAllowed(string acquirerName, SelectionContext context);
}

public sealed class DefaultAcquisitionPolicy : IAcquisitionPolicy
{
    private static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "wt", "conhost", "cmd", "powershell", "pwsh"
    };

    private readonly Func<IReadOnlyCollection<string>> _excludedProvider;

    public DefaultAcquisitionPolicy(IReadOnlyCollection<string> excludedProcesses)
        : this(() => excludedProcesses)
    {
    }

    public DefaultAcquisitionPolicy(Func<IReadOnlyCollection<string>> excludedProcesses)
    {
        _excludedProvider = excludedProcesses;
    }

    public bool IsAllowed(string acquirerName, SelectionContext context)
    {
        var process = context.ProcessName;
        if (string.IsNullOrEmpty(process))
            return true;

        if (_excludedProvider().Any(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (acquirerName == "clipboard" && TerminalProcesses.Contains(process))
            return false;

        return true;
    }
}
```

`src/SelectionTranslator.Core/Selection/SelectionPipeline.cs`:

```csharp
namespace SelectionTranslator.Core.Selection;

public sealed class SelectionPipeline
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(800);

    private readonly IReadOnlyList<ITextAcquirer> _acquirers;
    private readonly IAcquisitionPolicy _policy;
    private readonly TimeProvider _time;
    private string? _lastText;
    private DateTimeOffset _lastAt;

    public SelectionPipeline(
        IReadOnlyList<ITextAcquirer> acquirers,
        IAcquisitionPolicy policy,
        TimeProvider? timeProvider = null)
    {
        _acquirers = acquirers;
        _policy = policy;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<SelectionResult?> AcquireAsync(
        SelectionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var acquirer in _acquirers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_policy.IsAllowed(acquirer.Name, context))
                continue;

            string? text;

            try
            {
                text = await acquirer.TryAcquireAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            if (!TextValidator.TryNormalize(text, out var normalized, out _))
                continue;

            var now = _time.GetUtcNow();

            if (normalized == _lastText && now - _lastAt < DuplicateWindow)
                continue;

            _lastText = normalized;
            _lastAt = now;
            return new SelectionResult(normalized, acquirer.Name);
        }

        return null;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~SelectionPipelineTests
```

预期：6 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/SelectionTranslator.Core/Selection/SelectionContext.cs src/SelectionTranslator.Core/Selection/AcquisitionPolicy.cs src/SelectionTranslator.Core/Selection/SelectionPipeline.cs src/SelectionTranslator.Tests/Selection/SelectionPipelineTests.cs
git commit -m "feat: orchestrate text acquisition with fallback and policy"
```

---

## Task 10: UIA 取词

**Files:**
- Create: `src/SelectionTranslator.Core/Selection/UiaTextAcquirer.cs`
- Modify: `src/SelectionTranslator.Core/Interop/NativeMethods.cs`（追加 `GetCursorPos` 已存在，无需改动）

**Interfaces:**
- Consumes: `ITextAcquirer`
- Produces: `UiaTextAcquirer : ITextAcquirer`，`Name == "uia"`，内部 250ms 超时

- [ ] **Step 1: 写实现（本任务无可自动化测试，UIA 依赖真实桌面）**

`src/SelectionTranslator.Core/Selection/UiaTextAcquirer.cs`:

```csharp
using System.Text;
using System.Windows.Automation;
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Core.Selection;

public sealed class UiaTextAcquirer : ITextAcquirer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    public string Name => "uia";

    public async Task<string?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var work = StaRunner.RunAsync(ReadSelection);

        try
        {
            return await work.WaitAsync(Timeout, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or ElementNotAvailableException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadSelection()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            var fromFocus = TryReadFromElement(focused);
            if (fromFocus is not null)
                return fromFocus;

            NativeMethods.GetCursorPos(out var point);
            var atPoint = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            return TryReadFromElement(atPoint);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryReadFromElement(AutomationElement? element)
    {
        var current = element;

        for (var depth = 0; current is not null && depth < 6; depth++)
        {
            try
            {
                if (current.Current.IsPassword)
                    return null;

                if (current.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject))
                {
                    var ranges = ((TextPattern)patternObject).GetSelection();
                    if (ranges.Length > 0)
                    {
                        var builder = new StringBuilder();
                        foreach (var range in ranges)
                            builder.Append(range.GetText(-1));

                        var text = builder.ToString().Trim();
                        if (text.Length > 0)
                            return text;
                    }
                }

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }
}
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build SelectionTranslator.sln
```

预期：编译成功，无警告升级为错误。

- [ ] **Step 3: 手动验证 UIA（临时控制台工程）**

在 `src/SelectionTranslator.Tests` 中临时加入一个跳过的手动测试无法覆盖真实桌面。改为在 Task 13 完成集成后，按以下清单验证：

1. 打开 Chrome，选中一段英文，按热键，检查译文卡片出现且剪贴板未被改动。
2. 打开记事本重复上述操作。
3. 打开 Word 重复上述操作。
4. 在 Chrome 密码框附近选中文本，确认不弹窗。

- [ ] **Step 4: 提交**

```bash
git add src/SelectionTranslator.Core/Selection/UiaTextAcquirer.cs
git commit -m "feat: read selection through UI Automation with timeout"
```

---

## Task 11: OpenAI 兼容 Provider

**Files:**
- Create: `src/SelectionTranslator.Core/Translation/OpenAiCompatibleProvider.cs`
- Create: `src/SelectionTranslator.Core/Translation/ProviderFactory.cs`
- Test: `src/SelectionTranslator.Tests/Translation/OpenAiProviderTests.cs`

**Interfaces:**
- Consumes: `ITranslationProvider`、`PromptBuilder`、`OpenAiStream`
- Produces: `ProviderOptions(string BaseUrl, string Model, string? ApiKey, TimeSpan? Timeout = null)`
- Produces: `OpenAiCompatibleProvider(string id, string displayName, HttpClient http, ProviderOptions options)`
- Produces: `ProviderFactory.Create(TranslateSettings, ISecretProtector, HttpClient) : ITranslationProvider`

- [ ] **Step 1: 写失败的测试**

`src/SelectionTranslator.Tests/Translation/OpenAiProviderTests.cs`:

```csharp
using System.Net;
using System.Text;
using SelectionTranslator.Core.Translation;
using Xunit;

namespace SelectionTranslator.Tests.Translation;

public class OpenAiProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public SyncProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }

    private static OpenAiCompatibleProvider CreateProvider(HttpStatusCode status, string body) =>
        new("test", "Test", new HttpClient(new StubHandler(status, body)),
            new ProviderOptions("http://localhost/v1", "test-model", "key"));

    [Fact]
    public async Task Streams_deltas_and_reports_final()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var provider = CreateProvider(HttpStatusCode.OK, sse);
        var chunks = new List<TranslationChunk>();

        await provider.TranslateAsync(
            new TranslationRequest("hello", null, "简体中文"),
            new SyncProgress<TranslationChunk>(chunks.Add),
            CancellationToken.None);

        Assert.Equal("你好", string.Concat(chunks.Where(c => !c.IsFinal).Select(c => c.Delta)));
        Assert.Contains(chunks, c => c.IsFinal);
    }

    [Fact]
    public async Task Throws_on_401()
    {
        var provider = CreateProvider(HttpStatusCode.Unauthorized, "{\"error\":\"bad key\"}");

        var ex = await Assert.ThrowsAsync<TranslationException>(() =>
            provider.TranslateAsync(
                new TranslationRequest("hello", null, "简体中文"),
                new SyncProgress<TranslationChunk>(_ => { }),
                CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Throws_when_result_is_empty()
    {
        var provider = CreateProvider(HttpStatusCode.OK, "data: [DONE]\n\n");

        await Assert.ThrowsAsync<TranslationException>(() =>
            provider.TranslateAsync(
                new TranslationRequest("hello", null, "简体中文"),
                new SyncProgress<TranslationChunk>(_ => { }),
                CancellationToken.None));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~OpenAiProviderTests
```

预期：编译失败。

- [ ] **Step 3: 写实现**

`src/SelectionTranslator.Core/Translation/OpenAiCompatibleProvider.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SelectionTranslator.Core.Translation;

public sealed record ProviderOptions(
    string BaseUrl,
    string Model,
    string? ApiKey,
    TimeSpan? Timeout = null);

public sealed class OpenAiCompatibleProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private readonly ProviderOptions _options;

    public OpenAiCompatibleProvider(
        string id,
        string displayName,
        HttpClient http,
        ProviderOptions options)
    {
        Id = id;
        DisplayName = displayName;
        _http = http;
        _options = options;
    }

    public string Id { get; }
    public string DisplayName { get; }

    public async Task TranslateAsync(
        TranslationRequest request,
        IProgress<TranslationChunk> onChunk,
        CancellationToken cancellationToken)
    {
        var messages = PromptBuilder.Build(request);

        var payload = new
        {
            model = _options.Model,
            stream = true,
            temperature = 0.2,
            messages = messages.Select(m => new { role = m.Role, content = m.Content })
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl());
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        if (!string.IsNullOrEmpty(_options.ApiKey))
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout ?? TimeSpan.FromSeconds(60));

        using var response = await _http.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            throw new TranslationException(
                MapError(response.StatusCode, body),
                (int)response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);

        var builder = new StringBuilder();

        await foreach (var delta in OpenAiStream.ReadDeltasAsync(stream, timeout.Token))
        {
            builder.Append(delta);
            onChunk.Report(new TranslationChunk(delta, false));
        }

        if (builder.Length == 0)
            throw new TranslationException("翻译结果为空");

        onChunk.Report(new TranslationChunk(string.Empty, true));
    }

    private string BuildUrl()
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl + "/chat/completions";
    }

    private static string MapError(HttpStatusCode status, string body)
    {
        var detail = body.Length > 300 ? body[..300] : body;
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"API Key 无效或无权限（{(int)status}）",
            HttpStatusCode.TooManyRequests =>
                "请求过于频繁，请稍后再试（429）",
            _ => $"翻译服务返回 {(int)status}：{detail}"
        };
    }
}
```

`src/SelectionTranslator.Core/Translation/ProviderFactory.cs`:

```csharp
using SelectionTranslator.Core.Settings;

namespace SelectionTranslator.Core.Translation;

public static class ProviderFactory
{
    public static ITranslationProvider Create(
        TranslateSettings settings,
        ISecretProtector protector,
        HttpClient http)
    {
        var apiKey = string.IsNullOrEmpty(settings.ApiKeyProtected)
            ? null
            : protector.Unprotect(settings.ApiKeyProtected);

        var options = new ProviderOptions(
            settings.BaseUrl,
            settings.Model,
            apiKey,
            TimeSpan.FromSeconds(settings.TimeoutSeconds));

        var displayName = settings.Provider switch
        {
            "glm" => "智谱 GLM",
            "ollama" => "Ollama",
            _ => settings.Provider
        };

        return new OpenAiCompatibleProvider(settings.Provider, displayName, http, options);
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj --filter FullyQualifiedName~OpenAiProviderTests
```

预期：3 个测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add src/SelectionTranslator.Core/Translation/OpenAiCompatibleProvider.cs src/SelectionTranslator.Core/Translation/ProviderFactory.cs src/SelectionTranslator.Tests/Translation/OpenAiProviderTests.cs
git commit -m "feat: add streaming OpenAI-compatible translation provider"
```

---

## Task 12: WPF 宿主骨架（单实例、托盘、日志、DI）

**Files:**
- Create: `src/SelectionTranslator.App/AppPaths.cs`
- Create: `src/SelectionTranslator.Core/Diagnostics/AppLog.cs`
- Create: `src/SelectionTranslator.App/Services/TrayIconService.cs`
- Create: `src/SelectionTranslator.App/app.manifest`
- Modify: `src/SelectionTranslator.App/App.xaml`
- Modify: `src/SelectionTranslator.App/App.xaml.cs`

**Interfaces:**
- Produces: `AppPaths { string Root; string LogDirectory; }`
- Produces: `AppLog.Init(string dir)`、`Info/Warn/Error`
- Produces: `TrayIconService.Show()`、`Notify(string)`、事件 `SettingsRequested`/`ExitRequested`

- [ ] **Step 1: 写实现**

`src/SelectionTranslator.App/AppPaths.cs`:

```csharp
using System.IO;

namespace SelectionTranslator.App;

public sealed class AppPaths
{
    public AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SelectionTranslator");
        LogDirectory = Path.Combine(Root, "logs");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
    }

    public string Root { get; }
    public string LogDirectory { get; }

    public string ConfigFile => Path.Combine(Root, "config.json");
    public string CacheFile => Path.Combine(Root, "cache.json");
}
```

`src/SelectionTranslator.Core/Diagnostics/AppLog.cs`:

```csharp
namespace SelectionTranslator.Core.Diagnostics;

public static class AppLog
{
    private static readonly object Sync = new();
    private static string? _directory;

    public static void Init(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string message)
    {
        if (_directory is null)
            return;

        lock (Sync)
        {
            try
            {
                var path = Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
        }
    }
}
```

`src/SelectionTranslator.App/Services/TrayIconService.cs`:

```csharp
using System.Drawing;
using System.Windows.Forms;

namespace SelectionTranslator.App.Services;

public sealed class TrayIconService : IDisposable
{
    private NotifyIcon? _icon;

    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void Show()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("设置", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "划译",
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Notify(string message)
    {
        _icon?.ShowBalloonTip(3000, "划译", message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_icon is null)
            return;

        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}
```

`src/SelectionTranslator.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="SelectionTranslator.App"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
    </application>
  </compatibility>
</assembly>
```

`src/SelectionTranslator.App/App.xaml`:

```xml
<Application x:Class="SelectionTranslator.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
</Application>
```

`src/SelectionTranslator.App/App.xaml.cs`:

```csharp
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SelectionTranslator.App.Services;
using SelectionTranslator.Core.Diagnostics;
using SelectionTranslator.Core.Interop;
using SelectionTranslator.Core.Selection;
using SelectionTranslator.Core.Settings;
using SelectionTranslator.Core.Translation;

namespace SelectionTranslator.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private ServiceProvider? _services;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\SelectionTranslator.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var paths = new AppPaths();
        AppLog.Init(paths.LogDirectory);

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IInputSimulator, InputSimulator>();
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton(_ => new SettingsStore(paths.ConfigFile));
        services.AddSingleton(_ => new TranslationCache(paths.CacheFile));
        services.AddSingleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<SelectionPipeline>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsStore>().Current;
            var acquirers = new ITextAcquirer[]
            {
                new UiaTextAcquirer(),
                new ClipboardTextAcquirer(
                    sp.GetRequiredService<IClipboardService>(),
                    sp.GetRequiredService<IInputSimulator>())
            };
            return new SelectionPipeline(
                acquirers,
                new DefaultAcquisitionPolicy(settings.Exclude.Processes));
        });
        services.AddSingleton<TrayIconService>();
        _services = services.BuildServiceProvider();

        var settingsStore = _services.GetRequiredService<SettingsStore>();
        settingsStore.Load();
        _services.GetRequiredService<TranslationCache>().Load();

        _tray = _services.GetRequiredService<TrayIconService>();
        _tray.Show();
        _tray.ExitRequested += (_, _) => Shutdown();

        AppLog.Info("started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 2: 编译并运行**

```bash
dotnet build SelectionTranslator.sln
dotnet run --project src/SelectionTranslator.App
```

预期：任务栏出现托盘图标；右键有「设置」「退出」；退出后进程结束。首次运行会在 `%APPDATA%\SelectionTranslator` 生成 `config.json`、`cache.json`、`logs/`。

- [ ] **Step 3: 提交**

```bash
git add src/SelectionTranslator.App src/SelectionTranslator.Core/Diagnostics
git commit -m "feat: add single-instance WPF host with tray and logging"
```

---

## Task 13: 全局快捷键、协调器与译文卡片

**Files:**
- Create: `src/SelectionTranslator.Core/Interop/NativeMethods.cs`（追加 `RegisterHotKey`/`UnregisterHotKey`/`GetForegroundWindow`/`GetWindowThreadProcessId`/`GetWindowText`）
- Create: `src/SelectionTranslator.Core/Hotkey/HotkeyManager.cs`
- Create: `src/SelectionTranslator.App/ViewModels/TranslationPopupViewModel.cs`
- Create: `src/SelectionTranslator.App/Views/TranslationPopupWindow.xaml(.cs)`
- Create: `src/SelectionTranslator.App/Services/TranslationCoordinator.cs`
- Modify: `src/SelectionTranslator.App/App.xaml.cs`

**Interfaces:**
- Produces: `HotkeyManager.Register(Hotkey) : bool`、`Unregister()`、事件 `Pressed`
- Produces: `TranslationCoordinator.TranslateSelectionAsync(int x, int y, string? processName)`
- Produces: `TranslationCoordinator.TranslateTextAsync(string text, int x, int y)`
- Produces: `TranslationPopupViewModel`（`OriginalText`、`Translation`、`IsStreaming`、`Status`、`Error`、`IsPinned`、命令 `Copy/Retry/Close/TogglePin`）

- [ ] **Step 1: 追加 P/Invoke**

在 `NativeMethods` 中追加：

```csharp
[DllImport("user32.dll", SetLastError = true)]
public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll", SetLastError = true)]
public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

[DllImport("user32.dll")]
public static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll")]
public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}
```

- [ ] **Step 2: 写快捷键管理器**

`src/SelectionTranslator.Core/Hotkey/HotkeyManager.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Core.Hotkey;

public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;

    private HwndSource? _source;

    public event EventHandler? Pressed;

    public bool Register(Hotkey hotkey)
    {
        Unregister();

        var parameters = new HwndSourceParameters("SelectionTranslatorHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = new IntPtr(-3)
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        return NativeMethods.RegisterHotKey(
            _source.Handle, HotkeyId, hotkey.Modifiers, hotkey.VirtualKey);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_source is null)
            return;

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    public void Dispose() => Unregister();
}
```

- [ ] **Step 3: 写 ViewModel**

`src/SelectionTranslator.App/ViewModels/TranslationPopupViewModel.cs`:

```csharp
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SelectionTranslator.App.ViewModels;

public sealed partial class TranslationPopupViewModel : ObservableObject
{
    [ObservableProperty] private string _originalText = string.Empty;
    [ObservableProperty] private string _translation = string.Empty;
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isPinned;

    public event EventHandler? RetryRequested;
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Copy()
    {
        if (string.IsNullOrEmpty(Translation))
            return;

        try { Clipboard.SetText(Translation); }
        catch (Exception) { }
    }

    [RelayCommand]
    private void Retry() => RetryRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;
}
```

- [ ] **Step 4: 写卡片窗口**

`src/SelectionTranslator.App/Views/TranslationPopupWindow.xaml`:

```xml
<Window x:Class="SelectionTranslator.App.Views.TranslationPopupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="420" SizeToContent="Height" MaxHeight="560"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" Topmost="True" ShowActivated="False"
        ResizeMode="CanResizeWithGrip">
  <Border Background="#FF1F1F1F" CornerRadius="10" Padding="14" BorderBrush="#33FFFFFF" BorderThickness="1">
    <StackPanel>
      <TextBlock Text="{Binding OriginalText}" Foreground="#B0FFFFFF" TextWrapping="Wrap"
                 MaxHeight="80" TextTrimming="CharacterEllipsis" FontSize="12"/>
      <Border Height="1" Background="#22FFFFFF" Margin="0,10"/>
      <ScrollViewer MaxHeight="330" VerticalScrollBarVisibility="Auto">
        <TextBlock Text="{Binding Translation}" Foreground="#FFF0F0F0" TextWrapping="Wrap"
                   FontSize="{Binding FontSize, RelativeSource={RelativeSource AncestorType=Window}}"/>
      </ScrollViewer>
      <TextBlock Text="{Binding Error}" Foreground="#FFFF6B6B" TextWrapping="Wrap" Margin="0,6,0,0"/>
      <DockPanel Margin="0,10,0,0" LastChildFill="False">
        <TextBlock Text="{Binding Status}" Foreground="#80FFFFFF" FontSize="11" VerticalAlignment="Center"/>
        <Button Content="复制" Command="{Binding CopyCommand}" Margin="8,0,0,0" DockPanel.Dock="Right"/>
        <Button Content="重试" Command="{Binding RetryCommand}" Margin="8,0,0,0" DockPanel.Dock="Right"/>
        <Button Content="固定" Command="{Binding TogglePinCommand}" Margin="8,0,0,0" DockPanel.Dock="Right"/>
        <Button Content="关闭" Command="{Binding CloseCommand}" Margin="8,0,0,0" DockPanel.Dock="Right"/>
      </DockPanel>
    </StackPanel>
  </Border>
</Window>
```

若不想新增转换器，可把 `Error` 行直接绑定文本并保留空占位；本计划采用最简单的做法：删除 `Visibility` 绑定，错误为空时该行高度为 0：

```xml
<TextBlock Text="{Binding Error}" Foreground="#FFFF6B6B" TextWrapping="Wrap" Margin="0,6,0,0"/>
```

`src/SelectionTranslator.App/Views/TranslationPopupWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using SelectionTranslator.App.ViewModels;

namespace SelectionTranslator.App.Views;

public partial class TranslationPopupWindow : Window
{
    public TranslationPopupWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
    }

    public void PlaceNear(int screenX, int screenY)
    {
        var work = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : 200;

        Left = Math.Min(screenX + 16, work.Right - width - 8);
        Top = Math.Min(screenY + 16, work.Bottom - height - 8);
        Left = Math.Max(Left, work.Left + 8);
        Top = Math.Max(Top, work.Top + 8);
    }

    public void ShowFor(TranslationPopupViewModel viewModel, int x, int y)
    {
        DataContext = viewModel;
        if (!IsVisible)
            Show();
        PlaceNear(x, y);
    }
}
```

- [ ] **Step 5: 写协调器**

`src/SelectionTranslator.App/Services/TranslationCoordinator.cs`:

```csharp
using System.Diagnostics;
using System.Windows;
using SelectionTranslator.App.ViewModels;
using SelectionTranslator.App.Views;
using SelectionTranslator.Core.Diagnostics;
using SelectionTranslator.Core.Interop;
using SelectionTranslator.Core.Selection;
using SelectionTranslator.Core.Settings;
using SelectionTranslator.Core.Translation;

namespace SelectionTranslator.App.Services;

public sealed class TranslationCoordinator
{
    private readonly SelectionPipeline _pipeline;
    private readonly SettingsStore _settings;
    private readonly ISecretProtector _protector;
    private readonly HttpClient _http;
    private readonly TranslationCache _cache;
    private readonly TrayIconService _toast;

    private CancellationTokenSource? _cts;
    private TranslationPopupWindow? _popup;
    private TranslationPopupViewModel? _viewModel;
    private string? _lastText;
    private int _lastX;
    private int _lastY;

    public TranslationCoordinator(
        SelectionPipeline pipeline,
        SettingsStore settings,
        ISecretProtector protector,
        HttpClient http,
        TranslationCache cache,
        TrayIconService toast)
    {
        _pipeline = pipeline;
        _settings = settings;
        _protector = protector;
        _http = http;
        _cache = cache;
        _toast = toast;
    }

    public async Task TranslateSelectionAsync(int x, int y, string? processName)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var cancellationToken = _cts.Token;

        var result = await _pipeline.AcquireAsync(
            new SelectionContext(x, y, processName, IntPtr.Zero),
            cancellationToken);

        if (result is null)
        {
            _toast.Notify("未取到选中文字");
            return;
        }

        await TranslateTextAsync(result.Text, x, y, cancellationToken);
    }

    public async Task TranslateCurrentSelectionAsync()
    {
        NativeMethods.GetCursorPos(out var point);
        await TranslateSelectionAsync(point.X, point.Y, ForegroundProcessName());
    }

    public async Task TranslateTextAsync(string text, int x, int y, CancellationToken cancellationToken = default)
    {
        _lastText = text;
        _lastX = x;
        _lastY = y;

        var viewModel = new TranslationPopupViewModel
        {
            OriginalText = text,
            Translation = string.Empty,
            Error = null,
            IsStreaming = true,
            Status = "翻译中…"
        };

        viewModel.RetryRequested += (_, _) => _ = RetryAsync();
        viewModel.CloseRequested += (_, _) => _popup?.Hide();

        _viewModel = viewModel;

        if (_popup is null)
        {
            _popup = new TranslationPopupWindow();
            _popup.Closed += (_, _) => _popup = null;
        }

        _popup.ShowFor(viewModel, x, y);

        var settings = _settings.Current.Translate;
        var provider = ProviderFactory.Create(settings, _protector, _http);
        var key = CacheKey.For(provider.Id, settings.Model, settings.TargetLang, text);

        if (_cache.TryGet(key, out var cached))
        {
            viewModel.Translation = cached;
            viewModel.IsStreaming = false;
            viewModel.Status = "缓存";
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var firstChunk = true;

        var progress = new Progress<TranslationChunk>(chunk =>
        {
            if (chunk.IsFinal)
            {
                viewModel.IsStreaming = false;
                viewModel.Status = $"{stopwatch.ElapsedMilliseconds} ms";
                _cache.Set(key, viewModel.Translation);
                return;
            }

            viewModel.Translation += chunk.Delta;

            if (firstChunk)
            {
                firstChunk = false;
                viewModel.Status = $"首字 {stopwatch.ElapsedMilliseconds} ms";
            }
        });

        try
        {
            await provider.TranslateAsync(
                new TranslationRequest(text, null, settings.TargetLang, ForegroundWindowTitle()),
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            viewModel.IsStreaming = false;
        }
        catch (TranslationException ex)
        {
            viewModel.IsStreaming = false;
            viewModel.Error = ex.Message;
            viewModel.Status = "失败";
            AppLog.Error("translation failed", ex);
        }
        catch (Exception ex)
        {
            viewModel.IsStreaming = false;
            viewModel.Error = "网络错误，请检查连接后重试";
            viewModel.Status = "失败";
            AppLog.Error("translation failed", ex);
        }
    }

    private async Task RetryAsync()
    {
        if (_lastText is null)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        await TranslateTextAsync(_lastText, _lastX, _lastY, _cts.Token);
    }

    private static string? ForegroundProcessName()
    {
        try
        {
            var handle = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ForegroundWindowTitle()
    {
        try
        {
            var handle = NativeMethods.GetForegroundWindow();
            var buffer = new System.Text.StringBuilder(256);
            NativeMethods.GetWindowText(handle, buffer, buffer.Capacity);
            var title = buffer.ToString().Trim();
            return title.Length == 0 ? null : title;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
```

注意：`ForegroundProcessName`/`ForegroundWindowTitle` 在 `TranslateSelectionAsync` 取词前调用一次，避免取词后焦点变化。当前实现是在调用时获取，调用方传入即可；快捷键路径在 `TranslateCurrentSelectionAsync` 中先取进程名，再取词。把 `TranslateSelectionAsync` 内部的 `result` 分支保持原样。

- [ ] **Step 6: 在 App 中接线**

在 `App.xaml.cs` 的 DI 注册中加入：

```csharp
services.AddSingleton<TranslationCoordinator>();
services.AddSingleton<HotkeyManager>();
```

在 `OnStartup` 末尾加入：

```csharp
var hotkeyManager = _services.GetRequiredService<HotkeyManager>();
hotkeyManager.Pressed += async (_, _) =>
    await _services.GetRequiredService<TranslationCoordinator>().TranslateCurrentSelectionAsync();

if (!HotkeyParser.TryParse(settingsStore.Current.Hotkey, out var hotkey) ||
    !hotkeyManager.Register(hotkey))
{
    _tray.Notify("快捷键注册失败，请到设置中更换");
}
```

并补 `using SelectionTranslator.Core.Hotkey;`。

- [ ] **Step 7: 编译并手动验证**

```bash
dotnet build SelectionTranslator.sln
dotnet run --project src/SelectionTranslator.App
```

验收：
1. 打开 Chrome，选中英文，按 `Alt+D`，卡片出现并逐字显示译文。
2. 再次按 `Alt+D`，旧请求被取消，新卡片内容正确。
3. 打开记事本，剪贴板先复制一段中文，选中英文按热键，翻译后粘贴仍是你之前复制的中文。
4. 断网后按热键，卡片显示网络错误。

- [ ] **Step 8: 提交**

```bash
git add .
git commit -m "feat: wire global hotkey, translation coordinator and popup"
```

---

## Task 14: 全局鼠标钩子与悬浮图标

**Files:**
- Create: `src/SelectionTranslator.App/Services/LowLevelMouseHook.cs`
- Create: `src/SelectionTranslator.App/Views/SelectionIconWindow.xaml(.cs)`
- Create: `src/SelectionTranslator.App/Services/SelectionIconController.cs`
- Modify: `src/SelectionTranslator.App/App.xaml.cs`

**Interfaces:**
- Produces: `LowLevelMouseHook.Start() : bool`、`Stop()`、事件 `MouseDown/MouseUp/Wheel`（参数 `MouseHookEventArgs(int X, int Y, long Time)`）
- Produces: `SelectionIconController.Start()`、`Dispose()`

- [ ] **Step 1: 写钩子**

`src/SelectionTranslator.App/Services/LowLevelMouseHook.cs`:

```csharp
using System.Runtime.InteropServices;
using SelectionTranslator.Core.Diagnostics;

namespace SelectionTranslator.App.Services;

public sealed record MouseHookEventArgs(int X, int Y, long Time);

public sealed class LowLevelMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseWheel = 0x020A;

    private readonly HookProc _proc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private bool _running;

    public LowLevelMouseHook() => _proc = HookCallback;

    public event EventHandler<MouseHookEventArgs>? MouseDown;
    public event EventHandler<MouseHookEventArgs>? MouseUp;
    public event EventHandler<MouseHookEventArgs>? Wheel;

    public bool Start()
    {
        if (_running)
            return true;

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);

            if (_hook == IntPtr.Zero)
            {
                started.TrySetResult(false);
                return;
            }

            _running = true;
            started.TrySetResult(true);

            MSG message;
            while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _running = false;
        })
        {
            IsBackground = true,
            Name = "LowLevelMouseHook"
        };

        _thread.Start();
        return started.Task.GetAwaiter().GetResult();
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var args = new MouseHookEventArgs(data.pt.X, data.pt.Y, data.time);

            switch (wParam.ToInt32())
            {
                case WmLButtonDown: MouseDown?.Invoke(this, args); break;
                case WmLButtonUp: MouseUp?.Invoke(this, args); break;
                case WmMouseWheel: Wheel?.Invoke(this, args); break;
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Stop()
    {
        if (_threadId != 0)
            PostThreadMessage(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() => Stop();

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
}
```

- [ ] **Step 2: 写图标窗口**

`src/SelectionTranslator.App/Views/SelectionIconWindow.xaml`:

```xml
<Window x:Class="SelectionTranslator.App.Views.SelectionIconWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="24" Height="24" WindowStyle="None" AllowsTransparency="True"
        Background="Transparent" ShowInTaskbar="False" Topmost="True"
        ShowActivated="False" ResizeMode="NoResize">
  <Border Background="#F02B6CFF" CornerRadius="6" Cursor="Hand"
          MouseLeftButtonDown="OnClick" ToolTip="翻译">
    <TextBlock Text="译" Foreground="White" FontSize="12" FontWeight="SemiBold"
               HorizontalAlignment="Center" VerticalAlignment="Center"/>
  </Border>
</Window>
```

`src/SelectionTranslator.App/Views/SelectionIconWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Interop;

namespace SelectionTranslator.App.Views;

public partial class SelectionIconWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x00000080;

    public event EventHandler? Clicked;

    public SelectionIconWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExstyle);
            SetWindowLongPtr(handle, GwlExstyle, style | WsExNoactivate | WsExToolwindow);
        };
    }

    private void OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : (IntPtr)GetWindowLong32(hWnd, index);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, index, value) : (IntPtr)SetWindowLong32(hWnd, index, value.ToInt32());

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);
}
```

- [ ] **Step 3: 写控制器**

`src/SelectionTranslator.App/Services/SelectionIconController.cs`:

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SelectionTranslator.App.Views;
using SelectionTranslator.Core.Selection;
using SelectionTranslator.Core.Settings;

namespace SelectionTranslator.App.Services;

public sealed class SelectionIconController : IDisposable
{
    private readonly LowLevelMouseHook _hook;
    private readonly DragTracker _tracker = new();
    private readonly SettingsStore _settings;
    private readonly TranslationCoordinator _coordinator;
    private readonly DispatcherTimer _hideTimer;

    private SelectionIconWindow? _icon;
    private string? _processName;
    private int _x;
    private int _y;

    public SelectionIconController(
        LowLevelMouseHook hook,
        SettingsStore settings,
        TranslationCoordinator coordinator)
    {
        _hook = hook;
        _settings = settings;
        _coordinator = coordinator;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) => HideIcon();

        _hook.MouseDown += OnMouseDown;
        _hook.MouseUp += OnMouseUp;
        _hook.Wheel += (_, _) => Application.Current.Dispatcher.Invoke(HideIcon);
    }

    public void Start()
    {
        if (!_hook.Start())
            throw new InvalidOperationException("全局鼠标钩子启动失败");
    }

    private void OnMouseDown(object? sender, MouseHookEventArgs e)
    {
        _tracker.OnDown(e.X, e.Y, e.Time);

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_icon?.IsVisible == true && !IsInsideIcon(e.X, e.Y))
                HideIcon();
        });
    }

    private void OnMouseUp(object? sender, MouseHookEventArgs e)
    {
        var isDrag = _tracker.OnUp(e.X, e.Y, e.Time);
        if (!isDrag)
            return;

        _x = e.X;
        _y = e.Y;
        _processName = GetProcessName();

        if (_settings.Current.Trigger.AutoTranslate)
        {
            _ = _coordinator.TranslateSelectionAsync(_x, _y, _processName);
            return;
        }

        if (_settings.Current.Trigger.ShowIcon)
            Application.Current.Dispatcher.BeginInvoke(ShowIcon);
    }

    private void ShowIcon()
    {
        HideIcon();

        _icon = new SelectionIconWindow();
        _icon.Clicked += async (_, _) =>
            await _coordinator.TranslateSelectionAsync(_x, _y, _processName);

        _icon.Left = _x + 12;
        _icon.Top = _y + 12;
        _icon.Show();
        _hideTimer.Start();
    }

    private void HideIcon()
    {
        _hideTimer.Stop();

        if (_icon is null)
            return;

        _icon.Close();
        _icon = null;
    }

    private bool IsInsideIcon(int x, int y) =>
        _icon is not null && x >= _icon.Left && x <= _icon.Left + _icon.Width &&
        y >= _icon.Top && y <= _icon.Top + _icon.Height;

    private static string? GetProcessName()
    {
        try
        {
            var handle = SelectionTranslator.Core.Interop.NativeMethods.GetForegroundWindow();
            SelectionTranslator.Core.Interop.NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _hideTimer.Stop();
        _hook.Dispose();
    }
}
```

**实现注意（两个必须处理的坑）：**

1. **全局钩子会先于 WPF 收到鼠标按下**。如果 `OnMouseDown` 无条件隐藏图标，点击「译」图标的这一下会先把图标关掉，WPF 永远收不到点击。必须用窗口物理坐标判断是否点在图标上，在图标内时不隐藏。`_icon.Left` 是 DIP，不能和钩子的物理像素比较，改用 `GetWindowRect`：

```csharp
private bool IsInsideIcon(int x, int y)
{
    if (_icon is null) return false;

    var handle = new System.Windows.Interop.WindowInteropHelper(_icon).Handle;
    if (!SelectionTranslator.Core.Interop.NativeMethods.GetWindowRect(handle, out var rect))
        return false;

    return x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
}
```

并在 `NativeMethods` 中追加：

```csharp
[DllImport("user32.dll")]
public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
```

2. **不要在 WPF 的 `Left/Top` 里混用物理像素**。钩子回调给的是物理像素，`Window.Left` 是 DIP，150% 缩放下会偏移。图标和卡片统一用 `WindowPlacer.MoveTo`（内部 `SetWindowPos`，物理像素）定位，该类在 Task 16 创建；实施时请提前到本任务创建并使用。`SetWindowPos` 的 P/Invoke：

```csharp
[DllImport("user32.dll", SetLastError = true)]
public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
    int x, int y, int cx, int cy, uint flags);

public const uint SwpNoSize = 0x0001;
public const uint SwpNoZOrder = 0x0004;
public const uint SwpNoActivate = 0x0010;
```

- [ ] **Step 4: 接线**

在 `App.xaml.cs` 中注册并启动：

```csharp
services.AddSingleton<LowLevelMouseHook>();
services.AddSingleton<SelectionIconController>();
```

`OnStartup` 末尾：

```csharp
_services.GetRequiredService<SelectionIconController>().Start();
```

- [ ] **Step 5: 编译并手动验证**

```bash
dotnet build SelectionTranslator.sln
dotnet run --project src/SelectionTranslator.App
```

验收：
1. Chrome 里拖选一段英文，鼠标右下出现「译」图标，点击后出译文。
2. 点击空白处、滚轮、Esc、3 秒后图标消失。
3. 在设置里把 `autoTranslate` 改成 `true`（直接编辑 `%APPDATA%\SelectionTranslator\config.json`），拖选后直接出译文。
4. 单击不触发图标。

- [ ] **Step 6: 提交**

```bash
git add .
git commit -m "feat: add global mouse hook and selection icon"
```

---

## Task 15: 设置窗口、开机自启与排除列表

**Files:**
- Create: `src/SelectionTranslator.App/Views/SettingsWindow.xaml(.cs)`
- Create: `src/SelectionTranslator.App/Services/AutoStartService.cs`
- Modify: `src/SelectionTranslator.App/App.xaml.cs`

**Interfaces:**
- Produces: `AutoStartService.SetEnabled(bool)`、`IsEnabled()`
- Consumes: `SettingsStore`、`ISecretProtector`

- [ ] **Step 1: 写开机自启**

`src/SelectionTranslator.App/Services/AutoStartService.cs`:

```csharp
using Microsoft.Win32;

namespace SelectionTranslator.App.Services;

public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SelectionTranslator";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);

        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
```

- [ ] **Step 2: 写设置窗口**

`src/SelectionTranslator.App/Views/SettingsWindow.xaml`:

```xml
<Window x:Class="SelectionTranslator.App.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="划译设置" Width="520" Height="560"
        WindowStartupLocation="CenterScreen">
  <ScrollViewer Padding="20">
    <StackPanel>
      <TextBlock Text="触发" FontWeight="SemiBold" Margin="0,0,0,8"/>
      <TextBlock Text="快捷键"/>
      <TextBox x:Name="HotkeyBox" Margin="0,4,0,10"/>
      <CheckBox x:Name="ShowIconBox" Content="划词后显示悬浮图标" Margin="0,4"/>
      <CheckBox x:Name="AutoTranslateBox" Content="划词后直接翻译（容易误触）" Margin="0,4"/>
      <CheckBox x:Name="AutoStartBox" Content="开机自动启动" Margin="0,4,0,16"/>

      <TextBlock Text="翻译服务" FontWeight="SemiBold" Margin="0,0,0,8"/>
      <ComboBox x:Name="ProviderBox" Margin="0,4,0,10">
        <ComboBoxItem Content="glm" Tag="glm"/>
        <ComboBoxItem Content="siliconflow" Tag="siliconflow"/>
        <ComboBoxItem Content="deepseek" Tag="deepseek"/>
        <ComboBoxItem Content="ollama" Tag="ollama"/>
        <ComboBoxItem Content="自定义" Tag="custom"/>
      </ComboBox>
      <TextBlock Text="Base URL"/>
      <TextBox x:Name="BaseUrlBox" Margin="0,4,0,10"/>
      <TextBlock Text="模型"/>
      <TextBox x:Name="ModelBox" Margin="0,4,0,10"/>
      <TextBlock Text="API Key（DPAPI 加密保存）"/>
      <PasswordBox x:Name="ApiKeyBox" Margin="0,4,0,10"/>
      <TextBlock Text="目标语言"/>
      <TextBox x:Name="TargetLangBox" Margin="0,4,0,16"/>

      <TextBlock Text="排除的应用（每行一个进程名）" FontWeight="SemiBold" Margin="0,0,0,8"/>
      <TextBox x:Name="ExcludeBox" Height="90" AcceptsReturn="True"
               VerticalScrollBarVisibility="Auto" Margin="0,4,0,16"/>

      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
        <Button Content="取消" Width="80" Margin="0,0,8,0" Click="OnCancel"/>
        <Button Content="保存" Width="80" IsDefault="True" Click="OnSave"/>
      </StackPanel>
    </StackPanel>
  </ScrollViewer>
</Window>
```

`src/SelectionTranslator.App/Views/SettingsWindow.xaml.cs`:

```csharp
using System.Windows;
using SelectionTranslator.App.Services;
using SelectionTranslator.Core.Settings;

namespace SelectionTranslator.App.Views;

public partial class SettingsWindow : Window
{
    private static readonly Dictionary<string, (string BaseUrl, string Model)> Presets = new()
    {
        ["glm"] = ("https://open.bigmodel.cn/api/paas/v4", "glm-4-flash"),
        ["siliconflow"] = ("https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct"),
        ["deepseek"] = ("https://api.deepseek.com/v1", "deepseek-chat"),
        ["ollama"] = ("http://127.0.0.1:11434/v1", "qwen2.5:7b")
    };

    private readonly SettingsStore _store;
    private readonly ISecretProtector _protector;
    private readonly Action _onSaved;

    public SettingsWindow(SettingsStore store, ISecretProtector protector, Action onSaved)
    {
        InitializeComponent();
        _store = store;
        _protector = protector;
        _onSaved = onSaved;

        LoadSettings();
        ProviderBox.SelectionChanged += OnProviderChanged;
    }

    private void LoadSettings()
    {
        var settings = _store.Current;

        HotkeyBox.Text = settings.Hotkey;
        ShowIconBox.IsChecked = settings.Trigger.ShowIcon;
        AutoTranslateBox.IsChecked = settings.Trigger.AutoTranslate;
        AutoStartBox.IsChecked = AutoStartService.IsEnabled();

        foreach (var item in ProviderBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
            if (Equals(item.Tag, settings.Translate.Provider))
                ProviderBox.SelectedItem = item;

        if (ProviderBox.SelectedItem is null)
            ProviderBox.SelectedIndex = 0;

        BaseUrlBox.Text = settings.Translate.BaseUrl;
        ModelBox.Text = settings.Translate.Model;
        TargetLangBox.Text = settings.Translate.TargetLang;

        if (!string.IsNullOrEmpty(settings.Translate.ApiKeyProtected))
        {
            try { ApiKeyBox.Password = _protector.Unprotect(settings.Translate.ApiKeyProtected); }
            catch (Exception) { ApiKeyBox.Password = string.Empty; }
        }

        ExcludeBox.Text = string.Join(Environment.NewLine, settings.Exclude.Processes);
    }

    private void OnProviderChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProviderBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
            return;

        if (item.Tag is string id && Presets.TryGetValue(id, out var preset))
        {
            BaseUrlBox.Text = preset.BaseUrl;
            ModelBox.Text = preset.Model;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var settings = _store.Current;
        settings.Hotkey = HotkeyBox.Text.Trim();
        settings.Trigger.ShowIcon = ShowIconBox.IsChecked == true;
        settings.Trigger.AutoTranslate = AutoTranslateBox.IsChecked == true;

        if (ProviderBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string id)
            settings.Translate.Provider = id;

        settings.Translate.BaseUrl = BaseUrlBox.Text.Trim();
        settings.Translate.Model = ModelBox.Text.Trim();
        settings.Translate.TargetLang = TargetLangBox.Text.Trim();

        settings.Translate.ApiKeyProtected = string.IsNullOrEmpty(ApiKeyBox.Password)
            ? string.Empty
            : _protector.Protect(ApiKeyBox.Password);

        settings.Exclude.Processes = ExcludeBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _store.Save();
        AutoStartService.SetEnabled(AutoStartBox.IsChecked == true);
        _onSaved();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 3: 接线**

在 `App.xaml.cs` 中：

```csharp
_tray.SettingsRequested += (_, _) => ShowSettings();

void ShowSettings()
{
    var window = new SelectionTranslator.App.Views.SettingsWindow(
        settingsStore,
        _services!.GetRequiredService<ISecretProtector>(),
        onSaved: () => { });
    window.ShowDialog();
}
```

首次启动时根据 `Trigger.AutoStart` 调用 `AutoStartService.SetEnabled(...)`。

设置保存后立即重注册快捷键，不要求重启：

```csharp
void ApplySettings()
{
    if (HotkeyParser.TryParse(settingsStore.Current.Hotkey, out var hotkey) &&
        hotkeyManager.Register(hotkey))
    {
        return;
    }

    _tray.Notify("快捷键注册失败，请换一个组合键");
}
```

把 `hotkeyManager` 提升为 `App` 的字段；`ShowSettings` 的 `onSaved` 传 `ApplySettings`。

- [ ] **Step 4: 编译并手动验证**

```bash
dotnet build SelectionTranslator.sln
dotnet run --project src/SelectionTranslator.App
```

验收：
1. 托盘双击打开设置，修改快捷键并保存，新快捷键立即生效（无需重启）。
2. 填入 GLM API Key，保存后重新打开设置，Key 仍在（DPAPI 解密正常）。
3. 勾选开机自启，检查 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 出现 `SelectionTranslator`。
4. 排除列表加入 `notepad`，在记事本里划词不再触发。

- [ ] **Step 5: 提交**

```bash
git add .
git commit -m "feat: add settings window, autostart and exclusion list"
```

---

## Task 16: 多屏定位、错误提示收尾、打包与验收

**Files:**
- Create: `src/SelectionTranslator.App/Services/WindowPlacer.cs`
- Modify: `src/SelectionTranslator.App/Views/TranslationPopupWindow.xaml.cs`
- Modify: `src/SelectionTranslator.App/Program.cs`（若模板使用 `App.xaml` 自动生成入口，则跳过）
- Create: `README.md`

**Interfaces:**
- Produces: `WindowPlacer.PlaceNear(Window window, int screenX, int screenY)`

- [ ] **Step 1: 写多屏定位**

`src/SelectionTranslator.App/Services/WindowPlacer.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;

namespace SelectionTranslator.App.Services;

public static class WindowPlacer
{
    private const uint MonitorDefaultToNearest = 2;

    public static void PlaceNear(Window window, int screenX, int screenY)
    {
        var point = new POINT { X = screenX, Y = screenY };
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

        if (!GetMonitorInfo(monitor, ref info))
        {
            var work = SystemParameters.WorkArea;
            Place(window, work.Left, work.Top, work.Right, work.Bottom, screenX, screenY);
            return;
        }

        Place(window,
            info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom,
            screenX, screenY);
    }

    private static void Place(Window window, double left, double top, double right, double bottom, int x, int y)
    {
        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 0 ? window.ActualHeight : 200;

        var targetLeft = Math.Min(x + 16, right - width - 8);
        var targetTop = Math.Min(y + 16, bottom - height - 8);

        window.Left = Math.Max(targetLeft, left + 8);
        window.Top = Math.Max(targetTop, top + 8);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}
```

- [ ] **Step 2: 替换弹窗定位**

`TranslationPopupWindow.PlaceNear` 改为：

```csharp
public void PlaceNear(int screenX, int screenY) =>
    SelectionTranslator.App.Services.WindowPlacer.PlaceNear(this, screenX, screenY);
```

并在 `ShowFor` 中先 `Show()`，再 `Dispatcher.BeginInvoke(() => PlaceNear(x, y), DispatcherPriority.Loaded)`，确保 `ActualWidth/ActualHeight` 已计算：

```csharp
public void ShowFor(TranslationPopupViewModel viewModel, int x, int y)
{
    DataContext = viewModel;
    if (!IsVisible)
        Show();

    Dispatcher.BeginInvoke(
        () => PlaceNear(x, y),
        System.Windows.Threading.DispatcherPriority.Loaded);
}
```

- [ ] **Step 3: 错误提示与降级检查**

逐项确认代码中已有处理（缺的补上）：

- 取词失败 → `TrayIconService.Notify("未取到选中文字")`（Task 13 已实现）
- 401/403 → `TranslationException` 消息包含「API Key 无效」（Task 11 已实现）
- 429 → 消息包含「请求过于频繁」（Task 11 已实现）
- 流中断 → Provider 抛出异常，卡片显示错误（Task 13 已实现）
- 快捷键冲突 → 启动时托盘气泡（Task 13 已实现）

- [ ] **Step 4: 手动验收矩阵**

| 程序 | 操作 | 预期 |
|---|---|---|
| Chrome | 拖选英文 → 点「译」 | 卡片出现，剪贴板未变 |
| Edge | 选中英文 → `Alt+D` | 卡片出现 |
| Word | 拖选英文 → 点「译」 | 卡片出现 |
| WPS | 拖选英文 → `Alt+D` | 卡片出现 |
| 记事本 | 先复制中文，再划词翻译 | 翻译后粘贴仍是中文 |
| Windows Terminal | 选中文本 → `Alt+D` | 不发送 Ctrl+C，取不到则提示 |
| 密码框 | 双击密码框选中 | 不弹窗 |
| 多显示器 | 在副屏划词 | 卡片出现在副屏且不越界 |
| 150% 缩放 | 划词 | 图标与卡片位置正确 |
| 断网 | `Alt+D` | 卡片显示网络错误，可重试 |
| 管理员窗口（任务管理器） | 划词 | 提示未取到文字，程序不崩溃 |

- [ ] **Step 5: 性能验证**

在托盘右键时观察任务管理器：

- 空闲内存 < 120MB
- 空闲 CPU < 0.5%
- 冷启动到托盘图标出现 < 1 秒

若内存超标，检查是否在启动时加载了 UIA 程序集或缓存了过大的数据。

- [ ] **Step 6: 打包**

```bash
dotnet publish src/SelectionTranslator.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/framework-dependent
dotnet publish src/SelectionTranslator.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/portable
```

验收：
- `publish/framework-dependent/SelectionTranslator.App.exe` 在装有 .NET 8 Desktop Runtime 的机器上可直接运行。
- `publish/portable` 目录整体拷贝到无 .NET 的机器可运行。
- 两种包都能启动、划词、翻译、退出。

- [ ] **Step 7: 写 README**

`README.md` 必须包含：

```markdown
# 划译（SelectionTranslator）

Windows 系统级划词翻译工具。

## 功能
- 任意程序划词，点击悬浮图标或按 Alt+D 翻译
- 流式译文、剪贴板自动恢复、密码框不取词
- 默认智谱 GLM-4-Flash（免费），支持任意 OpenAI 兼容接口与 Ollama

## 使用
1. 运行 SelectionTranslator.App.exe
2. 托盘右键 → 设置，填入 API Key
3. 在任意程序中选中英文，按 Alt+D 或点击「译」图标

## 已知限制
- 管理员权限窗口中无法取词（除非本程序也以管理员运行）
- 全屏独占游戏、DRM 视频无法取词
- 终端类程序只使用 UIA 取词，可能失效

## 构建
需要 .NET 8 SDK：
    dotnet build SelectionTranslator.sln
    dotnet test src/SelectionTranslator.Tests/SelectionTranslator.Tests.csproj
```

- [ ] **Step 8: 提交**

```bash
git add .
git commit -m "feat: finish multi-monitor placement, packaging and acceptance docs"
```

---

## 自检记录

**Spec 覆盖检查**

| Spec 章节 | 对应任务 |
|---|---|
| 4.1 触发方式 | 6、13、14、15 |
| 4.2 鼠标钩子 | 7、14 |
| 4.3 取词流水线 | 8、9、10 |
| 4.4 文本校验 | 1、9 |
| 4.5 剪贴板安全 | 8 |
| 4.6 权限与兼容性 | 16 验收矩阵 |
| 5.1 Provider 抽象 | 2、11 |
| 5.2 提示词 | 2 |
| 5.3 流式 | 3、11 |
| 5.4 缓存 | 4、13 |
| 6.1 悬浮图标 | 14 |
| 6.2 译文卡片 | 13 |
| 6.3 托盘与设置 | 12、15 |
| 7 配置与密钥 | 5、15 |
| 8 错误处理 | 11、13、16 |
| 9 性能预算 | 16 |
| 10 安全与隐私 | 5、8、10 |
| 11 技术栈 | 1 |
| 12 目录结构 | 任务文件清单 |
| 13 测试策略 | 各任务单元测试 + 16 验收矩阵 |
| 14 里程碑 | Task 12–16 对应 M1–M4 |
| 15 风险 | 16 验收 + README 限制说明 |

**类型一致性**

- `ITextAcquirer.Name` 在 `ClipboardTextAcquirer` 返回 `"clipboard"`，`UiaTextAcquirer` 返回 `"uia"`，与 `DefaultAcquisitionPolicy` 中的判断一致。
- `TranslationChunk` 在 Provider 与 Coordinator 中签名一致。
- `SettingsStore.Current.Translate` 的类型在 ProviderFactory 中为 `TranslateSettings`，一致。
- `NativeMethods` 从 `internal` 改为 `public` 的改动在 Task 14 中明确说明，Task 13 的 P/Invoke 声明需同步改为 `public`。

**占位符扫描**

- 无 TBD/TODO。
- Task 10 无自动化测试，已明确说明原因并给出集成后的手动验证步骤。
- 设置窗口的错误提示采用托盘气泡，未引入额外 Toast 窗口，符合 YAGNI。

**已知偏差**

- 计划使用手写 P/Invoke 而非设计文档中的 CsWin32，原因：避免源码生成器配置差异，减少零上下文实现者的失败面；功能等价。
- 托盘使用 WinForms `NotifyIcon`（.NET 内置），未引入 `H.NotifyIcon.Wpf`，减少依赖。
- 这两点需要同步更新设计文档的依赖表。