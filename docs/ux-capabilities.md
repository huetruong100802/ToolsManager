# Khả năng tool, profile và bộ chạy: UX và thiết kế

Trạng thái: đề xuất, chờ duyệt. Mockup: `docs/mockup/workspace-overhaul-v5.html`.
Liên quan: `docs/plan-workspace.md` (các quyết định P1–P8). Những chỗ đề xuất đổi plan được liệt kê ở mục 5.

## 1. Vấn đề

Hiện Toolbox biết cách chạy npm và Angular bằng danh sách cố định hoặc đoán từ tên script. Nó không biết tool nhận tham số gì. Vì vậy form profile hoặc bị cứng, hoặc hiện mọi ô có thể, và cả hai đều rối khi số tool và app tăng.

Hướng mới: mỗi tool tự liệt kê khả năng của nó. Toolbox đọc danh sách đó trước, rồi mới cho cấu hình profile. Bước này chỉ đọc, không sửa gì.

Kiểm tra trên DxFactory (chỉ chạy lệnh liệt kê và trợ giúp):

| Tool | Liệt kê lệnh | Liệt kê tham số của một lệnh | Kết quả |
|---|---|---|---|
| npm | `npm run` (không đối số) | Không có. Tham số nằm trong thân script | Liệt kê 52 script được. Thân script đọc từ package.json |
| Angular CLI | `ng help` | `ng serve <project> --help` | Liệt kê lệnh được. Không kèm project thì không có cờ như `--port` |
| git | `git help -a` (203 dòng) | `git <lệnh> -h` (chưa kiểm định dạng) | Liệt kê lệnh được. Tham số chưa kiểm |
| .NET | `dotnet --help` in trợ giúp của host runtime, không phải danh sách lệnh SDK | Chưa kiểm | Chưa xác nhận |

Ví dụ thật, `ng serve dxfac --help`:
- Có `--port` (mặc định 0), `-c/--configuration`, `-o/--open` (mặc định true), `--dev`, `--rebuild-delay` (mặc định 2000), `--watch`, `--ssr`, `--target`…
- Không có `--host`.
- Script `start:dxfac` đã đặt `--port 3000` nên mặc định thực tế là 3000, không phải 0.
- `build:staging:dxfac` có bước phụ `node ./projects/dxfac/version.js` trước `ng build`. Profile không được bỏ bước này.

## 2. Nguyên tắc UX

1. Không đoán ngầm. Mọi tham số hiện kèm nguồn: `--help`, `script`, `angular.json`, hoặc `bạn khai`. Tham số `bạn khai` được đánh dấu là không kiểm tra.
2. Chỉ đề nghị thứ tool nhận. Cờ không có trong danh sách không xuất hiện trong form. Người dùng vẫn xem được danh sách đầy đủ ở phần "Khả năng".
3. Đọc rẻ trước. Tên lệnh và thân script đọc ngay khi mở workspace (chỉ đọc file). Lệnh trợ giúp chỉ chạy khi người dùng mở một lệnh hoặc bấm khám phá, để không bật nhiều tiến trình lúc mở app.
4. Lỗi phải kèm việc tiếp theo. Ví dụ: "Chưa có node_modules. Chạy `npm install` trong Micro-Frontend." Toolbox không tự cài.
5. Đường dẫn chỉ hiện ở tab Chi tiết (đã thống nhất). Trong form và xem trước dùng đường dẫn tương đối.

## 3. Giao thức khám phá khả năng

Ba tầng:
- **T1 Lệnh:** liệt kê tên lệnh của tool (`npm run`, `ng help`, `git help -a`).
- **T2 Tham số:** đọc trợ giúp của một lệnh (`ng serve <project> --help`, `git <lệnh> -h`).
- **T3 Giá trị hợp lệ:** lấy từ trợ giúp (nếu có), `angular.json` (configurations), hoặc danh sách project.

Mỗi loại tool có một adapter. Tool không có adapter thì dùng chế độ **khai tay**.

Snapshot lưu trong cache, không lưu vào `tools.json`:

```
Capability {
  toolKind, toolPath
  probedAt, probeCmd
  fingerprint: { mtime(package.json), mtime(angular.json), version(binary) }
  actions: [{
    id, label, status: ok | partial | failed, error?
    options: [{ flag, aliases, type: bool|number|string|choice|array,
                default, choices, description, source: help|script|config|user }]
    rawRef        // output gốc để người dùng xem
  }]
}
```

