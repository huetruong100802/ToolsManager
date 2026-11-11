# Kế hoạch: UX v2 — chạy được việc thật trước, đẹp và AI sau

Lập 2026-11-11. Trạng thái: **đề xuất, chờ duyệt** (chưa chạy phase nào).
Kế thừa `docs/plan-ux-overhaul.md` (P1–P21 đã làm, P22 chưa). Plan này thay P22 bằng P13.
Nguồn yêu cầu: tóm tắt mục tiêu ở plan cũ (quản lý công cụ, phối hợp và thực thi, chuẩn hoá và tái sử dụng; agent qua MCP) và câu trả lời của người dùng ngày 2026-11-11 (mục 2).

## 1. Hiện trạng đã kiểm chứng

Chạy bản Debug trên bản sao `%APPDATA%\Toolbox` (18 tool, 2 workspace), chụp 17 màn hình ở cả hai theme và máy trắng.

| # | Quan sát | Bằng chứng |
|---|---|---|
| V1 | Dữ liệu thật có 0 profile, 0 workflow. Trang chủ, tab Chạy, Ctrl+K đều sống nhờ profile/workflow nên rỗng | `workspaces.json`: `profiles: []`, `stacks: []` ở cả hai workspace; Trang chủ chỉ hiện "Mọi thứ đang yên ổn" |
| V2 | Script npm hiện lệnh sẽ chạy là `npm build:docker:dxai`, thiếu `run`. Chưa bấm chạy để xác nhận | Ảnh tab Tool; `catalog.default.json:31` có template `npm run {id}` nhưng mục trong `capabilities/npm.json` có `Source: import` không đi qua template |
| V3 | Lần quét `npm --help` lưu lỗi "Không đọc được hậu tố nào" | `capabilities/npm.json`, trường `Scans[0].Error` |
| V4 | Hướng dẫn ở tab Chạy sai sau P15: "Mở tab của một tool, chọn biến thể…" | `WorkspaceView.xaml.cs:271`; "biến thể" còn 17 chỗ trong `src/Toolbox` |
| V5 | Tab Tool: mỗi lệnh một thẻ cao, lặp "Tham số / Thêm tham số / Dán cả dòng / Quét gợi ý"; một màn khoảng 5 lệnh; nút "Lệnh… Tool… Sửa…" khó đoán | Ảnh `ws-2.png` |
| V6 | Bốn ô tìm không có chữ gợi ý (tab Chạy, Chia sẻ, Tác vụ ×2); tab Workflow có ba nút cạnh tranh; Thư viện năm điều khiển mỗi dòng; banner "Cài vào máy" không đóng được | Ảnh `ws-1`, `ws-3`, `ws-4`, `tasks`, `library` |
| V7 | Ctrl+K khi ô trống chỉ có 7 hành động điều hướng, mỗi dòng ghi "Hành động" hai lần | Ảnh `palette.png` |
| V8 | Còn 15 file `*Window.xaml` và 35 `ShowDialog()` (trước plan cũ: 14 và 34) | Đếm bằng grep |
| V9 | Tab Chia sẻ liệt kê cả 15 tool chưa gắn vào workspace | Ảnh `ws-4.png` |
| V10 | AI trong app chỉ là nút sao chép prompt; agent duy nhất là "Toàn máy", không kết nối từ 06/11 | `AgentPrompts.cs`; ảnh `agents.png` |
| V11 | Đã ổn, giữ nguyên: giao diện tối, dock, toast, ba bước đầu ở máy trắng, đồng bộ file | Ảnh `theme-b*`, `empty-home` |

## 2. Người dùng nói gì (2026-11-11)

- Chưa hài lòng ở cả bốn nhóm: khó chạy được việc thật, giao diện chưa đẹp, quá nhiều khái niệm, AI chưa đúng kỳ vọng.
- AI: kết hợp "AI tự thiết lập giúp" với "expose MCP theo workspace". Tổng hợp và đăng ký tool thông minh không cần đi qua MCP, app gọi thẳng. MCP chỉ nên ở phạm vi workspace, để agent dùng tool và tương tác với workspace.
- Việc hằng ngày: chạy lệnh theo repo, chạy chuỗi nhiều bước, mở app/tool nhanh, chia sẻ cho đồng đội.
- "Quan trọng vẫn phải trực quan, dễ dùng."

