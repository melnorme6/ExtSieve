using ExtSieve.Core.Models;

namespace ExtSieve.App.ViewModels;

public sealed class ExtensionGroupRowViewModel : ObservableObject
{
    private bool _isSelected;
    private string _displayLabel;
    private string _fileCountDisplay;
    private string _totalBytesDisplay;

    public ExtensionGroupRowViewModel(
        ExtensionGroup group,
        string displayLabel,
        string fileCountDisplay,
        string totalBytesDisplay)
    {
        Group = group ?? throw new ArgumentNullException(nameof(group));
        _displayLabel = displayLabel;
        _fileCountDisplay = fileCountDisplay;
        _totalBytesDisplay = totalBytesDisplay;
    }

    public ExtensionGroup Group { get; }

    public string ExtensionKey => Group.ExtensionKey;

    public long FileCount => Group.FileCount;

    public long TotalBytes => Group.TotalBytes;

    public string FileCountDisplay
    {
        get => _fileCountDisplay;
        internal set
        {
            if (string.Equals(_fileCountDisplay, value, StringComparison.Ordinal))
            {
                return;
            }

            _fileCountDisplay = value;
            RaisePropertyChanged();
        }
    }

    public string TotalBytesDisplay
    {
        get => _totalBytesDisplay;
        internal set
        {
            if (string.Equals(_totalBytesDisplay, value, StringComparison.Ordinal))
            {
                return;
            }

            _totalBytesDisplay = value;
            RaisePropertyChanged();
        }
    }

    public string DisplayLabel
    {
        get => _displayLabel;
        internal set
        {
            if (string.Equals(_displayLabel, value, StringComparison.Ordinal))
            {
                return;
            }

            _displayLabel = value;
            RaisePropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            RaisePropertyChanged();
        }
    }
}
