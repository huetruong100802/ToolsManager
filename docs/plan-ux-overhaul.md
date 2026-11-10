# Kế hoạch: cải tổ UI/UX toàn diện (Tool Manager)

Lập 2026-11-10. Trạng thái: **đề xuất, chờ duyệt** (chưa chạy phase nào).
Nguồn yêu cầu: bản mô tả mục tiêu sản phẩm (3 vấn đề: quản lý công cụ, phối hợp và thực thi, chuẩn hoá và tái sử dụng; vai trò AI Agent qua MCP) và yêu cầu "giải quyết khuất mắc có thật, gắn với AI, dễ dùng, thân thiện, nhẹ, đẹp".
Liên quan: `docs/ux-capabilities.md` (nguyên tắc UX đã thống nhất), `docs/plan-v10.md` (trang Agent, tab Chạy, sơ đồ bộ chạy), README mục "Giới hạn đã biết".

## 1. Hiện trạng đã kiểm chứng

Kiểm bằng code và ảnh trong `docs/images/` (ảnh chụp 2026-11-04, cũ hơn hai commit gần nhất; chưa chạy app thật trong lượt lập plan này).

| # | Quan sát | Bằng chứng |
|---|---|---|
| H1 | GUI không tự cập nhật khi agent đổi dữ liệu, và có thể ghi đè thay đổi của agent | Không có `FileSystemWatcher` trong `src/Toolbox`; README "Cần sửa trước khi phát hành" |
| H2 | Tìm kiếm chỉ có trong từng trang: Ctrl+K chỉ tìm tool ở Thư viện, ô tìm profile riêng ở tab Chạy, ô tìm workspace riêng ở rail | `LibraryView.xaml.cs` (xử lý phím), `WorkspaceView.xaml`, `MainWindow.xaml` |
| H3 | Trang Tổng quan gần như trống: ba số đếm và danh sách workspace, không có việc để làm tiếp | `OverviewView.xaml` (86 dòng), `docs/images/overview.png` |
| H4 | 14 cửa sổ modal, 34 lần `ShowDialog()`, khoảng 80 lần gọi `MessageBox`/`Dialogs`. Tạo một profile có thể mở 3 lớp cửa sổ (Profile mới → Tham số → Khả năng) | Đếm bằng grep trong `src/Toolbox` |
| H5 | Từ vựng không thống nhất: "hậu tố", "khả năng", "biến thể", "tham số", "cặp", "lệnh riêng", "tool riêng", "bộ chạy" cùng tồn tại; tên app là Toolbox còn tài liệu mục tiêu gọi là Tool Manager và Workflow | Chuỗi trong `WorkspaceView.xaml`, `ToolEditWindow.xaml`, `CapabilitiesWindow.xaml` |
| H6 | Form Thêm tool hiện mọi trường cùng lúc (đường dẫn, lệnh, hậu tố đọc từ file, khoá JSON, lệnh trợ giúp, 4 chế độ đóng gói, bộ cài) | `ToolEditWindow.xaml` |
| H7 | Mỗi tool là một tab ngang trong workspace; workspace nhiều tool thì hàng tab tràn | `docs/images/workspace.png`, `WorkspaceView.xaml` |
| H8 | Trạng thái chạy nằm ở một dòng chữ dưới đáy ("Không có tác vụ chạy ngầm") và trang Tác vụ; không có thông báo khi tác vụ dài xong hoặc lỗi lúc cửa sổ bị che | `MainWindow.xaml` (`StatusText`); không có `NotifyIcon`/toast |
| H9 | Lõi đã có trạng thái từng bước của bộ chạy (`StepStatus` trong `StackRunner.cs`) nhưng chưa có "chạy lại từ bước lỗi" | grep `RetryFrom|FromStep` không có kết quả |
| H10 | Agent chỉ hiện ở trang Agent. Ở Thư viện và Workspace không thấy agent đang kết nối hay vừa đổi gì | `MainWindow.xaml` chỉ có `AgentsBadge` (số đề xuất chờ duyệt) |
| H11 | Chế độ chạy của agent chỉ có "tự chạy" và "không cho chạy"; hằng `RunModes.Ask` có trong `AgentStore.cs` nhưng README ghi chưa làm | README "Giới hạn đã biết" |
| H12 | Chỉ có giao diện sáng; màu dùng `StaticResource` nên chưa đổi theme lúc chạy được | `App.xaml` (25 brush, không có bản tối) |
| H13 | Năm `DispatcherTimer` chạy 1–3 giây ở các view để vẽ lại; danh sách chưa bật ảo hoá | grep `DispatcherTimer`, `VirtualizingStackPanel` |
| H14 | Đã có sẵn và nên giữ: tìm không dấu, kéo thả, hoàn tác thay đổi của agent, đóng gói theo workspace và `PackageImporter.ImportIntoWorkspace`, `AutomationProperties` ở hầu hết view, một file exe không DLL kèm | `Core/Agents/ActivityUndo.cs`, `Core/PackageImporter.cs`, `Toolbox.csproj` |

