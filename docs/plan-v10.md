# Kế hoạch: đưa mockup v10 (WPF, Agent qua MCP) vào Toolbox

Nguồn thiết kế: `docs/mockup/workspace-v10-wpf.html`. Mô hình workspace hiện có: `docs/plan-workspace.md` (đã xong).

## Hiện trạng đã kiểm chứng trong code (khác với mockup)

- `Workspace` có `Bindings` (loại tool dựng sẵn dotnet/git/angular/npm từ `ToolCatalog`), `LibraryBindings` (tool thư viện gắn vào), `Customs`, `Profiles`, `Stacks`.
  Profile chỉ gắn được với tool dựng sẵn, chưa gắn được với tool thư viện hay tool riêng. Mockup muốn mọi tool đều có profile, đây là thay đổi mô hình lớn, **chưa làm** (mục Backlog).
- MCP: `McpServer.cs` (lõi) + `McpServer.Setup.cs` (công cụ workspace). Chế độ workspace bật bằng `--workspace <id>`. Hiện có ~28 công cụ, tăng theo số loại thao tác chứ không theo số profile. `list_setup` trả cả cấu hình một lần.
- `register_tool` và `update_tool` trong chế độ workspace sửa trực tiếp tool dùng chung. Chưa có danh tính agent, chưa có duyệt, chưa có nhật ký theo agent.

## Quyết định thiết kế (chốt trước)

- **Agent** lưu ở `%APPDATA%\Toolbox\agents.json`: `{ Id, Name, Client, WorkspaceId (null = toàn máy), RunMode (auto|ask|none), CreatedAt, LastSeenAt }`. MCP nhận `--agent <id>`. Quyền đọc lại từ file mỗi lần gọi công cụ, nên đổi trong giao diện là có hiệu lực ngay. Không có `--agent` thì chạy như cũ (tương thích).
- **Đề xuất** lưu ở `%APPDATA%\Toolbox\proposals.json`: `{ Id, AgentId, WorkspaceId, Kind (update_tool|delete_tool), ToolId, Fields, Reason, State (pending|approved|rejected), CreatedAt, DecidedAt }`. Trong chế độ workspace, `update_tool` và `unregister_tool` toàn cục trở thành đề xuất, không ghi vào thư viện. Duyệt có hai kiểu: sửa tool dùng chung, hoặc chỉ cho workspace (ghi vào `Args` của các profile của tool đó trong workspace; v1 chỉ áp dụng cho tool dùng chung).
- **Nhật ký** `%APPDATA%\Toolbox\activity.jsonl`: mỗi dòng `{ Time, AgentId, AgentName, WorkspaceId, Action, Target, Result, UndoToken? }`. Hoàn tác dựa vào bản sao lưu `tools.json` / `workspaces.json` sẵn có; v1 chỉ hoàn tác bằng cách khôi phục bản sao lưu ngay trước thay đổi đó (nếu không có thì nút bị tắt).
- **Công cụ MCP gom lại (chế độ workspace, quảng bá trong `tools/list`)**: `overview`, `search`, `get`, `save`, `delete`, `attach`, `run`, `run_status`, `run_stop`, `logs`, `propose`, `list_tools` (đọc thư viện). Tên cũ (`create_profile`, `run_profile`...) vẫn gọi được nhưng không quảng bá, để không vỡ client và test cũ. Số công cụ quảng bá cố định, không đổi theo số profile.
- **UI**: thêm trang `Agent` ở thanh trái (ba tab: Agent, Chờ duyệt, Nhật ký), hộp thoại duyệt, mở rộng cửa sổ Kết nối agent. Thanh trái workspace có ô tìm và ghim. Tab `Chạy` mới ở đầu trang workspace. Tác vụ có khung log dưới danh sách.
- **Kiểm chứng**: phase code chỉ build. Test viết cùng phase, chạy một lần ở P9.

