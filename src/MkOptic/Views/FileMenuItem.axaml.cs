using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MkOptic.Views;

/// <summary>"File" menu of the main window.</summary>
public sealed partial class FileMenuItem : MenuItem
{
    public FileMenuItem()
    {
        InitializeComponent();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) =>
        (TopLevel.GetTopLevel(this) as Window)?.Close();
}
