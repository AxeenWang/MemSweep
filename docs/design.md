# MemSweep Design

> 狀態：已核准（2026-10-26）
> 日期：2026-10-26
> 修訂：2026-10-26 依 Phase 01 實測修訂 1 的 JSON 列、3.3（公司名稱比對方式、殼層與系統清單）、3.11 文字摘要範例與第 6 章風險 1（隨 Phase 01 計畫核准）；2026-10-26 依 Phase 01 整體審查補上 3.3 的孤兒輔助行程規則與輔助行程清單；2026-10-26 隨 Phase 02 計畫修訂 3.3 步驟 6 用字、3.6 小面板的建立時機與位置、3.7 前景許可、3.11 結束碼 3、第 4 章 A-07 清單與第 6 章風險 3；2026-10-26 隨 Phase 03 計畫修訂第 3 章架構圖、3.1 元件表、3.6 完整視窗、程式圖示與自訂標題列、3.9 停用條件與寫入失敗、3.10 重新啟動時接續、第 5 章 Phase 03 列與第 6 章風險 3；2026-10-27 隨 Phase 04 計畫修訂 3.1 元件表、3.6 設定視窗、3.8 儲存與失敗處理、3.9 設定檔格式、紀錄檔範圍檢查、資料夾重建與保留天數生效時機，新增 3.12 發佈；2026-10-27 依 Phase 04 整體審查補上 3.8 登錄操作失敗後的重試
> 依據：[SPEC](spec.md)（2026-10-26 核准）。條文編號（F-、R-、A-、X-）沿用 SPEC。
>
> 標示「〔使用者決定〕」的是使用者在討論中明確選擇的項目，其餘是 Claude 的提案，核准本文件即視為採用。

## 1. 技術選型

| 項目 | 選擇 | 理由 |
| --- | --- | --- |
| 語言與框架 | C#、.NET 10 | 使用者熟悉 C#〔使用者決定〕。.NET 10 是目前的 LTS，支援到 2028 年 11 月；本機已有 SDK 10.0.401 與 Desktop Runtime 10.0.12 |
| 介面 | WPF，整個行程固定使用軟體繪圖 | 〔使用者決定〕依 1.1 的量測選定 |
| 系統匣 | .NET 內建的 WinForms `NotifyIcon`（專案同時開 `UseWPF`、`UseWindowsForms`） | 不需第三方套件 |
| Win32 呼叫 | `LibraryImport`（專案需開 `AllowUnsafeBlocks`） | 編譯時產生封送程式碼，與裁剪相容 |
| JSON | `System.Text.Json`：報告以 `Utf8JsonWriter` 逐欄寫出，設定檔用原始碼產生 | .NET 內建 |
| CSV | 自行撰寫的簡單寫入與讀取 | 欄位固定，不值得引入套件 |
| 命令列參數 | 自行解析 | 只有三種指令，不引入 `System.CommandLine` |
| 測試 | MSTest（`MSTest.Sdk`） | 微軟官方，只在開發時使用 |
| 方案檔 | `MemSweep.slnx` | .NET 10 預設格式，本機 Visual Studio 2026（18.10）可開啟 |
| 發佈 | framework-dependent、win-x64，兩支 exe 發佈到同一個資料夾 | exe 只有幾 MB |

執行時期不依賴任何第三方套件。開發時期唯一額外的套件是 MSTest。

### 1.1 介面技術的量測依據

2026-10-26 以拋棄式原型（WPF 應用程式 + `NotifyIcon`，開啟一個含 50 筆項目的清單視窗後關閉）量測行程本身的私有 commit：

| 做法 | 只有系統匣 | 開過視窗後 |
| --- | --- | --- |
| WPF 預設硬體加速 | 13 MB | 140~160 MB，關閉視窗後不會下降 |
| WPF 軟體繪圖 | 13 MB | 約 77 MB |
| WinForms 視窗 | 13 MB | 約 17 MB |

硬體加速時，顯示卡驅動程式（本機為 Intel 內顯與 RTX 5070 Ti）會在行程內配置記憶體，超過 X-1 的 100 MB 目標。MemSweep 每 2 秒只更新少量數字與一張表，軟體繪圖足以應付。77 MB 是空白清單的量測值，真實介面可能更高，因此每個含介面的 Phase 都要量 A-03（見第 6 章風險 3）。

## 2. Repo 目錄版面

