using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Toolbox
{
    /// <summary>Thứ tự các dòng trong Thư viện.</summary>
    public enum LibrarySort
    {
        Name,
        /// <summary>Tool vừa chạy gần đây lên đầu.</summary>
        Recent,
        /// <summary>Tool đã ghim lên đầu.</summary>
        Pinned,
    }

    /// <summary>Một lựa chọn trong ô sắp xếp của Thư viện.</summary>
    public sealed class SortOption
    {
        private SortOption(LibrarySort sort, string label)
        {
            Sort = sort;
            Label = label;
        }

        public LibrarySort Sort { get; }
        public string Label { get; }

        public static IReadOnlyList<SortOption> All { get; } = new[]
        {
            new SortOption(LibrarySort.Name, "Tên A–Z"),
            new SortOption(LibrarySort.Recent, "Dùng gần đây"),
            new SortOption(LibrarySort.Pinned, "Ghim trước"),
        };

        /// <summary>Giá trị lưu trong settings.json; không nhận ra thì về theo tên.</summary>
        public static LibrarySort Parse(string value) =>
            Enum.TryParse(value, ignoreCase: true, out LibrarySort sort) && Enum.IsDefined(typeof(LibrarySort), sort) ? sort : LibrarySort.Name;

        public static string Name(LibrarySort sort) => sort == LibrarySort.Name ? null : sort.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Danh sách cho ItemsControl, thay cả nội dung bằng một thông báo Reset duy nhất. Thêm từng dòng vào ObservableCollection thường
    /// bắn một sự kiện mỗi dòng, với cả nghìn tool thì giao diện phải bố trí lại cả nghìn lần.
    /// </summary>
    public sealed class BulkCollection<T> : ObservableCollection<T>
    {
        /// <summary>Thay nội dung. Dãy mới giống hệt dãy cũ thì không báo gì, nên vị trí cuộn và dòng đang chọn được giữ nguyên.</summary>
        public void ResetTo(IList<T> items)
        {
            if (Items.SequenceEqual(items))
                return;
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