## 2. Khuất mắc của người dùng và hướng giải quyết

Mỗi dòng nối một khuất mắc với mục tiêu sản phẩm và với phase xử lý.

| Khuất mắc | Mục tiêu | Giải pháp | Phase |
|---|---|---|---|
| K1 "Tôi biết mình cần chạy gì nhưng phải nhớ nó nằm ở workspace nào, tab nào" | Giảm thời gian tìm kiếm | Bảng lệnh Ctrl+K toàn app: gõ tên tool, profile, workflow, workspace hoặc hành động rồi Enter để chạy. Kết quả xếp theo mức dùng gần đây | P5, P6 |
| K2 "Mở app lên không biết làm gì tiếp" (người mới và cả người cũ mỗi sáng) | Người mới dễ tiếp cận | Trang chủ thay Tổng quan: "Cần bạn xử lý", "Đang chạy", "Tiếp tục". Lần đầu mở có danh sách 3 bước | P7 |
| K3 "Bấm Chạy rồi không biết nó đang ở đâu, xong chưa, lỗi gì" | Theo dõi trạng thái và kết quả | Dock tác vụ luôn hiện ở đáy; workflow hiện trạng thái từng bước; thông báo Windows khi tác vụ dài kết thúc | P9, P10, P12 |
| K4 "Lỗi rồi thì làm gì tiếp?" | Giảm phụ thuộc kinh nghiệm cá nhân | Thẻ lỗi nhận dạng mẫu lỗi hay gặp và đưa nút hành động; "Chạy lại từ bước này"; "Sao chép cho agent" kèm log | P10, P11 |
| K5 "Cấu hình một tool phải hiểu quá nhiều khái niệm" | Dễ dùng | Một bộ từ vựng; form hai mức (cơ bản, nâng cao); side sheet thay chồng cửa sổ | P4, P13, P14 |
| K6 "Agent đổi gì tôi không thấy, sợ ghi đè lên nhau" | Người và agent cùng làm việc | Đồng bộ trực tiếp; dòng "Agent vừa… · Hoàn tác" ngay tại trang đang mở; chỉ báo agent đang kết nối | P1, P16 |
| K7 "Tôi muốn nhờ agent làm hộ nhưng phải tự viết prompt và giải thích ngữ cảnh" | Gắn với AI | Nút "Nhờ agent…" ở các điểm hay kẹt, sinh prompt có sẵn ngữ cảnh workspace; kết nối agent nằm trong 3 bước đầu | P17 |
| K8 "Tôi không dám cho agent tự chạy lệnh" | Tin cậy | Chế độ "Hỏi trước khi chạy": yêu cầu chạy vào hàng chờ, người dùng duyệt trên GUI | P18 |
| K9 "Nhận gói của đồng nghiệp về, chạy không được vì máy thiếu thứ gì đó" | Chuẩn hoá và tái sử dụng | Kiểm tra sẵn sàng sau khi nhập: thiếu tool, lệnh không có trên PATH, cần cài; mỗi dòng có nút sửa | P19, P20 |
| K10 "App trông cũ, chói mắt ban đêm, phải dùng chuột cho mọi thứ" | Nhẹ và đẹp | Token thiết kế v2, giao diện tối theo Windows, phím tắt đầy đủ, phản hồi bằng toast thay hộp thoại | P2, P3, P8 |

### AI ở đây nghĩa là gì

Toolbox là MCP server, không tự gọi mô hình. Vì vậy "gắn với AI" trong plan này gồm ba việc, không gồm nhúng khung chat vào app:

1. **Thấy được**: agent đang kết nối, vừa làm gì, ở đâu, và hoàn tác được (P1, P16).
2. **Giao việc được**: tại chỗ đang kẹt có sẵn prompt để dán vào agent, kèm ngữ cảnh (P17).
3. **Kiểm soát được**: phạm vi, quyền chạy, duyệt đề xuất, duyệt từng lần chạy (đã có một phần; thêm P18).

Nếu muốn app tự gọi mô hình (khung chat trong app), đó là quyết định sản phẩm riêng, xem D5.

## 3. Nguyên tắc thiết kế

Kế thừa 5 nguyên tắc ở `docs/ux-capabilities.md` mục 2, thêm:

