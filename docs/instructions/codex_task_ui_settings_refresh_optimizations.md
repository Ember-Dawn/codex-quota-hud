# Codex Quota HUD 任务说明：UI、设置界面、颜色与刷新闪烁优化

> 目标仓库：`E:\github\codex-quota-hud`  
> 执行者：Codex  
> 任务类型：小范围 UI/体验优化，不改 Antigravity quota 读取协议，不改 Codex quota 读取协议。  
> 重要原则：保持现有“吸附态 / 展开态”交互不变；保持 Codex + AGY provider 架构不变；本任务只优化显示、设置界面、默认颜色和刷新重绘行为。

---

## 0. 当前用户反馈

当前实现中有以下问题需要处理：

1. 展开态 AGY 卡片底部显示了 `Source Managed AGY`，用户认为冗余，希望删除。
2. Settings 界面里每个颜色右侧有 `Change` 按钮，用户希望删除这些按钮。
3. 用户希望点击颜色预览色块本身即可打开调色盘选择颜色。
4. 删除 `Change` 按钮后，Settings 界面宽度应相应缩小。
5. 当前 5h 默认黄色颜色下，进度条内 `5h` / 百分比文字不够清晰，希望更换默认 5h 颜色。
6. 刷新额度时整个 HUD 会闪烁，希望优化为只刷新有变化的局部控件。

---

## 1. 总体要求

请只做以下改动：

```text
1. 删除展开态 AGY card 底部的 Source Managed AGY 文本。
2. Settings 删除颜色行里的 Change 按钮。
3. 点击 hex 色块 / 颜色预览块时打开 ColorDialog。
4. Settings 界面宽度缩小，布局重新对齐。
5. 默认 5h 颜色从 #FFB454 改为 #C2410C。
6. QuotaBarControl 增加自动文字颜色逻辑，浅色填充时使用深色文字，深色填充时使用浅色文字。
7. 优化刷新时的闪烁：不要每次刷新都重建 UI；只更新变化的 label/bar；Percent 未变化时不要 Invalidate。
```

不要做以下事情：

```text
不要改 CodexQuotaReader 的协议逻辑。
不要改 AGY Managed Provider 的进程发现、启动、端口发现和请求协议逻辑。
不要移除 Enable Antigravity 设置。
不要改吸附态 / 展开态的核心交互。
不要把本任务扩大为整体重构。
```

---

## 2. 展开态 AGY 卡片：删除 Source 行

### 2.1 当前问题

展开态 AGY 模块底部显示：

```text
Source Managed AGY
```

这行信息属于调试/来源信息，对普通用户冗余，而且占用 HUD 高度。

### 2.2 目标行为

正常读取成功时，AGY 卡片只显示：

```text
AGY · Gemini           Checked HH:mm
7d   [percent bar]     R <date/time>
5h   [percent bar]     R <time>
```

底部不再显示：

```text
Source Managed AGY
```

### 2.3 保留来源信息的位置

来源信息不要在主 UI 常驻显示。

可以保留在以下位置之一：

```text
1. Tooltip
2. debug log
3. 错误提示文本里
```

例如，仅在出错时显示简短错误：

```text
AGY Offline
AGY CLI not ready
Login/trust required
```

---

## 3. Settings 界面：删除 Change 按钮，点击色块打开调色盘

### 3.1 当前问题

当前 Settings 颜色行类似：

```text
7d Color       [#4EA1FF] [色块] [Change]
5h Color       [#FFB454] [色块] [Change]
Track Color    [#303740] [色块] [Change]
Track Border   [#FF80C0] [色块] [Change]
```

用户希望删除 `Change` 按钮，并把调色盘功能集成到颜色预览色块上。

### 3.2 目标布局

改成：

```text
7d Color        [#4EA1FF] [色块]
5h Color        [#C2410C] [色块]
Track Color     [#303740] [色块]
Track Border    [#7A8796] [色块]
```

色块本身表现为可点击控件。

### 3.3 交互要求

点击任意颜色预览色块：

```text
打开 ColorDialog
用户选色并确认
  -> 更新对应 hex TextBox
  -> 更新对应色块背景色
用户取消
  -> 不做修改
```

保留手动输入 hex 的能力：

```text
用户在 TextBox 输入合法 hex
  -> 色块预览自动更新
用户输入非法 hex
  -> 不要崩溃
  -> 可以保持旧预览色
  -> Save 时执行现有校验/归一化逻辑
```

### 3.4 色块样式建议

颜色预览块需要明显可点击：

```text
Cursor = Cursors.Hand
有边框
大小保持紧凑，例如 34x22 或接近当前尺寸
```