## Giai đoạn
- [x] P1 Kho agent, đề xuất, nhật ký — mode: direct — kind: code — touches: src/Toolbox/Core/Agents/AgentStore.cs, src/Toolbox/Core/Agents/ProposalStore.cs, src/Toolbox/Core/Agents/ActivityLog.cs, tests/Toolbox.Tests/AgentStoreTests.cs — risk: medium — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P2 MCP: danh tính agent, đề xuất thay cho sửa tool chung, nhật ký — mode: direct — kind: code — deps: P1 — touches: src/Toolbox/Mcp/McpServer.cs, src/Toolbox/App.xaml.cs, tests/Toolbox.Tests/McpAgentTests.cs — risk: high — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P3 MCP: bộ công cụ gom (overview, search, get, save, delete, attach, run, logs, propose) — mode: direct — kind: code — deps: P2 — touches: src/Toolbox/Mcp/McpServer.Generic.cs, src/Toolbox/Mcp/McpServer.cs, tests/Toolbox.Tests/McpGenericTests.cs — risk: high — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P4 Trang Agent, duyệt đề xuất, kết nối agent — mode: direct — kind: code — deps: P1 — touches: src/Toolbox/Views/AgentsView.xaml, src/Toolbox/Views/AgentsView.xaml.cs, src/Toolbox/ReviewWindow.xaml, src/Toolbox/ReviewWindow.xaml.cs, src/Toolbox/AgentWindow.xaml.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs — risk: medium — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P5 Thanh trái: tìm và ghim workspace — mode: direct — kind: code — deps: P4 — touches: src/Toolbox/Core/Workspaces/Workspace.cs, src/Toolbox/WorkspaceRow.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs — risk: low — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P6 Tab Chạy của workspace — mode: direct — kind: code — deps: P5 — touches: src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs — risk: medium — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [ ] P7 Tác vụ có khung log dưới — mode: direct — kind: code — deps: P6 — touches: src/Toolbox/Views/TasksView.xaml, src/Toolbox/Views/TasksView.xaml.cs — risk: low — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P9 Kiểm thử một lượt, chụp ảnh, README — mode: direct — kind: verify — deps: P7 — exit: `dotnet test tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore`
hot-files: src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/Mcp/McpServer.cs
commit: none
workdir: C:/Users/HueTruong/Work/Repositories/Researchs/ToolsManager

## Backlog (chưa làm, có trong mockup v10)

- Profile cho mọi tool (thư viện, riêng): đổi mô hình `Profile` và `ToolCatalog`.
- Hộp thoại Tạo profile theo ba bước (chọn tool, chọn lệnh, điền thông số).
- Sơ đồ bộ chạy sửa trực tiếp (dấu +, kéo thả).
- Tab Đóng gói theo workspace.
- Duyệt đề xuất kiểu "chỉ cho workspace này".
- Hoàn tác từng thay đổi lẻ.

## Điều chỉnh
- P2: thêm file Mcp/McpServer.Agents.cs, Core/Agents/ProposalApplier.cs (TOUCH-MISS: tách phần agent khỏi McpServer.cs cho gọn, ProposalApplier cần để GUI duyệt đề xuất).
- P3: bộ công cụ gom nằm ở Mcp/McpServer.Generic.cs; test ở McpGenericTests.cs. Chế độ chạy chỉ có auto và none (ask cần MCP hỏi người dùng, chưa có).
- P4: ReviewWindow không làm; giá trị cũ và mới hiện ngay trên thẻ đề xuất ở tab Chờ duyệt. Kết nối agent tạo bản ghi agent và truyền --agent trong AgentWindow, không thêm hộp thoại riêng.
- P5: thêm Core/DataDir.cs (biến TOOLBOX_DATA) để chạy thử và chụp ảnh mà không đụng dữ liệu thật. Cột trái xếp ghim rồi theo tên.
- P7: chưa làm (khung log dưới danh sách ở trang Tác vụ). Log vẫn mở ở panel Inspector bên phải như trước.
- P9: 270 test đạt. Ảnh README dựng bằng RenderTargetBitmap ngoài màn hình (màn hình đang khoá nên không chụp cửa sổ thật được); trang Agent và tab Chạy được dựng riêng từng trang, không phải cả cửa sổ chính.