6. **Một bấm để chạy thứ đã biết.** Từ bất kỳ màn nào, chạy một profile đã lưu mất tối đa Ctrl+K, gõ vài chữ, Enter.
7. **Không chồng cửa sổ.** Sửa trong ngữ cảnh bằng side sheet. Modal chỉ dùng cho xác nhận phá huỷ và chọn file.
8. **Phản hồi tại chỗ, hoàn tác thay cho hỏi lại.** Thao tác đảo được thì làm ngay rồi hiện toast có nút Hoàn tác. Chỉ hỏi trước khi thao tác không đảo được.
9. **Mỗi trạng thái trống có một việc để làm.** Trạng thái trống nêu việc tiếp theo và có nút.
10. **Agent là đồng nghiệp nhìn thấy được.** Mọi thay đổi của agent hiện ở nơi nó xảy ra, không chỉ ở trang Agent.
11. **Giữ nhẹ.** Vẫn một file exe trên .NET Framework 4.8, không thêm DLL. Mọi phase phải giữ ràng buộc này.

### Từ vựng chốt (áp dụng ở P4)

| Dùng | Thay cho | Ghi chú |
|---|---|---|
| Tool | tool, app | Một mục trong Thư viện |
| Lệnh | hậu tố, lệnh con, action | Việc tool làm được |
| Tham số | cờ, cặp, tham số | Giá trị truyền cho lệnh |
| Profile | profile | Một lệnh kèm tham số đã lưu |
| Workflow | bộ chạy | Theo tài liệu mục tiêu; xem D2 |
| Lệnh riêng | lệnh riêng, tool riêng | Lệnh chỉ có trong một workspace |
| Chia sẻ / Nhận | đóng gói / nhập gói | Tên hành động trên nút; định dạng file vẫn là `.toolbox` |

Tên công cụ MCP và tên trường JSON **không đổi** (hợp đồng với agent).

## 4. Kiến trúc thông tin đề xuất

```
Rail trái                       Nội dung
─────────────────────────────   ──────────────────────────────────────────
[Ctrl+K  Tìm hoặc chạy…]        Trang chủ: Cần bạn xử lý · Đang chạy · Tiếp tục
Trang chủ        (thay Tổng quan)
Thư viện                        Thư viện: như hiện tại, form thêm tool hai mức
Tác vụ      (số đang chạy)      Tác vụ: như hiện tại
Agent       (số chờ duyệt)      Agent: như hiện tại, thêm hàng chờ duyệt chạy
WORKSPACE                       Workspace: Chạy | Tool | Workflow | Chia sẻ
  ● DxFactory   ◉ agent           Tool = danh sách bên trái + chi tiết bên phải
  …                               (thay hàng tab mỗi tool một tab; xem D3)
─────────────────────────────   ──────────────────────────────────────────
Dock tác vụ (đáy): ● dev dxfac :4200  [Mở] [Log] [Dừng]   ● 2 đang chạy
```

Không bỏ trang nào đang có. Thay đổi cấu trúc duy nhất cần duyệt là D3 (tab tool trong workspace).

## 5. Quyết định cần bạn chốt

| # | Câu hỏi | Khuyến nghị | Ảnh hưởng |
|---|---|---|---|
| D1 | Tên hiển thị của app: Toolbox hay Tool Manager? | Giữ **Toolbox** (đuôi `.toolbox`, thư mục `%APPDATA%\Toolbox`, tên MCP đã phát hành cho đồng đội); chỉ đổi nếu bạn muốn | P4 |
| D2 | Đổi "Bộ chạy" thành "Workflow" trên giao diện? | Đổi, theo tài liệu mục tiêu | P4 |
| D3 | Workspace: gộp các tab tool thành một tab "Tool" dạng danh sách + chi tiết? | Làm, vì hàng tab tràn khi nhiều tool (H7). Đây là thay đổi luồng người dùng đang quen nên cần bạn đồng ý | P15 |
| D4 | "Chạy lại từ bước này" cho workflow (đang là đề xuất chờ duyệt ở `ux-capabilities.md` 4.6 và câu hỏi 2 mục 7)? | Làm | P10 |
| D5 | Có nhúng khung chat gọi mô hình trong app không? | **Không** trong plan này: thêm khoá API, phụ thuộc mạng, và trùng việc với Claude Code/Cursor đã kết nối qua MCP | Ngoài phạm vi |
| D6 | Thông báo Windows khi tác vụ dài kết thúc: bật mặc định? | Bật, chỉ khi cửa sổ không ở trước và tác vụ chạy trên 30 giây; có nút tắt | P12 |
| D7 | Chế độ "Hỏi trước khi chạy" (P18) đổi hợp đồng MCP của `run` (trả "đang chờ duyệt"). Có làm trong đợt này? | Làm sau cùng trong nhóm agent, vì rủi ro cao nhất | P18 |

Chưa chốt D3, D4, D7 thì P15, phần "chạy lại" của P10, và P18 không chạy; các phase khác không phụ thuộc.

