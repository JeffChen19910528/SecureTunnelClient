# SecureTunnel Connect

[English](README.md) | **繁體中文**

SecureTunnel 私有資源 VPN 存取平台的 Windows 用戶端。使用者可匯入由 Gateway 核發的 WireGuard 設定檔、安全地儲存，並連線到 SecureTunnel Gateway。

> **專案狀態：開發完成 / 本機驗證完成 / 尚不可正式發行（DEVELOPMENT COMPLETE / LOCALLY VERIFIED / RELEASE BLOCKED）。**
> 目前版本為 `0.6.0`，屬於 1.0 之前的原型軟體。尚未安裝過真正的 Windows Service，也尚未以真實的 `wireguard.exe` 測試過端對端連線，因此**還沒有可實際運作的端對端 VPN 連線**。詳見 [`docs/roadmap.md`](docs/roadmap.md)。

## 使用說明

**需求**：64 位元 Windows 10/11、.NET 10 執行階段，以及 Gateway 核發的 WireGuard 用戶端設定檔（`.conf`）。**不需要**另外安裝 WireGuard for Windows。

1. **啟動 Agent（需系統管理員，每次開機一次）**。Agent 要建立虛擬網卡，所以必須提升權限。
   - 安裝成服務：`.\deploy\package.ps1`，再於管理員 PowerShell 執行 `.\deploy\install-service.ps1 -ExecutablePath "<路徑>\SecureTunnel.Client.Agent.exe"` 與 `Start-Service SecureTunnelAgent`。
   - 只是試用：在管理員 PowerShell 執行 `SecureTunnel.Client.Agent.exe`，並保持視窗開啟。
2. **執行 SecureTunnel Connect**（一般使用者即可）。匯入 `.conf`，畫面會顯示摘要（Gateway、位址、允許網段）。私鑰由 Agent 以 DPAPI 加密保存，之後不會再顯示。
3. **連線**。狀態依序為 連線中 -> 等待握手 -> **已連線**；只有真的完成 WireGuard 握手才會顯示「已連線」。
4. **存取受保護資源**（例如 `http://10.20.0.5:8443/`）。只有 `AllowedIPs` 列出的網段會走 VPN，能連哪些資源仍由 Gateway 決定。
5. **用完請中斷連線**，會移除虛擬網卡與路由。關閉 App 不會中斷連線，停止 Agent 才會。

設定規則：`AllowedIPs` 請列出受保護網段（不支援 `0.0.0.0/0` 全流量）；`Endpoint` 必須能從這台電腦以 UDP 連到。

| 訊息 | 說明 / 處理 |
|---|---|
| 服務未執行 | 先啟動 Agent（步驟 1）。 |
| 存取被拒 | 請使用修正 Named Pipe 權限後建置的 Agent（舊版會擋掉所有使用者）。 |
| 內建 WireGuard 引擎遺失 | `wireguard.dll` 必須與 Agent 執行檔同資料夾；僅支援 64 位元 Windows。 |
| 需要系統管理員權限 | Agent 沒有以管理員身分執行。 |
| 一直停在「等待握手」 | Endpoint／埠錯誤、UDP 被擋，或 Gateway 沒有登錄你的公鑰。 |

**驗證狀態**：已用內建引擎對真實 Gateway（Linux 上的 WireGuard + nftables）實測：握手成功、已授權資源 HTTP 200、未授權資源被擋、中斷後存取被切斷且虛擬網卡被移除。也已用 `deploy\install-service.ps1` 把 Agent 安裝成 `SecureTunnelAgent` Windows 服務（執行於 `C:\Program Files\SecureTunnel\Agent`）重測，結果相同。尚未實測：手動操作 WPF 視窗、WiX MSI。

## 解決什麼問題

SecureTunnel 的產品原則是：*只有透過 SecureTunnel Gateway 連線、且持有已授權 Peer 設定檔的使用者，才能存取 Gateway 政策所允許的受保護資源。*

Gateway 伺服器端已在網路層強制執行此原則（WireGuard + nftables 預設拒絕）。SecureTunnel Connect 則是讓不熟悉技術的 Windows 使用者，也能持有並使用已授權的 Peer 設定檔，而不必了解 WireGuard、DPAPI，也不必為了開啟程式而以系統管理員身分執行。

## 角色說明

