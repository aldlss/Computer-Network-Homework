using Avalonia.Controls;

namespace Aya_Ftp.Views;

public partial class MainWindow : Window
{
    public static Window Instant { get; private set; } = new();
    public MainWindow()
    {
        Instant = this;
        InitializeComponent();
    }
}