## 6. Tiêu chí thành công

Không có telemetry nên đo bằng kịch bản thử tay ở P22, trên máy có DxFactory:

1. Chạy một profile đã lưu từ bất kỳ trang nào: không quá 3 thao tác bàn phím sau Ctrl+K, không dùng chuột.
2. Máy trắng dữ liệu (`TOOLBOX_DATA` trỏ thư mục rỗng): từ mở app tới chạy được lệnh đầu tiên trong một workspace chỉ theo hướng dẫn trên màn hình, không đọc README.
3. Nhận một gói workspace: sau khi nhập, màn hình nêu đủ thứ còn thiếu trên máy; sửa hết thì workflow chạy được.
4. Agent tạo một profile qua MCP khi GUI đang mở trang workspace đó: profile hiện ra trong 2 giây, có dòng báo kèm Hoàn tác, và GUI lưu thay đổi khác sau đó không làm mất profile.
5. Workflow 3 bước, bước 2 lỗi: thấy bước nào lỗi, mở log đúng bước, chạy lại từ bước 2.
6. Tạo profile mới không mở cửa sổ modal nào ngoài hộp chọn file.
7. Đổi Windows sang chế độ tối: app đổi theo, không còn chữ khó đọc (kiểm từng trang).
8. `Toolbox.exe` vẫn là một file, không DLL kèm; kích thước tăng không quá 150 KB so với hiện tại; thời gian mở tới khi thấy Thư viện không chậm hơn bản hiện tại (đo trước ở P21).

## 7. Nghiệm thu hành vi theo phase

Gate chỉ kiểm build. Các mục dưới đây thử tay ở P22 (phase `risk: high` bắt buộc có).

- **P1**: tiêu chí 4. Thêm: sửa tay `tools.json` bên ngoài khi app mở thì danh sách cập nhật; đang gõ dở trong một form thì không bị mất nội dung khi dữ liệu nạp lại.
- **P2**: tiêu chí 7. Mọi brush lấy từ token; không còn mã màu viết thẳng trong view.
- **P3**: xoá tool khỏi danh sách hiện toast có Hoàn tác trong 8 giây thay cho hộp hỏi; lỗi chạy tool hiện tại chỗ, không bật `MessageBox`. Xác nhận phá huỷ không đảo được (xoá bản chép trong thư viện) vẫn hỏi.
- **P6**: tiêu chí 1. Ctrl+K hoạt động ở mọi trang; Esc đóng; gõ không dấu vẫn khớp; Ctrl+F vẫn là tìm trong trang.
- **P7**: tiêu chí 2. Có đề xuất chờ duyệt, tác vụ lỗi hoặc tool mất đường dẫn thì nhóm "Cần bạn xử lý" liệt kê và bấm vào tới đúng nơi.
- **P10**: tiêu chí 5. Bước đang chờ cổng hiện thời gian đã chờ.
- **P13**: tiêu chí 6. Lệnh sẽ chạy trong sheet khớp lệnh thực sự chạy (so với log).
- **P15**: workspace 8 tool không cần cuộn ngang; tab Chạy không đổi hành vi.
- **P18**: agent ở chế độ hỏi gọi `run` thì nhận trạng thái chờ; duyệt trên GUI thì tác vụ chạy, từ chối thì agent nhận lý do; agent ở chế độ tự chạy không đổi hành vi.
- **P19, P20**: tiêu chí 3. Gói không chứa đường dẫn tuyệt đối của máy gửi (giữ acceptance P16 của plan-v10).

## Giai đoạn

Thứ tự: nền tảng tin cậy và hệ thiết kế trước (P1–P4), rồi tìm và chạy (P5–P8), theo dõi (P9–P12), cấu hình (P13–P15), agent (P16–P18), chia sẻ (P19–P20), hiệu năng và kiểm thử (P21–P22). `touches` của P9 trở đi phải kiểm lại bằng symbol trước khi dispatch, vì P2–P4 sửa trên diện rộng.

