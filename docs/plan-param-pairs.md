# Plan: tham số dạng cặp (key, value) và nguồn khám phá cấu hình được

Nguồn: trao đổi ngày 2026-11-06 sau `docs/plan-unify-tools.md`. Chưa thực thi; chờ người dùng duyệt phần "Câu hỏi mở".

## Vấn đề (đối chiếu code hiện tại)

Ví dụ chuẩn cần chạy được, không sửa code theo từng tool:

- `dotnet nuget locals all --clear`
- `dotnet run --project "Tools\X.csproj" -- --file "A.xlsx" --file "B.xlsx" --warehouse 3 --dry-run`
- `dotnet ef migrations add <Tên> --project P --startup-project P --context C`

Code hiện tại lệch ở các điểm sau (đã đọc nguyên văn):

1. **Tham số của profile là một chuỗi tự do.** `Profile.Vals["args"]` (`Workspace.cs`, `Profile.ExtraArgs`) nối thẳng vào lệnh (`CommandBuilder.LibraryCommandLine`, `JoinArgs`). Không có khái niệm key/value, flag, đối số vị trí, key lặp, hay "hỏi lúc chạy".
2. **Tham số kiểu `{key}` trong template.** `VariantDef.Template` + `ParamDef(Key, Prompt, Default, IsPort, Options)` và `CommandBuilder.Build` thay `{key}` bằng giá trị. Mỗi key một ô; không lặp được, không phân biệt flag với option.
3. **Hậu tố là một token.** `HelpParser.ParseSubcommands` chỉ nhận tên một từ (`CapabilityStore.SubcommandName`), `ToolDef.HelpCommand(suffix)` ghép `Command + suffix + " --help"`. `ef migrations add` (ba token) không biểu diễn được, và `--help` bị cố định trong code.
4. **Quét help chỉ biết kiểu GNU.** `HelpParser.FlagLine` đọc `-x`/`--name [value]`. Không đọc được `-c|--context <X>` của `dotnet ef`, mục `Arguments:` (đối số vị trí), hay `[default: False]`. Không có chọn parser, không có "Thử".
5. **Nguồn hậu tố chỉ có file/giá trị tĩnh.** `SuffixSource {file, jsonPath | pattern | values, template}` (đủ cho `package.json`); chưa có nguồn là lệnh do người dùng khai báo.

Đã có sẵn, tái dùng: `CapabilityScanner.Run` (chạy lệnh có timeout, đóng stdin, fingerprint), `CapabilityStore` (lưu kết quả quét theo toolId, giữ chỉnh tay khi quét lại), `CapabilitiesWindow`, `ValueSource`, `WorkspaceMigration` (idempotent, có backup).

## Thiết kế chốt (các phase sau coi đây là hợp đồng)

