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
        /// <summary>Id của tool trong thư viện (xem <c>ToolCatalog</c>). JSON giữ tên cũ "kind" để file cũ đọc đúng.</summary>
        [DataMember(Name = "kind", Order = 0)]
        public string ToolId { get; set; }

        /// <summary>Thư mục chạy, tương đối so với gốc workspace; rỗng hoặc "." là gốc. Ghi đè thư mục của tool.</summary>
        [DataMember(Name = "cwd", Order = 1, EmitDefaultValue = false)]
        public string Cwd { get; set; }

        /// <summary>Tham số riêng của workspace cho tool này; rỗng thì dùng tham số đăng ký trong thư viện.</summary>
        [DataMember(Name = "args", Order = 2, EmitDefaultValue = false)]
        public string Args { get; set; }
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
            HasPairs ? PairLine.Render(Pairs)
            : Vals != null && Vals.TryGetValue(ProfileKind.ArgsKey, out var args) ? (args ?? "").Trim() : "";

        /// <summary>
        /// Tham số dạng cặp (key, value) theo thứ tự; có thì thay cho chuỗi Vals["args"] (profile cũ chưa có thì vẫn chạy y như trước).
        /// </summary>
        [DataMember(Name = "pairs", Order = 7, EmitDefaultValue = false)]
        public List<ParamPair> Pairs { get; set; }

        /// <summary>Nhóm biến (<see cref="VariableGroup.Id"/>) gắn vào profile; trống = không gắn. Biến của nhóm vào môi trường process khi chạy.</summary>
        [DataMember(Name = "variableGroup", Order = 8, EmitDefaultValue = false)]
        public string VariableGroupId { get; set; }

        public bool HasPairs => Pairs != null && Pairs.Count > 0;

        /// <summary>Lỗi của tham số thêm (cặp hoặc chuỗi cũ); null nếu hợp lệ.</summary>
        public string ArgsError => HasPairs ? PairLine.Check(Pairs) : CommandBuilder.CheckExtraArgs(ExtraArgs);

        /// <summary>Các cặp hỏi lúc chạy mà chưa có giá trị.</summary>
        public IReadOnlyList<ParamPair> PendingAsks => PairLine.Pending(Pairs);

        /// <summary>Bản sao đã điền các cặp hỏi lúc chạy từ <paramref name="answers"/> (theo thứ tự <see cref="PendingAsks"/>); không ghi vào profile gốc.</summary>
        public Profile WithAnswers(IList<string> answers)
        {
            var copy = (Profile)MemberwiseClone();
            copy.Vals = new Dictionary<string, string>(Vals ?? new Dictionary<string, string>());
            copy.Pairs = HasPairs ? PairLine.Resolve(Pairs, answers) : null;
            return copy;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Vals == null)
                Vals = new Dictionary<string, string>();
        }
    }

    /// <summary>Kind đặc biệt của profile/bước: custom (lệnh riêng); library chỉ còn để đọc dữ liệu cũ (migration, gói cũ). Kind khác là toolId.</summary>
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

        /// <summary>
        /// Giá trị cho các cặp "hỏi lúc chạy" của profile (theo thứ tự), do người chạy nhập. Chỉ sống trong lần chạy này, không lưu.
        /// </summary>
        [IgnoreDataMember]
        public IList<string> Answers { get; set; }

        /// <summary>
        /// Nhóm biến (id) đưa vào môi trường của bước lệnh riêng trực tiếp (<c>run type=custom variableGroup=...</c> qua MCP).
        /// Chỉ sống trong lần chạy này, không lưu. Bước có profile dùng nhóm của profile, không dùng trường này.
        /// </summary>
        [IgnoreDataMember]
        public string RunGroupId { get; set; }
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

        /// <summary>
        /// Chỉ để đọc file schema 1: <see cref="WorkspaceMigration"/> chuyển các mục này vào <see cref="Bindings"/> rồi làm rỗng.
        /// </summary>
        [DataMember(Name = "libraryBindings", Order = 8)]
        public List<LibraryBinding> LibraryBindings { get; set; } = new List<LibraryBinding>();

        /// <summary>Việc cần làm khi tạo worktree mới cho workspace này (file local và lệnh cài đặt).</summary>
        [DataMember(Name = "worktree", Order = 9, EmitDefaultValue = false)]
        public WorktreeSetup Worktree { get; set; } = new WorktreeSetup();

        /// <summary>Nhóm biến của workspace (giống Library › Variable groups của ADO). Secret chỉ nằm trong file dưới dạng DPAPI base64.</summary>
        public List<VariableGroup> VariableGroups { get; set; } = new List<VariableGroup>();

        /// <summary>
        /// Dạng ghi/đọc JSON của <see cref="VariableGroups"/>: trả null khi chưa có nhóm nào để không ghi trường
        /// (EmitDefaultValue=false chỉ bỏ null, không bỏ list rỗng), nên file của người không dùng nhóm biến không đổi.
        /// </summary>
        [DataMember(Name = "variableGroups", Order = 12, EmitDefaultValue = false)]
        private List<VariableGroup> VariableGroupsJson
        {
            get => VariableGroups == null || VariableGroups.Count == 0 ? null : VariableGroups;
            set => VariableGroups = value ?? new List<VariableGroup>();
        }

        /// <summary>
        /// Tương thích cho caller cũ: binding của tool <paramref name="toolId"/> trong <see cref="Bindings"/> dưới dạng
        /// <see cref="LibraryBinding"/> (bản sao, sửa nó không đổi workspace; sửa qua <see cref="Binding"/>). null nếu chưa gắn.
        /// </summary>
        public LibraryBinding FindLibrary(string toolId)
        {
            var b = Binding(toolId);
            return b != null
                ? new LibraryBinding { ToolId = b.ToolId, Args = b.Args }
                : LibraryBindings.Find(x => string.Equals(x.ToolId, toolId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Gắn tool vào <see cref="Bindings"/>. Đã gắn thì không đổi gì. Trả về true nếu vừa gắn thêm. Không ghi file.</summary>
        public bool BindLibrary(ToolEntry tool) => BindTool(tool.Id);

        /// <summary>Gắn tool <paramref name="toolId"/> (id trong thư viện). Đã gắn thì không đổi gì. Trả về true nếu vừa gắn thêm.</summary>
        public bool BindTool(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId) || Binding(toolId) != null)
                return false;
            Bindings.Add(new ToolBinding { ToolId = toolId });
            return true;
        }

        /// <summary>Bỏ tool khỏi workspace; file gốc và thư viện không đổi. Trả về true nếu đã bỏ.</summary>
        public bool UnbindLibrary(string toolId) =>
            Bindings.RemoveAll(b => string.Equals(b.ToolId, toolId, StringComparison.OrdinalIgnoreCase))
            + LibraryBindings.RemoveAll(b => string.Equals(b.ToolId, toolId, StringComparison.OrdinalIgnoreCase)) > 0;

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
            Bindings.Find(b => string.Equals(b.ToolId, kind, StringComparison.OrdinalIgnoreCase));

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
            if (VariableGroups == null) VariableGroups = new List<VariableGroup>();
        }
    }

    /// <summary>
    /// Nhóm biến đặt tên của workspace. Người dùng tạo/sửa ở tab Biến; agent chỉ thấy tên biến và gắn nhóm vào profile
    /// (<see cref="AllowAgent"/> tắt thì agent không thấy nhóm).
    /// </summary>
    [DataContract]
    public class VariableGroup
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "label", Order = 1)]
        public string Label { get; set; }

        [DataMember(Name = "description", Order = 2, EmitDefaultValue = false)]
        public string Description { get; set; }

        /// <summary>Cho agent (MCP) liệt kê, gắn và chạy với nhóm này. Mặc định bật; file cũ/thiếu trường vẫn là bật.</summary>
        [DataMember(Name = "allowAgent", Order = 3)]
        public bool AllowAgent { get; set; } = true;

        /// <summary>Lần sửa cuối, ISO 8601 UTC ("o").</summary>
        [DataMember(Name = "modifiedAt", Order = 4, EmitDefaultValue = false)]
        public string ModifiedAt { get; set; }

        [DataMember(Name = "variables", Order = 5)]
        public List<GroupVariable> Variables { get; set; } = new List<GroupVariable>();

        // DataContractJsonSerializer không chạy initializer: đặt mặc định true trước khi đọc để JSON thiếu allowAgent vẫn là bật.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => AllowAgent = true;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Variables == null) Variables = new List<GroupVariable>();
        }
    }

    /// <summary>
    /// Một biến trong nhóm. Biến thường: <see cref="Value"/> là chữ thật. Secret: <see cref="Value"/> luôn null, giá trị nằm ở
    /// <see cref="Enc"/> (DPAPI CurrentUser, base64); <see cref="Enc"/> trống nghĩa là "Cần nhập".
    /// </summary>
    [DataContract]
    public class GroupVariable
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; }

        [DataMember(Name = "value", Order = 1, EmitDefaultValue = false)]
        public string Value { get; set; }

        [DataMember(Name = "secret", Order = 2, EmitDefaultValue = false)]
        public bool Secret { get; set; }

        [DataMember(Name = "enc", Order = 3, EmitDefaultValue = false)]
        public string Enc { get; set; }

        /// <summary>Secret chưa có giá trị (vd. vừa nhận từ gói chia sẻ): bước chạy sẽ báo hỏng cho tới khi nhập.</summary>
        [IgnoreDataMember]
        public bool NeedsInput => Secret && string.IsNullOrEmpty(Enc);
    }
}