- [x] P1 Đồng bộ trực tiếp: `StoreWatcher` theo dõi tools.json, workspaces.json, proposals.json, activity.jsonl (gộp sự kiện 300 ms); GUI nạp lại trước mỗi lần ghi và khi file đổi, giữ lựa chọn và form đang mở — mode: direct — kind: code — risk: high — touches: src/Toolbox/Core/StoreWatcher.cs, src/Toolbox/Core/ToolStore.cs, src/Toolbox/Core/Workspaces/WorkspaceStore.cs, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/AgentsView.xaml.cs, tests/Toolbox.Tests/StoreWatcherTests.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P2 Token thiết kế v2 và giao diện tối: tách App.xaml thành Themes/Tokens.Light.xaml, Tokens.Dark.xaml, Controls.xaml; brush chuyển sang DynamicResource; thang chữ, khoảng cách, bo góc; theo cài đặt Windows, có nút chọn Sáng/Tối/Theo hệ thống — mode: direct — kind: code — risk: medium — deps: P1 — touches: src/Toolbox/App.xaml, src/Toolbox/App.xaml.cs, src/Toolbox/Themes, src/Toolbox/Core/Theme.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/Views, src/Toolbox/AddToolWindow.xaml, src/Toolbox/AddWorkspaceWindow.xaml, src/Toolbox/AgentWindow.xaml, src/Toolbox/AppPickerWindow.xaml, src/Toolbox/CapabilitiesWindow.xaml, src/Toolbox/CapabilityImportWindow.xaml, src/Toolbox/CheckListWindow.xaml, src/Toolbox/CommandEditWindow.xaml, src/Toolbox/ImportWindow.xaml, src/Toolbox/NewProfileWindow.xaml, src/Toolbox/PairsWindow.xaml, src/Toolbox/PromptWindow.xaml, src/Toolbox/StackEditWindow.xaml, src/Toolbox/ToolBindingWindow.xaml, src/Toolbox/ToolEditWindow.xaml, src/Toolbox/WorktreeSetupWindow.xaml — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P3 Phản hồi tại chỗ: `ToastHost` (có nút hành động, Hoàn tác), `EmptyState`, thanh lỗi trong trang; thay các `MessageBox` thông báo và xác nhận đảo được; giữ hộp hỏi cho thao tác không đảo được — mode: direct — kind: code — risk: medium — deps: P2 — touches: src/Toolbox/Views/ToastHost.xaml, src/Toolbox/Views/ToastHost.xaml.cs, src/Toolbox/Views/EmptyState.xaml, src/Toolbox/Views/EmptyState.xaml.cs, src/Toolbox/Dialogs.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/Views/LibraryView.xaml, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/AgentsView.xaml.cs, src/Toolbox/Views/TasksView.xaml.cs, src/Toolbox/Views/TaskRows.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P4 Từ vựng thống nhất theo bảng mục 3 trên mọi chuỗi giao diện và README; không đổi tên công cụ MCP, trường JSON, tên file — mode: direct — kind: code — risk: low — deps: P3 — touches: src/Toolbox, README.md, docs/ux-capabilities.md — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P5 Chỉ mục tìm và mức dùng: `SearchIndex` gom tool, profile, workflow, lệnh riêng, workspace, hành động; xếp hạng không dấu + gần đây + tần suất; `UsageStore` ghi usage.json khi chạy — mode: isolated — kind: code — model: sonnet — effort: high — risk: medium — deps: P1 — touches: src/Toolbox/Core/SearchIndex.cs, src/Toolbox/Core/UsageStore.cs, tests/Toolbox.Tests/SearchIndexTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P6 Bảng lệnh Ctrl+K toàn app (lớp phủ trong MainWindow): Enter chạy, Ctrl+Enter mở nơi chứa, gợi ý gần đây khi ô trống; ô "Tìm hoặc chạy…" đầu rail; Ctrl+K ở Thư viện chuyển cho bảng lệnh, Ctrl+F giữ tìm trong trang; ghi mức dùng khi chạy — mode: direct — kind: code — risk: high — deps: P5, P3 — touches: src/Toolbox/Views/CommandPalette.xaml, src/Toolbox/Views/CommandPalette.xaml.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/Views/LibraryView.xaml, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P7 Trang chủ thay Tổng quan: nhóm "Cần bạn xử lý" (đề xuất chờ duyệt, tác vụ lỗi, tool và workspace mất đường dẫn), "Đang chạy" (cổng bấm mở được), "Tiếp tục" (ghim và gần đây, lấy từ UsageStore); lần đầu mở hiện 3 bước: thêm workspace, nhận tool gợi ý, kết nối agent — mode: direct — kind: code — risk: medium — deps: P5, P3 — touches: src/Toolbox/Views/OverviewView.xaml, src/Toolbox/Views/OverviewView.xaml.cs, src/Toolbox/Core/HomeFeed.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, tests/Toolbox.Tests/HomeFeedTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P8 Bàn phím: Ctrl+1..4 đổi trang, Ctrl+Tab đổi tab workspace, F5 chạy mục đang chọn, Shift+F5 dừng, Esc đóng sheet, viền focus thấy rõ, thứ tự Tab hợp lý; bảng phím tắt mở bằng F1 — mode: direct — kind: code — risk: low — deps: P6 — touches: src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/Themes/Controls.xaml, src/Toolbox/Views/ShortcutsSheet.xaml, src/Toolbox/Views/ShortcutsSheet.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P9 Dock tác vụ ở đáy thay dòng trạng thái: mỗi tác vụ đang chạy là một chip (tên, cổng bấm mở trình duyệt, Log, Dừng); tác vụ vừa lỗi giữ chip đỏ tới khi xem; mở log bằng InspectorView sẵn có — mode: direct — kind: code — risk: medium — deps: P3 — touches: src/Toolbox/Views/RunDock.xaml, src/Toolbox/Views/RunDock.xaml.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/Views/InspectorView.xaml, src/Toolbox/Views/InspectorView.xaml.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P10 Workflow khi chạy: trạng thái từng bước ngay trên thẻ workflow ở tab Chạy và trên sơ đồ (đang chạy, chờ cổng kèm thời gian, xong, lỗi); "Mở log bước lỗi"; "Chạy lại từ bước này" bằng `StackRunner` nhận chỉ số bước bắt đầu (cần D4) — mode: direct — kind: code — risk: high — deps: P9 — touches: src/Toolbox/Core/Workspaces/StackRunner.cs, src/Toolbox/Views/StackDiagram.xaml, src/Toolbox/Views/StackDiagram.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/CommandRows.cs, tests/Toolbox.Tests/StackParallelTests.cs, tests/Toolbox.Tests/StackRunFromTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P11 Lỗi kèm việc tiếp theo: `FailureHints` nhận dạng từ mã thoát và đuôi log (cổng đang bận, không tìm thấy lệnh, thiếu node_modules, thư mục chạy không còn, bị từ chối quyền) → thẻ lỗi có lời giải thích, nút hành động, và "Sao chép cho agent" (lệnh, thư mục tương đối, 40 dòng log cuối) — mode: isolated — kind: code — model: sonnet — effort: high — risk: medium — deps: P9 — touches: src/Toolbox/Core/Workspaces/FailureHints.cs, src/Toolbox/Views/InspectorView.xaml, src/Toolbox/Views/InspectorView.xaml.cs, src/Toolbox/Views/TasksView.xaml, src/Toolbox/Views/TasksView.xaml.cs, tests/Toolbox.Tests/FailureHintsTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P12 Thông báo Windows: tác vụ chạy trên 30 giây kết thúc hoặc lỗi khi cửa sổ không ở trước → nháy taskbar và thông báo (NotifyIcon của System.Windows.Forms đã tham chiếu sẵn); bấm vào mở log; có công tắc trong cài đặt (cần D6) — mode: direct — kind: code — risk: low — deps: P9 — touches: src/Toolbox/Core/Notifier.cs, src/Toolbox/Core/Settings.cs, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/App.xaml.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P13 Side sheet trong workspace: khung sheet bên phải; chuyển Profile mới, Tham số của profile và Lệnh của tool (cửa sổ Khả năng) vào sheet; ô "Lệnh sẽ chạy" luôn hiện; xoá ba cửa sổ modal tương ứng sau khi chuyển hết nơi gọi — mode: direct — kind: code — risk: high — deps: P4, P8 — touches: src/Toolbox/Views/SideSheet.xaml, src/Toolbox/Views/SideSheet.xaml.cs, src/Toolbox/Views/ProfileSheet.xaml, src/Toolbox/Views/ProfileSheet.xaml.cs, src/Toolbox/Views/CommandsSheet.xaml, src/Toolbox/Views/CommandsSheet.xaml.cs, src/Toolbox/NewProfileWindow.xaml, src/Toolbox/NewProfileWindow.xaml.cs, src/Toolbox/PairsWindow.xaml, src/Toolbox/PairsWindow.xaml.cs, src/Toolbox/CapabilitiesWindow.xaml, src/Toolbox/CapabilitiesWindow.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/PairEditor.xaml.cs, tests/Toolbox.Tests/PairEditorUiTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P14 Form Thêm tool hai mức: mức cơ bản chỉ có Đường dẫn hoặc Lệnh, Tên, Mô tả, Tag, tự nhận loại từ thứ người dùng thả vào; mức nâng cao gập lại chứa nguồn lệnh, lệnh trợ giúp, cách chia sẻ, bộ cài; lỗi nhập hiện cạnh ô — mode: direct — kind: code — risk: medium — deps: P4 — touches: src/Toolbox/ToolEditWindow.xaml, src/Toolbox/ToolEditWindow.xaml.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P15 Trang workspace bốn tab cố định Chạy, Tool, Workflow, Chia sẻ; tab Tool là danh sách bên trái (tool đã gắn và Lệnh riêng) cùng chi tiết bên phải, thay hàng tab mỗi tool một tab (cần D3) — mode: direct — kind: code — risk: high — deps: P13 — touches: src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/CommandRows.cs, src/Toolbox/Views/PackTab.xaml — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P16 Agent hiện diện: chấm agent đang kết nối cạnh workspace ở rail và ở đầu trang workspace; khi activity.jsonl có dòng mới thuộc trang đang mở thì hiện toast "Agent X vừa… · Xem · Hoàn tác" (dùng ActivityUndo sẵn có) và đánh dấu "mới" trên mục đó tới khi được xem — mode: direct — kind: code — risk: medium — deps: P1, P3, P15 — touches: src/Toolbox/Core/Agents/AgentPresence.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/WorkspaceRow.cs, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/LibraryView.xaml.cs, tests/Toolbox.Tests/AgentPresenceTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P17 Nhờ agent: `AgentPrompts` sinh prompt theo ngữ cảnh (thiết lập workspace mới, đọc lệnh của một tool, dựng workflow từ mô tả, tìm nguyên nhân tác vụ lỗi); nút "Nhờ agent…" ở trạng thái trống của workspace, sheet Lệnh, tab Workflow, thẻ lỗi; chưa có agent nào kết nối thì dẫn tới Kết nối agent — mode: direct — kind: code — risk: low — deps: P11, P13, P15 — touches: src/Toolbox/Core/Agents/AgentPrompts.cs, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/CommandsSheet.xaml.cs, src/Toolbox/Views/InspectorView.xaml.cs, src/Toolbox/Views/OverviewView.xaml.cs, tests/Toolbox.Tests/AgentPromptsTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P18 Hỏi trước khi chạy: agent ở chế độ ask gọi run thì yêu cầu vào hàng chờ và trả trạng thái chờ duyệt; GUI hiện ở Trang chủ, trang Agent và toast với Cho chạy, Luôn cho lệnh này, Từ chối; agent hỏi kết quả bằng run_status. Đầu phase đọc `RunApprovals.cs` và `McpServer.Agents.cs` để xác định phần đã có (cần D7) — mode: direct — kind: code — risk: high — deps: P16 — touches: src/Toolbox/Core/Agents/RunApprovals.cs, src/Toolbox/Core/Agents/AgentStore.cs, src/Toolbox/Mcp/McpServer.Agents.cs, src/Toolbox/Mcp/McpServer.Generic.cs, src/Toolbox/Views/AgentsView.xaml, src/Toolbox/Views/AgentsView.xaml.cs, src/Toolbox/Views/OverviewView.xaml.cs, src/Toolbox/Core/HomeFeed.cs, tests/Toolbox.Tests/RunApprovalsTests.cs, tests/Toolbox.Tests/McpAgentTests.cs, README.md — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P19 Kiểm tra sẵn sàng: `Readiness` kiểm một workspace (thư mục còn không, tool mất đường dẫn, lệnh gốc không có trên PATH, tool cần cài, profile trỏ lệnh không còn) và trả danh sách kèm cách sửa; hiện ở đầu tab Chạy và ở Trang chủ — mode: isolated — kind: code — model: sonnet — effort: high — risk: medium — deps: P7, P15 — touches: src/Toolbox/Core/Workspaces/Readiness.cs, src/Toolbox/Views/ReadinessPanel.xaml, src/Toolbox/Views/ReadinessPanel.xaml.cs, tests/Toolbox.Tests/ReadinessTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P20 Chia sẻ và Nhận một luồng: nút Chia sẻ ở Thư viện và workspace cùng một hộp xem trước "người nhận sẽ có gì"; Nhận gói có profile hoặc workflow thì hỏi workspace đích (hoặc tạo mới từ thư mục), nhập xong mở ReadinessPanel — mode: direct — kind: code — risk: medium — deps: P19 — touches: src/Toolbox/ImportWindow.xaml, src/Toolbox/ImportWindow.xaml.cs, src/Toolbox/Views/PackTab.xaml, src/Toolbox/Views/PackTab.xaml.cs, src/Toolbox/Views/LibraryView.xaml, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/MainWindow.xaml.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P21 Nhẹ: đo thời gian mở và bộ nhớ trước khi sửa; bật ảo hoá cho danh sách tool, profile, tác vụ, nhật ký; thay các DispatcherTimer vẽ lại mỗi giây bằng sự kiện từ TaskBoard và StoreWatcher (giữ một đồng hồ cho thời lượng đang chạy); nạp icon và quét lệnh sau khi hiện cửa sổ — mode: direct — kind: code — risk: medium — deps: P16, P20 — touches: src/Toolbox/Core/Workspaces/TaskBoard.cs, src/Toolbox/Views/LibraryView.xaml, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/Views/TasksView.xaml, src/Toolbox/Views/TasksView.xaml.cs, src/Toolbox/Views/OverviewView.xaml.cs, src/Toolbox/Views/AgentsView.xaml, src/Toolbox/Views/AgentsView.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/ShellIcon.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [ ] P22 Kiểm thử một lượt: chạy toàn bộ test, thử tay 8 tiêu chí mục 6 và nghiệm thu mục 7 trên app thật, chụp lại ảnh README, cập nhật README và "Giới hạn đã biết" — mode: direct — kind: verify — deps: P21 — exit: `dotnet test tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore`
hot-files: src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/App.xaml, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/LibraryView.xaml.cs
commit: none
workdir: C:/Users/HueTruong/Work/Repositories/Researchs/ToolsManager

