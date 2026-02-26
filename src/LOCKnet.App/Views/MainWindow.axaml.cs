using Avalonia.Controls;
using Avalonia.Input;

namespace LOCKnet.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        AppServices.Current.ActivityMonitor.RecordActivity();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        AppServices.Current.ActivityMonitor.RecordActivity();
    }
}
