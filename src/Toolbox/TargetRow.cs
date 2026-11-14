using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Toolbox.Core;
using Toolbox.Core.AgentClients;
using Toolbox.Core.Agents;

namespace Toolbox
{
    /// <summary>Cơ sở INotifyPropertyChanged cho các dòng của cửa sổ Kết nối agent (trạng thái sống đổi theo nhịp).</summary>
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>Một app agent ở cột trái: tên, biểu tượng chữ, đã cài chưa, đang kết nối ở mấy nơi.</summary>
    public sealed class ClientCard : Observable
    {
        private string _status;
        private bool _hasConnections;
        private bool _isLive;

        public ClientCard(AgentClient client)
        {
            Client = client;
            IsInstalled = client.IsInstalled;
        }

        public AgentClient Client { get; }
        public string Name => Client.Name;
        public string Monogram => Client.Monogram;
        public bool IsInstalled { get; }

        public string Status { get => _status; set => Set(ref _status, value); }
        public bool HasConnections { get => _hasConnections; set => Set(ref _hasConnections, value); }

        /// <summary>Có agent của app này vừa gọi tới Toolbox (trong 2 phút).</summary>
        public bool IsLive { get => _isLive; set => Set(ref _isLive, value); }
    }

    /// <summary>Một dòng phạm vi (một workspace hoặc toàn máy) của app đang chọn: trạng thái khai báo và agent đã gọi tới chưa.</summary>
    public sealed class TargetRow : Observable
    {
        public static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(2);

        private string _presence;
        private bool _isLive;

        public TargetRow(AgentClient client, ConnectTarget target, string title, string detail, ConnectionState state)
        {
            Client = client;
            Target = target;
            Title = title;
            Detail = detail;
            State = state;
        }

        public AgentClient Client { get; }
        public ConnectTarget Target { get; }
        public string Title { get; }
        public string Detail { get; }
        public ConnectionState State { get; }

        public AgentLink Link => State.Error != null ? AgentLink.Stale : State.Link;
        public bool IsManual => Client.IsManual;

        public string ChipText
        {
            get
            {
                if (State.Error != null)
                    return "Lỗi đọc cấu hình";
                if (IsManual)
                    return "Tự khai báo";
                if (State.Link == AgentLink.Connected)
                    return "Đã kết nối";
                if (State.Link == AgentLink.Stale)
                    return State.Reason ?? "Trỏ tới exe khác";
                return "Chưa kết nối";
            }
        }

        public string ConnectText => IsManual ? "Copy cấu hình" : Link == AgentLink.Stale ? "Kết nối lại" : "Kết nối";

        public string ToolTip
        {
            get
            {
                if (State.Error != null)
                    return State.Error;
                var parts = "";
                if (State.Entry?.Command != null)
                    parts = $"Khai báo hiện tại ({State.Server}): {AgentClient.CommandLine(State.Entry.Command, State.Entry.Args)}";
                var path = Client.ConfigPath(AgentConnections.Location(Client, Target));
                if (path != null)
                    parts += (parts.Length > 0 ? "\n" : "") + "File cấu hình: " + path;
                return parts.Length == 0 ? null : parts;
            }
        }

        /// <summary>"Agent đang hoạt động · cursor 1.7" / "Agent gọi lần cuối 3 giờ trước" / gợi ý mở lại app.</summary>
        public string Presence { get => _presence; private set => Set(ref _presence, value); }

        public bool IsLive { get => _isLive; private set => Set(ref _isLive, value); }

        public void UpdatePresence(AgentRecord agent, DateTime nowUtc)
        {
            if (agent == null || (Link != AgentLink.Connected && !IsManual))
            {
                Presence = null;
                IsLive = false;
                return;
            }
            var who = string.IsNullOrEmpty(agent.ClientName) ? "" : " · " + agent.ClientName + (string.IsNullOrEmpty(agent.ClientVersion) ? "" : " " + agent.ClientVersion);
            if (!TryTime(agent.LastSeenAt, out var seen))
            {
                IsLive = false;
                Presence = $"Chờ {Client.Name} gọi tới Toolbox… {Client.RestartHint}";
                return;
            }
            IsLive = nowUtc - seen < LiveWindow;
            Presence = IsLive ? "Agent đang hoạt động" + who : $"Agent gọi lần cuối {Ago(nowUtc - seen)}" + who;
        }

        private static bool TryTime(string value, out DateTime utc) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out utc) && (utc = utc.ToUniversalTime()) != default;

        private static string Ago(TimeSpan span)
        {
            if (span.TotalMinutes < 1)
                return "vừa xong";
            if (span.TotalHours < 1)
                return $"{(int)span.TotalMinutes} phút trước";
            if (span.TotalDays < 1)
                return $"{(int)span.TotalHours} giờ trước";
            return $"{(int)span.TotalDays} ngày trước";
        }
    }
}