## 3. Nguyên tắc v2

1. **Gắn tool xong là có thứ để bấm.** Không bắt tạo profile trước khi chạy.
2. **Ba khái niệm trên màn hình: Workspace, Lệnh, Workflow.** Profile hiện ra là "lệnh đã ghim" (lệnh kèm tham số đã lưu). Tham số, lệnh riêng, nguồn lệnh nằm sau một lần bấm.
3. **Một nút chính mỗi màn.** Hành động phụ vào menu "…" hoặc hiện khi rê chuột.
4. **App tự làm phần thiết lập.** Quét repo trước (không cần AI), AI bổ sung khi người dùng bấm, người dùng chỉ duyệt.
5. **Nghiệm thu bằng ảnh và kịch bản, không chỉ build.** Mỗi mốc chụp lại màn hình và đối chiếu mục 5.
6. Giữ ràng buộc cũ: một file exe, .NET Framework 4.8, không DLL kèm; không đổi trường JSON và tên công cụ MCP trong workspace.

## 4. Quyết định cần bạn chốt

| # | Câu hỏi | Đề xuất và căn cứ | Chặn phase |
|---|---|---|---|
| D1 | Gộp tab Tool vào tab Chạy? Trang workspace còn ba tab: Chạy (mọi lệnh, nhóm theo tool), Workflow, Chia sẻ. Quản lý tool gắn vào workspace chuyển vào menu "Tool của workspace…" | Gộp. Căn cứ: bạn chọn "quá nhiều khái niệm" và "khó chạy được việc thật". Đây là đổi luồng lần hai sau P15 nên cần bạn đồng ý | P5 |
| D2 | App gọi AI bằng cách nào? (a) gọi CLI agent có sẵn trên máy ở chế độ không tương tác, ví dụ `claude -p`; (b) gọi API bằng khoá do bạn nhập | (a). Không lưu khoá, không thêm DLL, dùng đúng tài khoản agent bạn đang có. Nhược: máy phải cài CLI | P9 |
| D3 | Agent "Toàn máy" qua MCP: bỏ hẳn, hay ẩn vào mục nâng cao? | Ẩn vào nâng cao, kết nối mới mặc định theo workspace; giao thức cũ vẫn chạy để không gãy agent đã đăng ký | P10 |
| D4 | Chữ "Profile" trên giao diện đổi thành "Lệnh đã ghim"? | Đổi trên giao diện, giữ `profiles` trong JSON và MCP | P5, P6, P7 |
| D5 | Hướng thị giác | P5 làm tab Chạy thành màn tham chiếu, dừng lại gửi ảnh sáng và tối để bạn duyệt trước khi áp cho các trang khác | P11 |

Chưa chốt D1 thì P5 trở đi không chạy. P1–P4 không phụ thuộc quyết định nào.

## 5. Tiêu chí thành công

Thử trên bản sao dữ liệu thật (`TOOLBOX_DATA`), chụp ảnh bằng `tools/shoot.ps1`.

1. DxFactory chưa có profile nào: mở workspace thấy ngay các script của `package.json`; bấm Chạy ở `build` thì log ghi lệnh `npm run build`.
2. Trang chủ với dữ liệu hiện tại có ít nhất: thẻ từng workspace kèm lệnh chạy được, và các tool đã ghim để mở nhanh.
3. Ctrl+K, gõ `dxfac`, Enter: chạy được một lệnh của DxFactory dù chưa lưu gì.
4. Thêm workspace từ một thư mục repo: sau khi chọn thư mục, tối đa hai lần bấm nữa là có lệnh chạy được.
5. Bấm "AI thiết lập" ở một workspace: không mở terminal, không dán prompt; nhận danh sách đề xuất, duyệt từng mục, mục đã duyệt xuất hiện ở tab Chạy.
6. Tab Chạy ở cửa sổ 1320×860 hiện được ít nhất 12 lệnh không cần cuộn.
7. Tab Chia sẻ chỉ liệt kê thứ thuộc workspace đó.
8. Kết nối agent từ trang workspace bằng một nút; agent chỉ thấy workspace đó.
9. Không còn ô nhập nào thiếu chữ gợi ý; không còn chuỗi "biến thể" hay "tab của một tool" trên giao diện.
10. Toàn bộ test pass; vẫn một file exe.

## 6. Nghiệm thu hành vi theo phase