Quy tắc chạy trợ giúp:
- Chỉ dùng binary đã cài (ví dụ `node_modules/.bin/ng`). Không dùng `npx`, vì `npx` có thể tự cài.
- Timeout 10 giây. Không ghi file.
- Lưu output gốc để xem khi cần.

Khi nào làm mới: fingerprint đổi, người dùng bấm "Làm mới", hoặc sau khi thêm tool.

Trạng thái khám phá (hiện bằng badge):

| Trạng thái | Ý nghĩa | Hành động gợi ý |
|---|---|---|
| Chưa đọc | Chưa chạy trợ giúp của lệnh này | Đọc |
| Đang đọc | Đang chạy trợ giúp, hiện lệnh đang chạy | Đợi hoặc huỷ |
| Đã đọc | Có n cờ, kèm thời điểm | Làm mới, xem output gốc |
| Một phần | Có lệnh nhưng không đọc được cờ | Khai tay phần còn thiếu |
| Lỗi | Ghi rõ lý do | Thử lại, khai tay |
| Khai tay | Tool không có adapter | Khai tham số |

## 4. Tính năng và UX

### 4.1 Khám phá khả năng
- **Mục tiêu:** biết tool làm được gì trước khi cấu hình.
- **Vào từ:** tab Chi tiết (mục Khả năng), nút "Đọc" trên từng lệnh, sau khi thêm tool.
- **Tương tác:** Làm mới, Xem output gốc, Khai tay.
- **Rủi ro:** "Khám phá tất cả" có tiến độ và có nút dừng. Không tự chạy hàng loạt khi mở app.

### 4.2 Cây lệnh
- **Mục tiêu:** thấy lệnh và khả năng của nó để chọn cái cần profile.
- **Cấu trúc:** App › Lệnh › Profile.
- **Chip khả năng** trên mỗi lệnh: `cổng`, `cấu hình`, `mở trình duyệt`, `N cờ`. Lệnh chưa đọc hiện chip xám "chưa đọc cờ" và nút "Đọc".
- **Hay dùng:** profile có ghim nằm trong nhóm đầu cây. Thay cho dải "Hay dùng" cũ.
- **Tìm:** theo tên lệnh, profile và chip.

### 4.3 Chạy trực tiếp
- Giữ như cũ: nút theo biến thể.
- Nếu lệnh có cờ bắt buộc chưa điền, mở form tạm (không lưu). Form tạm có nút "Lưu thành profile".

### 4.4 Profile: tạo, sửa, nhân bản, xoá
- **Vào từ:** "+ Profile" trên lệnh, "Nhân bản" trên profile, "Lưu thành profile" sau khi chạy tạm.
- **Hình thức:** side sheet bên phải, không dùng modal. Cây vẫn nhìn thấy để giữ ngữ cảnh. Form nhiều cờ, modal sẽ chật.
- **Cấu trúc form, theo thứ tự:**
  1. **Tên.** Gợi ý từ giá trị đã đổi, ví dụ "dxfac · cổng 4200".
  2. **Cờ chính.** Cờ đã có trong script, điền sẵn, hiện giá trị từ script làm gợi ý.
  3. **Cờ khác của lệnh (n).** Gập lại, có ô tìm. Chỉ liệt kê cờ từ trợ giúp.
  4. **Không có ở lệnh này.** Ghi rõ cờ người dùng hay tưởng có, ví dụ `--host`.
  5. **Tham số tự do.** Chỉ hiện với tool khai tay, có nhãn "không kiểm tra".
- **Mỗi ô** gồm: nhãn dễ đọc, cờ gốc (monospace), kiểu điều khiển (số có khoảng, danh sách, bật/tắt), nguồn, và giá trị gốc.
- **Xem trước:** lệnh sẽ chạy, cờ đã đổi được tô nổi bật so với lệnh gốc.
- **Kiểm tra:** số trong khoảng; cổng không trùng tác vụ đang chạy hoặc profile đang bật; lựa chọn phải nằm trong danh sách.
- **Lưu:** profile giữ (1) tham chiếu lệnh (script + action), (2) lệnh đã gộp để chạy, (3) snapshot lệnh gốc lúc tạo. Nếu script đổi sau đó, profile hiện cảnh báo "Script đã đổi, xem lại" và không tự chạy phần cũ.
- **Vì sao lưu lệnh đã gộp, không nối `-- --port`:** nối thêm có thể gây cờ trùng với `--port 3000` trong script. Mình chưa thử hành vi này, nên chọn cách tránh rủi ro.
- **Xoá:** xác nhận. Nếu bộ chạy đang dùng profile, liệt kê các bộ chạy bị ảnh hưởng.

