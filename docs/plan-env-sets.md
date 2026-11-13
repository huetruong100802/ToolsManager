# Kế hoạch: nhóm biến theo workspace (giống Library › Variable groups của ADO)

Lập 2026-11-13, sửa cùng ngày: giao diện bám ADO. Trạng thái: **đề xuất, chờ duyệt** (chưa chạy phase nào).

## 1. Yêu cầu

- 2026-11-10: "Phần env/secret gì đó là người dùng họ phải tự cài, agent không có luồng này. Về cơ bản UI/UX nó sẽ giống ADO, gần gũi, dễ quản lý each workspace."
- 2026-11-13: "bổ sung chức năng thêm biến môi trường (có thể qua mcp hoặc user set sẵn trên UI để agent attach vào Lệnh như một profile riêng)"; "giao diện nên giống ADO".
- Ví dụ gốc: cùng một lệnh chạy với connectionString A/B, lệnh khác chạy với X/Y.
- Đã kiểm chứng: biến môi trường điều khiển được `dotnet build/run`. Trong cấu hình .NET, env thắng `secrets.json`; chỉ tham số dòng lệnh mới thắng env.

## 2. Đối chiếu ADO → Toolbox

| ADO | Toolbox |
|---|---|
| Pipelines › Library › Variable groups | Tab thứ tư **"Biến"** trong workspace (cạnh Chạy, Workflow, Chia sẻ) |
| Danh sách group: Name, Date modified, Description; nút "+ Variable group" | Danh sách nhóm: Tên, Sửa lần cuối, Mô tả, Số biến, Đang dùng; nút chính "+ Nhóm biến"; ô lọc |
| Editor: Properties (name, description) + Variables (Name / Value / 🔒) + "+ Add" | Giống hệt: khối Thuộc tính và khối Biến; bảng Tên / Giá trị / 🔒; "+ Thêm" |
| Thanh lệnh: Save, Clone, Security, Pipeline permissions, Approvals and checks | Lưu (Ctrl+S, chỉ bật khi có thay đổi), Nhân bản, Xoá. "Pipeline permissions" thành công tắc **"Cho agent dùng nhóm này"** |
| Bấm 🔒 đổi biến thành secret; secret hiện `******`; đã lưu thì **không xem lại được**, chỉ ghi đè | Giống ADO. Trước khi lưu xem được giá trị đang gõ; sau khi lưu chỉ còn nút "Đổi giá trị" |
| Pipeline › Variables › "Link variable group" | ProfileSheet có mục **"Nhóm biến"**, nút "Liên kết nhóm biến" để chọn nhóm |
| `$(var)` trong task | Lệnh chạy qua `cmd`, nên `%TEN_BIEN%` trong tham số dùng được sẵn. Secret dùng cách này sẽ lộ trong danh sách process, UI cảnh báo |

Phác thảo:

```
Chạy   Workflow   Chia sẻ   [Biến]
────────────────────────────────────────────────────────────
Nhóm biến                               [ Lọc… ] [+ Nhóm biến]
Tên        Mô tả              Biến  Đang dùng   Sửa lần cuối
Dev-A      DB local           3     2 profile   hôm nay 10:21
Staging    DB staging         3     1 profile   12/11
────────────────────────────────────────────────────────────
Biến › Dev-A                    [Lưu] [Nhân bản] [Xoá]   ● chưa lưu
Thuộc tính
  Tên   [Dev-A            ]   Mô tả [DB local          ]
  [x] Cho agent dùng nhóm này
Biến                                                    [+ Thêm]
  Tên                         Giá trị                   🔒
  ASPNETCORE_ENVIRONMENT      Development               🔓
  ConnectionStrings__Default  ********  [Đổi giá trị]   🔒
  Jwt__Key                    (Cần nhập)                🔒  ← nhập từ gói chia sẻ
```

## 3. Quyết định (mặc định đã chọn)

