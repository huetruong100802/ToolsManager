using System.ComponentModel;

namespace Toolbox
{
    /// <summary>Một chip tag trên thanh lọc: tên, số tool có tag này, và trạng thái đang được chọn.</summary>
    public sealed class TagOption : INotifyPropertyChanged
    {
        private bool _isSelected;

        public TagOption(string name, int count)
        {
            Name = name;
            Count = count;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name { get; }

        public int Count { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}
