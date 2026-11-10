using Avalonia.Controls;
using Avalonia.Input;

namespace MkOptic.Views;

/// <summary>Persistent find-only controls shared by the active document's source and preview.</summary>
public sealed partial class DocumentSearchBar : UserControl
{
    public DocumentSearchBar()
    {
        InitializeComponent();
        KeyDown += OnSearchKeyDown;
    }

    public void FocusQuery()
    {
        if (DataContext is not DocumentSearch search)
        {
            return;
        }

        search.SeedSelection();
        QueryInput.Focus();
        QueryInput.SelectAll();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DocumentSearch search)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            search.Navigate(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            search.ClearCommand.Execute(null);
            search.FocusContent();
            e.Handled = true;
        }
    }
}
