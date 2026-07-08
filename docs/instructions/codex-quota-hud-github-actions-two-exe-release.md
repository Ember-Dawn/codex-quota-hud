# Codex 指令：GitHub Actions 生成两个 x64 EXE，并上传到 Release

## 背景

当前项目是 `E:\github\codex-quota-hud`，技术栈是 C# WinForms，目标框架目前是 `net8.0-windows`。

现在已有 GitHub Actions 可以编译，但当前产物不是我想要的最终发布形式。

## 目标

请只修改 GitHub Actions workflow，不要修改 C# 功能代码，不要修改 UI，不要改额度读取逻辑。

我要实现：

1. 只构建 Windows x64。
2. 不生成 zip，直接生成并上传 `.exe`。
3. 每次构建生成两个 exe：
   - `CodexQuotaHud-win-x64-no-dotnet.exe`
   - `CodexQuotaHud-win-x64-with-dotnet.exe`
4. 先构建和上传 `no-dotnet` 版本，再构建和上传 `with-dotnet` 版本。
5. push 到 `main` 时：
   - 编译两个 exe
   - 上传为 Actions Artifacts
6. push `v*` tag 时：
   - 编译两个 exe
   - 上传为 Actions Artifacts
   - 同时上传两个 exe 到 GitHub Release Assets
7. Release Assets 中也要是 `.exe`，不要 `.zip`。
8. 尽量使用 Node 24 兼容的新版 GitHub Actions，避免 Node.js 20 deprecated warning。

## 术语说明

### no-dotnet 版本

文件名：

```text
CodexQuotaHud-win-x64-no-dotnet.exe
```

含义：

- Framework-dependent
- 不携带 .NET 运行时
- 体积较小
- 用户电脑需要安装 .NET 8 Desktop Runtime x64

对应 publish 参数：

```powershell
--self-contained false
-p:PublishSingleFile=true
```

### with-dotnet 版本

文件名：

```text
CodexQuotaHud-win-x64-with-dotnet.exe
```

含义：

- Self-contained
- 自带 .NET 运行时
- 体积较大
- 用户通常可以直接运行

对应 publish 参数：

```powershell
--self-contained true
-p:PublishSingleFile=true
```

## 文件修改范围

优先修改或创建：

```text
.github/workflows/build-windows.yml
```

如果已有这个文件，请直接修改它，不要新增重复 workflow。

不要修改这些文件：

```text
*.cs
*.csproj
README.md
.gitignore
```

除非 workflow 当前文件名不同，比如 `.github/workflows/release.yml`，那就修改已有负责 Windows 构建的 workflow，避免重复触发。

## 推荐 workflow 内容

请将 Windows 构建 workflow 调整为接近以下内容。

注意：如果项目文件名不是 `CodexQuotaHud.csproj`，请先确认实际 `.csproj` 文件名并替换 `PROJECT_FILE`。

