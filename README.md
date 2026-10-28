# MemSweep

唯讀的 Windows 記憶體監看工具：系統匣顯示實體 RAM 使用率與使用 RAM 的應用程式，並顯示 commit 使用率與 commit 尖峰；另附命令列工具 `memsweep-cli`，讓代理在吃記憶體的工作前先檢查剩餘 commit。

MemSweep 只讀取系統資訊：不清理記憶體、不結束任何程式，也不需要系統管理員權限。需求見 [docs/spec.md](docs/spec.md)，設計見 [docs/design.md](docs/design.md)。

## 需求

- Windows 11 x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（x64）

## 建置與發佈

需要 .NET 10 SDK。在 repo 根目錄執行：

```bash
(cd app && dotnet publish MemSweep.Desktop -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)
(cd app && dotnet publish MemSweep.Cli -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)
```

`MemSweep.exe`、`memsweep-cli.exe` 與共用的 DLL 都會放在 `app/artifacts/publish/MemSweep/`。測試：`(cd app && dotnet test)`。

正式版本以 git tag 標示，例如 `v1.0.0`。要建置某個版本，先執行 `git switch --detach v1.0.0`，再執行上面的指令。

## 安裝

把 `MemSweep` 資料夾整個複製到自己有寫入權限的位置，例如 `D:\Tools\MemSweep`。不要放在 `C:\Program Files` 這類需要系統管理員權限才能寫入的位置：設定與尖峰紀錄會寫在 exe 旁邊的 `data\` 資料夾。

升級時先從系統匣結束 MemSweep，再用新版的檔案覆蓋，保留原本的 `data\`。目前的版本可在 exe 的「內容 → 詳細資料」查看：產品版本例如 `1.0.0+<commit>`，`+` 後面是建置時的 commit。

## 使用

執行 `MemSweep.exe` 後，MemSweep 常駐在系統匣，直到選「結束 MemSweep」。關閉任何視窗都不會結束它；再執行一次 `MemSweep.exe` 會叫出小面板。

- **系統匣圖示**：目前的實體 RAM 使用率（%），和工作管理員「處理程序」頁「記憶體」欄標題的百分比一致；兩邊取整數的方式可能不同，最多差 1。低於橘色門檻為綠色，達到橘色門檻為橘色，達到紅色門檻以上為紅色；預設橘色 70%、紅色 85%。滑鼠停在圖示上會顯示實體 RAM 與 commit 的用量。commit 不會讓圖示變色。
- **左鍵點圖示**：開關小面板，顯示 RAM 與 commit 量表、開機以來的 commit 尖峰、RAM 前五名的應用程式與「系統（不屬於任何程式）」。「複製報告」會把與 `memsweep-cli status` 相同的摘要複製到剪貼簿；摘要以 commit 為主，前五名可能和小面板不同。
- **右鍵選單**：開啟小面板、完整清單…、設定…、結束 MemSweep。
- **完整清單**：「總覽」「程式」「尖峰紀錄」三個分頁。「程式」依 RAM 列出所有應用程式，也顯示 commit，可展開看組成的行程；「尖峰紀錄」列出 commit 的開機尖峰與每日尖峰。
- **設定**：RAM 使用率的橘色與紅色門檻（1～100，橘色必須小於紅色）、尖峰紀錄保留天數（1～3650，預設 90）、開機時自動啟動（預設關閉）。減少保留天數後，超過天數的紀錄會在下一次取樣時刪除。

## 命令列

`memsweep-cli` 不需要 `MemSweep.exe` 在執行，直接向系統查詢，也不讀寫 `data\`。

| 指令 | 說明 |
| --- | --- |
| `memsweep-cli status` | 文字摘要：commit、RAM、開機以來尖峰、前五名與「系統（不屬於任何程式）」；前五名與「系統」以 commit 計 |
| `memsweep-cli status --json` | 相同內容的 JSON，記憶體以 bytes 整數表示 |
| `memsweep-cli check --need <GB>` | 剩餘 commit 是否足夠，`<GB>` 可帶小數，1 GB = 1024³ bytes |
| `memsweep-cli --help` | 用法說明 |

| 結束碼 | 意義 |
| --- | --- |
| 0 | 成功；`check` 時剩餘 commit 足夠 |
| 1 | `check` 時剩餘 commit 不足，並輸出剩餘量與前五名 |
| 2 | 參數錯誤 |
| 3 | 無法取得系統記憶體資訊 |

例如開始吃記憶體的分析前，先確認至少還有 8 GB：

```bash
memsweep-cli check --need 8
```

## 資料檔

`MemSweep.exe` 所在資料夾下的 `data\`，`MemSweep.exe` 啟動時自動建立：

| 檔案 | 內容 |
| --- | --- |
| `settings.json` | 設定；內容不合法時使用預設值，下次儲存時覆寫 |
| `peaks-boot.csv` | 每次開機一筆的 commit 尖峰 |
| `peaks-daily.csv` | 每天一筆的 commit 尖峰 |

CSV 是含 BOM 的 UTF-8，可以直接用 Excel 開啟。MemSweep 執行中用 Excel 開著紀錄檔也沒關係，寫入失敗時會稍後再試。

## 移除

1. 如果開啟過開機自動啟動，先在「設定…」取消勾選並儲存。
2. 從系統匣選「結束 MemSweep」。
3. 刪除整個資料夾。

MemSweep 不會寫入其他位置；開機自動啟動只登記在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `MemSweep` 值。