可以设置 tooltip：

```text
Click to choose color
```

### 3.5 Settings 窗口宽度调整

删除 `Change` 按钮后，Settings 窗口宽度应缩小。

建议目标宽度大约：

```text
320 - 340 px
```

以实际控件不拥挤为准。

底部按钮布局建议：

```text
[Reset]                         [Cancel] [Save]
```

窗口高度保持能完整显示内容即可。

---

## 4. 默认颜色调整

### 4.1 当前问题

当前 5h 默认颜色类似浅黄/浅橙：

```text
#FFB454
```

在该颜色下，进度条里的白色 `5h` 或百分比文字对比度不足，用户反馈看不清。

### 4.2 目标默认色

将默认 5h 颜色改为：

```text
#C2410C
```

这是偏深的 burnt orange / 深橙色，和 7d 蓝色仍然有明显区分，同时白色文字可读性更好。

### 4.3 需要修改的位置

请检查并修改所有默认值来源，例如：

```text
AppSettings.DefaultFiveHourColor
SettingsStore 默认归一化逻辑
Reset Defaults 逻辑
README / DEVELOPMENT 文档中的默认颜色说明（如果仓库里有）
```

如果 Settings 中已有用户保存过旧颜色，不要强行覆盖用户设置。

只有以下情况使用新默认值：

```text
1. 用户第一次运行，没有 settings.json。
2. 用户点击 Reset Defaults。
3. settings.json 中 5h 颜色非法，需要 fallback 默认值。
```

---

## 5. QuotaBarControl：自动选择文字颜色

### 5.1 目标

为了避免用户以后自定义浅色时看不清文字，QuotaBarControl 应根据填充色亮度自动选择进度条内文字颜色。

### 5.2 建议逻辑

计算填充色亮度，可使用常见 perceived brightness / luminance：

```text
brightness = (R * 299 + G * 587 + B * 114) / 1000
```

如果填充色较亮：

```text
bar 内文字使用深色，例如 #111827 或 Color.Black
```

如果填充色较暗：

```text
bar 内文字使用浅色，例如 #FFFFFF
```

建议阈值：

```text
brightness >= 150 -> dark text
brightness < 150  -> white text
```

可以按实际视觉微调。

### 5.3 注意事项

如果 QuotaBarControl 当前有统一 `TextColor` 属性，不要破坏外部 API。可以新增内部方法：

```text
GetTextColorForFill(Color fillColor)
```

或新增属性：

```text
AutoTextColor = true
```

默认启用自动文字颜色。

---

## 6. 刷新闪烁优化

### 6.1 当前问题

用户反馈：刷新额度时，整个 HUD 会闪烁一下。

可能原因包括：

```text
刷新时重建 provider card
刷新时 Controls.Clear / Controls.Add
刷新时切换整体 Visible
刷新时调整整个 Form Size
刷新时全窗口 Invalidate
QuotaBarControl 每次 set Percent 都 Invalidate，即使数值未变
```

### 6.2 目标行为

刷新额度时：

```text
只更新变化的数字、时间、进度条
HUD 窗口不闪
窗口尺寸不抖
provider card 不重建
没有变化的 bar 不重绘
```

### 6.3 MainHudForm / ProviderCard 优化要求

请检查当前刷新流程，确保：

```text
Provider card 创建后复用。
普通刷新不要 Controls.Clear。
普通刷新不要重新 Add 所有控件。
普通刷新不要调用 ShowCurrentView，除非折叠/展开状态或 provider 数量发生变化。
普通刷新不要改变 Form Size。
普通刷新只更新对应 ProviderCardControl 的 snapshot。
```

如果 provider 数量发生变化，例如启用/禁用 AGY，才重新 layout。

### 6.4 Label 更新优化

为 label 更新加简单比较：

```text
if (label.Text != newText)
    label.Text = newText;
```

避免相同文本反复赋值引发重绘。

### 6.5 QuotaBarControl Percent 更新优化

如果当前是：

```text
Percent = value;
Invalidate();
```

请改成：

```text
if old percent and new percent effectively same:
    return
else:
    assign and Invalidate()
```

比较建议：

```text
null 和 null 相同
null 和 number 不同
number 和 number 差值 < 0.01 可视为相同
```

### 6.6 双缓冲

确保以下控件有合适的双缓冲：

```text
MainHudForm
ProviderCardControl / card panel
CollapsedProviderRowControl
QuotaBarControl
```

WinForms 自定义控件可使用：

```text
SetStyle(ControlStyles.AllPaintingInWmPaint |
         ControlStyles.OptimizedDoubleBuffer |
         ControlStyles.UserPaint |
         ControlStyles.ResizeRedraw, true);
```

