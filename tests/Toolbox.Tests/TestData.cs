using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Toolbox.Core;
using Toolbox.Core.Workspaces;

namespace System.Runtime.CompilerServices
{
    // net48 chưa có sẵn attribute này; compiler chỉ cần tên và namespace.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace Toolbox.Tests
{
    /// <summary>
    /// Thư mục dữ liệu riêng cho cả lần chạy test: catalog mặc định cộng bảng khả năng của dotnet, git, angular.
    /// Catalog không còn khai báo action nào; action đến từ bảng khả năng, nên test cần một bảng thật trên đĩa
    /// (qua TOOLBOX_DATA) và không bao giờ đụng dữ liệu thật của người chạy.
    /// </summary>
    public static class TestData
    {
        /// <summary>Lệnh con có sẵn của git trong bảng khả năng test.</summary>
        public const string GitAction = "status";

        public static string Root { get; private set; }

        [ModuleInitializer]
        internal static void Init()
        {
            Root = Path.Combine(Path.GetTempPath(), "toolbox-tests-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(DataDir.EnvironmentVariable, Root);

            // Thư viện tool giả: mỗi preset mặc định sinh một tool (Id = kind), giống thư viện sau khi seed.
            // Gán một lần cho cả lần chạy để các test song song không giẫm lên nhau.
            var library = ToolPresets.Parse(ToolPresets.DefaultJson()).Select(ToolPresets.ToEntry).ToList();
            ToolCatalog.Source = () => library;

            Seed("dotnet", "build", "test", "run", "restore", "publish");
            Seed("git", "status", "log", "pull", "diff", "fetch");
            var angular = Seed("angular", "serve", "build", "test");
            angular.Entries.Add(new CapabilityEntry { Action = "serve", Name = "--port", TakesValue = true, Source = "scan", Role = CapabilityStore.PortRole });
            CapabilityStore.Save(CapabilityStore.DefaultDirectory, angular);

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try { Directory.Delete(Root, true); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            };
        }

        private static ToolCapabilities Seed(string kind, params string[] subcommands)
        {
            var data = new ToolCapabilities { Kind = kind };
            foreach (var name in subcommands)
                data.Entries.Add(new CapabilityEntry { Action = "", Name = name, Source = "manual", Role = "" });
            CapabilityStore.Save(CapabilityStore.DefaultDirectory, data);
            return data;
        }
    }
}