- **P1**: tiêu chí 1. Lệnh `dotnet` và `git` đang chạy đúng thì không đổi (test so chuỗi lệnh trước và sau).
- **P4**: `QuickCommands` trả cùng một id cho cùng một lệnh qua hai lần gọi; workspace không có tool gắn trả danh sách rỗng, không ném lỗi.
- **P5**: tiêu chí 1, 6. Ghim một lệnh rồi mở lại app: lệnh vẫn ghim và nằm trong `profiles` của `workspaces.json`. Workflow đang trỏ tới profile cũ vẫn chạy.
- **P8**: tiêu chí 4. Thư mục không nhận ra gì thì vẫn thêm được workspace và có lối tới "AI thiết lập" hoặc thêm tay.
- **P9**: tiêu chí 5. Máy không có CLI agent: nút báo rõ thiếu gì và cách cài, không treo. Huỷ giữa chừng thì tiến trình con dừng. Không mục nào được áp dụng khi chưa duyệt.
- **P10**: tiêu chí 8. Agent "Toàn máy" đã đăng ký trước đó vẫn gọi được như cũ.
- **P12**: tạo và sửa lệnh riêng, sửa workflow, gắn tool không mở cửa sổ modal nào ngoài hộp chọn file.

## Giai đoạn

Thứ tự: sửa đúng (P1–P3), vòng lõi (P4–P7), thiết lập và AI (P8–P10), thị giác (P11–P12), kiểm thử (P13). `touches` của P5 trở đi phải kiểm lại bằng symbol trước khi dispatch.

