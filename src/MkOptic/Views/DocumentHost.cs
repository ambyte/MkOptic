using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using MkOptic.ViewModels;

namespace MkOptic.Views;

/// <summary>
/// Lazily retains one attached view per visited document. Switching visibility preserves
/// editor and preview state without rebinding content or reinstalling syntax highlighting.
/// </summary>
public sealed class DocumentHost : Panel
{
    public static readonly StyledProperty<ObservableCollection<DocumentViewModel>?> DocumentsProperty =
        AvaloniaProperty.Register<DocumentHost, ObservableCollection<DocumentViewModel>?>(nameof(Documents));

    public static readonly StyledProperty<DocumentViewModel?> ActiveDocumentProperty =
        AvaloniaProperty.Register<DocumentHost, DocumentViewModel?>(nameof(ActiveDocument));

    private readonly Dictionary<DocumentViewModel, DocumentView> _views = [];
    private ObservableCollection<DocumentViewModel>? _observedDocuments;
    private DocumentView? _activeView;
    private bool _isAttached;

    public static readonly DirectProperty<DocumentHost, DocumentView?> ActiveViewProperty =
        AvaloniaProperty.RegisterDirect<DocumentHost, DocumentView?>(nameof(ActiveView), host => host.ActiveView);

    public DocumentView? ActiveView
    {
        get => _activeView;
        private set => SetAndRaise(ActiveViewProperty, ref _activeView, value);
    }

    public ObservableCollection<DocumentViewModel>? Documents
    {
        get => GetValue(DocumentsProperty);
        set => SetValue(DocumentsProperty, value);
    }

    public DocumentViewModel? ActiveDocument
    {
        get => GetValue(ActiveDocumentProperty);
        set => SetValue(ActiveDocumentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == DocumentsProperty || change.Property == ActiveDocumentProperty) &&
            _isAttached)
        {
            ObserveDocuments();
            UpdateViews();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        ObserveDocuments();
        UpdateViews();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        if (_observedDocuments is not null)
        {
            _observedDocuments.CollectionChanged -= OnDocumentsChanged;
            _observedDocuments = null;
        }

        foreach (var document in _views.Keys.ToArray())
        {
            RemoveView(document);
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void ObserveDocuments()
    {
        if (ReferenceEquals(_observedDocuments, Documents))
        {
            return;
        }

        if (_observedDocuments is not null)
        {
            _observedDocuments.CollectionChanged -= OnDocumentsChanged;
        }

        _observedDocuments = Documents;
        if (_observedDocuments is not null)
        {
            _observedDocuments.CollectionChanged += OnDocumentsChanged;
        }
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateViews();

    private void UpdateViews()
    {
        foreach (var document in _views.Keys.ToArray())
        {
            if (Documents?.Contains(document) != true)
            {
                RemoveView(document);
            }
        }

        if (_activeView is not null && ReferenceEquals(_activeView.DataContext, ActiveDocument))
        {
            return;
        }

        if (_activeView is not null)
        {
            _activeView.IsVisible = false;
        }

        ActiveView = null;
        if (ActiveDocument is not { } active || Documents?.Contains(active) != true)
        {
            return;
        }

        if (!_views.TryGetValue(active, out var view))
        {
            view = new DocumentView { DataContext = active, IsVisible = false };
            _views.Add(active, view);
            Children.Add(view);
        }

        ActiveView = view;
        view.IsVisible = true;
    }

    private void RemoveView(DocumentViewModel document)
    {
        var view = _views[document];
        if (ReferenceEquals(_activeView, view))
        {
            ActiveView = null;
        }

        Children.Remove(view);
        // Detachment disposes TextMate; clearing the context removes the model subscription
        // and releases bindings to the document. The host does not own/dispose view models.
        view.DataContext = null;
        _views.Remove(document);
    }
}
