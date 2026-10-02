using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace gMKVExtractGUI.Linux;

public partial class TranslationEditorWindow : Window
{
    private readonly IReadOnlyDictionary<string, string> _sourceStrings;
    private readonly Func<string, TranslationEditorDocument> _loadDocument;
    private readonly Action<TranslationEditorDocument> _saveDocument;
    private readonly Func<string, string> _text;
    private readonly Dictionary<string, TranslationEditorDocument> _documents = new(StringComparer.Ordinal);
    private readonly List<TranslationEditorRow> _allRows = new();
    private readonly ObservableCollection<TranslationEditorRow> _visibleRows = new();
    private string _currentLanguage = "en";
    private bool _initializing = true;

    public TranslationEditorWindow() : this(
        new[] { "en" },
        "en",
        new Dictionary<string, string>(),
        _ => new TranslationEditorDocument(),
        _ => { },
        key => key)
    {
    }

    public TranslationEditorWindow(
        IEnumerable<string> languages,
        string currentLanguage,
        IReadOnlyDictionary<string, string> sourceStrings,
        Func<string, TranslationEditorDocument> loadDocument,
        Action<TranslationEditorDocument> saveDocument,
        Func<string, string> text)
    {
        AvaloniaXamlLoader.Load(this);
        _sourceStrings = sourceStrings;
        _loadDocument = loadDocument;
        _saveDocument = saveDocument;
        _text = text;
        ApplyLocalization();
        TranslationRowsList.DataContext = _visibleRows;

        foreach (string language in languages)
        {
            LocaleSelector.Items.Add(new ComboBoxItem { Content = language.ToUpperInvariant(), Tag = language });
        }
        if (!languages.Contains(currentLanguage, StringComparer.Ordinal))
        {
            LocaleSelector.Items.Add(new ComboBoxItem { Content = currentLanguage.ToUpperInvariant(), Tag = currentLanguage });
        }

        LocaleSelector.SelectedIndex = Math.Max(0, Enumerable.Range(0, LocaleSelector.Items.Count)
            .FirstOrDefault(index => (LocaleSelector.Items[index] as ComboBoxItem)?.Tag as string == currentLanguage));
        _currentLanguage = currentLanguage;
        LoadCurrentDocument();
        _initializing = false;
    }

    private ComboBox LocaleSelector => this.FindControl<ComboBox>("LocaleBox")!;
    private TextBox TranslatorNameInput => this.FindControl<TextBox>("TranslatorBox")!;
    private TextBox SearchInput => this.FindControl<TextBox>("SearchBox")!;
    private CheckBox UntranslatedOnlyInput => this.FindControl<CheckBox>("UntranslatedCheckBox")!;
    private TextBlock TranslatedCountLabel => this.FindControl<TextBlock>("TranslatedCountText")!;
    private TextBlock EditorStatus => this.FindControl<TextBlock>("StatusText")!;
    private ItemsControl TranslationRowsList => this.FindControl<ItemsControl>("TranslationsList")!;

    private void ApplyLocalization()
    {
        Title = _text("translationEditor.title");
        this.FindControl<TextBlock>("SettingsTitleText")!.Text = _text("translationEditor.settings");
        this.FindControl<TextBlock>("LocaleLabelText")!.Text = _text("translationEditor.locale");
        this.FindControl<TextBlock>("TranslatorLabelText")!.Text = _text("translationEditor.translator");
        this.FindControl<TextBlock>("SearchLabelText")!.Text = _text("translationEditor.search");
        UntranslatedOnlyInput.Content = _text("translationEditor.showUntranslated");
        this.FindControl<TextBlock>("TranslationsTitleText")!.Text = _text("translationEditor.translations");
        this.FindControl<TextBlock>("KeyHeaderText")!.Text = _text("translationEditor.key");
        this.FindControl<TextBlock>("SourceHeaderText")!.Text = _text("translationEditor.source");
        this.FindControl<TextBlock>("TranslationHeaderText")!.Text = _text("translationEditor.translation");
        this.FindControl<TextBlock>("DoneHeaderText")!.Text = _text("translationEditor.done");
        this.FindControl<TextBlock>("NotesHeaderText")!.Text = _text("translationEditor.notes");
        this.FindControl<Button>("NewLocaleButton")!.Content = _text("translationEditor.newLocale");
        this.FindControl<Button>("SyncButton")!.Content = _text("translationEditor.sync");
        this.FindControl<Button>("SaveButton")!.Content = _text("translationEditor.save");
        this.FindControl<Button>("CloseButton")!.Content = _text("menu.close");
        EditorStatus.Text = _text("translationEditor.allSaved");
    }