```yaml
name: Build Windows

on:
  push:
    branches: [ main ]
    tags:
      - 'v*'
  pull_request:
    branches: [ main ]
  workflow_dispatch:

permissions:
  contents: write

jobs:
  build:
    name: Build win-x64
    runs-on: windows-latest

    env:
      PROJECT_FILE: .\CodexQuotaHud.csproj
      CONFIGURATION: Release
      RUNTIME_ID: win-x64
      ARTIFACT_DIR: .\artifacts

    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        shell: pwsh
        run: dotnet restore $env:PROJECT_FILE

      - name: Build
        shell: pwsh
        run: dotnet build $env:PROJECT_FILE -c $env:CONFIGURATION --no-restore

      - name: Prepare artifact directory
        shell: pwsh
        run: |
          if (Test-Path $env:ARTIFACT_DIR) {
            Remove-Item $env:ARTIFACT_DIR -Recurse -Force
          }
          New-Item -ItemType Directory -Force -Path $env:ARTIFACT_DIR | Out-Null

      - name: Publish no-dotnet exe first
        shell: pwsh
        run: |
          $out = '.\publish\no-dotnet'
          if (Test-Path $out) {
            Remove-Item $out -Recurse -Force
          }

          dotnet publish $env:PROJECT_FILE `
            -c $env:CONFIGURATION `
            -r $env:RUNTIME_ID `
            --self-contained false `
            -p:PublishSingleFile=true `
            -p:DebugType=none `
            -p:DebugSymbols=false `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $out

          $exe = Get-ChildItem $out -Filter '*.exe' | Select-Object -First 1
          if (-not $exe) {
            throw 'No exe was produced for no-dotnet publish.'
          }

          Copy-Item $exe.FullName "$env:ARTIFACT_DIR\CodexQuotaHud-win-x64-no-dotnet.exe" -Force

      - name: Upload no-dotnet artifact first
        uses: actions/upload-artifact@v7
        with:
          path: .\artifacts\CodexQuotaHud-win-x64-no-dotnet.exe
          archive: false
          if-no-files-found: error

      - name: Publish with-dotnet exe second
        shell: pwsh
        run: |
          $out = '.\publish\with-dotnet'
          if (Test-Path $out) {
            Remove-Item $out -Recurse -Force
          }

          dotnet publish $env:PROJECT_FILE `
            -c $env:CONFIGURATION `
            -r $env:RUNTIME_ID `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:DebugType=none `
            -p:DebugSymbols=false `
            -p:PublishTrimmed=false `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $out

          $exe = Get-ChildItem $out -Filter '*.exe' | Select-Object -First 1
          if (-not $exe) {
            throw 'No exe was produced for with-dotnet publish.'
          }

          Copy-Item $exe.FullName "$env:ARTIFACT_DIR\CodexQuotaHud-win-x64-with-dotnet.exe" -Force

      - name: Upload with-dotnet artifact second
        uses: actions/upload-artifact@v7
        with:
          path: .\artifacts\CodexQuotaHud-win-x64-with-dotnet.exe
          archive: false
          if-no-files-found: error

      - name: Upload no-dotnet exe to GitHub Release first
        if: startsWith(github.ref, 'refs/tags/v')
        uses: softprops/action-gh-release@v3
        with:
          files: .\artifacts\CodexQuotaHud-win-x64-no-dotnet.exe
          fail_on_unmatched_files: true
          generate_release_notes: true

      - name: Upload with-dotnet exe to GitHub Release second
        if: startsWith(github.ref, 'refs/tags/v')
        uses: softprops/action-gh-release@v3
        with:
          files: .\artifacts\CodexQuotaHud-win-x64-with-dotnet.exe
          fail_on_unmatched_files: true
```

## 实现细节要求

### 1. 不要 zip

不要使用：

```powershell
Compress-Archive
```

不要生成：

```text
*.zip
```

最终产物必须是：

```text
CodexQuotaHud-win-x64-no-dotnet.exe
CodexQuotaHud-win-x64-with-dotnet.exe
```

### 2. no-dotnet 必须先处理

workflow 步骤顺序必须是：

```text
Publish no-dotnet
Upload no-dotnet artifact
Publish with-dotnet
Upload with-dotnet artifact
Upload no-dotnet to Release
Upload with-dotnet to Release
```

### 3. 只构建 x64

不要添加 matrix。
不要添加 win-arm64。
不要添加 x86。
不要添加 Linux/macOS。

### 4. Release 仅在 tag 时上传

只有当 tag 类似下面这样时才上传到 Release：

```text
v0.1.0
v0.2.0
v1.0.0
```

普通 push 到 main 只需要上传 Actions Artifacts。

### 5. 保持 workflow_dispatch

保留手动运行入口：

```yaml
workflow_dispatch:
```

这样我可以在 GitHub Actions 页面手动触发编译。

## 验证要求

修改完成后，请执行：

```powershell
dotnet build .\CodexQuotaHud.csproj -c Release
```

如果项目文件不是 `CodexQuotaHud.csproj`，请使用实际项目文件名。

如果本地无法完整模拟 GitHub Actions，也至少要检查：

```text
.github/workflows/build-windows.yml
```

是否存在 YAML 缩进错误。

## 不要做的事情

请不要：

1. 修改任何 C# 功能代码。
2. 修改 HUD UI。
3. 修改额度读取逻辑。
4. 修改设置窗口。
5. 新增安装包、MSI、NSIS、Squirrel。
6. 新增 win-arm64 / x86 / macOS / Linux 构建。
7. 生成 zip。
8. 删除已有 tag 或 release。

## 完成后请汇报

请在完成后告诉我：

1. 修改了哪个 workflow 文件。
2. 是否生成两个 exe：
   - `CodexQuotaHud-win-x64-no-dotnet.exe`
   - `CodexQuotaHud-win-x64-with-dotnet.exe`
3. 是否保持 no-dotnet 在前、with-dotnet 在后。
4. `dotnet build` 是否通过。
5. 如何触发 Release：

```powershell
git tag v0.1.0
git push origin v0.1.0
```
