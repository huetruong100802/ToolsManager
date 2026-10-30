# Toolbox — Design Doc (v0.6, 2026-10-28)

> Trạng thái: **MVP đã code** (xem §7). Toolbox là **một danh sách công cụ**: đăng ký tool (nằm ở đâu cũng được), quản lý, đóng gói gửi qua chat, nhập nhanh từ gói, và **MCP server** để agent tìm, chạy, **đăng ký và sửa** tool. Người dùng chủ yếu quan sát và dùng; agent là bên ghi danh sách chính. App nhẹ: một file `Toolbox.exe` 70KB, không cần cài.

## 1. Vấn đề

Tool nằm rải rác (VSPE trong Program Files, EasyModbus trong Downloads và trong repo, script do agent tạo…), phải nhớ đường dẫn mới dùng được, và gửi cho người khác thì không tiện.

## 2. Phạm vi v1

| Có | Chưa làm (để sau) |
|---|---|
| Đăng ký tool vào danh sách (chọn file hoặc kéo thả) | Copy tool vào thư viện riêng / quản lý version |
| Xem, tìm, lọc tag, chạy, mở thư mục, sửa, xoá khỏi danh sách | Quét máy tìm tool, phát hiện bản trùng |
| Báo tool có đường dẫn không còn tồn tại | Workspace / preset theo repo |
| Chọn nhiều tool → đóng gói 1 file zip để gửi | Tab Lịch sử và hoàn tác trên GUI |
| Nhập gói nhận được (nút, kéo thả, hoặc mở exe kèm đường dẫn gói) | Hash, ký số, cài ngầm (silent) bộ cài |
| Gửi kèm bộ cài (`pack=setup`), nút Cài đặt bên nhận | Tự tìm lại tool khi đường dẫn mất (fingerprint, §4) |
| Thêm từ app đã cài (shortcut Start Menu), kéo thả `.lnk` | |
| MCP server (`Toolbox.exe --mcp`): `list_tools`, `run_tool`, `register_tool`, `update_tool`, `unregister_tool` | Gỡ cài đặt tự động |
| Agent ghi danh sách; mỗi lần ghi có sao lưu và nhật ký `changes.log`; file nhỏ được chép vào thư viện | |
| Nút **Cài vào máy** (không cần admin): copy vào `%LOCALAPPDATA%\Programs\Toolbox`, shortcut Start Menu, double-click `.toolbox` mở màn nhập gói | |

## 3. Dữ liệu: một file `tools.json`

Vị trí: `%APPDATA%\Toolbox\tools.json`

```json
{
  "tools": [
    {
      "id": "easymodbus-sim",
      "name": "EasyModbus Server Simulator",
      "path": "C:\\Users\\HueTruong\\Downloads\\EasyModbusTCP .NET Package V5.6\\EasyModbusServerSimulator.exe",
      "args": "",
      "description": "Modbus TCP server giả lập để test client PLC",
      "tags": ["modbus", "plc", "testing"],
      "pack": "folder"
    },
    {
      "id": "vspe",
      "name": "VSPE",
      "path": "C:\\Program Files\\Eterlogic.com\\Virtual Serial Ports Emulator (x64)\\VSPEmulator.exe",
      "description": "Tạo cổng COM ảo",
      "tags": ["serial"],
      "pack": "none"
    }
  ]
}
```

| Trường | Bắt buộc | Ý nghĩa |
|---|---|---|
| `id` | ✔ | Sinh tự động từ tên (kebab-case), duy nhất trong danh sách |
| `name` | ✔ | Tên hiển thị |
| `path` | ✔ | File để chạy (`.exe`, `.bat`, `.ps1`, `.py`…) hoặc thư mục |
| `args` | | Tham số mặc định khi chạy |
| `description`, `tags` | | Để tìm kiếm, và để agent đọc hiểu |
| `pack` | | Khi đóng gói lấy gì: `folder` (cả thư mục chứa file, mặc định), `file` (chỉ file đó), `none` (chỉ gửi thông tin, người nhận tự cài app), `setup` (gửi kèm file bộ cài, ví dụ VSPE: app cần driver nên copy thư mục đã cài không chạy được) |
| `setup` | khi `pack=setup` | File bộ cài `.exe`/`.msi`. Trong gói là `id/<tên file>`; sau khi nhập là đường dẫn tuyệt đối tới file đã giải nén. `path` vẫn là chỗ app sẽ được cài (vd `C:\Program Files\…`) |
| `store` | | `link` (mặc định): `path` trỏ tới chỗ gốc. `copy`: `path` trỏ tới bản chép trong thư viện, không mất khi file gốc bị di chuyển hay xoá |
| `fingerprint` | | `<tên file>\|<dung lượng>\|<sha256>` của file chính, ghi khi đăng ký. Dùng để nhận ra cùng một file khi đăng ký lại với đường dẫn khác |
| `notes` | | Ghi chú của agent: cách dùng, lưu ý, lý do đăng ký. Không dùng để tìm kiếm |
| `lastUsedAt`, `runCount` | | Lần cuối chạy qua MCP `run_tool` (UTC, ISO 8601) và số lần. Chỉ tính lần chạy qua MCP |

