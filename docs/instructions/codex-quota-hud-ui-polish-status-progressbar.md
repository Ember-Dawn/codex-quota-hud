# Codex 指令：HUD UI Polish、刷新状态栏、进度条绘制修正

> 目标项目路径：`E:\github\codex-quota-hud`  
> 技术栈：C# + WinForms + .NET 8/9 Windows  
> 本次任务只做 UI 细节修正，不要改 Codex 额度读取逻辑。

---

## 0. 重要限制

请严格控制修改范围，节省上下文和 token。

### 不要做

- 不要重写项目结构。
- 不要修改 Codex app-server 额度读取逻辑。
- 不要修改 `CodexQuotaReader.cs`，除非编译错误明确要求。
- 不要新增节奏判断、历史分析、开机自启、自动更新等功能。
- 不要新增大型 UI 框架或第三方依赖。
- 不要读取或生成 `bin/`、`obj/`、`.git/` 里的内容。

### 优先查看和修改

优先只查看这些文件：

- `MainHudForm.cs`
- `QuotaBarControl.cs`
- `SettingsForm.cs`
- `AppSettings.cs` / `SettingsStore.cs`，如果存在
- `QuotaModels.cs`，仅在 UI 绑定字段需要确认时查看
- `README.md`，仅在需要更新说明时查看

---

## 1. 本次修改目标概览

当前 HUD 已能显示额度，但还有这些 UI 问题需要修：

1. 刷新状态文字目前出现在窗口底部，且可能被遮挡。应移动到标题栏第一行。
2. 除 `Codex Quota` 标题外，主界面状态文字不够清晰，需要统一加粗。
3. 展开态窗口圆角还不够明显，需要增大。
4. 进度条轨道外边框不够明显，需要更清楚的默认颜色和更明显的线宽。
5. 当前圆角填充在低百分比时会变成固定大小的小圆球，导致 5%、11% 等低百分比不够准确。需要改成“圆角轨道 + 直角填充”的绘制方式。
6. Settings 中需要同步支持 Track Border Color，并使用新的默认值。

---

## 2. 标题栏布局修正

展开态第一行应显示三类信息：

```text
Codex Quota        Refreshing...   11:14
```

规则：

- 左侧：`Codex Quota`
- 中间偏右：刷新状态文字
- 最右：数据更新时间
- 刷新状态必须在更新时间左侧。
- 刷新状态不要再出现在窗口底部。
- 刷新状态不要被遮挡。
- 右上角数据更新时间仍然需要与下面 reset 时间的 `HH:mm` 列对齐。

### 状态文案

使用英文界面：

- 刷新中：`Refreshing...`
- 刷新成功：`Updated`
- 失败：`Failed`
- 平时无状态时可以显示为空字符串。

如果错误信息很长，不要直接显示在 HUD 主界面；HUD 只显示 `Failed` 即可。

---

## 3. 字体与字重

`Codex Quota` 标题保持当前大小和当前字重，不要改大。

以下文字需要统一使用更清楚的字重，建议 Bold：

- `7d`
- `5h`
- `R`
- reset 日期，例如 `Jul 10`
- reset 时间，例如 `18:39` / `13:33`
- 右上角 updated time，例如 `11:14`
- 刷新状态文字，例如 `Refreshing...` / `Updated` / `Failed`

字号尽量保持当前大小，不要明显增大窗口。

建议文字颜色保持或接近：

```text
#F2F4F8
```

如果需要 muted 文本，可以使用：

```text
#C8D0DA
```

---

## 4. 展开态布局要求

展开态继续保持当前信息结构，但要更清楚、对齐。

目标布局示意：

```text
Codex Quota        Refreshing...   11:14

7d  [          73%        ]  R  Jul 10  18:39
5h  [          11%        ]  R          13:33
```

要求：

- `7d` / `5h` 放在进度条左边。
- 百分比放在进度条内部。
- reset 前缀使用 `R`，不要使用 `↻`。
- `R` 字重和 `7d` / `5h` 一致。
- 7d 日期使用英文缩写，例如 `Jul 10`。
- 5h reset 始终只显示 `HH:mm`，即使跨天也不要显示日期。
- 右上角 updated time 要和右侧 reset 的 `HH:mm` 时间列右对齐。
- 不要把刷新状态放到底部。

---

## 5. 折叠态布局要求

