# Kế hoạch: đưa mục 1–6 của prototype "Toolbox Workspace" vào WPF

Nguồn: artifact `claude.ai/artifact/BSHRx5WWvoaX2V7zNHZN3h`. Phạm vi do người dùng chọn: (1) workspace + rail, (2) tool gắn theo workspace,
(3) lệnh/biến thể/profile/tham số/cổng, (4) bộ chạy, (5) inspector log + chạy ngầm, (6) thanh đang chạy + Tổng quan + Tác vụ.
Không nằm trong phạm vi: mục 7 (palette Ctrl+K), mục 8 (agent/MCP/toast), trừ trường `Source` đã chừa sẵn trong `TaskInfo`.

## Quyết định thiết kế (chốt trước để các phase không phải sửa lại)

- **Thư viện cũ giữ nguyên.** `ToolEntry`/`ToolStore`/`RunTracker`/MCP không đổi. Workspace là lớp mới, song song: một mục "Thư viện" trên rail mở danh sách cũ.
- **Lưu trữ:** `%APPDATA%\Toolbox\workspaces.json` (DataContract như `tools.json`), lớp `WorkspaceStore` có `Load/Save/Add/Update/Remove`. Log chạy ngầm: `%APPDATA%\Toolbox\runs\<taskId>.log`.
- **Mô hình** (namespace `Toolbox.Core.Workspaces`, thư mục `src/Toolbox/Core/Workspaces/`):
  - `Workspace { Id, Name, Path, ColorIndex, Bindings: List<ToolBinding>, Customs: List<CustomCommand>, Profiles: List<Profile>, Stacks: List<StackDef> }`
  - `ToolBinding { Kind (dotnet|git|angular|npm), Cwd }` — lệnh của tool lấy từ catalog `ToolCatalog` (mã C#), không lưu lại trong json.
  - `CustomCommand { Id, Label, CommandLine, Cwd, Window }` — thuộc tab "Lệnh riêng".
  - `Profile { Id, Label, Kind, ActionId, VariantId, Vals: Dictionary<string,string> }`
  - `StackDef { Id, Label, Steps: List<StackStep> }`, `StackStep { Kind, ActionId, VariantId, ProfileId, Wait (none|port|exit), Port }`
- **Catalog** (`ToolCatalog`): mỗi tool có `Actions → Variants → { Label, Template, Params[] }`. `Param { Key, Prompt, Default, Type (text|port) }`. Template dùng `{key}`.
- **Nhận diện** (`WorkspaceDetector`): quét thư mục tìm `.sln`, `.git`, `angular.json`, `package.json` (đọc `scripts`), trả về gợi ý binding kèm lý do tiếng Việt.
- **Chạy** (`TaskHost`, Core): `TaskInfo { Id, WorkspaceId, Label, CommandLine, Cwd, Source (user|agent), Window, State (Running|Done|Failed|Stopped), Port, StartedAt, EndedAt, ExitCode }`.
  Mặc định chạy **ngầm**: ẩn cửa sổ, stdout+stderr ghi vào log file và bộ đệm dòng trong bộ nhớ (giữ tối đa 2000 dòng), tự bắt cổng từ dòng `localhost:NNNN`/`:NNNN`.
  `Window=true` thì chạy cửa sổ riêng bằng ShellExecute như `Runner`, không có log. Stop = `Runner.KillTree`. Restart = chạy lại cùng `TaskSpec`. Sự kiện `Changed`/`LineAdded` bắn từ luồng bất kỳ.
- **Cổng:** hợp lệ 1024–65535; trùng nếu có task đang chạy cùng cổng (đã khai hoặc bắt được); `NextFree(from)` bỏ qua cổng đang dùng.
- **Bộ chạy** (`StackRunner`): chạy tuần tự; bước `Wait=port` chờ cổng nghe được (TCP connect, timeout 120s), `Wait=exit` chờ task thoát mã 0, `Wait=none` đi tiếp ngay. Bước lỗi thì dừng chuỗi và báo bước nào.
- **UI:** `MainWindow` thêm cột rail trái (Thư viện, Tổng quan, Tác vụ, danh sách workspace, "+ Thêm workspace"), vùng nội dung đổi trang, panel inspector trượt bên phải, thanh trạng thái. Trang mới là `UserControl` trong `src/Toolbox/Views/`.
  Danh sách tool cũ nằm trong `LibraryView` (chuyển nguyên, không đổi hành vi).
- **Kiểm chứng:** mỗi phase code chỉ build. Test viết cùng phase, chạy một lần ở P8 rồi sửa chỗ FAIL.

## Giai đoạn
- [x] P1 Mô hình + lưu workspace — mode: direct — kind: code — touches: src/Toolbox/Core/Workspaces/Workspace.cs, src/Toolbox/Core/Workspaces/WorkspaceStore.cs, tests/Toolbox.Tests/WorkspaceStoreTests.cs — risk: low — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P2 Catalog, nhận diện, dựng lệnh, cổng — mode: direct — kind: code — deps: P1 — touches: src/Toolbox/Core/Workspaces/ToolCatalog.cs, src/Toolbox/Core/Workspaces/WorkspaceDetector.cs, src/Toolbox/Core/Workspaces/CommandBuilder.cs, tests/Toolbox.Tests/WorkspaceDetectorTests.cs, tests/Toolbox.Tests/CommandBuilderTests.cs — risk: medium — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P3 TaskHost: chạy ngầm, log, dừng, chạy lại — mode: direct — kind: code — touches: src/Toolbox/Core/Workspaces/TaskHost.cs, tests/Toolbox.Tests/TaskHostTests.cs — risk: high — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P4 Khung rail + chuyển trang + thêm/gỡ workspace — mode: direct — kind: code — deps: P1, P2 — touches: src/Toolbox/WorkspaceRow.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/Views/LibraryView.xaml, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/AddWorkspaceWindow.xaml, src/Toolbox/AddWorkspaceWindow.xaml.cs, src/Toolbox/App.xaml — risk: high — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P5 Trang workspace: tab tool, lệnh, biến thể, profile, form tham số — mode: direct — kind: code — deps: P2, P3, P4 — touches: src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/CommandEditWindow.xaml, src/Toolbox/CommandEditWindow.xaml.cs, src/Toolbox/AddToolWindow.xaml, src/Toolbox/AddToolWindow.xaml.cs, src/Toolbox/Views/CommandRows.cs, src/Toolbox/PromptWindow.xaml, src/Toolbox/PromptWindow.xaml.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs — risk: high — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P6 Bộ chạy: StackRunner + panel + trình sửa — mode: direct — kind: code — deps: P3, P5 — touches: src/Toolbox/Core/Workspaces/StackRunner.cs, src/Toolbox/StackEditWindow.xaml, src/Toolbox/StackEditWindow.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Core/Workspaces/TaskKey.cs, src/Toolbox/Views/CommandRows.cs, tests/Toolbox.Tests/StackRunnerTests.cs — risk: high — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P7 Inspector log, trang Tác vụ, Tổng quan, thanh trạng thái — mode: direct — kind: code — deps: P3, P4, P5 — touches: src/Toolbox/Views/InspectorView.xaml, src/Toolbox/Views/InspectorView.xaml.cs, src/Toolbox/Views/TasksView.xaml, src/Toolbox/Views/TasksView.xaml.cs, src/Toolbox/Views/OverviewView.xaml, src/Toolbox/Views/OverviewView.xaml.cs, src/Toolbox/Views/TaskRows.cs, src/Toolbox/Core/Workspaces/TaskBoard.cs, tests/Toolbox.Tests/TaskBoardTests.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs — risk: high — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P8 Chạy test, sửa lỗi, mở app kiểm tay — mode: direct — kind: verify — deps: P1, P2, P3, P4, P5, P6, P7 — touches: tests/Toolbox.Tests/RunTrackerTests.cs — exit: `dotnet test tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore`
hot-files: src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/App.xaml
commit: none
workdir: C:/Users/HueTruong/Work/Repositories/Researchs/ToolsManager

## Điều chỉnh
- P2: ACCEPT_UNCHANGED: operator explicitly accepted; no reason supplied
- P2: ready gọi sau khi code xong nên baseline trùng kết quả; đã AcceptUnchanged sau khi build xanh và đối chiếu file. Từ P3 gọi ready trước khi sửa.
- P2: npm không có action tĩnh; action lấy từ package.json lúc hiển thị (ToolCatalog.ActionsFor), nên ToolBinding không cần danh sách script.
- P3 TaskHost dùng ctor có starter tiêm vào để test như RunTracker.
- P3: đã chạy test P1–P3 sớm (36/36 xanh) vì P3 risk high và P4–P7 phụ thuộc hành vi TaskHost.
- P3: tác vụ ngầm chạy bằng pipe nên sẽ chết khi Toolbox thoát; khác prototype ghi 'đóng cửa sổ không dừng tác vụ ngầm'. P4 phải hỏi xác nhận rồi gọi TaskHost.StopAll khi đóng cửa sổ.
- P4: thêm `src/Toolbox/WorkspaceRow.cs` (dòng rail: màu, thư mục mất, số tác vụ, IsSelected) vào touches.
- P4: thư viện cũ chuyển nguyên sang `Views/LibraryView` (UserControl, Host = Window.GetWindow). `MainWindow` chỉ còn khung: rail, `Navigate(row|null)`, trang workspace tạm (header, banner thư mục mất, Mở thư mục, Gỡ, Trỏ lại).
- P4: mục Tổng quan/Tác vụ trên rail để P7 thêm (tránh nút chết). `MainWindow.Tasks` (TaskHost) dùng chung cho P5–P7; P5 thay `WorkspaceBody` bằng `WorkspaceView`.
- P4: đóng cửa sổ khi còn tác vụ ngầm → hỏi rồi `TaskHost.StopAll`. Đã mở thử bản Debug 5s: không crash. Chưa kiểm tay luồng Thêm/Gỡ workspace (để P8).
- P5: thêm vào touches `Views/CommandRows.cs` (dòng VM: ActionRow/ProfileRow/CustomRow/ParamRow/TabRow, TaskKey, WorkspacePaths), `PromptWindow` (nhập tên profile) và `MainWindow.xaml(.cs)` (thay `WorkspaceBody` bằng `WorkspaceView`, gọi Load/Refresh/Forget).
- P5: `TaskSpec.Key` = `workspace|tool|action|variant|profile` (`TaskKey`); lệnh riêng dùng tool = `custom`, action = id lệnh. P6/P7 dùng lại để biết bước/dòng nào đang chạy.
- P5: mỗi tool một tab + tab "Lệnh riêng"; form tham số của từng action được nhớ theo workspace khi chuyển trang (cache trong WorkspaceView). Lưu lỗi thì `Mutate` khôi phục danh sách như cũ.
- P5: gỡ tool xoá luôn profile cùng tool; bước bộ chạy tham chiếu tool/profile/lệnh bị gỡ cảnh báo trước, xử lý hiển thị "hỏng" ở P6. Chưa có test hành vi cho view-model (cần tham chiếu WPF trong test); kiểm tay ở P8: thêm/đổi/gỡ tool, chạy action có cổng (trùng cổng), lưu/chạy/xoá profile, thêm/sửa/xoá lệnh riêng.
- P6: thêm vào touches `Core/Workspaces/TaskKey.cs` (chuyển `TaskKey` từ Views xuống Core để StackRunner dùng, tránh lõi phụ thuộc giao diện) và `Views/CommandRows.cs` (bỏ `TaskKey`, thêm dòng VM của bộ chạy).
- P6: `StackRunner.Start` kiểm trước (bước hỏng, thư mục workspace/chạy, trùng cổng với tác vụ khác hoặc giữa các bước) rồi mới bật; lỗi kiểm trước trả run ở trạng thái Failed, không bật tiến trình nào. Bước có tác vụ cùng `TaskKey` đang chạy thì dùng lại (không bật lần hai, không tính trùng cổng).
- P6: bước lỗi/hết giờ chờ (cổng 120s) thì dừng và báo "Dừng ở bước n"; tác vụ đã bật được giữ để xem log. `StackRun.Stop()` thôi chờ và chỉ dừng tác vụ do lần chạy đó bật. Chờ `exit` không có hạn giờ (huỷ bằng Dừng). Chờ cổng thử TCP 127.0.0.1 rồi ::1.
- P7: thêm vào touches `Views/TaskRows.cs` (TaskItem/TaskItems/TaskActions dùng chung cho Tác vụ và Tổng quan), `Core/Workspaces/TaskBoard.cs` (quy tắc thuần: thứ tự, nhãn trạng thái, cổng, thời gian, dòng thanh trạng thái) và `tests/Toolbox.Tests/TaskBoardTests.cs`. Phần test hành vi này là cách hạ warning "risk high chưa có test" của gate; giao diện vẫn kiểm tay ở P8.
- P7: MainWindow dùng enum `Area` (Library/Overview/Tasks/Workspace) và `GoTo(area, row)`. Panel log là overlay bên phải, mở từ trang Tác vụ, Tổng quan; log đọc từ bộ nhớ (`TaskInfo.Lines`), cập nhật 250ms, tác vụ cửa sổ riêng hiện thông báo không có log. Trang Tác vụ và Tổng quan làm mới mỗi giây khi đang hiện.
- P8: lần chạy gate đầu FAIL ở `RunTrackerTests.Stop_DungTienTrinhDangChay_DanhDauDaDung` (assert `Changed >= 2` đọc trước khi event kết thúc được bắn; `WaitUntilStopped` chờ `Current == null` mà `Changed` cuối bắn sau khi gỡ run khỏi danh sách). Lỗi nằm ở test (race), không ở `RunTracker.cs`. Sửa: chờ đủ 2 event bằng SpinUntil rồi mới assert. Thêm `RunTrackerTests.cs` vào touches của P8 để gate không báo SCOPE_VIOLATION.
- P6: tab "Bộ chạy" nằm trước "Lệnh riêng" (`TabRow.Id`: kind / "" / ":stacks"). `StackEditWindow` kiểm mỗi bước bằng `StackResolver` (cùng bộ kiểm lúc chạy). Kết quả lần chạy chỉ lưu trong bộ nhớ, mất khi đóng app. Test: 25 ca `StackRunnerTests`, toàn bộ 172 ca xanh. Chưa kiểm tay giao diện (thêm/sửa/xoá/chạy/dừng bộ chạy, bước hỏng) — để P8.