| # | Câu hỏi | Chọn |
|---|---|---|
| D1 | Phạm vi | Nhóm biến thuộc workspace. Chưa có nhóm dùng chung toàn máy. |
| D2 | Gắn vào đâu | `Profile.VariableGroupId`, mỗi profile một nhóm. Stack dùng profile nên kế thừa luôn. `run` type=action/custom qua MCP nhận thêm `variableGroup`. |
| D3 | Agent qua MCP | **Đã chốt 2026-11-13: "người dùng tự cài, agent chỉ gắn vào profile qua mcp".** Agent chỉ liệt kê nhóm (tên, mô tả, tên biến, cờ secret, đã có giá trị hay chưa), **không đọc được giá trị nào**, kể cả biến thường. Agent gắn hoặc bỏ gắn nhóm vào profile, và chạy lệnh kèm nhóm. Agent không tạo, sửa, xoá nhóm. Nhóm tắt "Cho agent dùng" thì agent không thấy, không gắn, không chạy được. |
| D4 | Lưu secret | DPAPI `ProtectedData`, scope CurrentUser, có entropy riêng. JSON chỉ chứa base64 đã mã hoá. Thêm `<Reference Include="System.Security" />` (BCL của net48, không thêm DLL). |
| D5 | Thiếu giá trị secret | Bước chạy báo hỏng: "Nhóm biến X chưa có giá trị cho KEY, nhập ở tab Biến". |
| D6 | Truyền vào process | Qua `ProcessStartInfo.Environment`. Không qua tham số, không ghi file tạm. Biến của nhóm ghi đè biến cùng tên kế thừa từ Toolbox. |
| D7 | Che log | Giá trị secret (dài từ 4 ký tự) xuất hiện trong output hoặc log được thay bằng `***`, giống ADO. |
| D8 | Chia sẻ | Xuất tên biến và giá trị thường. Secret chỉ xuất tên; người nhận thấy "Cần nhập". |
| D9 | Phê duyệt ("Approvals and checks") | Chưa làm. |

Ràng buộc giữ nguyên: một file exe, .NET Framework 4.8, chỉ thêm trường JSON, không đổi tên công cụ MCP.

## 4. Rủi ro kỹ thuật

- **Chế độ cửa sổ riêng** (`TaskSpec.Window`) dùng `UseShellExecute = true`, nên `Environment` bị bỏ qua. Đổi sang `UseShellExecute = false` không redirect cũng không được: khi Toolbox chạy `--mcp` (stdio), cmd con kế thừa pipe và đọc trộm kênh MCP. Cách làm: P/Invoke `CreateProcessW` với `CREATE_NEW_CONSOLE | CREATE_UNICODE_ENVIRONMENT`, `bInheritHandles = false`, rồi `Process.GetProcessById` để `TaskHost` vẫn theo dõi Exited và KillTree như cũ. Cửa sổ riêng không che được secret trong output.
- `TaskSpec.Env` giữ plaintext trong bộ nhớ. MCP (`run_status`, `logs`, `get`) không được serialize trường này.
- `Restart` dùng `Spec.Clone()`, nên chạy lại dùng giá trị cũ. Chấp nhận; tooltip của nút Chạy lại ghi rõ điều này.
- Thêm tab thứ tư đi ngược nguyên tắc UX v2 "ba khái niệm trên màn hình". Ở đây làm theo yêu cầu giống ADO.