- [x] P1 Sửa lệnh npm script và lỗi quét: tái hiện `npm build` thiếu `run` với mục `Source: import`, tìm nguyên nhân gốc trong đường dựng lệnh (`CommandBuilder`, `Capabilities`, `Preview` ở `CommandRows.cs`), sửa cả lệnh chạy lẫn dòng xem trước; lỗi quét hiện lời dễ hiểu kèm nút quét lại — mode: direct — kind: code — risk: high — touches: src/Toolbox/Core/Workspaces/CommandBuilder.cs, src/Toolbox/Core/Workspaces/Capabilities.cs, src/Toolbox/Core/Workspaces/ToolCatalog.cs, src/Toolbox/Views/CommandRows.cs, tests/Toolbox.Tests/CommandBuilderNpmTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P2 Chuỗi và nhãn: bỏ "biến thể" và hướng dẫn cũ ở tab Chạy; thêm chữ gợi ý cho bốn ô tìm bằng `SearchField` sẵn có; banner "Cài vào máy" có nút đóng và nhớ lựa chọn — mode: direct — kind: code — risk: low — deps: P1 — touches: src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/PackTab.xaml, src/Toolbox/Views/PackTab.xaml.cs, src/Toolbox/Views/TasksView.xaml, src/Toolbox/Views/TasksView.xaml.cs, src/Toolbox/Views/LibraryView.xaml, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/Views/CommandRows.cs, src/Toolbox/Core/Settings.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P3 Script chụp màn hình trong repo: `tools/shoot.ps1` chạy app với `TOOLBOX_DATA` trỏ bản sao, chụp từng trang ở hai theme và máy trắng vào thư mục ngoài repo; README thêm mục cách dùng — mode: direct — kind: code — risk: low — touches: tools/shoot.ps1, README.md — exit: `pwsh -NoProfile -Command "if (-not (Test-Path tools/shoot.ps1)) { exit 1 }"`
- [x] P4 Lõi lệnh chạy nhanh: `QuickCommands` liệt kê mọi lệnh chạy được của một workspace từ tool đã gắn (script, action của catalog, lệnh riêng, profile) với id ổn định và chuỗi lệnh cuối; `SearchIndex` nhận thêm nguồn này; `UsageStore` ghi mức dùng theo id — mode: isolated — kind: code — model: sonnet — effort: high — risk: medium — deps: P1 — touches: src/Toolbox/Core/Workspaces/QuickCommands.cs, src/Toolbox/Core/SearchIndex.cs, src/Toolbox/Core/UsageStore.cs, tests/Toolbox.Tests/QuickCommandsTests.cs, tests/Toolbox.Tests/SearchIndexTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P5 Tab Chạy mới, màn tham chiếu thị giác: danh sách lệnh dạng dòng gọn nhóm theo tool (tên, chuỗi lệnh mờ, Chạy, ghim, "…"); tham số mở trong sheet khi cần; lệnh đã ghim lên đầu; gộp tab Tool vào đây và chuyển quản lý tool gắn vào menu (cần D1, D4); xong thì chụp ảnh hai theme và dừng chờ duyệt (D5) — mode: direct — kind: code — risk: high — deps: P2, P4 — touches: src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/CommandRows.cs, src/Toolbox/Views/ProfileSheet.xaml, src/Toolbox/Views/ProfileSheet.xaml.cs, src/Toolbox/Views/ActionSheet.xaml, src/Toolbox/Views/ActionSheet.xaml.cs, src/Toolbox/Themes/Controls.xaml, src/Toolbox/MainWindow.xaml.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P6 Trang chủ có nội dung khi chưa lưu gì: thẻ từng workspace kèm lệnh ghim và lệnh dùng gần đây (thiếu thì lấy vài lệnh đầu của `QuickCommands`), hàng tool đã ghim để mở nhanh, giữ nhóm "Cần bạn xử lý" và "Đang chạy"; ghim tool từ Thư viện — mode: direct — kind: code — risk: medium — deps: P4, P5 — touches: src/Toolbox/Core/HomeFeed.cs, src/Toolbox/Views/OverviewView.xaml, src/Toolbox/Views/OverviewView.xaml.cs, src/Toolbox/Views/LibraryView.xaml, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/Core/ToolEntry.cs, src/Toolbox/ToolItem.cs, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml.cs, tests/Toolbox.Tests/HomeFeedTests.cs, tests/Toolbox.Tests/HomeViewTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P7 Ctrl+K chạy được lệnh chưa lưu: kết quả gồm lệnh từ `QuickCommands`; ô trống hiện lệnh ghim, gần đây và tool ghim trước các hành động điều hướng; mỗi dòng một nhãn loại, không lặp — mode: direct — kind: code — risk: medium — deps: P4, P5 — touches: src/Toolbox/Views/CommandPalette.xaml, src/Toolbox/Views/CommandPalette.xaml.cs, src/Toolbox/MainWindow.xaml.cs — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P8 Thêm workspace tự thiết lập (không AI): chọn thư mục xong hiện "Tìm thấy trong repo" từ `WorkspaceDetector.Detect` (tool, thư mục chạy, số lệnh), tích sẵn, một nút "Thêm và gắn"; chuyển từ cửa sổ modal sang sheet — mode: direct — kind: code — risk: medium — deps: P5 — touches: src/Toolbox/AddWorkspaceWindow.xaml, src/Toolbox/AddWorkspaceWindow.xaml.cs, src/Toolbox/Views/AddWorkspaceSheet.xaml, src/Toolbox/Views/AddWorkspaceSheet.xaml.cs, src/Toolbox/Core/Workspaces/WorkspaceDetector.cs, src/Toolbox/MainWindow.xaml.cs, tests/Toolbox.Tests/WorkspaceDetectorTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P9 AI thiết lập gọi thẳng: `AgentRunner` chạy CLI agent không tương tác với prompt từ `AgentPrompts` và yêu cầu trả JSON; kết quả vào `ProposalStore` rồi hiện để duyệt ngay trong sheet; có tiến độ, huỷ, giới hạn thời gian, báo thiếu CLI; lệnh gọi agent đặt được trong cài đặt (cần D2) — mode: direct — kind: code — risk: high — deps: P8 — touches: src/Toolbox/Core/Agents/AgentRunner.cs, src/Toolbox/Core/Agents/AgentPrompts.cs, src/Toolbox/Core/Agents/ProposalStore.cs, src/Toolbox/Core/Settings.cs, src/Toolbox/Views/AiSetupSheet.xaml, src/Toolbox/Views/AiSetupSheet.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, tests/Toolbox.Tests/AgentRunnerTests.cs — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P10 MCP theo workspace: nút "Kết nối agent" ở đầu trang workspace, mặc định giới hạn trong workspace đó; trang Agent nhóm theo workspace; lựa chọn "Toàn máy" chuyển vào mục nâng cao, agent cũ vẫn chạy (cần D3) — mode: direct — kind: code — risk: medium — deps: P5 — touches: src/Toolbox/AgentWindow.xaml, src/Toolbox/AgentWindow.xaml.cs, src/Toolbox/Views/AgentsView.xaml, src/Toolbox/Views/AgentsView.xaml.cs, src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, README.md — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P11 Áp kiểu màn tham chiếu cho các trang còn lại: Thư viện (dòng gọn, hành động phụ vào "…"), Workflow (một nút chính), Tác vụ, Chia sẻ (chỉ thứ thuộc workspace), Agent (cần duyệt ảnh P5 theo D5) — mode: direct — kind: code — risk: medium — deps: P6, P7, P10 — touches: src/Toolbox/Views/LibraryView.xaml, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/Views/TasksView.xaml, src/Toolbox/Views/PackTab.xaml, src/Toolbox/Views/PackTab.xaml.cs, src/Toolbox/Views/AgentsView.xaml, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Themes/Controls.xaml — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [x] P12 Bỏ cửa sổ modal mở từ trang workspace: lệnh riêng, gắn tool, sửa workflow chuyển sang sheet; xoá cửa sổ cũ sau khi chuyển hết nơi gọi và test tham chiếu — mode: direct — kind: code — risk: high — deps: P11 — touches: src/Toolbox/CommandEditWindow.xaml, src/Toolbox/CommandEditWindow.xaml.cs, src/Toolbox/ToolBindingWindow.xaml, src/Toolbox/ToolBindingWindow.xaml.cs, src/Toolbox/StackEditWindow.xaml, src/Toolbox/StackEditWindow.xaml.cs, src/Toolbox/Views, tests/Toolbox.Tests — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P13 Kiểm thử một lượt: chạy toàn bộ test, chụp lại mọi màn bằng `tools/shoot.ps1`, đối chiếu 10 tiêu chí mục 5 và nghiệm thu mục 6, thay ảnh README, cập nhật "Giới hạn đã biết"; đóng luôn P22 của plan cũ — mode: direct — kind: verify — deps: P9, P12 — exit: `dotnet test tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore`
hot-files: src/Toolbox/MainWindow.xaml, src/Toolbox/MainWindow.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/CommandRows.cs, src/Toolbox/Views/LibraryView.xaml.cs, src/Toolbox/Themes/Controls.xaml
commit: none
workdir: C:/Users/HueTruong/Work/Repositories/Researchs/ToolsManager