### Mốc bàn giao

| Mốc | Phase | Người dùng nhận được |
|---|---|---|
| M0 Nền tảng | P1–P4 | Không còn ghi đè với agent; giao diện tối; toast và hoàn tác; một bộ từ vựng |
| M1 Tìm và chạy | P5–P8 | Ctrl+K, Trang chủ, phím tắt |
| M2 Theo dõi | P9–P12 | Dock tác vụ, trạng thái workflow, gợi ý khi lỗi, thông báo |
| M3 Cấu hình | P13–P15 | Side sheet, form hai mức, workspace bốn tab |
| M4 Agent | P16–P18 | Thấy agent, nhờ agent, duyệt từng lần chạy |
| M5 Chia sẻ | P19–P20 | Kiểm tra sẵn sàng, một luồng chia sẻ và nhận |
| M6 Hoàn thiện | P21–P22 | Nhẹ hơn, đã thử tay, README và ảnh mới |

Mỗi mốc dùng được độc lập. Nên dừng lại thử tay sau M0 và M1 trước khi đi tiếp, vì hai mốc này đổi cảm giác dùng app nhiều nhất với rủi ro thấp nhất. P2 và P4 sửa trên diện rộng nên chạy khi không có nhánh nào khác đang mở.

## Ngoài phạm vi