| 名詞 | 意義 |
|---|---|
| Gateway | SecureTunnel Gateway 伺服器（獨立的程式庫），負責終結 WireGuard 通道並執行存取政策。 |
| Client / Peer | 由 Gateway 核發、匯入本應用程式的單一已授權 WireGuard 設定檔。 |
| SecureTunnel Connect（本專案） | 使用者日常操作、不需提升權限的 WPF 應用程式。 |
| 特權邊界（`SecureTunnel.Client.Agent`） | 名為 `SecureTunnelAgent` 的 Worker Service，UI 僅能透過 Named Pipe 呼叫它來執行連線、中斷、查詢狀態與驗證設定。詳見 [`docs/windows-service-boundary.md`](docs/windows-service-boundary.md) 與 [`docs/ipc-protocol.md`](docs/ipc-protocol.md)。 |

## 架構概覽

```
SecureTunnel.Client.Core            領域模型、介面、狀態機、IPC 傳輸型別（不含 I/O）
        ^
SecureTunnel.Client.Infrastructure  DPAPI 儲存、WireGuard 連線引擎、設定檔解析器、Named Pipe 用戶端代理
        ^
SecureTunnel.Client.Agent           Worker Service 主機（SecureTunnelAgent）：特權操作契約 + Named Pipe 伺服器
        ^
SecureTunnel.Connect                WPF 介面（無提升權限）：匯入、連線／中斷、診斷、系統匣
```

完整架構圖與設計理由請見 [`docs/architecture.md`](docs/architecture.md)。

## 主要功能與設計重點

- **完整的設定檔模型**：支援 WireGuard `[Interface]` + `[Peer]` 的解析與確定性產生；Peer 公鑰與已驗證的 Endpoint 為必填，`AllowedIPs` 絕不使用預設值。
- **安全儲存**：以 DPAPI（`LocalMachine` 範圍）加密、原子寫入，遇到損毀或不明狀態時一律失敗封閉（fail closed）。
- **嚴謹的連線狀態機**：只有在確認 WireGuard 握手（handshake）成功後才會進入 `Connected`，不會僅憑指令結束碼或 Pipe 往返成功就判定已連線。
- **強化的 IPC 邊界**：固定 Pipe 名稱、含長度前綴的 JSON 訊框、大小與逾時限制、封閉式操作白名單，以及拒絕 Everyone／Anonymous／Network 的 ACL。
- **機密保護**：私鑰使用 `SensitiveString` 處理，不會出現在日誌、命令列參數或 Pipe 訊息中；外部程序輸出會經過遮蔽（redaction）。
- **WPF 介面**：設定檔匯入（檔案選取、驗證結果與摘要）、連線／中斷／重新整理、診斷面板，以及系統匣（最小化、還原、結束；不會影響 Agent 的連線）。
- **可重複的本機打包**：`deploy\package.ps1` 產出帶版本號與 SHA256 校驗碼的成品。

## 目前進度

已完成並於本機驗證：

- [x] 方案／專案骨架與共用版本號（`Directory.Build.props`）
- [x] Core 領域模型、驗證與診斷型別
- [x] 用戶端狀態機
- [x] WireGuard 設定檔解析器與產生器
- [x] DPAPI 加密的設定檔儲存
- [x] WireGuard 連線引擎（介面／握手狀態解析、逾時並終止整個程序樹、冪等中斷連線）
- [x] .NET 10 Worker Service 主機（服務名稱 `SecureTunnelAgent`）
- [x] 強化的 Named Pipe IPC 傳輸層
- [x] WPF 介面（匯入、連線控制、診斷、系統匣）
- [x] 以測試替身（test double）模擬 WireGuard，可在沒有真實 Gateway 或 WireGuard 的情況下驗證用戶端
- [x] Windows Service 安裝／解除安裝 PowerShell 指令碼（僅撰寫與語法檢查，**尚未實際執行**）
- [x] 本機打包流程（已實際執行）

尚未完成：

- [ ] 實際安裝 Windows Service：**受阻**（需要提升權限）
- [ ] 跨身分的 Named Pipe 存取被拒測試：**受阻**（需要第二個 Windows 身分）
- [ ] 真實 WireGuard 連線測試：**受阻／未測試**（需要安裝 WireGuard）
- [x] UI 自動化測試（`tests/SecureTunnel.Client.UiTests`，FlaUI）：檔案對話框匯入、剪貼簿、關閉後隱藏到系統匣、中英文切換，6/6 通過（連跑兩次）。需要互動式桌面；沒有 Agent 時匯入只會得到安全的錯誤訊息。系統匣「還原／結束」選單**尚未自動化**
- [x] 正式安裝程式（WiX MSI）：已建置成功（`deployuild-msi.ps1`），但**尚未實際安裝測試**（需要提升權限），見 [`docs/installer-technology-assessment.md`](docs/installer-technology-assessment.md)

## 系統需求

