using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MkOptic.ViewModels;
using MkOptic.Services;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace MkOptic.Views;

public sealed partial class MainWindow : SukiWindow
{
    private bool _closeConfirmed;
    private bool _closingInProgress;
    private readonly UserPreferencesService? _preferences;
    private MainWindowViewModel? _observedViewModel;
    private readonly DispatcherTimer _externalChangeTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <summary>Used by the XAML designer and runtime loader.</summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnSearchShortcut, RoutingStrategies.Tunnel);
        Activated += OnDocumentActivated;
        // AvaloniaEdit can mark focus events handled before they bubble to the host.
        DocumentsHost.AddHandler(GotFocusEvent, OnDocumentActivated, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += OnWindowDataContextChanged;
        _externalChangeTimer.Tick += OnExternalChangeTimerTick;
    }

    private async void OnExternalChangeTimerTick(object? sender, EventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.ReloadUnmodifiedDocumentsAsync();
        }
    }

    private void OnWindowDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observedViewModel = ViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private async void OnDocumentActivated(object? sender, EventArgs e)
    {
        if (IsActive && ViewModel is { } viewModel)
        {
            await viewModel.CheckActiveDocumentForExternalChangesAsync();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ActiveDocument))
        {
            OnDocumentActivated(this, EventArgs.Empty);
        }
    }

    public MainWindow(MainWindowViewModel viewModel, ISukiDialogManager dialogManager, ISukiToastManager toastManager,
        UserPreferencesService preferences)
        : this()
    {
        DataContext = viewModel;
        DialogHost.Manager = dialogManager;
        ToastHost.Manager = toastManager;
        _preferences = preferences;
    }

    /// <summary>Files passed on the command line, opened once the window is shown.</summary>
    public IReadOnlyList<string> StartupFiles { get; set; } = [];

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnSearchShortcut(object? sender, KeyEventArgs e)
    {
        if (DocumentsHost.ActiveView is not { } view)
        {
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBar.FocusQuery();
            e.Handled = true;
        }
        else if (e.Key == Key.F3 && (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Shift))
        {
            view.Search.Navigate(e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
            e.Handled = true;
        }
    }

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
        _externalChangeTimer.Start();
        if (ViewModel is { } viewModel)
        {
            await viewModel.OpenFilesAsync(StartupFiles);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _externalChangeTimer.Stop();
        _externalChangeTimer.Tick -= OnExternalChangeTimerTick;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnClosed(e);
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || (ViewModel is not { HasUnsavedChanges: true }
            && (_preferences is null || _preferences.FlushAsync().IsCompletedSuccessfully)))
        {
            return;
        }

        // Closing cannot be awaited, so cancel it and close again once the user has decided.
        e.Cancel = true;
        if (_closingInProgress)
        {
            return;
        }

        _closingInProgress = true;
        try
        {
            if (ViewModel is { HasUnsavedChanges: true } viewModel && !await viewModel.ConfirmCloseAllAsync())
            {
                return;
            }

            if (_preferences is not null)
            {
                await _preferences.FlushAsync();
            }

            _closeConfirmed = true;
            Dispatcher.UIThread.Post(Close);
        }
        finally
        {
            _closingInProgress = false;
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