Toolbox **không đụng tới file của tool**. Xoá một tool chỉ là xoá dòng đó khỏi danh sách.

**Thư viện và sao lưu** (nằm cạnh `tools.json`, cùng thư mục `%APPDATA%\Toolbox\`):
```
tools.json                 # danh sách
library\<id>\…             # bản chép của tool store=copy; setup\<tên file> nếu có bộ cài
backup\tools-<thời điểm>.json   # bản trước mỗi lần ghi có thay đổi; giữ 50 bản mới nhất
changes.log                # mỗi thay đổi một dòng: thời gian, nguồn (gui/mcp), thao tác, id, tên
```
- `backup` và `changes.log` chỉ ghi khi có thay đổi danh sách (thêm, sửa, xoá, nhập). Ghi lần chạy (`lastUsedAt`, `runCount`) không sao lưu, tránh tạo bản sao mỗi lần chạy tool.
- Mọi lần ghi và đọc `tools.json` giữ mutex `Global\ToolboxStore` (chờ tối đa 10 giây). Thao tác đọc-sửa-ghi của agent giữ khoá từ đầu tới cuối.
- **Đã biết, chưa sửa:** cửa sổ GUI giữ danh sách trong bộ nhớ, nên có thể ghi đè thay đổi agent vừa làm. Trước khi phát hành phải để GUI nạp lại `tools.json` trước mỗi thao tác ghi (xem §7).

## 4. Chức năng

**Đăng ký:** bấm **+ Thêm** hoặc kéo thả file/thư mục vào cửa sổ. Form điền sẵn tên (lấy từ tên file hoặc thông tin version của exe), người dùng thêm mô tả và tag rồi Lưu.
- **App đã cài…**: danh sách app lấy từ shortcut Start Menu (của user và của máy), có icon, tìm không dấu; bỏ trình gỡ cài đặt, đánh dấu app đã có trong danh sách. Chọn app → form điền sẵn exe thật, tên, tham số, mô tả của shortcut. Shortcut kiểu MSI (Office…) được hỏi `msi.dll` để ra exe thật.
- Kéo thả hoặc chọn file `.lnk` → đăng ký file mà shortcut trỏ tới (shortcut hỏng thì giữ nguyên `.lnk`).
- App trong `Program Files` được gợi ý `pack=none`; nếu muốn gửi kèm bộ cài thì đổi sang **Kèm bộ cài** và chọn file setup.

**Quản lý:**
- Danh sách có ô tìm kiếm (tên, mô tả, tag) và lọc theo tag.
- Double-click hoặc bấm ▶ để chạy. Riêng `.ps1` thì chạy qua `pwsh` hoặc `powershell`.
- Nút 📁 mở thư mục chứa tool. Ngoài ra có Sửa, Xoá khỏi danh sách.
- Xoá khỏi danh sách (GUI): nếu tool được chọn có bản chép trong thư viện, hỏi Có/Không/Huỷ. Có = xoá luôn thư mục `library\<id>\` sau khi `tools.json` đã lưu. Không = chỉ bỏ khỏi danh sách. Không có bản chép thì giữ xác nhận đơn giản. File gốc không bao giờ bị xoá.
- Tool có `path` không còn tồn tại hiện ⚠, kèm nút "Trỏ lại đường dẫn".

**Đóng gói:** tick chọn nhiều tool, bấm **📦 Đóng gói**, chọn nơi lưu, được file `<tên>.toolbox`:
```
toolbox-pack.json        # các entry đã chọn, path đổi thành đường dẫn tương đối trong gói
easymodbus-sim/…         # nội dung theo `pack`
plc-viewer/              # pack = none → chỉ có thông tin trong toolbox-pack.json
vspe/SetupVSPE.msi       # pack = setup → chỉ file bộ cài; path giữ nguyên chỗ cài
```
Trước khi lưu, Toolbox hiện tổng dung lượng và danh sách thư mục sẽ lấy. Đây là bước chặn việc vô tình nén cả `Downloads`.

**Nhập gói:** bấm **Nhập gói** (hoặc kéo file `.toolbox` vào cửa sổ), xem danh sách tool trong gói rồi bỏ tick tool không cần. Chọn thư mục giải nén, mặc định `%LOCALAPPDATA%\Toolbox\imported\`. Toolbox thêm các entry vào danh sách với `path` mới.
- Trùng `id` thì hỏi: thay thế, giữ cả hai (thêm hậu tố), hoặc bỏ qua.
- Gói có file `.exe`/script thì cảnh báo "chỉ nhập gói từ người tin cậy".
- Khi giải nén, Toolbox chặn file có đường dẫn thoát ra ngoài thư mục đích (zip-slip).
- Tool `pack=setup` mà máy nhận chưa có app: card hiện "Chưa cài trên máy này" (màu cam) và nút **Cài đặt** thay cho Chạy. Bấm thì mở bộ cài như double-click (có UAC, người dùng tự bấm qua các bước, Toolbox không cài ngầm). Cài xong (hoặc quay lại cửa sổ) Toolbox kiểm lại `path` và card trở về bình thường. Cài vào chỗ khác thì bấm Sửa để chỉnh `path`.
- Trường `setup` của tool không phải `pack=setup` trong manifest bị bỏ khi nhập.

**Sao lưu:** Copy file `tools.json` (chỉ danh sách, không kèm file).

**Agent (MCP):** `Toolbox.exe --mcp` chạy MCP server qua stdio, không mở cửa sổ. Nút **Kết nối agent…** kết nối vào Claude Code bằng một nút:
- **Toàn bộ máy** (scope `user`) hoặc từng **workspace Claude Code** (scope `local`, riêng cho người dùng, không ghi `.mcp.json` vào repo). Danh sách workspace lấy từ `projects` trong `~/.claude.json` (gộp các cách viết `C:/a`, `c:/a`, bỏ thư mục không còn); thư mục chưa có trong danh sách thì bấm **Thư mục khác…**.
- Mỗi dòng có trạng thái *Chưa kết nối / Đã kết nối / Trỏ tới exe khác* và nút **Kết nối**, **Kết nối lại**, **Huỷ kết nối**. Toolbox chỉ đọc `~/.claude.json`; ghi thì gọi `claude mcp add/remove` (Claude Code tự quản file này). Máy không có CLI `claude` thì báo lỗi kèm lệnh để tự chạy.
- Đang chạy bản tải về (chưa cài) thì hỏi cài vào máy trước, để agent không mất kết nối khi file bị xoá hoặc di chuyển.
- **desk-mcp…** (nút riêng ngoài cửa sổ chính): khai báo [desk-mcp](https://github.com/DigitalDog1/desk-mcp) (điều khiển giao diện Windows) vào Claude Code theo cùng cách: toàn máy hoặc từng workspace, và **Gỡ cài**. Lệnh khởi chạy là `cmd /c npx -y desk-mcp`, cần Node.js 20+ trên PATH; Toolbox không cài gói trước, npx tải khi agent khởi động server. Không có scope ghi vào repo.
- Claude Desktop/Cursor và VS Code/Copilot vẫn khai báo tay: mỗi client một khối JSON với nút Copy riêng.

| MCP tool | Làm gì |
|---|---|
| `list_tools(query?, tag?)` | Tìm tool theo id/tên/mô tả/tag (không phân biệt dấu); trả id, tên, mô tả, tag, path, args, file còn tồn tại không |
| `run_tool(id, args?, wait?, timeoutSeconds?)` | `wait=false`: mở tool như người dùng bấm, trả PID (dùng cho app GUI, server giả lập). `wait=true`: chạy ẩn, trả exitCode + stdout/stderr, timeout mặc định 60s, tối đa 600s; chỉ cho `.exe/.com/.bat/.cmd/.ps1/.py`. Ghi nhận `lastUsedAt`, `runCount` |
| `register_tool(name, path, id?, args?, description?, tags?, notes?, setupPath?)` | Đăng ký tool. Trùng `path` (hoặc trùng `fingerprint`) với tool đã có thì **cập nhật** tool đó thay vì tạo mới. Quy tắc chọn store/pack bên dưới. Trả entry vừa ghi và cảnh báo nếu có |
| `update_tool(id, name?, path?, args?, description?, tags?, notes?)` | Sửa các trường được truyền. Đổi `path` không chép lại file và không đổi `store` |
| `unregister_tool(id)` | Bỏ tool khỏi danh sách. Agent không xoá file gốc cũng không xoá bản trong thư viện (còn để khôi phục từ sao lưu). Xoá hẳn bản chép chỉ làm được từ GUI, có xác nhận |

- **Danh sách không còn là allowlist bảo mật.** Agent đăng ký, sửa, bỏ và chạy được mọi tool, kể cả tool nó vừa đăng ký. Rào thay thế là: mọi thay đổi có sao lưu và nhật ký (§3), và Agent client (ví dụ Claude Code) vẫn hỏi người dùng trước mỗi lần gọi MCP tool theo quyền đã cấu hình. Người dùng xem thay đổi của agent ở `changes.log` hoặc tab Lịch sử (chưa làm).
- Mỗi lần gọi đọc lại `tools.json`, nên tool vừa thêm trên GUI dùng được ngay.

**Quy tắc chọn store và pack khi `register_tool`** (ngưỡng là hằng số trong `Core/Library.cs`):
1. Có `setupPath` (bộ cài `.exe`/`.msi`, đang có trên máy): chép bộ cài vào `library\<id>\setup\` nếu ≤ **500MB**, đặt `pack=setup`, `store=link`. `path` là chỗ app được cài, có thể chưa tồn tại. Bộ cài không tồn tại hoặc sai đuôi thì báo lỗi; bộ cài quá 500MB thì `pack=none` và có cảnh báo.
2. Không có `setupPath`, `path` nằm trong `Program Files` (hoặc `Program Files (x86)`): app đã cài, không chép (driver và thư mục cài đặt không chạy được khi chép), `pack=none`, `store=link`. Cảnh báo để agent tìm bộ cài rồi đăng ký lại với `setupPath`.
3. Còn lại: thư mục chứa file (hoặc chính thư mục) ≤ **50MB** thì chép vào `library\<id>\`, `store=copy`, `path` trỏ tới bản chép. Lớn hơn thì `store=link`, giữ đường dẫn gốc.
- Đăng ký lại cùng file: cập nhật metadata; nếu `store=copy` và file gốc vẫn ở ngoài thư viện thì chép lại. Không chép lại khi `path` đã nằm trong thư viện.
- `id` không truyền thì sinh từ `name`. `id` truyền vào đã có trên một tool khác thì báo lỗi, không đánh số lại.
- Không có `setupPath` và `path` không tồn tại thì báo lỗi.

**Chưa làm (§7):** tìm lại tool khi `path` mất bằng `fingerprint`. Hiện `fingerprint` chỉ dùng để nhận ra file đã đăng ký.
- Tiến trình con không kế thừa stdin/stdout của server, để không làm hỏng kênh MCP.
- `args` là chuỗi do agent truyền vào; với `.bat/.cmd` thì `cmd.exe` sẽ diễn giải chuỗi này. Mô tả tool nên ghi rõ tham số hợp lệ.

## 5. Giao diện

```
┌ Toolbox ─────────────────────────────────────────────────────────┐
│ 🔍 modbus              Tag: [Tất cả ▾]   [+ Thêm] [Nhập gói]      │
│ ┌──────────────────────────────────────────────────────────────┐ │
│ │ ☑ EasyModbus Server Simulator   modbus, plc    [▶] [📁] [✎] │ │
│ │   Modbus TCP server giả lập để test client PLC               │ │
│ ├──────────────────────────────────────────────────────────────┤ │
│ │ ☐ VSPE                          serial         [▶] [📁] [✎] │ │
│ ├──────────────────────────────────────────────────────────────┤ │
│ │ ☐ plc-log-parser  ⚠ không tìm thấy file        [Trỏ lại]    │ │
│ └──────────────────────────────────────────────────────────────┘ │
│                                       [📦 Đóng gói (1)] [🗑 Xoá] │
└──────────────────────────────────────────────────────────────────┘
```

## 6. Stack

Mục tiêu: **nhẹ**, tức là file gửi đi nhỏ và người nhận không phải cài gì.

- **WPF trên .NET Framework 4.8** (`net48`), build bằng .NET SDK đang có (SDK-style csproj, `UseWPF`). Kết quả là **một file `Toolbox.exe`, mục tiêu dưới 200KB**. Đo thật ngày 2026-10-26: app WPF tối thiểu build ra `Probe.exe` 5KB. Phần chiếm dung lượng chủ yếu sẽ là icon và XAML.
- .NET Framework 4.8/4.8.1 là thành phần có sẵn của Windows 10 (1903 trở lên) và Windows 11, theo Component Lifecycle Policy của Windows, không có ngày hết hỗ trợ riêng. Nguồn: [.NET Framework lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework). Máy dev hiện có 4.8.1 (Release 533325).
- Chỉ dùng thư viện có sẵn trong Framework để giữ một file exe duy nhất, không kèm DLL:
  - JSON: `DataContractJsonSerializer`, vì `System.Text.Json` không có sẵn trong 4.8.
  - Nén zip: `System.IO.Compression` + `System.IO.Compression.FileSystem`.
  - Chạy tool: `Process`.
- Chấp nhận: API cũ hơn .NET 10, C# bị giới hạn tính năng ngôn ngữ mới. Với app danh sách, chạy và nén zip thì không ảnh hưởng.

| Phương án khác | Vì sao không chọn |
|---|---|
| .NET 10 WPF self-contained | File khoảng 60–70MB |
| .NET 10 WPF framework-dependent | Nhẹ, nhưng người nhận phải cài .NET Desktop Runtime 10 |
| Tauri (Rust + web UI) | Khoảng 5–10MB và vẫn chạy không cần cài. Đây là lựa chọn thứ hai nếu muốn giao diện web; máy dev đã có Rust và Node |

Một project duy nhất để build ra đúng một exe; test tham chiếu thẳng project này.
```
src/Toolbox/              # Toolbox.exe (WPF, net48)
  Core/                   # ToolEntry, ToolStore, Packer, PackageImporter, Runner, ToolDefaults, ClaudeCode
  Core/AgentTools.cs      # đăng ký/sửa/bỏ tool cho agent: quy tắc store/pack
  Core/Library.cs         # ngưỡng, đo dung lượng, chép vào thư viện, fingerprint
  Mcp/McpServer.cs        # JSON-RPC qua stdio (JavaScriptSerializer có sẵn trong Framework)
  *.xaml                  # MainWindow, ToolEditWindow, ImportWindow, AgentWindow
tests/Toolbox.Tests/      # xUnit
```

## 7. Tiến độ

| Bước | Nội dung | Trạng thái |
|---|---|---|
| 1 | Core: `tools.json`, Packer (pack/extract, chặn zip-slip, trùng id), Runner | ✔ có test |
| 2 | GUI: danh sách, tìm/lọc, thêm/kéo thả, sửa, xoá, chạy, mở thư mục, ⚠ đường dẫn hỏng | ✔ đã chụp màn hình kiểm tra |
| 3 | GUI: đóng gói (chọn nhiều, xem trước dung lượng), nhập gói | ✔ e2e qua UI Automation: gói EasyModbus thật + VSPE (276KB), mở `Toolbox.exe <gói>`, bấm Nhập; cả 2 tool vào danh sách, file tồn tại |
| 4 | MCP: `list_tools`, `run_tool` | ✔ có test + thử qua stdio thật |
| 5 | Cài đặt + đuôi `.toolbox` | ✔ có test (registry ghi vào key tạm rồi xoá) |
| 6 | Gọi MCP từ Claude Code thật; bấm tay form Thêm/Sửa và luồng Đóng gói trên GUI | Chưa làm |
| 9 | Kết nối agent một nút (Claude Code: toàn máy / theo workspace, huỷ kết nối) | ✔ có test (`ClaudeCodeTests`); thử với `claude` CLI thật: kết nối → kết nối lại → huỷ ở scope local; chụp màn hình cửa sổ. Chưa bấm tay trên GUI, chưa thử scope user |
| 7 | Theme phẳng (App.xaml), card tool, form nhãn-trên-ô | ✔ chụp màn hình 4 cửa sổ |
| 8 | `pack=setup` (gửi kèm bộ cài, nút Cài đặt) + "App đã cài…" (Start Menu, `.lnk`, MSI) | ✔ có test (Packer, Shortcuts); chụp màn hình cửa sổ chọn app. Chưa chạy tay luồng gói setup → nhập → Cài đặt trên GUI |
| 10 | Agent CRUD: `register_tool`, `update_tool`, `unregister_tool`; thư viện `store=copy`, bộ cài `setupPath`; sao lưu, `changes.log`, mutex | ✔ có test (`AgentToolsTests`, `ToolStoreTests`, `McpServerTests`). Chưa làm: GUI nạp lại `tools.json` trước khi ghi (§3, bắt buộc trước phát hành); FileSystemWatcher; tab Lịch sử; tìm lại khi `path` mất; chạy tay với Claude Code thật |