## Giai đoạn
- [x] P1 Model VariableGroup + DPAPI + chia sẻ — touches: src/Toolbox/Core/Workspaces/Workspace.cs, src/Toolbox/Core/Workspaces/VariableGroups.cs, src/Toolbox/Core/Workspaces/WorkspacePack.cs, src/Toolbox/Core/PackageImporter.cs, src/Toolbox/Toolbox.csproj, tests/Toolbox.Tests/VariableGroupTests.cs — kind: code — model: sonnet — effort: high — risk: medium — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P2 Đưa biến vào process + che log — deps: P1 — touches: src/Toolbox/Core/Workspaces/TaskHost.cs, src/Toolbox/Core/Workspaces/StackRunner.cs, src/Toolbox/Core/Workspaces/ConsoleLauncher.cs, src/Toolbox/Views/WorkspaceView.xaml.cs, tests/Toolbox.Tests/VariableRunTests.cs — kind: code — model: opus — effort: high — risk: high — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P3 MCP: liệt kê nhóm (chỉ đọc), profile.variableGroup, run — deps: P1, P2 — touches: src/Toolbox/Mcp/McpServer.Generic.cs, src/Toolbox/Mcp/McpServer.Setup.cs, src/Toolbox/Core/Workspaces/Workspace.cs, src/Toolbox/Core/Workspaces/StackRunner.cs, tests/Toolbox.Tests/McpVariableGroupTests.cs — kind: code — mode: direct — risk: high — exit: `dotnet build tests/Toolbox.Tests/Toolbox.Tests.csproj --no-restore -p:RunAnalyzers=false`
- [x] P4 UI kiểu ADO: tab Biến + liên kết nhóm trong profile + README — deps: P1, P3 — touches: src/Toolbox/Views/VariablesPage.xaml, src/Toolbox/Views/VariablesPage.xaml.cs, src/Toolbox/Views/ProfileSheet.xaml, src/Toolbox/Views/ProfileSheet.xaml.cs, src/Toolbox/Views/WorkspaceView.xaml, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Views/CommandRows.cs, src/Toolbox/Core/Workspaces/WorkspaceStore.cs, README.md — mode: direct — risk: high — exit: `dotnet build src/Toolbox/Toolbox.csproj --no-restore -p:RunAnalyzers=false`
- [~] P5 Kiểm thử toàn bộ — deps: P1, P2, P3, P4 — touches: src/Toolbox/Core/Agents/ActivityUndo.cs, src/Toolbox/Core/Workspaces/Workspace.cs, tests/Toolbox.Tests/VariableGroupTests.cs, tests/Toolbox.Tests/WorkspaceTabsTests.cs — kind: verify — model: sonnet — effort: low — exit: `dotnet test tests/Toolbox.Tests/Toolbox.Tests.csproj`
hot-files: src/Toolbox/Core/Workspaces/Workspace.cs, src/Toolbox/Views/WorkspaceView.xaml.cs, src/Toolbox/Toolbox.csproj
commit: none
workdir: C:/Users/HueTruong/Work/Repositories/Researchs/ToolsManager

### Nội dung và nghiệm thu từng phase

**P1. Model + lưu trữ**
- `VariableGroup { id, label, description, allowAgent (mặc định true), modifiedAt, variables }` và `GroupVariable { name, value, secret, enc }` trong `Workspace.cs`.
- `Workspace.VariableGroups` (`variableGroups`, Order 12, EmitDefaultValue=false). `Profile.VariableGroupId` (`variableGroup`, Order 8, EmitDefaultValue=false). `OnDeserialized` khởi tạo list null.
- `VariableGroups.cs`:
  - `Protect/Unprotect` bằng DPAPI.
  - `Validate`: tên không rỗng, không chứa `=` hoặc ký tự điều khiển, không trùng (không phân biệt hoa thường), tối đa 255 ký tự.
  - `Warnings` cho `PATH`, `Configuration`, `Platform`, `OutDir`, `TOOLBOX_DATA`.
  - `Resolve(ws, groupId)` trả về dictionary plaintext hoặc lỗi D5.
  - `UsedBy(ws, groupId)` trả về danh sách profile đang dùng nhóm.
- `WorkspacePack` và `PackageImporter` theo D8.
- Test:
  - File cũ không có `variableGroups` vẫn đọc được.
  - Protect/unprotect round-trip.
  - JSON đã ghi không chứa plaintext secret.
  - Validate đúng các luật trên.
  - Pack không có giá trị secret.

**P2. Chạy với biến**
- `TaskSpec.Env` và `TaskSpec.Secrets` (không serialize).
- `StackRunner`: ở 3 chỗ dựng `TaskSpec` từ profile (`ResolveAction`, `ResolveBase`, nhánh profile custom) gọi `VariableGroups.Resolve`; lỗi thì `Broken`. `ResolveAdHoc` và `ResolveCustom` nhận `groupId` tuỳ chọn.
- `TaskHost.CreateStartInfo`: chạy ngầm thì gán `Environment`. Cửa sổ riêng có env thì đi qua `ConsoleLauncher` (mục 4). Không có env thì giữ nguyên đường cũ.
- `OnLine` và `OpenLog`: che giá trị secret.
- `WorkspaceView.xaml.cs` dòng 317/984/1083 tự dựng `TaskSpec`: chỗ nào chạy từ profile thì đi qua cùng helper.
- Test:
  - Profile liên kết nhóm `{FOO=bar}` chạy `echo %FOO%` ra `bar`.
  - Secret xuất hiện trong output thì `Lines()` và file log đều ra `***`.
  - Thiếu secret thì bước báo hỏng đúng thông báo D5.
  - Profile không liên kết nhóm thì chạy y như trước.
