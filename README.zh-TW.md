# SecureTunnel Connect

[English](README.md) | **繁體中文**

SecureTunnel Connect 是搭配 [SecureTunnel Gateway](https://github.com/JeffChen19910528/SecureTunnelGateway) 使用的 Windows VPN 用戶端。匯入 Gateway 管理員核發的 WireGuard 設定檔，按下「連線」，就能存取你被授權使用的內部資源。所有需要的元件都已內建：**不需要**另外安裝 WireGuard for Windows 或任何其他 VPN 軟體。

- 一鍵連線／中斷，狀態即時顯示（完成真正的握手後才會顯示「已連線」）
- 內建 WireGuard 引擎（含虛擬網卡）
- 匯入的金鑰以 Windows DPAPI 加密保存，之後不會再顯示
- 只有 Gateway 允許的網段會走 VPN
- 可縮小到系統匣；介面支援繁體中文與英文

## 系統需求

- Windows 10 / 11（64 位元）
- Gateway 管理員核發的 WireGuard 用戶端設定檔（`.conf`）
- **安裝時需要一次**系統管理員權限（背景服務要負責建立虛擬網卡）
- 不需要其他任何東西：安裝程式已包含 .NET 執行階段與 WireGuard 引擎

## 安裝

**使用安裝程式（建議）。** 依照下方[從原始碼建置](#從原始碼建置)產生安裝程式，執行 `SecureTunnelConnect-Setup-<版本>-win-x64.exe`（會要求系統管理員權限）。它會安裝到 `C:\Program Files\SecureTunnel`：

- **SecureTunnel Agent** Windows 服務（自動啟動，在背景執行）
- **SecureTunnel Connect** 應用程式，並建立開始功能表捷徑與「設定 > 應用程式」項目

之後以較新版本再執行一次即可升級，匯入的設定檔會保留。靜默安裝：`SecureTunnelConnect-Setup-<版本>-win-x64.exe /silent`。

**不使用安裝程式。** 執行 `deploy\package.ps1` 後，把 `artifacts\<版本>\Agent` 複製到 `C:\Program Files\SecureTunnel\Agent`，再於系統管理員 PowerShell 執行：

```powershell
.\deploy\install-service.ps1 -ExecutablePath "C:\Program Files\SecureTunnel\Agent\SecureTunnel.Client.Agent.exe"
Start-Service SecureTunnelAgent
```

之後執行 `artifacts\<版本>\Connect` 內的 `SecureTunnel.Connect.exe`。

## 使用方式

1. 開啟 **SecureTunnel Connect**（一般使用者即可，不需要系統管理員權限）。
2. 按「匯入設定檔...」，選擇 Gateway 管理員給你的 `.conf`。畫面會顯示 Gateway 位址、你的通道位址與允許的網段。再次匯入同一份檔案只會覆蓋原本的設定。
3. 按「連線」。狀態會先是「等待握手」，接著變成 **已連線**。
4. 照常使用受保護的資源（瀏覽器、資料庫工具等）。能連到哪些資源由 Gateway 決定。
5. 用完請按「中斷連線」。關閉視窗只會縮小到系統匣，按下「中斷連線」之前 VPN 會保持連線。

「English／中文」按鈕可切換介面語言。「重新整理診斷」與「複製到剪貼簿」會產生一份不含任何金鑰的簡短報告，可提供給管理員。

## 設定檔格式

標準的 WireGuard 用戶端設定檔：

```ini
[Interface]
PrivateKey = <你的私鑰>
Address = 10.250.0.10/32
DNS = 10.250.0.1            # 選填
MTU = 1380                  # 選填

[Peer]
PublicKey = <Gateway 公鑰>
AllowedIPs = 10.20.0.0/24, 10.250.0.0/24
Endpoint = gateway.example.com:51820
PersistentKeepalive = 25    # 選填
```

- `PublicKey`、`Endpoint`、`AllowedIPs` 為必填，且 `AllowedIPs` 不會自動補預設值。
- `AllowedIPs` 請列出受保護的網段。不支援全流量（`0.0.0.0/0`、`::/0`），會被拒絕。
- `Endpoint` 必須能從你的電腦以 UDP 連到。

## 疑難排解

| 看到的狀況 | 處理方式 |
|---|---|
| 「SecureTunnel Connect 服務未執行」 | 以系統管理員執行 `Start-Service SecureTunnelAgent`。 |
| 一直停在 **等待握手** | 檢查 `Endpoint` 位址與埠、防火牆是否擋住 UDP，以及 Gateway 管理員是否已登錄你的公鑰。 |
| 「需要系統管理員權限」 | 服務不是以系統服務執行，請用安裝程式重新安裝。 |
| 「內建 WireGuard 引擎遺失」 | `wireguard.dll` 必須與 `SecureTunnel.Client.Agent.exe` 在同一個資料夾；僅支援 64 位元 Windows。 |
| 已連線但連不到某個資源 | Gateway 只開放管理員授權的資源，請請管理員確認你的權限。 |
| 存取服務被拒 | 請重新安裝目前版本。 |

## 解除安裝

在「設定 > 應用程式」移除 **SecureTunnel Connect**，或執行 `"C:\Program Files\SecureTunnel\Setup\SecureTunnelSetup.exe" /uninstall`（加上 `/removedata` 會一併刪除匯入的設定檔，`/silent` 則不顯示提示）。匯入的設定檔會保留在 `%ProgramData%\SecureTunnel\Agent`，重新安裝時不必再匯入；要刪除請移除該資料夾。未使用安裝程式時：`.\deploy\uninstall-service.ps1`（加上 `-RemoveData` 會一併刪除設定檔）。

## 安全性

請見 [docs/security.md](docs/security.md)。

## 從原始碼建置

需要 Windows 與 .NET 10 SDK，不需要其他任何工具（不需要安裝程式製作套件）。

```powershell
dotnet build SecureTunnelClient.slnx
dotnet test  SecureTunnelClient.slnx
.\deploy\package.ps1       # artifacts\<版本>\{Agent,Connect}、檢查碼與 zip（自包含）
.\deploy\build-setup.ps1   # artifacts\<版本>\SecureTunnelConnect-Setup-<版本>-win-x64.exe
```

測試包含 UI 自動化（`SecureTunnel.Client.UiTests`），會開啟應用程式，需要互動式桌面。SecureTunnel Agent 服務在同一台電腦執行時，會略過少數管道測試。

內建的 `wireguard.dll` 為 [wireguard-nt](https://www.wireguard.com/)（WireGuard LLC），授權條款在 `src/SecureTunnel.Client.Agent/native/win-x64/WIREGUARD-NT-LICENSE.txt`。
