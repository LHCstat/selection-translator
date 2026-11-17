using System.Windows;
using System.Windows.Interop;
using SelectionTranslator.App.Services;
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.App.Views;

public partial class SelectionIconWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x00000080;

    public event EventHandler? Clicked;

    public SelectionIconWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExstyle);
            SetWindowLongPtr(handle, GwlExstyle, style | WsExNoactivate | WsExToolwindow);
        };
    }

    public void MoveTo(int x, int y) => WindowPlacer.MoveTo(this, x, y);

    private void OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int index) =>
        (IntPtr)GetWindowLongW(hWnd, index);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value) =>
        (IntPtr)SetWindowLongW(hWnd, index, value.ToInt32());

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr hWnd, int index, int value);
}