- Kiểm tay: cửa sổ riêng hiện được `%FOO%`; Dừng đóng cả cây process; chạy `--mcp` stdio thì cửa sổ riêng không làm hỏng phiên MCP.

**P3. MCP**
- `search/get` nhận `type=variablegroup` và trả về id, tên, mô tả, danh sách `{name, secret, hasValue}`. Không có trường giá trị.
- `save/delete` với `type=variablegroup` bị từ chối: "Nhóm biến do người dùng tạo ở tab Biến trong Toolbox".
- `allowAgent = false`: nhóm không có trong `search`; `get/run` và việc gắn vào profile báo không tìm thấy.
- `save type=profile` và `create_profile/update_profile` nhận `variableGroup` (id hoặc tên, chuỗi rỗng là bỏ gắn). Nhóm còn biến chưa có giá trị thì vẫn gắn được, nhưng kết quả trả về kèm cảnh báo.
- `get type=profile` trả về tên nhóm đang gắn. `run` nhận `variableGroup` cho action/custom.
- `overview` đếm số nhóm agent được dùng. `GenericInstructions` thêm một câu tóm tắt D3.
- Hoàn tác: sửa profile đã có trong `ActivityUndo`, trường mới đi theo snapshot của profile. Viết test để xác nhận, không cần sửa `ActivityUndo`.
- Test:
  - Đặt một giá trị thường và một secret đã biết, tìm hai chuỗi đó trong mọi JSON trả về: không được thấy.
  - `save/delete type=variablegroup` bị từ chối và workspace không đổi.
  - Nhóm `allowAgent = false` không hiện ra và không gắn được.
  - Agent gắn nhóm vào profile, chạy qua MCP: process có env. Hoàn tác thì profile trở về không gắn nhóm.
  - `run_status/logs` không có trường Env.

**P4. UI kiểu ADO** (direct: chạy app, chụp màn hình ở cả hai theme)
- `VariablesPage`: danh sách và editor như phác thảo ở mục 2. Thêm vào `TabStrip` tab "Biến" (`WorkspaceView.xaml:157`).
- Hành vi editor:
  - Cờ "● chưa lưu"; rời trang khi còn thay đổi thì hỏi.
  - Ctrl+S lưu; nút Lưu chỉ bật khi có thay đổi.
  - Bấm 🔒 đổi biến thường thành secret và ngược lại. Đổi secret về thường thì xoá giá trị cũ, giống ADO.
  - Cảnh báo tên biến dễ xung đột hiện ngay dưới dòng.
  - Xoá nhóm đang được dùng thì liệt kê các profile đang dùng và chặn xoá.
- `ProfileSheet`: mục "Nhóm biến" với nút "Liên kết nhóm biến". Đã liên kết thì hiện tên nhóm, nút "Bỏ liên kết" và link "Mở nhóm" (sang tab Biến).
- `CommandRows`: lệnh đã ghim có liên kết nhóm thì hiện chip tên nhóm. Nhóm còn biến "Cần nhập" thì chip màu cảnh báo.
- README: mục "Nhóm biến".
- Kịch bản nghiệm thu:
  1. Tạo nhóm "Dev-A" có `ConnectionStrings__Default` (secret), liên kết vào profile `dotnet run`, chạy: app đọc chuỗi A.
  2. Liên kết sang "Dev-B", chạy: app đọc chuỗi B.
  3. Kết nối agent: agent liệt kê thấy "Dev-A" và "Staging", gắn "Staging" vào profile, chip trên lệnh đổi theo, chạy được. Tắt "Cho agent dùng" ở "Staging" thì agent không còn thấy nhóm này.
  4. Sau khi lưu, không cách nào xem lại giá trị secret trên UI.

**P5. Kiểm thử**: chạy cả bộ test một lần. Test nào FAIL thì sửa và ghi lại ở mục Điều chỉnh.

## Ngoài phạm vi

- Nhóm dùng chung toàn máy; liên kết nhiều nhóm vào một profile (ADO cho phép, bản này chỉ một).
- Liên kết Key Vault; Secure files; Approvals and checks.
- Cú pháp `$(var)` riêng của Toolbox (đã có `%VAR%` của cmd).
- Đọc hoặc ghi `secrets.json` / `.env` của repo.
- Env cho tool mở từ thư viện (`Runner.cs`), cho quét capability, cho worktree và cho agent runner.