```text
MemSweep/                      repo 根目錄
├─ AGENTS.md、AGENTS_cht.md、CLAUDE.md、README.md
├─ .gitignore、.gitattributes
├─ docs/
│  ├─ spec.md
│  ├─ design.md
│  └─ development/             每個 Phase 一份實作計畫，開工時才寫
└─ app/                        所有程式碼、測試與建置產出
   ├─ MemSweep.slnx
   ├─ global.json              固定 SDK 10.0.x
   ├─ Directory.Build.props    共用設定：TargetFramework、Nullable、artifacts 輸出
   ├─ MemSweep.Core/           類別庫：系統讀取、歸組、尖峰、報告、設定
   ├─ MemSweep.Desktop/        WPF 常駐程式，產出 MemSweep.exe
   ├─ MemSweep.Cli/            主控台程式，產出 memsweep-cli.exe
   ├─ MemSweep.Core.Tests/     單元測試與即時系統測試
   └─ artifacts/               建置與發佈產出，不進版控
```

- 程式碼資料夾用 `app/`。專案名稱用 `MemSweep.Desktop` 而不是 `MemSweep.App`，避免 `app/MemSweep.App` 意思重複；exe 名稱由 `AssemblyName` 指定。
- 建置產出統一放在 `app/artifacts/`（`UseArtifactsOutput`），各專案資料夾內不會出現 `bin/`、`obj/`。
- Phase 01 在 `.gitignore` 加入 `/app/artifacts/`、`.vs/`、`*.user`。
- Development 計畫命名為 `docs/development/phase-NN-<主題>.md`。

## 3. 架構

```text
MemSweep.Core
  SystemReader ──▶ SystemSnapshot ──▶ AppGrouper ──▶ GroupedSnapshot ──┬──▶ ReportFormatter（文字／JSON）
  ProcessInfoCache（每個行程只查一次路徑、檔案描述、公司）                ├──▶ HeadroomCheck（check 結束碼）
                                                                        └──▶ PeakRecorder ──▶ PeakTracker、PeakStore（CSV）
  SettingsStore（JSON）

MemSweep.Desktop：每 2 秒取樣 ──▶ 系統匣圖示、小面板、完整視窗；PeakTracker；單一執行個體；開機啟動
MemSweep.Cli：單次取樣 ──▶ status／status --json／check
```

### 3.1 MemSweep.Core 元件

