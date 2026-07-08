# Codex 指令：为 CodexQuotaHud 添加 GitHub Actions 自动编译

## 你的身份
你是一个谨慎的代码助手。请在当前仓库 `E:\github\codex-quota-hud` 中为现有 C# WinForms 项目添加 GitHub Actions 自动编译流程。

## 当前项目背景
- 项目类型：C# WinForms 桌面程序
- 目标框架：当前项目实际使用 `net8.0-windows`
- 项目文件：`CodexQuotaHud.csproj`
- 目标平台：Windows x64
- 用户希望：push 到 GitHub 后自动编译，并可以从 GitHub Actions 的 Artifacts 下载 zip 包
- 暂时不需要复杂安装器，不需要自动更新，不需要签名，不需要改应用代码

## 重要限制
请节省上下文和 token。

不要递归读取整个仓库。
不要读取这些目录：

```text
.git/
bin/
obj/
.vs/
.vscode/
node_modules/
artifacts/
publish/
```

本次任务只需要查看：

```text
CodexQuotaHud.csproj
README.md
.gitignore
```

如果这些文件不存在，再根据实际情况处理。

## 本次任务目标

只完成以下内容：

1. 新建 GitHub Actions workflow：

```text
.github/workflows/build-windows.yml
```

2. workflow 需要支持：

```text
push 到 main 分支时自动构建
pull_request 到 main 分支时自动构建
workflow_dispatch 手动触发
```

3. workflow 需要执行：

```text
dotnet restore
dotnet build
dotnet publish
压缩 publish 输出为 zip
上传 zip 到 GitHub Actions Artifacts
```

4. 更新 `.gitignore`，确保忽略本地构建产物。

5. 更新 `README.md`，添加简短的 “Build from GitHub Actions” 说明。

6. 不要修改 C# 功能代码，不要改 UI，不要改额度读取逻辑。

## 推荐 workflow 内容

请创建或覆盖 `.github/workflows/build-windows.yml`，内容如下：

```yaml
name: Build Windows

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]
  workflow_dispatch:

jobs:
  build:
    name: Build win-x64
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore .\CodexQuotaHud.csproj

      - name: Build
        run: dotnet build .\CodexQuotaHud.csproj -c Release --no-restore

      - name: Publish win-x64 self-contained single file
        run: >
          dotnet publish .\CodexQuotaHud.csproj
          -c Release
          -r win-x64
          --self-contained true
          -p:PublishSingleFile=true
          -p:DebugType=none
          -p:DebugSymbols=false
          -o .\publish\CodexQuotaHud-win-x64

      - name: Zip artifact
        shell: pwsh
        run: |
          Compress-Archive -Path .\publish\CodexQuotaHud-win-x64\* -DestinationPath .\CodexQuotaHud-win-x64.zip -Force

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: CodexQuotaHud-win-x64
          path: .\CodexQuotaHud-win-x64.zip
```

## .gitignore 要求

如果 `.gitignore` 里还没有这些规则，请追加：

```gitignore
# .NET build outputs
bin/
obj/

# Local publish outputs
publish/
artifacts/
*.zip

# Visual Studio / editor
.vs/
.vscode/
.idea/

# User/local files
*.user
*.suo
*.log
```

不要删除已有规则。

## README.md 要求

请在 README.md 中追加一个简短章节：

```markdown
## GitHub Actions Build

This repository includes a Windows build workflow.

On every push to `main`, pull request to `main`, or manual workflow dispatch, GitHub Actions will:

1. restore dependencies,
2. build the project,
3. publish a self-contained Windows x64 single-file build,
4. upload `CodexQuotaHud-win-x64.zip` as a workflow artifact.

To download a build:

1. open the repository on GitHub,
2. go to **Actions**,
3. open the latest **Build Windows** run,
4. download the `CodexQuotaHud-win-x64` artifact.
```

如果 README 已经有类似内容，请合并，不要重复堆叠。

## 本地验证命令

修改完成后，请运行：

```powershell
dotnet build .\CodexQuotaHud.csproj -c Release
```

如果本机可用，也可以运行：

```powershell
dotnet publish .\CodexQuotaHud.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o .\publish\CodexQuotaHud-win-x64
```

如果 publish 成功，请不要把 `publish/` 或 zip 文件提交到 git。

## 交付说明

完成后请告诉用户：

1. 修改了哪些文件。
2. 本地 `dotnet build` 是否通过。
3. 如果已本地 publish，publish 是否通过。
4. 用户接下来需要执行的 git 命令：

```powershell
git add .github\workflows\build-windows.yml .gitignore README.md
git commit -m "Add Windows GitHub Actions build"
git push
```

5. push 后如何在 GitHub 下载 artifact：

```text
GitHub 仓库页面 -> Actions -> Build Windows -> 最新运行 -> Artifacts -> CodexQuotaHud-win-x64
```

## 不要做的事情

本次不要做：

```text
不要创建 Release 自动发布
不要添加安装器
不要代码签名
不要自动更新
不要改应用版本号机制
不要修改 C# UI 或业务逻辑
不要执行 git commit / git push，除非用户明确要求
```