折叠态保持之前确认的设计：

```text
[          7d          ]   [          5h          ]
```

要求：

- `7d` / `5h` 放在进度条内部。
- 不显示百分比。
- 不显示 reset 时间。
- 不显示刷新状态。
- 轨道应有可见边框，让用户看清 100% 的边界。

---

## 6. 窗口圆角

展开态窗口圆角需要更明显。

建议：

```text
Expanded window radius: 14 px
Collapsed window radius: 10 px 或 12 px
```

要求：

- 不只是内部背景圆角，Form 本体也应使用圆角 region，避免实际窗口仍然是直角。
- 展开态和折叠态切换后，都应重新应用对应圆角 region。
- 如果窗口大小变化，需要重新计算圆角 region。

---

## 7. 进度条绘制修正

### 7.1 Track 与 Border

进度条由三部分组成：

- Track：灰色背景轨道，也就是未填充部分。
- Track Border：整个进度条外圈细边框，用来显示 100% 边界。
- Fill：已填充部分，表示剩余额度。

默认颜色请改成：

```text
Track Color:        #303740
Track Border Color: #7A8796
```

边框宽度建议：

```text
Track Border Width: 2 px
```

如果项目里已有 setting 字段，请同步更新默认值。

### 7.2 改成“圆角轨道 + 直角填充”

当前圆角填充在低百分比时会变成固定大小小圆球，导致 5%、11% 等低百分比不准确。

请改成：

```text
Track: rounded rectangle, with visible border
Fill: exact-width rectangular fill clipped inside the rounded track
```

要求：

- Track 保持圆角。
- Track Border 可见。
- Fill 宽度必须按真实百分比计算：`barWidth * percent / 100`。
- 不要把最小填充宽度设置为 bar height。
- 如果需要最小可见宽度，最多 2-3 px。
- Fill 的右边缘应为直角，低百分比时不要变成固定大小圆球。
- Fill 不能画到 Track Border 外面。
- 百分比文字仍然可以在进度条内部居中显示。

视觉目标：

```text
73% [███████|░░░]
11% [█|░░░░░░░░]
5%  [▌|░░░░░░░░]
```

其中 `|` 表示 Fill 的真实结束位置。

---

## 8. Settings 修改

Settings 保持英文界面。

如果 Settings 当前已有颜色设置，请确认包含这些项：

- `7d Color`
- `5h Color`
- `Track Color`
- `Track Border Color`

每个颜色项建议保持当前 hex 输入 + 小色块预览的方式。

### 默认值

请使用：

```text
7d Color:           #4EA1FF
5h Color:           #C2410C
Track Color:        #303740
Track Border Color: #7A8796
```

`Reset Defaults` 应恢复以上颜色值，并恢复默认刷新间隔。

### 自动刷新选项保持

保持这些选项：

- `30 sec`
- `1 min`
- `5 min`
- `10 min`
- `20 min`

默认：

```text
1 min
```

Settings 继续保存到用户目录，例如：

```text
%APPDATA%\CodexQuotaHud\settings.json
```

---

## 9. 验收标准

修改完成后运行：

```powershell
dotnet build
```

必须通过。

手动运行：

```powershell
dotnet run
```

检查：

1. HUD 能正常启动。
2. 展开态第一行显示：左侧 `Codex Quota`，右侧 updated time，中间可显示刷新状态。
3. 刷新时 `Refreshing...` 不再出现在底部，也不被遮挡。
4. 失败时主界面只显示简短 `Failed`，不显示长错误导致布局溢出。
5. `7d` / `5h` / `R` / reset 时间 / updated time 更清楚，统一加粗。
6. `Codex Quota` 标题大小保持当前，不要变大。
7. 展开态窗口圆角比之前更明显。
8. 折叠态仍然显示进度条内部的 `7d` / `5h`。
9. 进度条 Track Border 明显可见。
10. 低百分比，例如 5%、11%，填充宽度能真实变短，不再固定成小圆球。
11. Settings 中可以设置 `Track Border Color`，并能保存到用户目录。
12. 不要破坏额度读取逻辑。

---

## 10. 修改完成后回复内容

完成后请简要回复：

- 修改了哪些文件。
- `dotnet build` 是否通过。
- 是否新增或修改了 settings 字段。
- 是否没有改动额度读取逻辑。