| 元件 | 職責 | SPEC |
| --- | --- | --- |
| `Native/` | 所有 `LibraryImport` 宣告，只放唯讀查詢 | E-5、A-07 |
| `SystemReader`（介面 `ISystemReader`） | 產生 `SystemSnapshot`：時間、開機時間、commit 已用／上限／尖峰、實體 RAM、分頁檔大小、所有行程（PID、父 PID、建立時間、映像名稱、私有 commit、工作集） | F-02、F-05、F-11 |
| `ProcessInfoCache` | 以（PID、建立時間）為鍵，快取執行檔路徑、檔案描述、公司名稱與 node 套件標籤；行程結束後移除 | 第 8 章 |
| `AppGrouper` | 依 3.3 的演算法，把快照轉成依 commit 排序的應用程式清單與「系統（不屬於任何程式）」 | 第 8 章、F-23 |
| `ReportFormatter` | 文字摘要與 JSON | F-40、F-41、F-12 |
| `HeadroomCheck` | 計算剩餘 commit，回傳結束碼與輸出內容 | F-42 |
| `PeakTracker` | 追蹤開機尖峰與每日尖峰，保存全部尖峰紀錄並做保留天數清理 | F-30～F-33 |
| `PeakCsv`、`PeakStore` | CSV 格式；`data\` 裡紀錄檔的讀寫 | F-34、F-52 |
| `PeakRecorder` | 以每次取樣更新 `PeakTracker`，依 3.9 的時機寫入 `PeakStore`；取樣執行緒與結束時共用 | F-33、F-34 |
| `AppSettings`、`SettingsStore` | 設定值、預設值與驗證；讀寫 `settings.json` | F-50、F-52 |
| `SettingsForm` | 設定視窗的輸入內容與檢查 | F-50 |

### 3.2 使用的 Win32 API

全部是唯讀查詢，一般使用者權限即可。

| 用途 | API |
| --- | --- |
| commit 已用、上限、開機以來尖峰；實體 RAM | `K32GetPerformanceInfo` |
| 開機時間 | `NtQuerySystemInformation(SystemTimeOfDayInformation)` |
| 所有行程的 PID、父 PID、建立時間、映像名稱、私有 commit、工作集 | `NtQuerySystemInformation(SystemProcessInformation)` |
| 分頁檔大小 | `NtQuerySystemInformation(SystemPageFileInformation)` |
| 執行檔完整路徑 | `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageNameW` |
| 檔案描述與公司名稱 | `System.Diagnostics.FileVersionInfo`（以路徑快取） |
| node 套件標籤 | `NtQueryInformationProcess(ProcessCommandLineInformation)` |

- 行程 commit 採用 `SYSTEM_PROCESS_INFORMATION` 的 `PagefileUsage`（即 `PrivateUsage`），預期與工作管理員「認可大小」一致；Phase 01 以 A-02 驗證。
- 受保護的行程開不了控制代碼時，路徑、描述、公司視為取不到，不重試。
- 「系統（不屬於任何程式）」= commit 已用 − 所有行程 `PagefileUsage` 的總和，小於 0 時以 0 計。

### 3.3 應用程式歸組演算法（實作 SPEC 第 8 章）

**清單**（R-7，實作時可依實測調整）：

- 殼層與系統行程（往上追到它們就停止）：`Idle`、`System`、`Secure System`、`Registry`、`Memory Compression`、`smss.exe`、`csrss.exe`、`wininit.exe`、`winlogon.exe`、`services.exe`、`svchost.exe`、`userinit.exe`、`explorer.exe`、`sihost.exe`。
- 輔助行程（R-2，一律歸屬啟動者）：`cmd.exe`、`conhost.exe`、`OpenConsole.exe`、`pwsh.exe`、`powershell.exe`、`bash.exe`、`sh.exe`、`node.exe`、`node_repl.exe`、`bun.exe`、`python.exe`、`pythonw.exe`、`uv.exe`、`uvx.exe`、`msedgewebview2.exe`。

**「獨立應用程式」的判斷**（R-1 第三點）：行程不是輔助行程，而且它和父行程的執行檔公司名稱（`FileVersionInfo.CompanyName`）第一個單字不同，或任一方取不到公司名稱。第一個單字是去頭尾空白後，開頭連續的字母或數字，比較時不分大小寫。

| 範例 | 判斷結果 |
| --- | --- |
| Steam（Valve Corporation）啟動的遊戲 | 第一個單字不同，遊戲自成一列 |
| 終端機（Microsoft Corporation）裡的 pwsh（輔助）啟動的 Claude Code（Anthropic PBC） | 自成一列 |
| IDE 偵錯啟動、沒有公司名稱的修改器 | 取不到公司，自成一列 |
| Codex 桌面版（OpenAI OpCo, LLC）啟動、沒有公司名稱的 `codex.exe` | 取不到公司，自成一列〔使用者決定〕 |
| Claude 桌面版（Anthropic）啟動的工作階段（Anthropic PBC） | 第一個單字相同，併入 Claude |
| Claude 工作階段啟動的 node MCP | 輔助行程，併入 Claude |

**步驟**：

1. 父行程存在且建立時間不晚於子行程，才視為有效父行程（R-3）。
2. 對每個行程往上追：沒有有效父行程、父行程在殼層與系統清單中、父行程是孤兒輔助行程、或目前行程符合「獨立應用程式」，就以目前行程為最上層；否則改看父行程，繼續往上。結果以 PID 記憶，避免重算。
3. 以最上層行程的執行檔路徑（取不到時用映像名稱）為分組鍵，路徑相同的最上層行程自然合併成一列（R-5）。
4. 最上層執行檔位於 `%SystemRoot%` 之下，或取不到路徑而名稱在殼層與系統清單中的，全部併入「Windows（系統與服務）」（R-6）。
5. 名稱取最上層執行檔的檔案描述，取不到時用執行檔名稱（R-4）。
6. 展開明細（F-23）依執行檔名稱合併計數；node 行程再依命令列取出的套件標籤分開，例如命令列含 `@playwright/mcp` 就標為「node：@playwright/mcp」，取不到時維持執行檔名稱「node.exe」。

**實測**（2026-10-26）：Claude 桌面版的公司名稱是 `Anthropic`，工作階段（Claude Code）是 `Anthropic PBC`。原本「完整名稱相同」的判斷會把工作階段拆出去，因此改比第一個單字。`Idle`、`Secure System`、`Memory Compression` 沒有執行檔路徑，加進殼層與系統清單後，才會依 R-6 併入「Windows（系統與服務）」。

**孤兒輔助行程**（R-1 第二點「父行程已不存在」的解讀）：輔助行程不是應用程式。一個輔助行程往上都是輔助行程，最後父行程不存在或是殼層與系統行程時，代表啟動它的應用程式已不存在，它就是孤兒輔助行程，它的子孫各自成為最上層。例如 Claude 或 Codex 結束後留下的 `cmd.exe → node.exe`，node 依 R-5 併入 node 那一列，不會跟著 `cmd.exe` 進入「Windows（系統與服務）」；從檔案總管開啟的 pwsh 底下的程式也各自成列。這條規則與 `bash.exe`、`sh.exe`、`node_repl.exe` 是 Phase 01 整體審查後補上的：MCP 常以 `cmd /c npx` 啟動，而 `node_repl.exe` 在 2026-10-26 的實機上有 12 個。

### 3.4 取樣（MemSweep.Desktop）

- 背景執行緒以 `PeriodicTimer` 每 2 秒取樣一次（F-04），完成後透過 `Dispatcher` 交給 UI 更新，避免查詢新行程的資訊時卡住介面。
- 每次取樣：`SystemReader` → `AppGrouper` → `PeakTracker`，結果發布給系統匣圖示與開著的視窗。
- 取樣迴圈因程式錯誤結束時，以 `Dispatcher.BeginInvoke` 在 UI 執行緒重新拋出，讓程式明顯結束。不可用 `Dispatcher.InvokeAsync`：它會把例外收進回傳的 Task，沒有人觀察就消失，圖示會停在舊數字（Phase 02 整體審查發現）。
- 系統匣與視窗的更新、顯示小面板的要求與主題切換，同樣以 `Dispatcher.BeginInvoke` 交給 UI 執行緒，出錯時才會明顯結束，而不是悄悄停在舊資料（Phase 03 整體審查發現）。

### 3.5 系統匣圖示

- 以 `System.Drawing` 依目前 DPI 的小圖示尺寸繪製數字圖示（F-02、F-03）。使用率 100% 時以較小字型畫出「100」。
- 每次更新後釋放舊圖示的 `HICON`，避免控制代碼累積。
- 提示文字（F-05）範例：`MemSweep` / `Commit 31.4 / 39.4 GB（80%）` / `RAM 20.5 / 31.4 GB`。
- 右鍵選單（F-07）的項目隨 Phase 逐步出現：Phase 02 有「開啟小面板」「結束 MemSweep」，Phase 03 加「完整清單…」，Phase 04 加「設定…」。

### 3.6 視窗

- `App.OnStartup` 在建立任何視窗前設定 `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly`。
- 小面板（F-10～F-12）：無邊框、不顯示在工作列、最上層；出現在主螢幕工作區的右下角（Windows 11 的通知區域固定在主螢幕底部的工作列）；失去焦點或按 Esc 時隱藏。視窗在第一次開啟時才建立，之後以顯示／隱藏切換（量測顯示關閉視窗也不會釋放記憶體，隱藏可讓重開更快）。Phase 02 實測：只掛在系統匣時行程約 18 MB，若啟動時就建立小面板約 58 MB，開過小面板後約 80 MB。
- 小面板開著時點系統匣圖示，焦點會先移走讓小面板隱藏；隱藏後 400 毫秒內的同一次點擊不再把它打開，所以點圖示可以開關小面板。
- 完整視窗（F-20～F-24）：一般視窗，三個分頁。第一次開啟時才建立，只在顯示時更新。關閉時真正關閉，並在 UI 空閒時以 `GCCollectionMode.Aggressive` 回收 MemSweep 自己的記憶體，下次開啟再建立：隱藏的視窗會保留整個畫面樹，Phase 03 原型實測隱藏後仍約 109 MB，關閉並回收後穩定在約 86 MB。「程式」分頁用 `ItemsControl`，每 2 秒原地更新各列而不整份重建；展開狀態保存在顯示模型，關閉再開仍在。
- 程式圖示（F-62）：`MemSweep.Desktop/Assets/MemSweep.ico`，深藍圓角方塊上的 240 度 commit 量表，綠、橘、紅三段依預設門檻 70%、85% 分段（配色同深色主題），白色指針指向 80%。含 16、20、24、32、40、48、64、256 px 八格，每格都是 PNG。兩支 exe 以 `ApplicationIcon` 內嵌，自訂標題列依 DPI 取用最接近的一格；系統匣仍顯示數字（F-02）。
- 自訂標題列（F-62）：視窗以 `WindowChrome` 取代系統標題列（`CaptionHeight` 32、`GlassFrameThickness` 0），拖曳、雙擊最大化、邊緣調整大小、右鍵系統選單與貼齊仍由系統處理。`TitleBar` 顯示圖示、視窗標題與最小化、最大化（還原）、關閉三個按鈕（Segoe Fluent Icons 字型），顏色跟著深淺色，滑過關閉按鈕時為紅底白字，視窗不在前景時標題變淡。最大化時內容往內縮 `SM_CXSIZEFRAME` + `SM_CXPADDEDBORDER`（WPF 的 `SystemParameters` 不含邊框間距），標題列才不會超出螢幕。不提供 Windows 11 滑過最大化按鈕時的貼齊版面選單，可用 Win+Z 或拖曳到螢幕邊緣。完整視窗的捲軸也改成跟著深淺色的細捲軸。設定視窗（Phase 04）沿用同一個 `TitleBar`。
- 設定視窗（F-50）：橘色門檻、紅色門檻、開機自動啟動、保留天數。驗證規則：1 ≤ 橘色 < 紅色 ≤ 100；保留天數 1～3650。
  - 數字用輸入框，關閉輸入法，注音模式下也直接打出半形數字；貼上的全形數字也接受。按「儲存」（Enter）時才檢查，不合法時在按鈕上方顯示第一個錯誤，視窗不關閉；「取消」（Esc）不儲存就關閉。
  - 固定大小，沿用 `TitleBar`，只顯示關閉按鈕：`TitleBar` 依視窗的 `ResizeMode` 隱藏最小化與最大化按鈕。
  - 和完整視窗一樣，第一次開啟時才建立，關閉時真正關閉並在 UI 空閒時回收記憶體；開著時再選「設定…」只會把它帶到前面。
  - 儲存成功後立即套用：系統匣圖示、小面板與完整視窗以最近一次取樣改用新的門檻；保留天數從下一次取樣開始生效（3.9）。
- 深淺色（F-61）：自備淺色與深色兩組 `ResourceDictionary`，依 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` 的 `AppsUseLightTheme` 切換，並監聽 `SystemEvents.UserPreferenceChanged`。不使用 WPF 仍在實驗階段的 `ThemeMode`。
- 「複製報告」（F-12）使用 `ReportFormatter` 的文字摘要。

