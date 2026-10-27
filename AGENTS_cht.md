# MemSweep

> 本檔為 `AGENTS.md` 的繁體中文參考版本，不是指令入口。
> 兩個版本衝突時以 `AGENTS.md` 為準，並回報不一致之處。修改時兩份須在同一次變更中同步更新。

## CodeLab Governance Bootstrap

依 CodeLab 治理規則（`governance_cht.md` 亦同），本區塊保留英文原文。

CodeLab-Governance: optional

This project supports CodeLab-managed and standalone operation.

When `../../AGENTS.md` contains the exact standalone line `CodeLab-Workspace-Root: v1` and the required anchors are available, use CodeLab-managed mode. Before project work, read `../../AGENTS.md` and `../../governance_eng.md`. Claude Code / Cowork also reads `../../CLAUDE.md`. Read `../../governance_cht.md` only for a request for the Chinese rule text, bilingual maintenance, or an inconsistency check.

Otherwise, do not search elsewhere for substitutes. Use standalone mode and project-local rules. Without user authorization, do not read or write outside the project directory. Report the mode and reason in interactive work and non-interactive output.

Change the marker to `CodeLab-Governance: required` when failed managed-mode verification must block project work.

## 專案

MemSweep 是唯讀的 Windows commit 監看工具。系統匣程式顯示 commit 使用率、佔用 commit 的應用程式與 commit 尖峰；命令列工具讓代理在吃記憶體的工作開始前，先檢查剩餘 commit。要做什麼由 `docs/spec.md` 定義，怎麼做由 `docs/design.md` 定義。

- 遠端：`https://github.com/AxeenWang/MemSweep.git`
- 預設分支：`main`

### 技術

- C#、.NET 10。視窗使用 WPF，整個行程固定軟體繪圖；系統匣圖示使用 .NET 內建的 WinForms `NotifyIcon`。
- Win32 一律透過 `LibraryImport` 呼叫，而且只做唯讀查詢。
- 執行時期不依賴第三方套件。開發時期唯一的套件是 MSTest（`MSTest.Sdk`）。
- 以 framework-dependent、win-x64 發佈，兩支 exe 放在同一個資料夾。
- 所有 `dotnet` 指令都在 `app/` 底下執行，因為 `app/global.json` 固定了 SDK、MSTest.Sdk 版本與測試執行器。例如：`(cd app && dotnet build)`。
- 測試：`(cd app && dotnet test)`。只跑單元測試：`(cd app && dotnet test --filter "TestCategory!=Live")`。即時測試會讀取真實系統，全部是唯讀查詢。
- 發佈（framework-dependent、win-x64，兩支 exe 都在 `app/artifacts/publish/MemSweep/`）：`(cd app && dotnet publish MemSweep.Desktop -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)` 與 `(cd app && dotnet publish MemSweep.Cli -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)`。

## 語言

- 文件、程式碼註解與 commit 訊息使用繁體中文（台灣）。
- 識別字一律英文：程式符號、檔案與資料夾名稱、分支名稱。
- commit 主旨只用繁體中文，不加 `feat:`、`fix:` 等類型前綴。

## 文件

工作依三層文件進行，存放在 `docs/`：

1. **SPEC**：要做什麼、為什麼做，包含範圍與非目標。
2. **Design**：怎麼做，包含技術選型、repo 目錄版面與依序排列的 Phase 清單。
3. **Development**：每個 Phase 一份實作計畫，在該 Phase 開工時才撰寫，不預先寫好。

SPEC 核准後才寫 Design，Design 核准後才寫 Development 計畫。Development 計畫要引用所實作的 SPEC 與 Design 條文原文，不得改寫轉述。

## Repo 目錄版面

- repo 根目錄只放規則檔、`README.md`、repo 設定檔與 `docs/`。
- 應用程式碼（專案檔、原始碼、測試與建置產出）放在 `app/`，絕不放在 repo 根目錄。
  - `app/` 內有 `MemSweep.slnx`、`global.json`、`Directory.Build.props`，以及專案 `MemSweep.Core/`、`MemSweep.Desktop/`（產出 `MemSweep.exe`）、`MemSweep.Cli/`（產出 `memsweep-cli.exe`）、`MemSweep.Core.Tests/`。
  - 建置與發佈產出放在 `app/artifacts/`，不進版控。
- Development 計畫放在 `docs/development/`，每個 Phase 一份，命名為 `phase-NN-<主題>.md`。
- 提出資料夾名稱前，先檢查名稱是否和上層或下層意思重複，例如 `src/src`、`app/src/app`。

## 分支與 Pull Request

- 文件（規則檔、SPEC、Design、Development 計畫）直接提交到 `main`。
- 程式碼變更走 Phase 分支與 pull request。
- Phase 分支在該 Phase 開工時才建立，不得預先建立後續 Phase 的分支。各 Phase 嚴格依序進行。
- 每完成一個 Task 就推送。該 Phase 所有 Task 都完成後才開 pull request。
- 何時合併由使用者決定，使用 merge commit 合併。
- 合併完成後必須清理所有分支：刪除本地與遠端的已合併分支，清掉過期的 remote-tracking 參照，並確認本地、remote-tracking 與遠端都只剩 `main`，也沒有殘留的 worktree。
- 不改動 SPEC 與 Design 的小修，經使用者同意後可走 `fix/` 分支。
- 每次推送前先 fetch，確認遠端分支仍存在，且對應的 pull request 沒有被合併或關閉。

## 版本發佈

版本號規則與發佈流程由 Design 3.12 定義。只在使用者要求時才發佈新版本。

- 版本號只在 `app/Directory.Build.props` 的 `Version` 設定一次，格式為「主版本.次版本.修訂」。修正而不改 SPEC 時修訂加一；要改 SPEC 的新功能次版本加一；不相容的改動主版本加一，例如 CLI 的參數、結束碼或 JSON 欄位改變，或舊版寫的 CSV、`settings.json` 無法再讀取。
- 改版本號走 `release/vX.Y.Z` 分支與 pull request。
- 使用者合併後，在 `main` 的該 merge commit 打附註 tag，並只推送這個 tag：`git tag -a vX.Y.Z -m "MemSweep X.Y.Z"` 與 `git push origin vX.Y.Z`。不建立 GitHub Release。
- 把該 commit 發佈到全新、空的 `app/artifacts/release/vX.Y.Z/`：`(cd app && dotnet publish MemSweep.Desktop -c Release -r win-x64 --self-contained false -o artifacts/release/vX.Y.Z)` 與 `(cd app && dotnet publish MemSweep.Cli -c Release -r win-x64 --self-contained false -o artifacts/release/vX.Y.Z)`。
- 正式版放在 repo 外的 `D:\Utility\MemSweep\`，每次寫入都要先取得使用者同意。先由使用者結束執行中的 MemSweep，再把發佈的檔案複製過去，保留既有的 `data\`。`app/artifacts/publish/MemSweep/` 只用於驗收版。

## 系統安全

MemSweep 作用於整台電腦的記憶體狀態，而開發機就是使用者的日常電腦。

- 執行任何會改變系統記憶體狀態的動作，每次都要先取得使用者明確同意。包含修剪其他行程的工作集（working set）、清除待命（standby）或已修改（modified）分頁清單、清空檔案快取，以及啟用 token 權限。
- 未經使用者明確同意，不得啟動提權（系統管理員）行程，也不得變更分頁檔等系統設定。
- 唯讀查詢記憶體狀態與行程清單不受此限。
- 絕不以結束使用者行程的方式釋放記憶體。
