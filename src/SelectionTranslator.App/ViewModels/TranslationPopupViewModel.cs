using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SelectionTranslator.App.ViewModels;

public sealed partial class TranslationPopupViewModel : ObservableObject
{
    [ObservableProperty] private string _originalText = string.Empty;
    [ObservableProperty] private string _translation = string.Empty;
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string? _error;

    public event EventHandler? RetryRequested;
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Copy()
    {
        if (string.IsNullOrEmpty(Translation))
            return;

        try { System.Windows.Clipboard.SetText(Translation); }
        catch (Exception) { }
    }

    [RelayCommand]
    private void Retry() => RetryRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}