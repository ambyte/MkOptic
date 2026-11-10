using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MkOptic.ViewModels;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace MkOptic.Views;

public sealed partial class MainWindow : SukiWindow
{
    private bool _closeConfirmed;

    /// <summary>Used by the XAML designer and runtime loader.</summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public MainWindow(MainWindowViewModel viewModel, ISukiDialogManager dialogManager, ISukiToastManager toastManager)
        : this()
    {
        DataContext = viewModel;
        DialogHost.Manager = dialogManager;
        ToastHost.Manager = toastManager;
    }

    /// <summary>Files passed on the command line, opened once the window is shown.</summary>
    public IReadOnlyList<string> StartupFiles { get; set; } = [];

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>Opens files and brings the window to the front (used when the OS hands files to a running instance).</summary>
    public async Task OpenFilesAsync(IReadOnlyList<string> paths)
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        if (ViewModel is { } viewModel)
        {
            await viewModel.OpenFilesAsync(paths);
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is { } viewModel)
        {
            await viewModel.OpenFilesAsync(StartupFiles);
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || ViewModel is not { HasUnsavedChanges: true } viewModel)
        {
            return;
        }

        // Closing cannot be awaited, so cancel it and close again once the user has decided.
        e.Cancel = true;
        if (await viewModel.ConfirmCloseAllAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .OfType<string>()
            .Where(File.Exists)
            .ToArray();

        if (paths is { Length: > 0 } && ViewModel is { } viewModel)
        {
            await viewModel.OpenFilesAsync(paths);
        }
    }
}