### Mốc bàn giao

| Mốc | Phase | Bạn nhận được | Dừng chờ bạn |
|---|---|---|---|
| M0 Sửa đúng | P1–P3 | Lệnh npm chạy đúng, hết chuỗi cũ, có script chụp ảnh | Không |
| M1 Vòng lõi | P4–P7 | Mở workspace là chạy được; Trang chủ và Ctrl+K có nội dung | Có: duyệt ảnh sau P5 |
| M2 Thiết lập và AI | P8–P10 | Thêm workspace tự gắn tool; AI thiết lập một bấm; MCP theo workspace | Có: thử P9 trên máy bạn |
| M3 Thị giác | P11–P12 | Mọi trang cùng một kiểu; hết chồng cửa sổ ở workspace | Không |
| M4 Kiểm thử | P13 | Ảnh mới, README đúng, đã đối chiếu tiêu chí | Không |

## Ngoài phạm vi

- Khung chat trong app.
- Đổi định dạng `tools.json`, `workspaces.json`, tên công cụ MCP trong workspace.
- Kho chia sẻ tập trung, đồng bộ qua mạng.
- Cắt kích thước exe về dưới ngân sách cũ (đang vượt khoảng 33 KB); xem lại sau P13.

## Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| P5 viết lại `WorkspaceView` lần ba (1.456 dòng code-behind) | Dựng danh sách từ `QuickCommands` của P4 thay vì sửa tiếp các lớp dòng cũ; workflow và profile cũ có test giữ hành vi |
| P1 sửa đường dựng lệnh làm lệch tool khác | Test so chuỗi lệnh của dotnet, git, lệnh riêng trước và sau |
| P9 phụ thuộc CLI ngoài: khác phiên bản, đầu ra không phải JSON, chạy lâu | Kiểm đầu ra chặt, lỗi thì hiện nguyên văn và không áp dụng gì; có giới hạn thời gian và huỷ; test bằng tiến trình giả |
| Đổi luồng lần hai làm bạn phải học lại | D1 và D4 chốt trước; dừng duyệt ảnh sau P5 |
| Gate chỉ kiểm build như lần trước | P3 đưa script chụp vào repo; M1 và M2 có điểm dừng thử tay |