1. **Tham số = danh sách có thứ tự các `ParamPair { Key, Value, Ask }`.** `Key` trống + `Value` có = đối số vị trí; `Key` có + `Value` rỗng = flag; cả hai có = option. `--` chỉ là một cặp `Key="--"`, `Value` rỗng (các cặp sau nó thuộc app). Key lặp được (`--file` ×3). `Ask=true` = hỏi lúc chạy, không lưu giá trị.
2. **Action = chuỗi token cố định**, lưu thành một chuỗi có dấu cách (`ef migrations add`). `Profile.ActionId` và `StackStep.ActionId` giữ tên nhưng được phép nhiều token; không đổi schema, chỉ đổi cách kiểm tra tên và cách ghép lệnh.
3. **`Profile.Pairs` (JSON `"pairs"`, EmitDefaultValue=false)** thay vai trò của `Vals["args"]`. `ExtraArgs` giữ lại chỉ để đọc dữ liệu cũ; migration tách chuỗi cũ thành cặp (tách theo khoảng trắng có tôn trọng dấu `"`; nếu có dấu hiệu mơ hồ thì giữ nguyên một cặp `Key=""` với cả chuỗi làm `Value`, không đoán). `StackStep` tương tự.
4. **`CommandBuilder` dựng**: `base + tokens + từng cặp`. Value có khoảng trắng hoặc ký tự đường dẫn mà chưa có dấu `"` thì tự bọc `"…"`. Kiểm tra ký tự nguy hiểm (`CheckExtraArgs`) áp cho từng cặp, không đổi mức kiểm tra hiện có. Hành vi `{key}` cũ vẫn chạy cho profile loại preset để không vỡ dữ liệu.
5. **Nguồn khám phá (`DiscoverySource`)**, hai ô trên `ToolEntry`: `actionsSource` và `paramsSource`. Mỗi nguồn: `kind` = `command` | `file` | `manual`; `command` là template có `{base}`, `{tokens}`; `file/jsonPath/pattern/values/template` như `SuffixSource` hiện tại; `parser` = `auto` | tên định dạng | `regex` (+ `regex` do người dùng khai báo). `SuffixSource` cũ được đọc thành `actionsSource` kind `file` (tương thích ngược, không mất dữ liệu).
6. **Mặc định** (khi tool chưa khai báo): nguồn action = `{base} --help`, nguồn tham số = `{base} {tokens} --help`, parser `auto`. Mặc định này là hằng số chung, không phụ thuộc tên tool.
7. **Parser nằm ở mức định dạng, không bao giờ ở mức tool**: `gnu` (đang có), `system-commandline` (`-c, --clear`, `<ARG>`, `[default: …]`, mục `Arguments:`), `mcmaster` (`-c|--context <X>`), `regex` (người dùng khai báo). `auto` thử mọi định dạng, chọn cái ra nhiều mục nhất và nói rõ đã chọn cái nào.
8. **Quét lười theo action**: chỉ chạy lệnh tham số khi người dùng chọn action; kết quả cache trong `CapabilityStore` theo (toolId, tokens) kèm fingerprint để biết khi nào cũ. Chỉnh tay (`TakesValue`, `Role`, repeatable, ask, nhãn) lưu cùng chỗ và **không bị quét lại ghi đè** (cơ chế `ApplyScan` hiện có).
9. **Nút "Thử"** ở nơi sửa nguồn: chạy nguồn, hiện kết quả đã phân tích và đoạn gốc; chưa lưu cho đến khi người dùng bấm lưu. Timeout và đóng stdin như `CapabilityScanner.Run`.
10. **An toàn khi quét**: chỉ chạy lệnh nguồn của tool đã đăng ký trong thư viện; thư mục chạy = thư mục workspace/path của tool; có timeout; không ghép tham số của profile vào lệnh quét.
11. **UI profile**: "Thêm action" → chọn action (có tìm kiếm) → hiện các key gợi ý từ quét (bấm để thêm cặp) + "Thêm cặp tự do" → nhập value, bật "hỏi lúc chạy", kéo thứ tự → xem trước dòng lệnh đầy đủ. Không còn ô "args" tự do (còn dạng "chế độ văn bản" để dán cả dòng, tách thành cặp khi lưu).
12. **Không tên tool cụ thể trong code** (giữ A5 của plan trước). `ToolPresets`/`catalog.default.json` chỉ là dữ liệu mẫu có thể xoá; cấu hình nguồn (npm đọc `package.json`) nằm trong `ToolEntry`.

## Acceptance theo hành vi (đối chiếu ở P7)

- B1. Dựng và chạy đúng ba lệnh ví dụ trên bằng profile (không dùng chuỗi `args` tự do), kể cả `--file` lặp ba lần, đường dẫn có dấu cách, cặp `--` tách phần của app, và tên migration hỏi lúc chạy.
- B2. `dotnet`, `dotnet ef migrations add` quét ra đúng key/flag/đối số vị trí mà không viết code riêng cho dotnet; `npm` lấy action từ `package.json` bằng nguồn `file`.
- B3. Đổi lệnh lấy (ví dụ `{base} {tokens} --help` thành lệnh khác) và đổi parser trong UI, bấm "Thử" thấy kết quả đổi ngay, không cần build lại.
- B4. Tool không có `--help` chuẩn vẫn dùng được hoàn toàn bằng nhập tay.
- B5. Profile/stack cũ mở lên không mất gì; chạy lại cho ra đúng dòng lệnh cũ.
- B6. Quét lại không làm mất phần chỉnh tay.

