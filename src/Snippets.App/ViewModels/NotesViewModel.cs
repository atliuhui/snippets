using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Snippets.Core.Notes;

namespace Snippets.App.ViewModels;

public sealed class NotesViewModel : INotifyPropertyChanged
{
    private readonly NoteService _notes;
    private readonly QuickCopyExtractor _quickCopyExtractor = new();
    private NoteListItemViewModel? _selectedNote;
    private string _newNoteName = string.Empty;
    private string _content = string.Empty;
    private string _statusText = string.Empty;
    private bool _isDirty;

    public NotesViewModel(NoteService notes)
    {
        _notes = notes;
        Notes = [];
        QuickCopyItems = [];
        Issues = [];
        Reload();
    }

    public ObservableCollection<NoteListItemViewModel> Notes { get; }
    public ObservableCollection<QuickCopyItemViewModel> QuickCopyItems { get; }
    public ObservableCollection<QuickCopyIssueViewModel> Issues { get; }
    public bool HasNotes => Notes.Count > 0;
    public bool HasSelectedNote => SelectedNote is not null;
    public bool HasQuickCopyItems => QuickCopyItems.Count > 0;
    public bool HasIssues => Issues.Count > 0;
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public string DraftsPath => _notes.DraftsDirectory;

    public event Action? DraftsChanged;

    public NoteListItemViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (ReferenceEquals(_selectedNote, value))
            {
                return;
            }

            _selectedNote = value;
            OnChanged();
            OnChanged(nameof(HasSelectedNote));
            LoadSelected();
        }
    }

    public string NewNoteName
    {
        get => _newNoteName;
        set
        {
            if (_newNoteName == value)
            {
                return;
            }

            _newNoteName = value;
            OnChanged();
        }
    }

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value)
            {
                return;
            }

            _content = value;
            _isDirty = SelectedNote is not null;
            OnChanged();
            OnChanged(nameof(IsDirty));
            RefreshQuickCopy();
        }
    }

    public bool IsDirty => _isDirty;

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnChanged();
            OnChanged(nameof(HasStatus));
        }
    }

    public async Task CreateAsync()
    {
        var name = string.IsNullOrWhiteSpace(NewNoteName) ? "untitled" : NewNoteName;
        var document = await _notes.SaveAsync(name, "# New note\r\n\r\n");
        Reload();
        SelectedNote = Notes.FirstOrDefault(note => string.Equals(note.Name, document.Name, StringComparison.OrdinalIgnoreCase));
        NewNoteName = string.Empty;
        StatusText = $"Created {document.Name}.";
        DraftsChanged?.Invoke();
    }

    public async Task SaveAsync()
    {
        if (SelectedNote is null)
        {
            return;
        }

        var document = await _notes.SaveAsync(SelectedNote.Name, Content);
        SelectedNote.Update(document);
        _isDirty = false;
        OnChanged(nameof(IsDirty));
        StatusText = $"Saved {document.Name}.";
        RefreshQuickCopy();
        DraftsChanged?.Invoke();
    }

    public void Delete(NoteListItemViewModel note)
    {
        if (!note.IsPendingDelete)
        {
            foreach (var item in Notes)
            {
                if (!ReferenceEquals(item, note))
                {
                    item.IsPendingDelete = false;
                }
            }

            note.IsPendingDelete = true;
            StatusText = $"Click delete again to remove {note.Name}.";
            return;
        }

        var name = note.Name;
        var selectedName = SelectedNote?.Name;
        _notes.Delete(name);
        Reload();
        SelectedNote = string.Equals(selectedName, name, StringComparison.OrdinalIgnoreCase)
            ? Notes.FirstOrDefault()
            : Notes.FirstOrDefault(item => string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Deleted {name}.";
        DraftsChanged?.Invoke();
    }

    public void Refresh()
    {
        var selectedName = SelectedNote?.Name;
        Reload();
        SelectedNote = selectedName is null
            ? Notes.FirstOrDefault()
            : Notes.FirstOrDefault(note => string.Equals(note.Name, selectedName, StringComparison.OrdinalIgnoreCase)) ?? Notes.FirstOrDefault();
        StatusText = "Refreshed drafts.";
        DraftsChanged?.Invoke();
    }

    private void Reload()
    {
        Notes.Clear();
        foreach (var document in _notes.List())
        {
            Notes.Add(new NoteListItemViewModel(document));
        }

        OnChanged(nameof(HasNotes));
    }

    private void LoadSelected()
    {
        if (SelectedNote is null)
        {
            SetContent(string.Empty, isDirty: false);
            QuickCopyItems.Clear();
            Issues.Clear();
            OnChanged(nameof(HasQuickCopyItems));
            OnChanged(nameof(HasIssues));
            return;
        }

        SetContent(SelectedNote.Content, isDirty: false);
        StatusText = $"Opened {SelectedNote.Name}.";
    }

    private void SetContent(string content, bool isDirty)
    {
        _content = content;
        _isDirty = isDirty;
        OnChanged(nameof(Content));
        OnChanged(nameof(IsDirty));
        RefreshQuickCopy();
    }

    private void RefreshQuickCopy()
    {
        QuickCopyItems.Clear();
        Issues.Clear();

        if (SelectedNote is not null)
        {
            var result = _quickCopyExtractor.Extract(Content, SelectedNote.Path, SelectedNote.Updated);
            foreach (var item in result.Items)
            {
                QuickCopyItems.Add(new QuickCopyItemViewModel(item));
            }

            foreach (var issue in result.Issues)
            {
                Issues.Add(new QuickCopyIssueViewModel(issue));
            }
        }

        OnChanged(nameof(HasQuickCopyItems));
        OnChanged(nameof(HasIssues));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class NoteListItemViewModel : INotifyPropertyChanged
{
    private NoteDocument _document;
    private bool _isPendingDelete;

    public NoteListItemViewModel(NoteDocument document)
    {
        _document = document;
    }

    public string Name => _document.Name;
    public string Path => _document.Path;
    public string Content => _document.Content;
    public DateTimeOffset Updated => _document.Updated;
    public string UpdatedText => Updated.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string DeleteToolTip => IsPendingDelete ? "Click again to delete" : "Delete note";

    public bool IsPendingDelete
    {
        get => _isPendingDelete;
        set
        {
            if (_isPendingDelete == value)
            {
                return;
            }

            _isPendingDelete = value;
            OnChanged();
            OnChanged(nameof(DeleteToolTip));
        }
    }

    public void Update(NoteDocument document)
    {
        _document = document;
        OnChanged(nameof(Content));
        OnChanged(nameof(Updated));
        OnChanged(nameof(UpdatedText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class QuickCopyItemViewModel
{
    private readonly QuickCopyItem _item;

    public QuickCopyItemViewModel(QuickCopyItem item)
    {
        _item = item;
    }

    public string Id => _item.Id;
    public string Label => _item.Label;
    public string Value => _item.Value;
    public string SourceText => $"{_item.Source.StartIndex}-{_item.Source.EndIndex}";
}

public sealed class QuickCopyIssueViewModel
{
    private readonly QuickCopyIssue _issue;

    public QuickCopyIssueViewModel(QuickCopyIssue issue)
    {
        _issue = issue;
    }

    public string Code => _issue.Code;
    public string Message => _issue.Message;
    public string LocationText => $"index {_issue.Index}";
}