## Điều chỉnh
- P2: TOUCH-MISS: src/Toolbox/Views/PackTab.xaml.cs, src/Toolbox/Views/TasksView.xaml.cs — SearchField.TextChanged là EventHandler(EventArgs), handler cũ nhận TextChangedEventArgs/RoutedEventArgs nên phải đổi chữ ký.
- P2: ACCEPT_UNCHANGED: operator explicitly accepted; no reason supplied
- P3: ACCEPT_UNCHANGED: operator explicitly accepted; no reason supplied
- P4: CÒN LẠI: Agent dừng do rate limit; có QuickCommands.cs, chưa có test, SearchIndex, UsageStore
- P4: CONTRACT: `QuickCommands.For(Workspace) -> IReadOnlyList<QuickCommand>` (Toolbox.Core.Workspaces, QuickCommands.cs); QuickCommand: Id, Title, Group (tên tool | "Lệnh riêng" | "Profile"), CommandLine, Source (QuickSource Script/Action/Custom/Profile), WorkspaceId, ToolId, Binding, ActionId, VariantId, RefId (custom/profile id), Cwd, Problem (null = chạy được), IsRunnable.
- P4: CONTRACT: Id = lowercase `ws:nguồn:...` (Script/Action: toolId:actionId:variantId; Custom/Profile: id). Lệnh gốc của tool có ActionId "" . Chạy lại: Script/Action -> StackResolver.ResolveAdHoc(ws, ToolId, ActionId, VariantId, null); Custom -> Spec custom; Profile -> StackResolver.Resolve với StackStep{Kind=profile.Kind, ProfileId=RefId}.
- P4: CONTRACT: `UsageStore.QuickKey(quickId)` = Key("quick", id); `SearchIndex.Build(tools, workspaces, actions, includeQuickCommands: false)`; bật thì thêm SearchEntry Kind=SearchKind.Quick (enum mới, cuối), TargetId = QuickCommand.Id, Key = QuickKey, Runnable = IsRunnable. Chỉ Script/Action được thêm; Custom/Profile đã có mục riêng nên không lặp.
- P4: Profile hỏng/cần nhập giá trị vẫn được liệt kê với Problem != null, CommandLine rỗng.
- 2026-11-11 Bạn chốt D1 gộp, D2 CLI agent có sẵn, D3 ẩn vào nâng cao, D4 đổi chữ trên giao diện. D5 giữ như đề xuất: dừng sau P5 để duyệt ảnh.
- 2026-11-11 TOUCH-MISS: src/Toolbox/Views/ActionSheet.xaml, ActionSheet.xaml.cs (mới) — tham số nằm trong sheet thì cần một sheet riêng cho lệnh; ProfileSheet là sheet tạo lệnh ghim, không dùng lại được.
- 2026-11-11 TOUCH-MISS: ToolItem.cs (glyph/tip nút ghim), MainWindow.xaml.cs (HomeTarget.OpenTool), WorkspaceView.xaml.cs (RunTarget/Reveal cho SearchKind.Quick để nút Chạy trên thẻ chạy được; P7 dùng lại), HomeViewTests.cs (trạng thái yên ổn nay chỉ khi không có workspace) — không thấy lúc lập plan.
- P12: Đã chuyển CommandEdit, ToolBinding, StackEdit sang sheet (Views/*Sheet) và xoá cửa sổ cũ; tách StepEditRow ra src/Toolbox/StepEditRow.cs vì StackDiagram dùng chung. Còn modal ngoài danh sách touches: ToolEditWindow (dùng chung với Thư viện), PromptWindow, StackDiagram.ShowDialog, WorktreeSetupWindow, CheckListWindow — xét ở P13 hoặc plan sau.
- P13: test 663 pass, ảnh chụp lại 40 ảnh, README thay 5 ảnh và cập nhật "Giới hạn đã biết"; đóng P22 plan cũ. Tiêu chí 5 (AI thiết lập với CLI thật) và 6 (12 lệnh ở 1320×860, ước 11–12) chưa kiểm được; 1–4, 7–10 đạt theo test/ảnh. Cần bạn thử P9 trên máy (mốc M2).