- Khung chat gọi mô hình trong app (D5).
- Đồng bộ qua mạng, tài khoản, kho chia sẻ tập trung của team: chia sẻ vẫn bằng file `.toolbox`.
- Chuyển sang .NET mới, MVVM toàn bộ, hoặc thư viện giao diện ngoài: trái ràng buộc một file exe.
- Đổi tên công cụ MCP hoặc định dạng `tools.json`, `workspaces.json`.

## Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| P1 nạp lại làm mất thao tác đang dở | Nạp lại không đụng form đang mở; giữ lựa chọn theo id; test với file đổi liên tục |
| P2 đổi sang DynamicResource trên mọi XAML gây lệch giao diện khó thấy khi build | Thử tay từng trang ở cả hai theme; có thể dựng test STA theo mẫu `PairEditorUiTests` |
| P13 và P15 viết lại phần lớn `WorkspaceView` (1.148 dòng code-behind) | Chạy tuần tự, không song song; P13 xong mới P15; giữ nguyên các lớp dòng trong `CommandRows.cs` |
| P18 đổi hành vi `run` với agent đang dùng | Chỉ áp dụng cho agent đặt chế độ ask; chế độ tự chạy giữ nguyên; có test hợp đồng MCP |
| Kích thước exe tăng | Không thêm ảnh bitmap, không thêm DLL; kiểm ở P21 |

