using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Cách một bước trong bộ chạy chờ trước khi sang bước kế.</summary>
    public static class WaitKind
    {
        /// <summary>Không chờ, sang bước kế ngay.</summary>
        public const string None = "none";
        /// <summary>Chờ cổng của bước này nghe được.</summary>
        public const string Port = "port";
        /// <summary>Chờ tiến trình thoát với mã 0.</summary>
        public const string Exit = "exit";
    }

    /// <summary>Tool từ catalog được gắn vào workspace. Lệnh lấy từ <c>ToolCatalog</c>, không lưu lại ở đây.</summary>
    [DataContract]
    public class ToolBinding
    {
        /// <summary>dotnet, git, angular, npm (xem <c>ToolCatalog</c>).</summary>
        [DataMember(Name = "kind", Order = 0)]
        public string Kind { get; set; }

        /// <summary>Thư mục chạy, tương đối so với gốc workspace; rỗng hoặc "." là gốc.</summary>
        [DataMember(Name = "cwd", Order = 1, EmitDefaultValue = false)]
        public string Cwd { get; set; }
    }

    /// <summary>
    /// Tool trong thư viện được gắn vào workspace. Chỉ giữ Id tham chiếu, không copy: sửa tool ở thư viện thì mọi workspace theo.
    /// </summary>
    [DataContract]
    public class LibraryBinding
    {
        [DataMember(Name = "toolId", Order = 0)]
        public string ToolId { get; set; }

        /// <summary>Tên lúc gắn; hiện khi tool đã bị xoá khỏi thư viện.</summary>
        [DataMember(Name = "name", Order = 1, EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>Tham số riêng của workspace; rỗng thì dùng tham số đăng ký trong thư viện.</summary>
        [DataMember(Name = "args", Order = 2, EmitDefaultValue = false)]
        public string Args { get; set; }

        /// <summary>Tham số thật khi chạy trong workspace này.</summary>
        public string EffectiveArgs(ToolEntry tool) =>
            string.IsNullOrWhiteSpace(Args) ? tool?.Args : Args;
    }

    /// <summary>Việc cần làm khi tạo worktree mới: file local đưa vào, rồi lệnh cài đặt chạy trong worktree.</summary>
    [DataContract]
    public class WorktreeSetup
    {
        /// <summary>File tương đối so với gốc workspace. Không theo git thì copy; theo git mà đã sửa thì áp diff so với HEAD.</summary>
        [DataMember(Name = "files", Order = 0)]
        public List<string> Files { get; set; } = new List<string>();

        /// <summary>Lệnh chạy trong worktree sau khi đưa file vào, ví dụ "pnpm install --frozen-lockfile". Rỗng: không chạy.</summary>
        [DataMember(Name = "command", Order = 1, EmitDefaultValue = false)]
        public string Command { get; set; }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Files == null) Files = new List<string>();
        }
    }

    /// <summary>Lệnh do người dùng tự thêm (tab "Lệnh riêng").</summary>
    [DataContract]
    public class CustomCommand
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "label", Order = 1)]
        public string Label { get; set; }

        [DataMember(Name = "commandLine", Order = 2)]
        public string CommandLine { get; set; }

        [DataMember(Name = "cwd", Order = 3, EmitDefaultValue = false)]
        public string Cwd { get; set; }

        /// <summary>Chạy trong cửa sổ terminal riêng (xem được, nhập được) thay vì chạy ngầm.</summary>
        [DataMember(Name = "window", Order = 4, EmitDefaultValue = false)]
        public bool Window { get; set; }
    }

    /// <summary>Một cách chạy đã đặt tên, ví dụ "FE Đối tác" = Angular · serve · cổng 4201.</summary>
    [DataContract]
    public class Profile
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "label", Order = 1)]
        public string Label { get; set; }

        [DataMember(Name = "kind", Order = 2)]
        public string Kind { get; set; }

        [DataMember(Name = "action", Order = 3)]
        public string ActionId { get; set; }

        [DataMember(Name = "variant", Order = 4)]
        public string VariantId { get; set; }

        /// <summary>
        /// Chỉ với Kind "library" (toolId của thư viện) hoặc "custom" (<see cref="CustomCommand.Id"/>). Hai loại này để
        /// <see cref="ActionId"/>/<see cref="VariantId"/> trống; tham số thêm nằm ở Vals["args"], nối sau tham số hiệu lực của tool / sau dòng lệnh riêng.
        /// </summary>
        [DataMember(Name = "refId", Order = 6, EmitDefaultValue = false)]
        public string RefId { get; set; }

        [DataMember(Name = "vals", Order = 5)]
        public Dictionary<string, string> Vals { get; set; } = new Dictionary<string, string>();

        /// <summary>Tham số thêm của profile loại library/custom (Vals["args"]); rỗng nếu không có.</summary>
        public string ExtraArgs =>
            Vals != null && Vals.TryGetValue(ProfileKind.ArgsKey, out var args) ? (args ?? "").Trim() : "";

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Vals == null)
                Vals = new Dictionary<string, string>();
        }
    }

    /// <summary>Hai loại profile ngoài catalog dựng sẵn (dotnet, git, angular, npm): tool thư viện và lệnh riêng.</summary>
    public static class ProfileKind
    {
        public const string Library = "library";
        public const string Custom = "custom";
        /// <summary>Khoá trong <see cref="Profile.Vals"/> chứa tham số thêm.</summary>
        public const string ArgsKey = "args";

        public static bool IsLibrary(string kind) => string.Equals(kind, Library, System.StringComparison.OrdinalIgnoreCase);

        public static bool IsCustom(string kind) => string.Equals(kind, Custom, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Loại trỏ bằng <see cref="Profile.RefId"/> thay vì action của catalog.</summary>
        public static bool IsRef(string kind) => IsLibrary(kind) || IsCustom(kind);
    }

    [DataContract]
    public class StackStep
    {
        /// <summary>Tool của bước: một kind trong catalog hoặc "custom".</summary>
        [DataMember(Name = "kind", Order = 0)]
        public string Kind { get; set; }

        /// <summary>Với kind catalog: id action; với "custom": id của <see cref="CustomCommand"/>.</summary>
        [DataMember(Name = "action", Order = 1)]
        public string ActionId { get; set; }

        [DataMember(Name = "variant", Order = 2, EmitDefaultValue = false)]
        public string VariantId { get; set; }

        /// <summary>Có profile thì dùng tham số của profile thay vì mặc định.</summary>
        [DataMember(Name = "profile", Order = 3, EmitDefaultValue = false)]
        public string ProfileId { get; set; }

        /// <summary><see cref="WaitKind"/>; mặc định none.</summary>
        [DataMember(Name = "wait", Order = 4, EmitDefaultValue = false)]
        public string Wait { get; set; }

        /// <summary>Cổng cần chờ khi <see cref="Wait"/> là port và lệnh không tự khai cổng.</summary>
        [DataMember(Name = "port", Order = 5, EmitDefaultValue = false)]
        public int Port { get; set; }

        /// <summary>
        /// 0 = không nhóm. Các bước liên tiếp cùng Group &gt; 0 bắt đầu cùng lúc; bước sau nhóm chỉ chạy khi mọi bước trong nhóm
        /// đã đạt <see cref="Wait"/> của riêng nó.
        /// </summary>
        [DataMember(Name = "group", Order = 6, EmitDefaultValue = false)]
        public int Group { get; set; }
    }

    /// <summary>Chuỗi các bước chạy tuần tự bằng một cú bấm.</summary>
    [DataContract]
    public class StackDef
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "label", Order = 1)]
        public string Label { get; set; }

        [DataMember(Name = "steps", Order = 2)]
        public List<StackStep> Steps { get; set; } = new List<StackStep>();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Steps == null)
                Steps = new List<StackStep>();
        }
    }

    /// <summary>Một repo/thư mục làm việc cùng các tool, lệnh, profile và bộ chạy của nó.</summary>
    [DataContract]
    public class Workspace
    {
        /// <summary>Số màu trong bảng màu chấm của rail.</summary>
        public const int ColorCount = 8;

        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }

        [DataMember(Name = "path", Order = 2)]
        public string Path { get; set; }

        [DataMember(Name = "color", Order = 3)]
        public int ColorIndex { get; set; }

        [DataMember(Name = "bindings", Order = 4)]
        public List<ToolBinding> Bindings { get; set; } = new List<ToolBinding>();

        [DataMember(Name = "customs", Order = 5)]
        public List<CustomCommand> Customs { get; set; } = new List<CustomCommand>();

        [DataMember(Name = "profiles", Order = 6)]
        public List<Profile> Profiles { get; set; } = new List<Profile>();

        [DataMember(Name = "stacks", Order = 7)]
        public List<StackDef> Stacks { get; set; } = new List<StackDef>();

        /// <summary>Ghim lên đầu danh sách workspace ở rail, để tìm nhanh khi có nhiều workspace.</summary>
        [DataMember(Name = "pinned", Order = 10, EmitDefaultValue = false)]
        public bool Pinned { get; set; }

        /// <summary>Lưu trữ: ẩn khỏi rail, vẫn giữ nguyên dữ liệu; bấm "Lưu trữ" ở rail để xem lại.</summary>
        [DataMember(Name = "archived", Order = 11, EmitDefaultValue = false)]
        public bool Archived { get; set; }

        [DataMember(Name = "libraryBindings", Order = 8)]
        public List<LibraryBinding> LibraryBindings { get; set; } = new List<LibraryBinding>();

        /// <summary>Việc cần làm khi tạo worktree mới cho workspace này (file local và lệnh cài đặt).</summary>
        [DataMember(Name = "worktree", Order = 9, EmitDefaultValue = false)]
        public WorktreeSetup Worktree { get; set; } = new WorktreeSetup();

        public LibraryBinding FindLibrary(string toolId) =>
            LibraryBindings.Find(b => string.Equals(b.ToolId, toolId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Gắn tool thư viện. Đã gắn thì không đổi gì. Trả về true nếu vừa gắn thêm. Không ghi file.</summary>
        public bool BindLibrary(ToolEntry tool)
        {
            if (FindLibrary(tool.Id) != null)
                return false;
            LibraryBindings.Add(new LibraryBinding { ToolId = tool.Id, Name = tool.Name });
            return true;
        }

        /// <summary>Bỏ tool thư viện khỏi workspace; file gốc và thư viện không đổi. Trả về true nếu đã bỏ.</summary>
        public bool UnbindLibrary(string toolId) =>
            LibraryBindings.RemoveAll(b => string.Equals(b.ToolId, toolId, StringComparison.OrdinalIgnoreCase)) > 0;

        [IgnoreDataMember]
        public string ResolvedPath => Environment.ExpandEnvironmentVariables(Path ?? "");

        /// <summary>Thư mục còn tồn tại; không còn thì các lệnh không chạy được cho tới khi trỏ lại.</summary>
        [IgnoreDataMember]
        public bool PathExists => Directory.Exists(ResolvedPath);

        /// <summary>Thư mục chạy thật của một binding hoặc lệnh riêng (tương đối so với gốc workspace).</summary>
        public string ResolveCwd(string cwd)
        {
            var root = ResolvedPath;
            if (string.IsNullOrWhiteSpace(cwd) || cwd.Trim() == ".")
                return root;
            return System.IO.Path.IsPathRooted(cwd) ? cwd : System.IO.Path.GetFullPath(System.IO.Path.Combine(root, cwd));
        }

        public ToolBinding Binding(string kind) =>
            Bindings.Find(b => string.Equals(b.Kind, kind, StringComparison.OrdinalIgnoreCase));

        // DataContractJsonSerializer không chạy initializer khi đọc.
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Bindings == null) Bindings = new List<ToolBinding>();
            if (Customs == null) Customs = new List<CustomCommand>();
            if (Profiles == null) Profiles = new List<Profile>();
            if (Stacks == null) Stacks = new List<StackDef>();
            if (LibraryBindings == null) LibraryBindings = new List<LibraryBinding>();
            if (Worktree == null) Worktree = new WorktreeSetup();
        }
    }
}