### 3.7 單一執行個體（F-60）

- 以具名 Mutex `Local\MemSweep.SingleInstance` 判斷是否已有執行個體。
- 第二個執行個體設定具名事件 `Local\MemSweep.ShowPanel` 後立即結束；第一個執行個體收到事件就顯示小面板。
- 第二個執行個體是使用者剛啟動的前景行程，設定事件前先呼叫 `AllowSetForegroundWindow(ASFW_ANY)` 把前景許可讓出，第一個執行個體的小面板才拿得到焦點。

### 3.8 開機自動啟動（F-51）

- 開啟時在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 寫入值 `MemSweep`，內容是加上引號的 `MemSweep.exe` 完整路徑；關閉時刪除該值。
- 設定為開啟時，每次啟動都檢查路徑；exe 被移動過就更新。
- 登錄操作包在 `IAutoStartRegistry` 介面後面。測試使用假的實作；開發期間不在使用者電腦上實際切換（AGENTS.md「System Safety」）。
- 儲存設定時先寫入 `settings.json`，成功後才套用新設定；開機自動啟動的開關有改變時才寫入或刪除登錄值，只改其他設定時不動登錄（例如另一份 MemSweep 登記的值）。上一次登錄操作失敗時，下一次儲存也會重試，不會因為開關沒有改變就回報成功（Phase 04 整體審查發現）。
- 寫入 `settings.json` 失敗時設定維持不變，設定視窗顯示「無法寫入 settings.json，設定沒有儲存」。登錄操作失敗時新設定照樣生效，設定視窗顯示「設定已儲存，但無法變更開機自動啟動」。啟動時的路徑檢查失敗不顯示，下次啟動再試。