## Điều chỉnh

- 2026-11-10 (lượt thực hiện): P1–P21 đã làm trên nhánh `feature/ux-overhaul`, mỗi phase một commit, 632 test pass. **P22 chưa làm**: chưa thử tay 8 tiêu chí mục 6 trên app thật, chưa chụp lại ảnh README, README và "Giới hạn đã biết" chưa cập nhật (README còn nhắc ảnh `overview.png` cũ và chế độ "hỏi trước khi chạy" là chưa làm, nay đã có).
- Kích thước (tiêu chí 8): `Toolbox.exe` Release tăng 182.784 byte (650.240 → 833.024), vượt mức 150 KB của plan; vẫn một file, không DLL kèm. Bộ nhớ sau 3 giây mở app 93–94 MB so với 87–90 MB; thời gian tới khi thấy cửa sổ cùng cỡ (dao động 0,4–0,8 giây ở cả hai bản). CPU lúc rảnh còn cao hơn bản cũ trong mẫu đo ngắn (16–62 ms/5 giây so với 0–16), cần đo lại kỹ ở P22.
- P18: chế độ hỏi trước khi chạy và hàng chờ duyệt đã có sẵn trong mã từ trước; phase này chỉ thêm thông báo ba nút, "Luôn cho lệnh này", lý do khi bị từ chối. Hợp đồng `run` không đổi: agent gọi lại đúng lệnh sau khi được duyệt (không thêm `run_status` chờ duyệt).
- Test bật tiến trình thật nay chạy chung một collection xUnit để hết lỗi chập chờn do tranh CPU.