## Điều chỉnh
- 2026-11-13: user chốt D3, "người dùng tự cài, agent chỉ gắn vào profile qua mcp". P3 bỏ phần tạo/sửa nhóm qua MCP và bỏ `ActivityUndo.cs` khỏi touches.
- 2026-11-13: giữ P2/P3 `risk: high` với `exit` chỉ build, có chủ đích: test hành vi của P2/P3 viết cùng phase nhưng chạy một lượt ở P5 (quy tắc "implement hết rồi kiểm thử một lượt"); kiểm tay cửa sổ riêng ở P4.
- 2026-11-13: user yêu cầu "tự làm đi, không chia subagent nữa". P2 hết 25 lượt (PARTIAL), phiên chính làm nốt rồi gate PASS. Từ P3 trở đi `mode: direct`.
- 2026-11-13: TOUCH-MISS: Workspace.cs, StackRunner.cs — `run type=custom` kèm `variableGroup` phải đi qua bộ chạy để còn `waitReady`/`port`; `StartResolved` không chờ cổng. Thêm `StackStep.RunGroupId` (`[IgnoreDataMember]`, như `Answers`) và `ResolveCustomStep` đọc nó. Lúc lập plan chưa thấy vì chưa đọc `SessionRuns.StartResolved`.
- 2026-11-13: D3 đọc nghiêm: `LaunchStack` (mọi lần chạy qua MCP) từ chối nếu bước dùng profile có liên kết nhóm `allowAgent=false`, vì D3 ghi "không chạy được". Thông báo không nêu tên nhóm. Nếu user muốn nới (profile do user liên kết thì agent vẫn chạy được) thì bỏ kiểm tra này.
- 2026-11-13: TOUCH-MISS P4: `WorkspaceStore.cs` — thêm `MergeList(existing.VariableGroups, …)` vào `MergeFrom` (xử lý RISK của P1): không có nó, nhóm do tiến trình khác ghi ra đĩa sẽ bị GUI ghi đè mất ở lần lưu kế. Một dòng, theo đúng cách các danh sách khác được gộp.
- 2026-11-13: P4 làm khác plan hai chỗ: (1) `ProfileSheet` chỉ tạo profile mới nên không có "Bỏ liên kết/Mở nhóm": ô **Nhóm biến** là ComboBox ở bước cuối (chỉ hiện khi workspace có nhóm); với profile đã ghim, liên kết/bỏ/mở nhóm làm ở menu ⋯ › Nhóm biến và bấm chip. (2) Rời **workspace** (không phải tab) khi nhóm đang sửa dở không hỏi, vì `MainWindow` ngoài touches; ghi ở README mục Giới hạn. `WorkspaceView.Mutate` cũng hoàn tác `VariableGroups` và `VariableGroupId` khi lưu lỗi.
- 2026-11-13: P4 kiểm tay bằng UI Automation (scratch, không commit): app khởi động được, tab Biến hiện ở sáng và tối, chip nhóm vàng ở tab Chạy, sửa → "● chưa lưu" + Lưu bật, thêm secret + Ctrl+S → `workspaces.json` chỉ có `enc` (DPAPI), không có plaintext, sau lưu chỉ còn `********` + "Đổi giá trị". Chưa kiểm tay: chạy lệnh thật với nhóm A rồi B (kịch bản 1-2), agent thật (kịch bản 3); test tự động của P2/P3 chạy ở P5.
- 2026-11-13: P5 lần 1: 11/710 FAIL, phân loại: (a) **lỗi code P3** `ActivityUndo.Normalize(Profile)` sao chép tay từng trường và bỏ sót `VariableGroupId`: agent gắn nhóm thì không có dữ liệu hoàn tác, và hoàn tác lần sửa khác của agent (vd. đổi tên) trên profile đang có nhóm làm mất liên kết; đã thêm trường vào bản sao (profile không nhóm sinh JSON y cũ nên băm hoàn tác đã ghi vẫn khớp). (b) **lỗi code P1** `Workspace.VariableGroups` ghi `"variableGroups": [ ]` cho mọi workspace dù chưa dùng nhóm (EmitDefaultValue=false chỉ bỏ null): đổi sang thuộc tính proxy `VariableGroupsJson` trả null khi rỗng. (c) **lỗi test P1** `Save_Load_…AllowAgentThieuLaBat` thay chuỗi compact `"allowAgent":true,` trong khi file ghi có thụt lề: đổi sang regex. (d) 3 test `WorkspaceTabsTests` cố định ba tab: cập nhật thành bốn tab (đổi tên `BonTab…`, `CycleTab_QuayVongBonTab`). (e) 5 lỗi `Unable to remove the file to be replaced` (cả `ActivityUndoTests`, `RunApprovalsAskTests` vốn có) và `RunCustomKemNhom…` (15 giây): chạy riêng các lớp đó thì qua, nên là tranh chấp file/tải khi cả bộ chạy song song, không phải lỗi logic; xem kết quả lần 2.
- 2026-11-13: P5 lần 2 (gate hết 2/2 lượt, **chưa đánh [x]**): chạy song song còn 7/710 FAIL (5 test tiến trình "không kết thúc" và 2 lỗi `File.Replace`). Đối chứng: full suite trên worktree `HEAD` sạch (663 test, không có thay đổi của feature) cũng FAIL đúng `RunTrackerTests.Stop_DungTienTrinhDangChay_DanhDauDaDung` và `StackParallelTests.Run_BuocTrongNhomLoi_DungOBienNhom_KhongKillBuocCungNhom`, nên flake tiến trình là sẵn có trên máy này. Chạy tuần tự (`-- xUnit.ParallelizeTestCollections=false`) trên cây hiện tại: **709/710 pass**, test còn lại là `Unable to remove the file to be replaced` ở `ActivityUndoTests.BoChayVaLenhRieng_HoanTacDuoc`; mỗi lần chạy là một test khác nhau (`RunApprovalsAskTests`, `McpVariableGroupTests`…) và đều pass khi chạy riêng, nên là flake của `File.Replace` (hay gặp khi trình quét file giữ handle), không phải logic. Việc còn lại cho người duyệt: chạy lại `dotnet test` khi máy rảnh, và cân nhắc gia cố `File.Replace` bằng thử lại ngắn ở `WorkspaceStore.Save` / `ListFile.Save` (ngoài phạm vi feature này).
- P1: CONTRACT: `VariableGroups.Resolve(Workspace ws, string groupId) -> ResolvedVariables { Dictionary<string,string> Env (OrdinalIgnoreCase); List<string> Secrets (>=4 ký tự, dài trước); string Error; bool Ok }`; groupId trống => Ok + Env rỗng; không ném ngoại lệ; lỗi D5 "Nhóm biến X chưa có giá trị cho KEY, nhập ở tab Biến". Còn có `VariableGroups.MaskSecrets(text, secrets)` (thay bằng `***`, hằng `VariableGroups.Mask`) cho P2.
- P1: CONTRACT: `UsedBy(ws,groupId)->List<Profile>`; `Find(ws,groupId)`; `Protect/Unprotect(string)` (DPAPI CurrentUser+entropy, Unprotect ném CryptographicException/FormatException); `SetSecret(GroupVariable, plain)` (rỗng => "Cần nhập"); `Validate(VariableGroup)->List<string>` (rỗng = hợp lệ); `ValidateName(name)->string|null`; `Warnings(group)->List<string>`, `WarningFor(name)`; `GroupVariable.NeedsInput` (secret chưa có Enc).
- P1: CONTRACT: secret chỉ nằm ở `GroupVariable.Enc` (Value luôn null); pack/import (D8) bỏ Value/Enc của secret, người nhận "Cần nhập"; pack tự kèm nhóm của profile đã chọn (WorkspacePackData.VariableGroups, Order 5); `WorkspaceImportResult.VariableGroupsAdded`. `VariableGroup.AllowAgent` mặc định true kể cả khi JSON thiếu (OnDeserializing).
- P1: RISK: `WorkspaceStore.MergeFrom` (ngoài touches) chưa gộp `VariableGroups` (chỉ profile theo id nên VariableGroupId được gộp); vì agent không sửa nhóm nên giữ bản bộ nhớ là đúng, nhưng nếu P3/P4 cần thì thêm MergeList. Chưa chạy test (theo yêu cầu), chỉ build tests csproj: thành công.
- P2: CÒN LẠI: subagent het 25 luot, chua bao cao; user yeu cau tu lam tiep P2 truc tiep