### 3.9 資料檔（F-34、F-52）

所在位置：`MemSweep.exe` 所在資料夾下的 `data\`，不存在時自動建立。無法寫入時，小面板顯示「data 資料夾無法寫入，尖峰紀錄已停用」，其他功能照常。

既有的紀錄檔無法讀取或格式不符時（例如在 Excel 另存成其他編碼），同樣停用尖峰紀錄，小面板顯示「peaks-daily.csv 無法讀取，尖峰紀錄已停用」（依實際檔名），而且不覆寫該檔。停用時仍在記憶體中追蹤，小面板與完整視窗照常顯示本次執行期間的尖峰。時間換算成 UTC 會超出範圍（例如台北時間 0001-01-01 凌晨）或 GB 數值大於 1048576（1 PB）時，也視為格式不符，不讓 MemSweep 啟動時當掉。

`settings.json`：

```json
{
  "orangeThreshold": 70,
  "redThreshold": 85,
  "autoStart": false,
  "retentionDays": 90
}
```

檔案不存在或內容不合法時使用預設值，並在下次儲存時覆寫。四個欄位都必須存在且型別正確，任何一項不符合或違反 3.6 的驗證規則，就整份使用預設值；不認得的欄位略過。檔案是不含 BOM 的 UTF-8、CRLF 換行，和紀錄檔一樣先寫暫存檔再取代。

尖峰 CSV：含 BOM 的 UTF-8、CRLF 換行。欄位名稱用中文，方便在 Excel 閱讀；記憶體以 GB 表示，保留兩位小數。

| 檔案 | 欄位 |
| --- | --- |
| `peaks-boot.csv` | 開機時間、尖峰時間、commit已用GB、commit上限GB、第1名…第5名、第1名GB…第5名GB |
| `peaks-daily.csv` | 日期、尖峰時間、commit已用GB、commit上限GB、第1名…第5名、第1名GB…第5名GB |

- 時間格式為本機時間 `yyyy-MM-dd HH:mm:ss`；未知時填「未知」。
- 寫入時機：尖峰有變動就標記，最多每 60 秒寫一次，正常結束時再寫一次。先寫暫存檔並寫入磁碟後再取代原檔，異常結束最多遺失 60 秒內的更新；只寫進系統快取就取代的話，斷電後原檔可能整份是 0 而無法讀取（Phase 03 整體審查發現）。
- 寫入失敗（例如 Excel 開著檔案）時不顯示警告，保留變動，到下一個寫入時機再試。寫入時 `data\` 不存在（例如執行中被刪除）就重新建立。
- 保留天數清理：啟動時與跨日時執行，刪除尖峰時間（或日期）早於保留天數的列（F-33）。在設定視窗改了保留天數時，下一次取樣就依新的天數清理。

### 3.10 尖峰追蹤（F-30～F-32）

- **開機尖峰**：以開機時間識別。每次取樣讀取 Windows 的開機以來尖峰：
  - 數值比目前紀錄高，代表尖峰發生在上次取樣之後（最多 2 秒前），就以本次取樣的時間與前五名作為該筆紀錄。這是近似值。
  - MemSweep 啟動時，如果 Windows 的尖峰高於這次開機已有的紀錄（或還沒有紀錄），代表尖峰發生在 MemSweep 未執行期間，發生時間與前五名都記為「未知」（F-31）。
- **每日尖峰**：只看取樣到的 commit 已用；跨過本機午夜就開新的一筆（F-32）。
- Windows 的「快速啟動」會讓「關機再開機」沿用同一次開機，所以一筆開機尖峰可能跨好幾天。介面一律顯示開機時間，方便辨認。
- MemSweep 在同一次開機或同一天內重新啟動時，接續 CSV 裡的紀錄：開機時間相差 60 秒以內視為同一次開機；Windows 的尖峰比紀錄高出不到 0.005 GB（CSV 兩位小數的一半）視為同一個尖峰，沿用發生時間與前五名。

### 3.11 命令列（F-40～F-43）

| 指令 | 行為 |
| --- | --- |
| `memsweep-cli status` | 文字摘要 |
| `memsweep-cli status --json` | JSON |
| `memsweep-cli check --need <GB>` | 剩餘 commit ≥ 指定值時結束碼 0；不足時結束碼 1，並輸出剩餘量與前五名 |
| 無參數、`--help` | 用法說明，結束碼 0 |
| 參數錯誤 | 錯誤訊息與用法，結束碼 2 |
| 無法取得系統記憶體資訊 | 錯誤訊息寫到 stderr，不印堆疊，結束碼 3 |

- `<GB>` 可帶小數，必須大於 0。全工具的 1 GB 都是 1024³ bytes，與工作管理員一致。
- 輸出使用 UTF-8。
- 文字摘要範例：

  ```text
  MemSweep  2026-10-26 14:35:02
  Commit  31.4 / 39.4 GB（80%），剩 8.0 GB
  RAM     20.5 / 31.4 GB（65%）
  尖峰    36.8 GB（開機以來）
  前五名
    Claude  36 個行程  3.2 GB
    ChatGPT  13 個行程  2.2 GB
    …
  系統（不屬於任何程式）  6.9 GB
  ```

  中文字在終端機裡佔兩格，對不齊欄位，所以名稱與數字之間固定隔兩個空格，不做欄位對齊。

- JSON 內容與文字摘要相同（前五名，不是全部應用程式），記憶體以 bytes 整數表示：

  ```json
  {
    "timestamp": "2026-10-26T14:35:02+08:00",
    "commit": { "usedBytes": 0, "limitBytes": 0, "freeBytes": 0, "peakBytes": 0 },
    "physical": { "usedBytes": 0, "totalBytes": 0 },
    "topApps": [ { "name": "Claude", "processCount": 36, "commitBytes": 0 } ],
    "unattributedBytes": 0
  }
  ```

- 不讀寫 `data\`（F-43）。

### 3.12 發佈

- 在 repo 根目錄執行：

  ```bash
  (cd app && dotnet publish MemSweep.Desktop -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)
  (cd app && dotnet publish MemSweep.Cli -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)
  ```

- 兩支 exe、共用的 `MemSweep.Core.dll` 與除錯符號（pdb）都在 `app/artifacts/publish/MemSweep/`，整個資料夾複製到使用者有寫入權限的位置就能使用（E-3）。不做安裝程式（N-6），也不做單一檔案發佈：兩支 exe 共用同一份 Core。
- README 說明需求、建置與發佈、安裝、使用、命令列、資料檔與移除步驟。移除前要先關閉開機自動啟動，否則 Run 機碼會留下指向已刪除 exe 的值。

## 4. 測試策略

- **單元測試**（`MemSweep.Core.Tests`，用合成快照，不碰真實系統）：
  - `AppGrouper`：R-1～R-6 每一條與 A-09 的五個範例。
  - `PeakTracker`：開機尖峰、每日尖峰、跨日、「未知」的情況。
  - `PeakStore`：CSV 往返、BOM、CRLF、保留天數清理。
  - `SettingsStore`：預設值、不合法內容、門檻驗證。
  - `ReportFormatter`、`HeadroomCheck`（A-04）、命令列參數解析。
- **即時系統測試**（標記 `Live` 類別，唯讀）：`SystemReader` 取得的數值合理；commit 已用與 `Win32_OperatingSystem` 的數值相差在容許範圍內，作為 A-01 的自動化替代。
- **A-07 自動檢查**：以反射列出 Core、Desktop、Cli 組件中所有 P/Invoke 進入點，確認沒有 `EmptyWorkingSet`、`SetProcessWorkingSetSize`、`SetProcessWorkingSetSizeEx`、`NtSetSystemInformation`、`SetSystemFileCacheSize`、`AdjustTokenPrivileges`、`TerminateProcess`、`NtTerminateProcess`、`NtSetInformationProcess`、`VirtualFreeEx`。這項檢查只涵蓋以 `DllImport`／`LibraryImport` 宣告的呼叫。
- **人工驗收**（各 Phase 結束時）：A-01 對照工作管理員、A-02、A-03 常駐 1 小時量測、A-05 以 Excel 開啟、A-06、A-08。
- 任何測試都不得改變系統記憶體狀態或實際寫入開機啟動登錄（AGENTS.md「System Safety」）。

## 5. Phase 切分

各 Phase 嚴格依序進行。每個 Phase 開工時才建立分支並撰寫 Development 計畫，完成後開一個 PR。

| Phase | 分支 | 內容 | 涵蓋的 SPEC 條文 |
| --- | --- | --- | --- |
| 01 核心與命令列 | `phase-01-core-cli` | 建立 `app/` 方案與共用設定、更新 `.gitignore`；Core 的 `SystemReader`、`ProcessInfoCache`、`AppGrouper`、`ReportFormatter`、`HeadroomCheck`；`memsweep-cli` 三種指令；單元、即時與 A-07 測試；確定建置與測試指令並寫入 AGENTS.md | F-40～F-43、第 8 章、A-01、A-02、A-04、A-06、A-07、A-09 |
| 02 系統匣與小面板 | `phase-02-tray-flyout` | MemSweep.Desktop：軟體繪圖、數字圖示與提示、右鍵選單（開啟小面板、結束）、小面板、複製報告、單一執行個體、深淺色；`PeakTracker`（只在記憶體中，供小面板顯示開機尖峰）；門檻先用預設值 | F-01～F-07（選單部分）、F-10～F-12（不含「完整清單…」）、F-31、F-60、F-61、A-03、A-08 |
| 03 尖峰紀錄與完整視窗 | `phase-03-peaks-window` | `PeakStore` 與 CSV、寫入節流、保留天數清理（先用預設 90 天）；完整視窗三個分頁；選單與小面板加「完整清單…」；程式圖示與自訂標題列 | F-20～F-24、F-30～F-34、F-52（尖峰紀錄部分）、F-62、A-03、A-05 |
| 04 設定與發佈 | `phase-04-settings-release` | `SettingsStore` 與設定視窗、門檻與保留天數改讀設定、開機自動啟動、選單加「設定…」；發佈指令與 README 使用說明；完整驗收 A-01～A-09 | F-50～F-52、F-07（設定）、A-01～A-09 |

Phase 01 完成後，Claude 就能先用 `memsweep-cli check` 檢查剩餘 commit，不必等常駐程式完成。

## 6. 風險與驗證

1. **公司名稱判斷不如預期**：Claude 桌面版與工作階段的執行檔公司名稱如果不同或缺漏，A-09 第三個範例會失敗。Phase 01 用實機快照驗證；不符時改用其他判斷方式（例如安裝根目錄），並先回報再修改本文件。2026-10-26 已驗證：兩者是 `Anthropic` 與 `Anthropic PBC`，已改為比對第一個單字（3.3）。
2. **行程 commit 的口徑**：`PagefileUsage` 若與工作管理員「認可大小」不一致，A-02 會失敗。Phase 01 驗證。2026-10-26 已驗證：546 個行程的 `PagefileUsage` 與私有 commit 完全一致。
3. **介面記憶體超標**：1.1 的 77 MB 是空白清單的量測值。Phase 02、03 結束時都量 A-03；超過 100 MB 時先回報，再討論對策（例如完整視窗關閉時真正釋放，或改用 WinForms）。Phase 02 原型實測：開過小面板後約 80 MB，餘裕約 20 MB。Phase 03 原型實測：完整視窗開著時約 95～105 MB，隱藏後仍約 109 MB；使用者選擇關閉時真正釋放（3.6），關閉後約 86 MB。另試過的 GC 設定：`System.GC.ConserveMemory` 與 `System.GC.RegionSize` 效果不明顯；`System.GC.Gen0MaxBudget` 設為 4 MB 時開著視窗約省 10 MB，但官方文件沒有列出這個設定，沒有採用，留作之後的備案。Phase 03 結束時 A-03 為 98.7 MB（最高 99.7 MB）。Phase 04 結束時以發佈版量測（開過小面板、完整視窗與設定視窗後全部關閉，常駐 60 分鐘）：73.3 MB，期間最高 91.1 MB。
4. **清單不完整**：殼層、系統與輔助行程清單漏列時，歸組會出錯。依實測調整（R-7），調整時同步更新 3.3。

## 7. 對 SPEC 的解讀與補充

- F-31：尖峰發生在 MemSweep 未執行期間時，不只前五名，發生時間也無從得知。SPEC F-31 隨本文件一併修訂，補上「發生時間」。
- F-31：MemSweep 執行期間偵測到的開機尖峰，前五名取自偵測到尖峰上升的那次取樣，與實際尖峰最多相差 2 秒（3.10）。
- F-41：JSON 只含前五名，與文字摘要相同。
- F-07、F-12：「完整清單…」「設定…」在對應功能完成的 Phase 才出現（第 5 章）。
- 全工具的 1 GB = 1024³ bytes。

## 8. 核准後的規則檔更新

- 本文件核准後，AGENTS.md 與 AGENTS_cht.md 的「Project」一節補上技術選型與目錄版面。
- 建置、測試、發佈指令在 Phase 01 實際確認後再寫入。
