using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SelectionTranslator.App.Services;
using SelectionTranslator.App.ViewModels;

namespace SelectionTranslator.App.Views;

public partial class TranslationPopupWindow : Window
{
    public event EventHandler? Dismissed;

    public TranslationPopupWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public void PlaceNear(int screenX, int screenY) => WindowPlacer.PlaceNear(this, screenX, screenY);

    public void ShowFor(TranslationPopupViewModel viewModel, int x, int y, double width)
    {
        DataContext = viewModel;
        Width = width + 20;

        if (!IsVisible)
            Show();

        // WPF 首次显示后会按默认位置再摆一次，这里在布局完成和渲染完成时各校正一次。
        Dispatcher.BeginInvoke(
            new Action(() => PlaceNear(x, y)),
            DispatcherPriority.Loaded);

        Dispatcher.BeginInvoke(
            new Action(() => PlaceNear(x, y)),
            DispatcherPriority.ApplicationIdle);
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        // 文字区域留给选中和滚动，只有卡片空白处（顶部拖拽条、内边距）能拖动。
        if (e.OriginalSource is System.Windows.Controls.TextBlock or System.Windows.Controls.ScrollViewer)
            return;

        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
}