## Giai đoạn
- [ ] P1 Hợp đồng dữ liệu: ParamPair, Profile.Pairs/StackStep.Pairs, DiscoverySource trên ToolEntry (đọc SuffixSource cũ), action nhiều token — touches: src/Toolbox/Core/ToolEntry.cs, src/Toolbox/Core/Workspaces/Workspace.cs, src/Toolbox/Core/Workspaces/ToolCatalog.cs, tests/Toolbox.Tests — kind: code — model: opus — effort: high — risk: high — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [ ] P2 Dựng lệnh từ cặp + quoting + migration chuỗi args cũ thành cặp — deps: P1 — touches: src/Toolbox/Core/Workspaces/CommandBuilder.cs, src/Toolbox/Core/Workspaces/WorkspaceMigration.cs, src/Toolbox/Core/Workspaces/WorkspaceStore.cs, tests/Toolbox.Tests/CommandBuilderPairsTests.cs, tests/Toolbox.Tests/WorkspaceMigrationTests.cs — kind: code — model: opus — effort: high — risk: high — exit: `dotnet test tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false --filter "FullyQualifiedName~CommandBuilderPairs|FullyQualifiedName~WorkspaceMigration"`
- [ ] P3 Engine khám phá: định dạng help (gnu, system-commandline, mcmaster, regex), auto, nguồn `command`, quét lười theo tokens, cache + giữ chỉnh tay — deps: P1 — touches: src/Toolbox/Core/Workspaces/Capabilities.cs, src/Toolbox/Core/Workspaces/ToolCatalog.cs, tests/Toolbox.Tests/HelpFormatTests.cs, tests/Toolbox.Tests/CapabilitiesTests.cs — kind: code — model: opus — effort: high — risk: medium — exit: `dotnet test tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false --filter "FullyQualifiedName~HelpFormat|FullyQualifiedName~Capabilities"`
- [ ] P4 Runtime theo cặp: StackRunner, WorkspaceEditor, TaskKey, WorkspacePack, PackageImporter, ActivityUndo, MCP, hỏi lúc chạy (`Ask`) — deps: P2 — touches: src/Toolbox/Core, src/Toolbox/Mcp, tests/Toolbox.Tests — kind: code — model: opus — effort: high — risk: high — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [ ] P5 UI profile: thêm action, gợi ý key từ quét, cặp tự do, hỏi lúc chạy, thứ tự, xem trước dòng lệnh, chế độ văn bản — deps: P3, P4 — touches: src/Toolbox/NewProfileWindow.xaml, src/Toolbox/NewProfileWindow.xaml.cs, src/Toolbox/StackEditWindow.xaml, src/Toolbox/StackEditWindow.xaml.cs, src/Toolbox/Views/StackDiagram.xaml.cs, src/Toolbox/Views/CommandRows.cs, src/Toolbox/Views/WorkspaceView.xaml.cs — kind: code — risk: high — mode: direct — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [ ] P6 UI nguồn khám phá trong ToolEditWindow + nút "Thử" + chọn parser — deps: P3 — touches: src/Toolbox/ToolEditWindow.xaml, src/Toolbox/ToolEditWindow.xaml.cs, src/Toolbox/CapabilitiesWindow.xaml, src/Toolbox/CapabilitiesWindow.xaml.cs — kind: code — risk: medium — mode: direct — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [ ] P7 Kiểm chứng: build sln, toàn bộ test, đối chiếu B1–B6 bằng ba lệnh ví dụ — deps: P5, P6 — touches: src/Toolbox, tests/Toolbox.Tests — kind: code — mode: direct — exit: `dotnet test tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [ ] P8 Tài liệu: README, plan-v10 Backlog, ux-capabilities — deps: P7 — touches: docs/plan-v10.md, docs/ux-capabilities.md, README.md — kind: code — mode: direct — exit: `git diff --stat -- docs README.md`
hot-files: src/Toolbox/Core/ToolEntry.cs, src/Toolbox/Core/Workspaces/Workspace.cs, src/Toolbox/Core/Workspaces/Capabilities.cs, src/Toolbox/Views/WorkspaceView.xaml.cs
commit: none
workdir: C:/Users/HueTruong/Work/Repositories/Researchs/ToolsManager

## Tiến độ (2026-11-06, một lượt, không subagent)

Đã làm và có test (`tests/Toolbox.Tests/ParamPairTests.cs`, 12 test; toàn bộ suite 405/406 qua, lỗi duy nhất `RunTrackerTests.Stop_*` là flaky: chạy riêng qua 16/16):
- `ParamPair`, `PairLine` (Render/Check/Parse/Pending/Resolve) trong `Core/Workspaces/ParamPairs.cs`; key lặp, `--`, flag, đối số vị trí, `--key=`, tự bọc nháy khi value có khoảng trắng.
- `Profile.Pairs`, `ExtraArgs` render từ cặp (profile cũ không có `pairs` chạy y như trước, nên không cần migration phá dữ liệu), `ArgsError`, `PendingAsks`, `WithAnswers`.
- `CommandBuilder.BuildWithPairs`; `StackResolver` dùng cặp ở cả ba đường (lệnh gốc, action, lệnh riêng), chặn chạy khi còn cặp `Ask` chưa có giá trị; `WorkspaceEditor` kiểm bằng `ArgsError`.
- Action nhiều token (`ef migrations add`): `CapabilityStore.SubcommandName`/`IsSuffix`.
- `HelpParser` đọc thêm `-c|--context <X>` (McMaster/dotnet-ef) và short flag 2 chữ (`-lp, --launch-profile`); kiểm với output thật của `dotnet run --help`, `dotnet ef migrations add --help`.
- `ToolEntry.HelpTemplate` (`{base}`, `{tokens}`) để đổi lệnh lấy help; mặc định `{base} {tokens} --help`.

Đã làm thêm (cùng ngày, 411/412 test qua; chỉ `RunTrackerTests.Stop_*` flaky):
- UI: `Views/PairEditor` (dòng `[key ▾] [giá trị] [Hỏi] ▲ ▼ ✕`, nút ＋ Thêm tham số, "Dán cả dòng…" tách theo `TakesValue`); gợi ý key đọc từ bảng khả năng của action (`PairEditor.SuggestionsFor`), vẫn gõ tự do. Gắn vào `NewProfileWindow` (thay ô "Tham số thêm"; ô `{args}` dạng chuỗi bị ẩn) và nút **Tham số…** trên profile đã lưu (`PairsWindow`; profile cũ chỉ có chuỗi args được tách thành cặp khi mở).
- Hỏi lúc chạy: `StackStep.Answers` (không lưu) + `StackResolver.Resolve` điền cặp `Ask`; UI hỏi từng ô qua `PromptWindow` trước khi chạy (`WorkspaceView.RunProfile`); profile tool có cặp giờ chạy qua resolver nên dùng đúng cặp.
- MCP: `pairs` trong `create_profile`/`update_profile`/`save` (mảng rỗng khi sửa = xoá), `get` trả `pairs`, `run_profile`/`run` nhận `answers`, thiếu thì báo lỗi kèm danh sách cần điền. `WorkspaceEditor` kiểm cặp (key sai bị từ chối).
- `WorkspacePack.CloneProfile`, `PackageImporter` (ghi đè), `ActivityUndo.Normalize`, nhân bản profile trong UI đều mang theo `Pairs` (có test).
- `ToolEditWindow`: ô "Lệnh lấy trợ giúp" cho `HelpTemplate`.

Đã làm thêm vòng sau (2026-11-06, 418/418 test qua):
- Thẻ lệnh ở trang tool cũng dùng `PairEditor`; nút **Quét gợi ý** ngay trong thẻ (`CapabilityScanner.ScanAndStore` chạy nền). Dropdown của lệnh con gồm cờ riêng + cờ chung của tool, nên lệnh nào cũng có gợi ý.
- Nguồn không phải quét: **Nhập…** trong "Khả năng…" (`CapabilityImportWindow`): JSON (`package.json` → `scripts`), file + regex (Makefile), dán danh sách/help; tiền tố cha (`run` + `build` = `run build`); mục `Source = "import"` được giữ qua các lần quét lại.
- Icon ▲ ▼ ✕ bị cắt vì Button mặc định `Padding="14,6"`; sửa bằng `Padding="0"`, có test dựng thật (`PairEditorUiTests`).
- Ô tìm kiếm dùng chung `Views/SearchField` (không phân biệt hoa thường/dấu): AddToolWindow, CheckListWindow, ImportWindow, NewProfileWindow (bước chọn tool), palette profile của bộ chạy, CapabilitiesWindow (khớp tên/mô tả, lệnh khớp qua cờ tự mở), `ParamOptionMenu` khi hơn 8 gợi ý. Dropdown key của `PairEditor` lọc khi gõ (tên + mô tả).
- Template `ComboBox` trong App.xaml trước đây **không có `PART_EditableTextBox`** nên `IsEditable` không gõ tự do được; đã thêm ô nhập.
- Đóng gói: tab Đóng gói của workspace đọc `LibraryBindings` (rỗng từ sau khi gộp "một loại tool") nên mục tool trống; nay liệt kê `Bindings` + toàn bộ tool trong thư viện (đánh dấu "chưa gắn vào workspace"). `Packer` không chép thư mục làm việc của tool dạng lệnh (có thể chứa `.env`), chỉ gửi thông tin.
- Test UI chạy chung một luồng STA và `ShutdownMode.OnExplicitShutdown`, vì đóng cửa sổ cuối làm tắt `Application` của test sau.
- Dán dòng có cờ chưa quét (`--project X --startup-project X --context Y`): token sau cờ lạ coi là value (`PairLine.Parse` với callback `entry == null || entry.TakesValue`); chỉ cờ đã quét là công tắc mới không nhận value.
- Quét/gợi ý theo **phạm vi vị trí**: các đối số vị trí đứng đầu dòng (`migrations add`) cộng id action tạo thành phạm vi (`ActionRow.ScopeOf`); nút Quét chạy `<base> <phạm vi> --help` và lưu dưới phạm vi đó, dropdown gồm cờ phạm vi + cờ action + cờ chung, tự nạp lại khi phạm vi đổi. Lý do: `--project` của `dotnet ef` chỉ có trong help của lệnh lá, không có ở `dotnet ef --help`.
- Gợi ý cho **đối số vị trí** (nút ▾ ở ô giá trị, dòng key trống), hoàn toàn từ help đã quét: mục `Arguments:` → `ToolCapabilities.Args` (tên, mô tả, `Choices`, `IsPath`, `Variadic`); cờ có `Choices` khi help liệt kê giá trị (`{a,b}`, `<a|b>`, "Allowed values are…", `q[uiet]` mở thành `quiet`; chỉ nhận khi mọi mục là một từ hợp lệ). `PositionalOptions.Resolve` tính theo chuỗi lệnh đứng trước: lệnh con của phạm vi kế tiếp + giá trị của đối số thứ n; đối số kiểu đường dẫn thì liệt kê file trong thư mục chạy (sâu 2 cấp, bỏ bin/obj/node_modules) và có nút "Chọn file…". Quét giờ luôn đọc lệnh con ở mọi phạm vi; sửa luôn tiêu đề `Commands:` trần và lệnh con kèm `<tham số>` (`dotnet new`). Chưa nối cho cửa sổ profile (`PairsWindow`, `NewProfileWindow`).
- Trang tool có ô tìm lệnh (hiện khi hơn 3 lệnh): lọc theo tên, id, tên biến thể; xoá khi đổi tab.

Chưa làm (không chặn dùng; làm tiếp khi cần):
- Mục `Arguments:` (đối số vị trí) chưa được quét vào bảng khả năng (`CapabilityStore.Load` lọc theo `IsSuffix`); hiện gõ key trống + giá trị.
- `DiscoverySource` đầy đủ (kind `command` chọn được parser/`regex` người dùng khai báo) và nút "Thử"; mới có `HelpTemplate` + parser tự nhận GNU/System.CommandLine/McMaster.
- Quét lười theo cây action (`dotnet ef` → `migrations` → `add`): hiện thêm action nhiều token bằng tay ở "Khả năng…" rồi quét riêng action đó.
- `StackEditWindow` chưa có trình sửa cặp (bộ chạy dùng profile nên sửa ở profile).

## Quyết định đã chốt (người dùng, 2026-11-06)

1. **Profile giữ một action, không gộp với Stack (bộ chạy).** "Append" là nút **(+)** trong trình sửa profile, không phải nối nhiều action. Dòng profile có dạng `[base] [action] [cặp] [cặp] … (+)`: bấm (+) thêm một cặp mới; ô key là dropdown gợi ý từ danh sách đã quét (cờ, tên option), chọn xong thì ô value hiện ra nếu cờ nhận giá trị (`TakesValue`), và luôn cho gõ tự do ngoài gợi ý. Mỗi cặp có nút xoá, bật/tắt "hỏi lúc chạy", kéo đổi thứ tự. Phía dưới là xem trước dòng lệnh đầy đủ.
2. **`Ask` hỏi ở cả hai nơi**: UI hiện hộp thoại nhập trước khi chạy; MCP/agent bắt buộc truyền giá trị trong lời gọi, thiếu thì báo lỗi (runner đã chặn chạy khi còn cặp chờ).
3. **Dán cả dòng lệnh**: dùng `TakesValue` đã quét để tách; chưa biết thì coi là flag (quyết theo đề xuất của Claude).
4. **Nguồn `command` mặc định là `--help`**, có thể sửa trong UI (`HelpTemplate` đã có trên `ToolEntry`); không thêm hộp xác nhận riêng. Người dùng tự chỉnh nếu mặc định sai.

## Điều chỉnh
- Lập plan: P3 và P4 độc lập nhau (P3 chạm Capabilities/ToolCatalog, P4 chạm Runner/MCP) nhưng cùng phụ thuộc hợp đồng P1; P5 chờ cả hai. P5 và P6 đều chạm `src/Toolbox` nhưng khác file; chạy `-MaxParallel 1` vì `WorkspaceView.xaml.cs` là hot-file.