- Windows（x64）
- [.NET 10 SDK](https://dotnet.microsoft.com/)（建置與測試）
- 目標機器需安裝 .NET 10 Desktop Runtime（打包成品為 framework-dependent）
- 執行真實連線時需安裝 WireGuard，並以具備權限的方式安裝 Agent 服務

## 建置、測試與打包

```powershell
# 建置
dotnet build SecureTunnelClient.slnx

# 執行所有自動化測試
dotnet test SecureTunnelClient.slnx

# 本機打包（不需提升權限，可重複執行）
.\deploy\package.ps1
```

打包成品預設輸出到 `artifacts\`，每個元件（Agent 與 Connect）各有一個帶版本號的 zip 與 SHA256 校驗檔。

## 安裝 Agent 服務

安裝與解除安裝由 `deploy\install-service.ps1`、`deploy\uninstall-service.ps1` 處理，需要以**系統管理員**身分執行。這些指令碼目前只經過撰寫與語法檢查，尚未在真實環境執行，請先閱讀 [`docs/windows-service-installation.md`](docs/windows-service-installation.md) 與 [`docs/deployment-guide.md`](docs/deployment-guide.md) 再使用。

資料存放位置：

| 資料 | 位置 |
|---|---|
| 加密後的設定檔 | `%ProgramData%\SecureTunnel\Agent\configs\*.stc`（ACL 僅限 SYSTEM 與 Administrators） |
| 暫時的通道檔 | `%TEMP%\stc-<guid>.conf`（使用後立即刪除，包含逾時情況） |
| 服務日誌 | Windows 事件檢視器 |

## 專案結構

```
SecureTunnelClient/
├── SecureTunnelClient.slnx
├── Directory.Build.props                   共用版本號與建置路徑處理（PathMap）
├── deploy/                                 打包與服務安裝／解除安裝指令碼
├── src/
│   ├── SecureTunnel.Connect/               WPF 應用程式（匯入、連線、診斷、系統匣）
│   ├── SecureTunnel.Client.Core/           領域模型、介面、IPC 傳輸型別
│   ├── SecureTunnel.Client.Agent/          Worker Service 主機：特權操作契約 + Named Pipe 伺服器
│   └── SecureTunnel.Client.Infrastructure/ Windows／WireGuard 整合與 Named Pipe 用戶端代理
├── tests/
│   ├── SecureTunnel.Client.Core.Tests/
│   ├── SecureTunnel.Client.Agent.Tests/
│   ├── SecureTunnel.Client.Infrastructure.Tests/
│   ├── SecureTunnel.Client.Connect.Tests/  WPF ViewModel 測試
│   └── SecureTunnel.Client.TestSupport/    共用的假物件／測試替身
└── docs/                                   設計與操作文件
```

## 文件索引

| 文件 | 內容 |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | 分層架構與元件圖 |
| [`docs/security-model.md`](docs/security-model.md) | 安全模型 |
| [`docs/configuration-format.md`](docs/configuration-format.md) | 設定檔格式 |
| [`docs/windows-service-boundary.md`](docs/windows-service-boundary.md) | 特權服務邊界 |
| [`docs/ipc-protocol.md`](docs/ipc-protocol.md) | Named Pipe IPC 協定 |
| [`docs/wireguard-connection-lifecycle.md`](docs/wireguard-connection-lifecycle.md) | WireGuard 連線生命週期 |
| [`docs/diagnostics.md`](docs/diagnostics.md) | 診斷功能 |
| [`docs/windows-service-installation.md`](docs/windows-service-installation.md) | 服務安裝說明 |
| [`docs/deployment-guide.md`](docs/deployment-guide.md) | 部署指南 |
| [`docs/installer-technology-assessment.md`](docs/installer-technology-assessment.md) | 安裝程式技術評估 |
| [`docs/testing-guide.md`](docs/testing-guide.md) | 測試指南 |
| [`docs/roadmap.md`](docs/roadmap.md) | 開發藍圖 |

## 參與貢獻

請先閱讀 [`CONTRIBUTING.md`](CONTRIBUTING.md)。核心原則包含：

1. 絕不記錄、列印或以命令列參數傳遞私鑰等機密，一律使用 `SensitiveString`。
2. 啟動外部程序時使用 `ArgumentList`，不得以字串拼接組成命令。
3. 不新增通用的特權操作；每個新能力都需要新的 `PrivilegedRequest` 子類型與白名單項目。
4. 遇到不明或損毀的狀態時失敗封閉。
5. 只有經確認的 `GetStatusAsync` 結果才能支持 `Connected` 狀態。
6. 文件中僅在功能實際執行過後才標示為通過。
