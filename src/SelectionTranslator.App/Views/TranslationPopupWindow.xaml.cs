using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SelectionTranslator.App.Services;
using SelectionTranslator.App.ViewModels;

namespace SelectionTranslator.App.Views;

public partial class TranslationPopupWindow : Window
{
    public TranslationPopupWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Hide();
        };
    }

    public void PlaceNear(int screenX, int screenY) => WindowPlacer.PlaceNear(this, screenX, screenY);

    public void ShowFor(TranslationPopupViewModel viewModel, int x, int y, double width)
    {
        DataContext = viewModel;
        Width = width;

        if (!IsVisible)
            Show();

        Dispatcher.BeginInvoke(
            new Action(() => PlaceNear(x, y)),
            DispatcherPriority.Loaded);
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
}