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

MemSweep 是 Windows 記憶體（RAM）工具。功能範圍、功能清單與技術選型尚未決定，將在 SPEC 與 Design 文件中定義；Design 核准後，本檔會補上技術選型、指令與目錄版面。

- 遠端：`https://github.com/AxeenWang/memsweep.git`
- 預設分支：`main`

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
- 應用程式碼（專案檔、原始碼、測試與建置產出）放在 Design 指定的單一專屬子資料夾，絕不放在 repo 根目錄。
- 提出資料夾名稱前，先檢查名稱是否和上層或下層意思重複，例如 `src/src`、`app/src/app`。

## 分支與 Pull Request

- 文件（規則檔、SPEC、Design、Development 計畫）直接提交到 `main`。
- 程式碼變更走 Phase 分支與 pull request。
- Phase 分支在該 Phase 開工時才建立，不得預先建立後續 Phase 的分支。各 Phase 嚴格依序進行。
- 每完成一個 Task 就推送。該 Phase 所有 Task 都完成後才開 pull request。
- 何時合併由使用者決定，使用 merge commit 合併。
- 不改動 SPEC 與 Design 的小修，經使用者同意後可走 `fix/` 分支。
- 每次推送前先 fetch，確認遠端分支仍存在，且對應的 pull request 沒有被合併或關閉。

## 系統安全

MemSweep 作用於整台電腦的記憶體狀態，而開發機就是使用者的日常電腦。

- 執行任何會改變系統記憶體狀態的動作，每次都要先取得使用者明確同意。包含修剪其他行程的工作集（working set）、清除待命（standby）或已修改（modified）分頁清單、清空檔案快取，以及啟用 token 權限。
- 未經使用者明確同意，不得啟動提權（系統管理員）行程，也不得變更分頁檔等系統設定。
- 唯讀查詢記憶體狀態與行程清單不受此限。
- 絕不以結束使用者行程的方式釋放記憶體。