如果是 Form / Panel，需要按现有实现选择合适方式，不要引入不必要的大改动。

### 6.7 不要过度优化

不要为了消除闪烁重写整个 UI 框架。

本任务只需要：

```text
避免重建控件
避免无意义 Invalidate
启用双缓冲
只更新变化内容
```

---

## 7. AGY / Codex 展示一致性

保持用户已确认的展示风格：

```text
Codex
  7d
  5h

AGY · Gemini
  7d
  5h
```

注意：AGY 内部仍然解析：

```text
gemini-weekly -> UI label 7d
gemini-5h     -> UI label 5h
```

不要把 AGY UI 改回 `Weekly`。

---

## 8. 验收标准

完成后请验证：

### 8.1 展开态

```text
AGY card 不再显示 Source Managed AGY。
Codex card 正常显示 7d / 5h。
AGY card 正常显示 7d / 5h。
Checked / Updated 时间仍正常显示。
Reset 时间仍正常显示。
```

### 8.2 折叠态

```text
吸附态交互不变。
折叠态仍能显示 Codex 行。
启用 AGY 时折叠态仍能显示 AGY 行。
鼠标悬停展开仍正常。
```

### 8.3 Settings

```text
Settings 中不再有 Change 按钮。
点击颜色色块可以打开 ColorDialog。
选择颜色后 hex 文本框更新。
手动修改合法 hex 后色块预览更新。
Settings 窗口宽度比之前更紧凑。
Save / Cancel / Reset 正常工作。
Enable Antigravity 设置仍正常保存。
```

### 8.4 默认颜色

```text
首次运行默认 5h 颜色为 #C2410C。
Reset Defaults 后 5h 颜色为 #C2410C。
非法 5h 颜色 fallback 为 #C2410C。
已有用户合法自定义颜色不会被强制覆盖。
```

### 8.5 可读性

```text
5h 进度条文字在默认 #C2410C 下清晰可读。
如果用户选择浅色填充，bar 内文字自动变成深色。
如果用户选择深色填充，bar 内文字自动变成浅色。
```

### 8.6 刷新闪烁

```text
点击 Refresh Now 或自动刷新时，HUD 不再整体闪烁。
只有发生变化的进度条 / label 更新。
无变化的百分比不会导致 bar 重绘。
窗口大小不会因为普通刷新抖动。
```

---

## 9. 建议修改文件

根据当前项目结构，优先检查这些文件：

```text
src/UI/MainHudForm.cs
src/UI/SettingsForm.cs
src/UI/QuotaBarControl.cs
src/UI/ProviderCardControl.cs          如果已存在
src/UI/CollapsedProviderRowControl.cs  如果已存在
src/Services/AppSettings.cs
src/Services/SettingsStore.cs
src/Models/*
README.md
DEVELOPMENT.md
```

实际文件名以当前仓库为准。

---

## 10. 构建与测试

修改完成后请运行：

```powershell
dotnet build .\CodexQuotaHud.csproj
```

如果可能，请手动运行：

```powershell
dotnet run --project .\CodexQuotaHud.csproj
```

手动测试：

```text
1. 打开 HUD，确认 Codex 展示正常。
2. 打开 Settings，确认没有 Change 按钮。
3. 点击色块，确认可选色。
4. 保存颜色后回到 HUD，确认颜色应用。
5. 点击 Reset，确认 5h 默认色是 #C2410C。
6. 吸附到顶部，确认折叠态仍正常。
7. 悬停展开，确认展开态仍正常。
8. 点击 Refresh Now 多次，观察是否仍有明显整窗闪烁。
9. 启用 Antigravity，确认 AGY card 不再显示 Source Managed AGY。
10. 关闭 Antigravity，确认 AGY 相关 UI 正常隐藏或 disabled。
```

---

## 11. 提交说明建议

建议 commit message：

```text
Polish HUD cards, settings color picker, and refresh rendering
```

建议变更分组：

```text
1. UI text cleanup: remove AGY source row
2. Settings color picker simplification
3. Default 5h color update
4. Quota bar text contrast
5. Refresh rendering optimization
```

---

## 12. 最终目标

最终用户体验应为：

```text
展开态更干净：AGY 不再显示冗余 Source 行。
Settings 更紧凑：没有 Change 按钮，点击色块即可选色。
默认 5h 颜色更清晰：#C2410C。
进度条文字自适应明暗：浅色条用深色字，深色条用浅色字。
刷新更平滑：只更新变化部分，不再整窗闪烁。
```
