using System.Drawing;
using System.Windows.Forms;

namespace SelectionTranslator.App.Services;

public sealed class TrayIconService : IDisposable
{
    private NotifyIcon? _icon;

    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void Show()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("设置", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "划译",
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Notify(string message)
    {
        _icon?.ShowBalloonTip(3000, "划译", message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_icon is null)
            return;

        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}