### 4.5 Chạy profile
- Một bấm. Khi đang chạy, nút đổi thành Dừng và có chấm trạng thái.
- Chạy lần hai: không bật thêm. Mở tác vụ đang chạy và nói rõ lý do, ví dụ "đang chạy, cổng 4200".
- Profile có cổng: hiện cổng cạnh tên, lấy từ output khi chạy.

### 4.6 Bộ chạy
- **Tạo:** thêm bước bằng cây thả xuống (cùng cây với lệnh và profile). Mỗi bước là một profile hoặc một lệnh trực tiếp.
- **Chờ:** ba lựa chọn, nhưng chỉ hiện lựa chọn có căn cứ.
  - "Chờ cổng N": chỉ bật khi bước có cổng. Nếu không có cổng thì báo "không có cổng để chờ".
  - "Chờ thoát mã 0": cho lệnh chạy xong rồi thoát. Trợ giúp không cho biết lệnh có thoát hay không. Toolbox đoán từ tên (build, test, lint là thoát; start, serve là chạy nền) và gắn nhãn "đoán" để người dùng sửa.
  - "Không chờ".
- **Kiểm tra trước khi chạy:** bước lỗi, profile đã cũ, cổng trùng giữa các bước. Lỗi chặn chạy. Cảnh báo thì vẫn cho chạy.
- **Chạy:** mỗi bước có trạng thái. Bước đang chờ có đếm ngược. Có nút Dừng.
- **Khi lỗi:** "Mở log bước lỗi" và "Chạy lại từ bước này" (đề xuất mới, chờ duyệt).

### 4.7 Log và tác vụ
- Giữ drawer hiện tại. Từ một bước bộ chạy có thể mở log của bước đó.
- Cảnh báo lặp lại được gom (đã có trong mockup).

### 4.8 Riêng tư
- Đường dẫn tuyệt đối chỉ hiện ở tab Chi tiết.
- Trong form và xem trước dùng đường dẫn tương đối (thư mục chạy).
- Nút "Ẩn đường dẫn" để chụp màn hình.

### 4.9 Thêm tool
- Giữ hộp thư viện. Sau khi thêm, nếu tool có adapter thì hỏi "Khám phá ngay?" (mặc định là có).

## 5. Thay đổi so với plan (cần duyệt)

| Plan hiện tại | Đề xuất |
|---|---|
| `ParamDef` và `VariantDef` cố định trong `ToolCatalog` (P2, P5) | Snapshot khả năng đọc khi cần, theo adapter |
| Angular: serve, build, test, lint cố định | Lấy từ `ng help`, `angular.json` và trợ giúp của từng project |
| npm: action lấy từ scripts (giữ) | Giữ, thêm thân script làm nguồn tham số |
| Profile: `Vals` dictionary | Profile gồm tham chiếu lệnh, giá trị cờ, lệnh đã gộp, snapshot lệnh gốc |
| Bộ chạy: `Wait` none/port/exit | Giữ. Thêm kiểm tra theo khả năng, nhãn "đoán" cho exit, "Chạy lại từ bước lỗi" |
| Hay dùng: dải riêng (v2) | Nhóm ghim trong cây |
| Form profile: modal | Side sheet |

## 6. Chưa kiểm chứng

- .NET: danh sách lệnh SDK và cờ của `dotnet build --help`, `dotnet run --help`.
- git: `git <lệnh> -h` có đủ tham số để tạo form không.
- Schema của builder: chưa tìm thấy ở các đường dẫn đã thử.
- Trợ giúp của các project khác trong DxFactory: mới đọc dxfac.
- Thời gian mỗi lần đọc trợ giúp: chưa đo.
- Hành vi của npm khi nối `-- --cờ` trùng cờ trong script: chưa thử.

## 7. Câu hỏi cần bạn trả lời

1. Chấp nhận khám phá khi cần (cần binary đã cài) thay vì đọc tĩnh?
2. "Chạy lại từ bước lỗi" có cần trong bản đầu không?
3. Đoán "chờ thoát" từ tên lệnh có chấp nhận được, nếu có nhãn "đoán" và cho sửa?