    private void LocaleBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing || (LocaleSelector.SelectedItem as ComboBoxItem)?.Tag is not string selectedLanguage || selectedLanguage == _currentLanguage)
        {
            return;
        }

        _documents[_currentLanguage] = CaptureCurrentDocument();
        _currentLanguage = selectedLanguage;
        LoadCurrentDocument();
        EditorStatus.Text = _text("translationEditor.unsavedChanges");
    }

    private void LoadCurrentDocument()
    {
        if (!_documents.TryGetValue(_currentLanguage, out TranslationEditorDocument? document))
        {
            document = _loadDocument(_currentLanguage);
            _documents[_currentLanguage] = document;
        }

        TranslatorNameInput.Text = document.Translator;
        _allRows.Clear();
        foreach ((string key, string source) in _sourceStrings.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            string translation = document.Translations.GetValueOrDefault(key) ?? "";
            bool isDone = document.Entries.TryGetValue(key, out TranslationEditorEntryState? state)
                ? state.IsDone
                : !string.IsNullOrWhiteSpace(translation);
            var row = new TranslationEditorRow(key, source, translation, isDone, state?.Notes ?? "");
            row.PropertyChanged += TranslationRow_PropertyChanged;
            _allRows.Add(row);
        }
        RefreshVisibleRows();
        UpdateTranslatedCount();
    }

    private TranslationEditorDocument CaptureCurrentDocument()
    {
        var document = new TranslationEditorDocument
        {
            LanguageCode = _currentLanguage,
            Translator = TranslatorNameInput.Text?.Trim() ?? ""
        };
        foreach (TranslationEditorRow row in _allRows)
        {
            document.Translations[row.Key] = row.Translation;
            document.Entries[row.Key] = new TranslationEditorEntryState
            {
                IsDone = row.IsDone,
                Notes = row.Notes
            };
        }
        return document;
    }

    private void RefreshVisibleRows()
    {
        string query = SearchInput.Text?.Trim() ?? "";
        bool untranslatedOnly = UntranslatedOnlyInput.IsChecked == true;
        _visibleRows.Clear();
        foreach (TranslationEditorRow row in _allRows)
        {
            if (untranslatedOnly && row.IsDone)
            {
                continue;
            }
            if (query.Length > 0 &&
                !row.Key.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !row.Source.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !row.Translation.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !row.Notes.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            _visibleRows.Add(row);
        }
    }

    private void UpdateTranslatedCount()
    {
        int translated = _allRows.Count(row => row.IsDone);
        TranslatedCountLabel.Text = MainWindow.FormatTemplate(
            _text("translationEditor.translatedCount"),
            ("translated", translated),
            ("total", _allRows.Count));
    }

    private void TranslationRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationEditorRow.IsDone))
        {
            UpdateTranslatedCount();
            RefreshVisibleRows();
        }
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => RefreshVisibleRows();

    private void UntranslatedCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e) => RefreshVisibleRows();

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        TranslationEditorDocument document = CaptureCurrentDocument();
        try
        {
            _saveDocument(document);
            _documents[_currentLanguage] = document;
            EditorStatus.Text = _text("translationEditor.allSaved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            EditorStatus.Text = MainWindow.FormatTemplate(_text("translationEditor.saveFailed"), ("error", ex.Message));
        }
    }

    private void SyncButton_Click(object? sender, RoutedEventArgs e)
    {
        var existingRows = _allRows.ToDictionary(row => row.Key, StringComparer.Ordinal);
        _allRows.Clear();
        foreach ((string key, string source) in _sourceStrings.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (existingRows.TryGetValue(key, out TranslationEditorRow? row))
            {
                row.Source = source;
                _allRows.Add(row);
            }
            else
            {
                var newRow = new TranslationEditorRow(key, source, "", false, "");
                newRow.PropertyChanged += TranslationRow_PropertyChanged;
                _allRows.Add(newRow);
            }
        }
        RefreshVisibleRows();
        UpdateTranslatedCount();
        EditorStatus.Text = _text("translationEditor.synced");
    }

    private async void NewLocaleButton_Click(object? sender, RoutedEventArgs e)
    {
        var codeBox = new TextBox { MinWidth = 240, Watermark = _text("translationEditor.localeCodeHint") };
        var errorText = new TextBlock { Foreground = Avalonia.Media.Brushes.IndianRed, IsVisible = false };
        var createButton = new Button { Content = _text("translationEditor.create"), MinWidth = 82 };
        var cancelButton = new Button { Content = _text("menu.cancel"), MinWidth = 82 };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, createButton }
        };
        var prompt = new Window
        {
            Title = _text("translationEditor.newLocaleTitle"),
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 10,
                Children = { new TextBlock { Text = _text("translationEditor.localeCodePrompt") }, codeBox, errorText, buttons }
            }
        };
        string? newLanguage = null;
        cancelButton.Click += (_, _) => prompt.Close();
        createButton.Click += (_, _) =>
        {
            string code = (codeBox.Text ?? "").Trim().ToLowerInvariant();
            bool valid = code.Length > 0 && code.Split('-').All(part => part.Length > 0 && part.All(char.IsAsciiLetterOrDigit));
            bool exists = Enumerable.Range(0, LocaleSelector.Items.Count)
                .Any(index => (LocaleSelector.Items[index] as ComboBoxItem)?.Tag as string == code);
            if (!valid || exists)
            {
                errorText.Text = exists
                    ? _text("translationEditor.localeExists")
                    : _text("translationEditor.localeCodeInvalid");
                errorText.IsVisible = true;
                return;
            }
            newLanguage = code;
            prompt.Close();
        };
        await prompt.ShowDialog(this);
        if (newLanguage == null)
        {
            return;
        }

        _documents[_currentLanguage] = CaptureCurrentDocument();
        var newDocument = new TranslationEditorDocument { LanguageCode = newLanguage };
        _documents[newLanguage] = newDocument;
        LocaleSelector.Items.Add(new ComboBoxItem { Content = newLanguage.ToUpperInvariant(), Tag = newLanguage });
        LocaleSelector.SelectedIndex = LocaleSelector.Items.Count - 1;
        EditorStatus.Text = _text("translationEditor.newLocaleCreated");
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}

public sealed class TranslationEditorDocument
{
    public string LanguageCode { get; set; } = "en";
    public string Translator { get; set; } = "";
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, TranslationEditorEntryState> Entries { get; set; } = new(StringComparer.Ordinal);
}

public sealed class TranslationEditorMetadata
{
    public string Translator { get; set; } = "";
    public Dictionary<string, TranslationEditorEntryState> Entries { get; set; } = new(StringComparer.Ordinal);
}

public sealed class TranslationEditorEntryState
{
    public bool IsDone { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class TranslationEditorRow : INotifyPropertyChanged
{
    private string _source;
    private string _translation;
    private bool _isDone;
    private string _notes;

    public string Key { get; }
    public string Source { get => _source; set => Set(ref _source, value); }
    public string Translation { get => _translation; set => Set(ref _translation, value); }
    public bool IsDone { get => _isDone; set => Set(ref _isDone, value); }
    public string Notes { get => _notes; set => Set(ref _notes, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TranslationEditorRow(string key, string source, string translation, bool isDone, string notes)
    {
        Key = key;
        _source = source;
        _translation = translation;
        _isDone = isDone;
        _notes = notes;
    }

    private void Set(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        value ??= "";
        if (field == value)
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void Set(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}