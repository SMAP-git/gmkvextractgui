using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using gMKVToolNix;
using gMKVToolNix.MkvExtract;
using gMKVToolNix.Segments;

namespace gMKVExtractGUI.Linux;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<SegmentRow> _segmentRows = new();
    private readonly ObservableCollection<InputFileGroup> _inputFiles = new();
    private Dictionary<string, string> _englishStrings = new(StringComparer.Ordinal);
    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private AppPreferences _preferences = new();
    private bool _loadingPreferences = true;
    private gMKVExtractFilenamePatterns _filenamePatterns = new();
    private bool _filenamePatternsCustomized;
    private bool _disableBomForTextFiles;
    private bool _useRawExtractionMode;
    private bool _useFullRawExtractionMode;
    private string _currentLanguage = "en";
    private bool _isAnalyzed;
    private bool _isAnalyzing;
    private int _completedExtractionFiles;
    private int _extractionFileCount;

    public ObservableCollection<SegmentRow> SegmentRows => _segmentRows;
    public ObservableCollection<InputFileGroup> InputFiles => _inputFiles;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string[]? inputPaths)
    {
        AvaloniaXamlLoader.Load(this);
        _preferences = LoadAppPreferences();
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = GetThemeVariant(_preferences.Theme);
        }
        this.FindControl<CheckBox>("UseInputFolderCheckBox")!.IsChecked = _preferences.UseInputFolder;
        _englishStrings = LoadBundledCatalog("en");
        _strings = LoadCatalog("en");
        string savedLanguage = LoadSavedLanguage();
        if (GetAvailableLanguages().Contains(savedLanguage, StringComparer.Ordinal))
        {
            try
            {
                _strings = LoadCatalog(savedLanguage);
                _currentLanguage = savedLanguage;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Debug.WriteLine($"Could not load saved language '{savedLanguage}': {ex.Message}");
            }
        }
        _filenamePatternsCustomized = _preferences.FilenamePatternsCustomized && _preferences.FilenamePatterns != null;
        _filenamePatterns = _filenamePatternsCustomized
            ? _preferences.FilenamePatterns!
            : CreateDefaultFilenamePatterns();
        _disableBomForTextFiles = _preferences.DisableBomForTextFiles;
        _useRawExtractionMode = _preferences.UseRawExtractionMode;
        _useFullRawExtractionMode = _preferences.UseFullRawExtractionMode;
        Opened += MainWindow_Opened;
        DataContext = this;
        string? configuredOutputPath = Environment.GetEnvironmentVariable("GMKVEXTRACTGUI_DEFAULT_OUTPUT_PATH");
        if (!string.IsNullOrWhiteSpace(configuredOutputPath))
        {
            this.FindControl<TextBox>("OutputPathBox")!.Text = configuredOutputPath;
        }
        string? configuredToolPath = Environment.GetEnvironmentVariable("GMKVEXTRACTGUI_TOOL_PATH");
        if (!string.IsNullOrWhiteSpace(configuredToolPath))
        {
            this.FindControl<TextBox>("ToolPathBox")!.Text = configuredToolPath;
        }
        this.FindControl<ItemsControl>("SegmentList")!.ItemsSource = _inputFiles;
        ApplyLocalization();
        UpdateOutputFolderControls();
        _loadingPreferences = false;

        string[] existingPaths = (inputPaths ?? Array.Empty<string>()).Where(File.Exists).ToArray();
        if (existingPaths.Length > 0)
        {
            this.FindControl<TextBox>("InputPathBox")!.Text = string.Join(Environment.NewLine, existingPaths);
            TextBox outputPathBox = this.FindControl<TextBox>("OutputPathBox")!;
            if (string.IsNullOrWhiteSpace(outputPathBox.Text))
            {
                outputPathBox.Text = Path.GetDirectoryName(existingPaths[0]) ?? "";
            }
            SetStatus(T("status.analyzingInput"));
        }
    }

    private void InitializeBannerMenu()
    {
        Button bannerMenuButton = this.FindControl<Button>("BannerMenuButton")!;
        var menu = new ContextMenu();

        menu.Items.Add(CreateBannerMenuItem(T("menu.fileNameOptions"), OptionsMenu_Click));

        var languageMenu = new MenuItem { Header = T("menu.language") };
        foreach (string languageCode in GetAvailableLanguages())
        {
            var languageItem = new MenuItem
            {
                Header = languageCode.ToUpperInvariant(),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = languageCode == _currentLanguage
            };
            languageItem.Click += (_, _) => SetLanguage(languageCode);
            languageMenu.Items.Add(languageItem);
        }
        languageMenu.Items.Add(new Separator());
        languageMenu.Items.Add(CreateBannerMenuItem(T("filename.translations"), TranslationMenu_Click));
        menu.Items.Add(languageMenu);

        var themeMenu = new MenuItem { Header = T("menu.theme") };
        themeMenu.Items.Add(CreateThemeMenuItem(T("menu.themeFollowSystem"), ThemeVariant.Default));
        themeMenu.Items.Add(CreateThemeMenuItem(T("menu.themeLight"), ThemeVariant.Light));
        themeMenu.Items.Add(CreateThemeMenuItem(T("menu.themeDark"), ThemeVariant.Dark));
        menu.Items.Add(themeMenu);

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateBannerMenuItem(T("menu.about"), AboutMenu_Click));
        bannerMenuButton.ContextMenu = menu;
    }

    private void ApplyLocalization()
    {
        Title = T("app.title");
        this.FindControl<TextBlock>("AppSubtitleText")!.Text = T("app.subtitle");
        this.FindControl<TextBlock>("LinuxEditionText")!.Text = T("app.edition", ("version", GetAppVersion()));
        this.FindControl<TextBlock>("InputFilesLabel")!.Text = T("input.filesLabel");
        this.FindControl<TextBox>("InputPathBox")!.Watermark = T("input.filesWatermark");
        this.FindControl<Button>("BrowseInputButton")!.Content = T("input.browse");
        this.FindControl<TextBlock>("OutputFolderLabel")!.Text = T("output.folderLabel");
        this.FindControl<TextBox>("OutputPathBox")!.Watermark = T("output.folderWatermark");
        this.FindControl<CheckBox>("UseInputFolderCheckBox")!.Content = T("output.useInputFolder");
        this.FindControl<Button>("BrowseOutputButton")!.Content = T("output.chooseFolder");
        this.FindControl<TextBlock>("ToolPathLabel")!.Text = T("tool.pathLabel");
        this.FindControl<TextBlock>("ToolPathDescription")!.Text = T("tool.pathDescription");
        this.FindControl<TextBlock>("TracksTitleText")!.Text = T("tracks.title");
        this.FindControl<TextBlock>("ItemCountText")!.Text = T("tracks.notAnalyzed");
        this.FindControl<Button>("SelectByTypeButton")!.Content = T("tracks.selectByType");
        this.FindControl<Button>("ExpandCollapseAllButton")!.Content = T("tracks.expandCollapseAll");
        this.FindControl<Button>("SelectAllButton")!.Content = T("tracks.selectAll");
        this.FindControl<Button>("ClearSelectionButton")!.Content = T("tracks.clear");
        this.FindControl<TextBlock>("ChapterFormatLabel")!.Text = T("chapterFormat.label");
        this.FindControl<ComboBoxItem>("ChapterFormatXmlItem")!.Content = T("chapterFormat.xml");
        this.FindControl<ComboBoxItem>("ChapterFormatOgmItem")!.Content = T("chapterFormat.ogm");
        this.FindControl<ComboBoxItem>("ChapterFormatCueItem")!.Content = T("chapterFormat.cue");
        this.FindControl<ComboBoxItem>("ChapterFormatPbfItem")!.Content = T("chapterFormat.pbf");
        this.FindControl<CheckBox>("OverwriteCheckBox")!.Content = T("extraction.overwriteExisting");
        this.FindControl<TextBlock>("StatusText")!.Text = T("status.chooseInput");
        this.FindControl<TextBlock>("FileProgressLabel")!.Text = T("progress.file");
        this.FindControl<TextBlock>("OverallProgressLabel")!.Text = T("progress.overall");
        this.FindControl<Button>("ExtractButton")!.Content = T("extraction.start");

        foreach (SegmentRow row in _segmentRows)
        {
            row.RefreshLocalization(key => T(key));
        }
        foreach (InputFileGroup inputFile in _inputFiles)
        {
            inputFile.RefreshLocalization();
        }
        this.FindControl<TextBlock>("ItemCountText")!.Text = _isAnalyzing
            ? T("tracks.analyzing")
            : _isAnalyzed
                ? T("tracks.itemCount", ("count", _segmentRows.Count))
                : T("tracks.notAnalyzed");

        SetMenuHeader("AddInputFilesMenuItem", "input.context.addFiles");
        SetMenuHeader("CheckAllTracksMenuItem", "input.context.checkAllTracks");
        SetMenuHeader("UncheckAllTracksMenuItem", "input.context.uncheckAllTracks");
        SetMenuHeader("RemoveAllInputFilesMenuItem", "input.context.removeAllFiles");
        SetMenuHeader("RemoveSelectedInputFileMenuItem", "input.context.removeSelectedFile");
        SetMenuHeader("OpenSelectedInputFileMenuItem", "input.context.openSelectedFile");
        SetMenuHeader("OpenSelectedInputFolderMenuItem", "input.context.openSelectedFileFolder");
        SetMenuHeader("ExpandInputFilesMenuItem", "input.context.expandAll");
        SetMenuHeader("CollapseInputFilesMenuItem", "input.context.collapseAll");

        InitializeBannerMenu();
    }

    private void SetMenuHeader(string controlName, string stringKey) =>
        this.FindControl<MenuItem>(controlName)!.Header = T(stringKey);

    private string GetAppVersion() => typeof(App).Assembly.GetName().Version?.ToString(2) ?? "1.0";

    private IEnumerable<string> GetAvailableLanguages()
    {
        string[] languageDirectories =
        {
            Path.Combine(AppContext.BaseDirectory, "Languages"),
            GetUserLanguageDirectory()
        };
        var languageCodes = languageDirectories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.json"))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!languageCodes.Contains("en", StringComparer.Ordinal))
        {
            languageCodes.Add("en");
        }

        return languageCodes.OrderBy(code => code == "en" ? 0 : 1).ThenBy(code => code, StringComparer.Ordinal);
    }

    private Dictionary<string, string> LoadCatalog(string languageCode)
    {
        Dictionary<string, string> catalog = LoadBundledCatalog(languageCode);
        string userCatalogPath = Path.Combine(GetUserLanguageDirectory(), $"{languageCode}.json");
        if (File.Exists(userCatalogPath))
        {
            foreach ((string key, string value) in ReadCatalog(userCatalogPath))
            {
                catalog[key] = value;
            }
        }
        return catalog;
    }

    private Dictionary<string, string> LoadBundledCatalog(string languageCode)
    {
        string catalogPath = Path.Combine(AppContext.BaseDirectory, "Languages", $"{languageCode}.json");
        return File.Exists(catalogPath)
            ? ReadCatalog(catalogPath)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ReadCatalog(string catalogPath)
    {
        string json = File.ReadAllText(catalogPath);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidDataException($"The language catalog '{Path.GetFileName(catalogPath)}' is empty.");
    }

    private static string GetUserLanguageDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "gMKVExtractGUI",
        "Languages");

    private TranslationEditorDocument LoadTranslationDocument(string languageCode)
    {
        string metadataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "gMKVExtractGUI",
            "TranslationEditor",
            $"{languageCode}.json");
        TranslationEditorMetadata metadata = File.Exists(metadataPath)
            ? JsonSerializer.Deserialize<TranslationEditorMetadata>(File.ReadAllText(metadataPath)) ?? new()
            : new();

        return new TranslationEditorDocument
        {
            LanguageCode = languageCode,
            Translations = LoadCatalog(languageCode),
            Translator = metadata.Translator,
            Entries = metadata.Entries
        };
    }

    private void SaveTranslationDocument(TranslationEditorDocument document)
    {
        string userDirectory = GetUserLanguageDirectory();
        string metadataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "gMKVExtractGUI",
            "TranslationEditor");
        Directory.CreateDirectory(userDirectory);
        Directory.CreateDirectory(metadataDirectory);
        File.WriteAllText(
            Path.Combine(userDirectory, $"{document.LanguageCode}.json"),
            JsonSerializer.Serialize(document.Translations, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(metadataDirectory, $"{document.LanguageCode}.json"),
            JsonSerializer.Serialize(new TranslationEditorMetadata
            {
                Translator = document.Translator,
                Entries = document.Entries
            }, new JsonSerializerOptions { WriteIndented = true }));

        if (document.LanguageCode == _currentLanguage)
        {
            SetLanguage(_currentLanguage);
        }
        else
        {
            InitializeBannerMenu();
        }
    }

    private static string GetLanguagePreferencePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "gMKVExtractGUI",
        "language.txt");

    private static string LoadSavedLanguage()
    {
        try
        {
            return File.ReadAllText(GetLanguagePreferencePath()).Trim().ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "en";
        }
    }

    private static void SaveLanguagePreference(string languageCode)
    {
        string preferencePath = GetLanguagePreferencePath();
        Directory.CreateDirectory(Path.GetDirectoryName(preferencePath)!);
        File.WriteAllText(preferencePath, languageCode);
    }

    private string T(string key, params (string Name, object? Value)[] replacements)
    {
        string value = _strings.GetValueOrDefault(key) ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            value = _englishStrings.GetValueOrDefault(key) ?? key;
        }

        return FormatTemplate(value, replacements);
    }

    internal static string FormatTemplate(string value, params (string Name, object? Value)[] replacements)
    {
        foreach ((string name, object? replacement) in replacements)
        {
            string replacementText = replacement is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? ""
                : replacement?.ToString() ?? "";
            value = value.Replace($"{{{name}}}", replacementText, StringComparison.Ordinal);
        }

        return value;
    }

    private void SetLanguage(string languageCode)
    {
        try
        {
            _strings = LoadCatalog(languageCode);
            _currentLanguage = languageCode;
            try
            {
                SaveLanguagePreference(languageCode);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not save language preference '{languageCode}': {ex.Message}");
            }
            if (!_filenamePatternsCustomized)
            {
                _filenamePatterns = CreateDefaultFilenamePatterns();
            }
            ApplyLocalization();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus(T("status.languageLoadFailed", ("language", languageCode.ToUpperInvariant())));
        }
    }

    private void BannerMenuButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.Open(button);
        }
    }

    private static MenuItem CreateBannerMenuItem(string header, EventHandler<RoutedEventArgs>? clickHandler)
    {
        var item = new MenuItem { Header = header };
        if (clickHandler != null)
        {
            item.Click += clickHandler;
        }
        return item;
    }

    private MenuItem CreateThemeMenuItem(string header, ThemeVariant theme)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => SetTheme(theme);
        return item;
    }

    private void SetTheme(ThemeVariant theme)
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = theme;
        }

        _preferences.Theme = theme == ThemeVariant.Dark
            ? "Dark"
            : theme == ThemeVariant.Light
                ? "Light"
                : "System";
        SaveAppPreferences();
    }

    private static ThemeVariant GetThemeVariant(string theme) => theme.ToLowerInvariant() switch
    {
        "dark" => ThemeVariant.Dark,
        "system" => ThemeVariant.Default,
        _ => ThemeVariant.Light
    };

    private static string GetAppPreferencesPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "gMKVExtractGUI",
        "settings.json");

    private static AppPreferences LoadAppPreferences()
    {
        try
        {
            string settingsPath = GetAppPreferencesPath();
            if (File.Exists(settingsPath))
            {
                return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(settingsPath)) ?? new();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"Could not load app preferences: {ex.Message}");
        }

        return new AppPreferences();
    }

    private void SaveAppPreferences()
    {
        try
        {
            string settingsPath = GetAppPreferencesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(_preferences, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not save app preferences: {ex.Message}");
        }
    }

    private async void OptionsMenu_Click(object? sender, RoutedEventArgs e)
    {
        var videoPattern = new TextBox { Text = _filenamePatterns.VideoTrackFilenamePattern, MinHeight = 34 };
        var audioPattern = new TextBox { Text = _filenamePatterns.AudioTrackFilenamePattern, MinHeight = 34 };
        var subtitlePattern = new TextBox { Text = _filenamePatterns.SubtitleTrackFilenamePattern, MinHeight = 34 };
        var chapterPattern = new TextBox { Text = _filenamePatterns.ChapterFilenamePattern, MinHeight = 34 };
        var attachmentPattern = new TextBox { Text = _filenamePatterns.AttachmentFilenamePattern, MinHeight = 34 };
        var tagsPattern = new TextBox { Text = _filenamePatterns.TagsFilenamePattern, MinHeight = 34 };

        var information = CreateSection(T("filename.information"), new TextBlock
        {
            Text = T("filename.optionsDescription"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(6)
        });
        var patternRows = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                CreatePatternRow("filename.videoLabel", videoPattern, () => videoPattern.Text = CreateDefaultFilenamePatterns().VideoTrackFilenamePattern),
                CreatePatternRow("filename.audioLabel", audioPattern, () => audioPattern.Text = CreateDefaultFilenamePatterns().AudioTrackFilenamePattern),
                CreatePatternRow("filename.subtitleLabel", subtitlePattern, () => subtitlePattern.Text = CreateDefaultFilenamePatterns().SubtitleTrackFilenamePattern),
                CreatePatternRow("filename.chapterLabel", chapterPattern, () => chapterPattern.Text = CreateDefaultFilenamePatterns().ChapterFilenamePattern),
                CreatePatternRow("filename.attachmentLabel", attachmentPattern, () => attachmentPattern.Text = CreateDefaultFilenamePatterns().AttachmentFilenamePattern),
                CreatePatternRow("filename.tagsLabel", tagsPattern, () => tagsPattern.Text = CreateDefaultFilenamePatterns().TagsFilenamePattern)
            }
        };

        var disableBom = new CheckBox
        {
            Content = T("filename.disableBom"),
            IsChecked = _disableBomForTextFiles
        };
        var useRaw = new CheckBox
        {
            Content = T("filename.useRaw"),
            IsChecked = _useRawExtractionMode
        };
        var useFullRaw = new CheckBox
        {
            Content = T("filename.useFullRaw"),
            IsChecked = _useFullRawExtractionMode
        };
        var advanced = CreateSection(T("filename.advancedOptions"), new StackPanel
        {
            Spacing = 4,
            Children =
            {
                disableBom,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children = { useRaw, useFullRaw }
                }
            }
        });

        var defaultsButton = new Button { Content = T("filename.defaults") };
        var cancelButton = new Button { Content = T("menu.cancel"), MinWidth = 82 };
        var okButton = new Button { Content = T("menu.ok"), MinWidth = 82 };
        var actionGrid = new Grid { ColumnSpacing = 8 };
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(defaultsButton, 0);
        Grid.SetColumn(cancelButton, 2);
        Grid.SetColumn(okButton, 3);
        actionGrid.Children.Add(defaultsButton);
        actionGrid.Children.Add(cancelButton);
        actionGrid.Children.Add(okButton);

        var content = new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 6,
            Children = { information, patternRows, advanced, actionGrid }
        };
        var dialog = new Window
        {
            Title = T("filename.optionsTitle"),
            Width = 706,
            Height = 650,
            MinWidth = 680,
            MinHeight = 600,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = content
            }
        };

        defaultsButton.Click += (_, _) =>
        {
            gMKVExtractFilenamePatterns defaults = CreateDefaultFilenamePatterns();
            videoPattern.Text = defaults.VideoTrackFilenamePattern;
            audioPattern.Text = defaults.AudioTrackFilenamePattern;
            subtitlePattern.Text = defaults.SubtitleTrackFilenamePattern;
            chapterPattern.Text = defaults.ChapterFilenamePattern;
            attachmentPattern.Text = defaults.AttachmentFilenamePattern;
            tagsPattern.Text = defaults.TagsFilenamePattern;
            disableBom.IsChecked = false;
            useRaw.IsChecked = false;
            useFullRaw.IsChecked = false;
        };
        cancelButton.Click += (_, _) => dialog.Close();

        bool saved = false;
        okButton.Click += (_, _) =>
        {
            _filenamePatterns = new gMKVExtractFilenamePatterns
            {
                VideoTrackFilenamePattern = videoPattern.Text ?? "",
                AudioTrackFilenamePattern = audioPattern.Text ?? "",
                SubtitleTrackFilenamePattern = subtitlePattern.Text ?? "",
                ChapterFilenamePattern = chapterPattern.Text ?? "",
                AttachmentFilenamePattern = attachmentPattern.Text ?? "",
                TagsFilenamePattern = tagsPattern.Text ?? ""
            };
            _filenamePatternsCustomized = true;
            _disableBomForTextFiles = disableBom.IsChecked == true;
            _useRawExtractionMode = useRaw.IsChecked == true;
            _useFullRawExtractionMode = useFullRaw.IsChecked == true;
            _preferences.FilenamePatternsCustomized = _filenamePatternsCustomized;
            _preferences.FilenamePatterns = CreateFilenamePatterns();
            _preferences.DisableBomForTextFiles = _disableBomForTextFiles;
            _preferences.UseRawExtractionMode = _useRawExtractionMode;
            _preferences.UseFullRawExtractionMode = _useFullRawExtractionMode;
            SaveAppPreferences();
            saved = true;
            dialog.Close();
        };

        await dialog.ShowDialog(this);
        if (saved)
        {
            SetStatus(T("filename.saved"));
        }
    }

    private Border CreatePatternRow(string labelKey, TextBox editor, Action reset)
    {
        var addButton = new Button { Content = T("filename.add"), MinWidth = 78 };
        addButton.Click += (_, _) => OpenPlaceholderPicker(editor, addButton);
        var defaultButton = new Button { Content = T("filename.default"), MinWidth = 78 };
        defaultButton.Click += (_, _) => reset();

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(addButton, 1);
        Grid.SetColumn(defaultButton, 2);
        row.Children.Add(editor);
        row.Children.Add(addButton);
        row.Children.Add(defaultButton);
        return CreateSection(T(labelKey), row);
    }

    private Border CreateSection(string title, Control content)
    {
        var sectionContent = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                content
            }
        };
        return new Border
        {
            BorderBrush = new Avalonia.Media.SolidColorBrush(
                ActualThemeVariant == ThemeVariant.Dark ? 0xFF334049 : 0xFFD0D6D4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5),
            Child = sectionContent
        };
    }

    private void OpenPlaceholderPicker(TextBox editor, Control anchor)
    {
        string[] placeholders =
        {
            gMKVExtractFilenamePatterns.FilenameNoExt,
            gMKVExtractFilenamePatterns.Filename,
            gMKVExtractFilenamePatterns.DirectorySeparator,
            gMKVExtractFilenamePatterns.TrackNumber,
            gMKVExtractFilenamePatterns.TrackNumber_0,
            gMKVExtractFilenamePatterns.TrackNumber_00,
            gMKVExtractFilenamePatterns.TrackNumber_000,
            gMKVExtractFilenamePatterns.TrackID,
            gMKVExtractFilenamePatterns.TrackID_0,
            gMKVExtractFilenamePatterns.TrackID_00,
            gMKVExtractFilenamePatterns.TrackID_000,
            gMKVExtractFilenamePatterns.TrackName,
            gMKVExtractFilenamePatterns.TrackLanguage,
            gMKVExtractFilenamePatterns.TrackLanguageIetf,
            gMKVExtractFilenamePatterns.TrackCodecID,
            gMKVExtractFilenamePatterns.TrackCodecPrivate,
            gMKVExtractFilenamePatterns.TrackDelay,
            gMKVExtractFilenamePatterns.TrackEffectiveDelay,
            gMKVExtractFilenamePatterns.TrackForced,
            gMKVExtractFilenamePatterns.VideoPixelWidth,
            gMKVExtractFilenamePatterns.VideoPixelHeight,
            gMKVExtractFilenamePatterns.AudioSamplingFrequency,
            gMKVExtractFilenamePatterns.AudioChannels,
            gMKVExtractFilenamePatterns.AttachmentID,
            gMKVExtractFilenamePatterns.AttachmentID_0,
            gMKVExtractFilenamePatterns.AttachmentID_00,
            gMKVExtractFilenamePatterns.AttachmentID_000,
            gMKVExtractFilenamePatterns.AttachmentFilename,
            gMKVExtractFilenamePatterns.AttachmentMimeType,
            gMKVExtractFilenamePatterns.AttachmentFileSize
        };

        var menu = new ContextMenu();
        foreach (string placeholder in placeholders)
        {
            var item = new MenuItem { Header = placeholder };
            item.Click += (_, _) =>
            {
                int start = editor.SelectionStart;
                int length = editor.SelectionEnd - start;
                string value = editor.Text ?? "";
                editor.Text = value.Remove(start, length).Insert(start, placeholder);
                editor.CaretIndex = start + placeholder.Length;
                editor.Focus();
            };
            menu.Items.Add(item);
        }
        menu.Open(anchor);
    }

    private async void TranslationMenu_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await ShowTranslationsDialogAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Translation editor failed to open: {ex}");
            var details = new TextBox
            {
                Text = $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Height = 220
            };
            var closeButton = new Button
            {
                Content = T("menu.close"),
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 82
            };
            var errorWindow = new Window
            {
                Title = T("translationEditor.openErrorTitle"),
                Width = 560,
                SizeToContent = SizeToContent.Height,
                CanResize = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(14),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = T("translationEditor.openError"),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        details,
                        closeButton
                    }
                }
            };
            closeButton.Click += (_, _) => errorWindow.Close();
            await errorWindow.ShowDialog(this);
        }
    }

    private async Task ShowTranslationsDialogAsync()
    {
        var dialog = new TranslationEditorWindow(
            GetAvailableLanguages(),
            _currentLanguage,
            _englishStrings,
            LoadTranslationDocument,
            SaveTranslationDocument,
            key => T(key));
        await dialog.ShowDialog(this);
    }

    private static string GetCultureDisplayName(string languageCode)
    {
        string cultureCode = languageCode.ToLowerInvariant() switch
        {
            "zn-cn" => "zh-CN",
            "zn-tw" => "zh-TW",
            _ => languageCode
        };

        try
        {
            return CultureInfo.GetCultureInfo(cultureCode).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return languageCode;
        }
    }

    private void AboutMenu_Click(object? sender, RoutedEventArgs e)
    {
        var versionText = GetAppVersion();
        var aboutWindow = new Window
        {
            Title = T("menu.aboutTitle"),
            Width = 420,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = T("about.appName"), FontSize = 24, FontWeight = Avalonia.Media.FontWeight.Bold },
                    new TextBlock { Text = T("about.edition", ("version", versionText)), Foreground = new Avalonia.Media.SolidColorBrush(0xFF2E6753) },
                    new TextBlock
                    {
                        Text = T("menu.aboutDescription"),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Foreground = new Avalonia.Media.SolidColorBrush(0xFF52615B)
                    },
                    new Button
                    {
                        Content = T("menu.close"),
                        Width = 90,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 8, 0, 0)
                    }
                }
            }
        };

        Button closeButton = (Button)((StackPanel)aboutWindow.Content!).Children[3];
        closeButton.Click += (_, _) => aboutWindow.Close();
        aboutWindow.ShowDialog(this);
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (GetInputPaths().Length > 0)
        {
            await AnalyzeInputFilesAsync();
        }
    }

    private async void BrowseInput_Click(object? sender, RoutedEventArgs e) => await SelectInputFilesAsync(false);

    private async void AddInputFilesMenu_Click(object? sender, RoutedEventArgs e) => await SelectInputFilesAsync(true);

    private async Task SelectInputFilesAsync(bool append)
    {
        var pickerOptions = new FilePickerOpenOptions
        {
            Title = T("input.selectFilesDialogTitle"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(T("input.fileTypeMatroska")) { Patterns = new[] { "*.mkv", "*.mka", "*.mks" } },
                new FilePickerFileType(T("input.fileTypeAll")) { Patterns = new[] { "*" } }
            }
        };
        string? configuredInputPath = Environment.GetEnvironmentVariable("GMKVEXTRACTGUI_DEFAULT_INPUT_PATH");
        if (!string.IsNullOrWhiteSpace(configuredInputPath) && Directory.Exists(configuredInputPath))
        {
            string fullInputPath = Path.GetFullPath(configuredInputPath);
            Uri inputFolderUri = new UriBuilder
            {
                Scheme = Uri.UriSchemeFile,
                Host = string.Empty,
                Path = Path.TrimEndingDirectorySeparator(fullInputPath) + Path.DirectorySeparatorChar
            }.Uri;
            IStorageFolder? inputFolder = await StorageProvider.TryGetFolderFromPathAsync(inputFolderUri);
            if (inputFolder != null)
            {
                pickerOptions.SuggestedStartLocation = inputFolder;
            }
            else
            {
                Debug.WriteLine($"Could not resolve input picker start directory '{inputFolderUri}'.");
            }
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(pickerOptions);

        if (files.Count == 0)
        {
            return;
        }

        string[] selectedPaths = files.Select(file => file.Path.LocalPath).ToArray();
        string[] inputPaths = append
            ? GetInputPaths().Concat(selectedPaths).Distinct(StringComparer.Ordinal).ToArray()
            : selectedPaths;
        this.FindControl<TextBox>("InputPathBox")!.Text = string.Join(Environment.NewLine, inputPaths);
        TextBox outputBox = this.FindControl<TextBox>("OutputPathBox")!;
        if (string.IsNullOrWhiteSpace(outputBox.Text))
        {
            outputBox.Text = Path.GetDirectoryName(inputPaths[0]) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        await AnalyzeInputFilesAsync();
    }

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = T("output.selectFolderDialogTitle"),
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            this.FindControl<TextBox>("OutputPathBox")!.Text = folders[0].Path.LocalPath;
        }
    }

    private void UseInputFolderCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        UpdateOutputFolderControls();
        if (!_loadingPreferences)
        {
            _preferences.UseInputFolder = this.FindControl<CheckBox>("UseInputFolderCheckBox")!.IsChecked == true;
            SaveAppPreferences();
        }
    }

    private void UpdateOutputFolderControls()
    {
        bool useOutputFolder = this.FindControl<CheckBox>("UseInputFolderCheckBox")!.IsChecked != true;
        this.FindControl<TextBox>("OutputPathBox")!.IsEnabled = useOutputFolder;
        this.FindControl<Button>("BrowseOutputButton")!.IsEnabled = useOutputFolder;
    }

    private async Task AnalyzeInputFilesAsync()
    {
        if (_isAnalyzing)
        {
            return;
        }

        string[] inputPaths = GetInputPaths();
        string toolPath = this.FindControl<TextBox>("ToolPathBox")!.Text?.Trim() ?? "";
        if (inputPaths.Length == 0 || inputPaths.Any(inputPath => !File.Exists(inputPath)))
        {
            SetStatus(T("status.selectExistingInputs"));
            return;
        }

        if (!Directory.Exists(toolPath))
        {
            SetStatus(T("status.selectToolFolder"));
            return;
        }

        _isAnalyzed = false;
        _isAnalyzing = true;
        _segmentRows.Clear();
        _inputFiles.Clear();
        this.FindControl<TextBlock>("ItemCountText")!.Text = T("tracks.analyzing");
        SetBusy(true);
        try
        {
            for (int i = 0; i < inputPaths.Length; i++)
            {
                string inputPath = inputPaths[i];
                SetStatus(T("status.analyzingFile", ("current", i + 1), ("total", inputPaths.Length), ("fileName", Path.GetFileName(inputPath))));
                List<gMKVSegment> segments = await Task.Run(() => gMKVHelper.GetMergedMkvSegmentList(toolPath, inputPath));
                List<SegmentRow> fileRows = segments
                    .Where(IsExtractableSegment)
                    .Select(segment => new SegmentRow(inputPath, segment, key => T(key)))
                    .ToList();
                foreach (SegmentRow row in fileRows)
                {
                    _segmentRows.Add(row);
                }

                _inputFiles.Add(new InputFileGroup(inputPath, fileRows, key => T(key)));
                UpdateExpandCollapseAllButton();
            }

            this.FindControl<TextBlock>("ItemCountText")!.Text = T("tracks.itemCount", ("count", _segmentRows.Count));
            _isAnalyzed = true;
            this.FindControl<StackPanel>("OverallProgressPanel")!.IsVisible = false;
            this.FindControl<ProgressBar>("FileProgressBar")!.Value = 0;
            this.FindControl<ProgressBar>("OverallProgressBar")!.Value = 0;
            this.FindControl<TextBlock>("FileProgressLabel")!.Text = T("progress.file");
            this.FindControl<TextBlock>("FileProgressPercent")!.Text = "0%";
            this.FindControl<TextBlock>("OverallProgressPercent")!.Text = "0%";
            SetStatus(_segmentRows.Count == 0
                ? T("status.noExtractableItems")
                : T("status.analysisComplete", ("count", inputPaths.Length)));

            TextBox outputBox = this.FindControl<TextBox>("OutputPathBox")!;
            if (string.IsNullOrWhiteSpace(outputBox.Text))
            {
                outputBox.Text = Path.GetDirectoryName(inputPaths[0]) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }
        catch (Exception ex)
        {
            SetStatus(T("status.analysisFailed", ("message", ex.Message)));
        }
        finally
        {
            _isAnalyzing = false;
            SetBusy(false);
        }
    }

    private async void Extract_Click(object? sender, RoutedEventArgs e)
    {
        string[] inputPaths = GetInputPaths();
        string outputPath = this.FindControl<TextBox>("OutputPathBox")!.Text?.Trim() ?? "";
        bool useInputFolder = this.FindControl<CheckBox>("UseInputFolderCheckBox")!.IsChecked == true;
        string toolPath = this.FindControl<TextBox>("ToolPathBox")!.Text?.Trim() ?? "";
        if (inputPaths.Length == 0 || inputPaths.Any(inputPath => !File.Exists(inputPath)) || !Directory.Exists(toolPath))
        {
            SetStatus(T("status.chooseInputsAndTools"));
            return;
        }

        if (!useInputFolder && string.IsNullOrWhiteSpace(outputPath))
        {
            SetStatus(T("status.chooseOutputFolder"));
            return;
        }

        if (!_segmentRows.Any(row => row.IsSelected))
        {
            SetStatus(T("status.selectItemsToExtract"));
            return;
        }

        try
        {
            List<(string InputPath, List<gMKVSegment> Segments)> extractionFiles = inputPaths
                .Select(inputPath => (
                    InputPath: inputPath,
                    Segments: _segmentRows
                        .Where(row => row.InputPath == inputPath && row.IsSelected)
                        .Select(row => row.Segment)
                        .ToList()))
                .Where(file => file.Segments.Count > 0)
                .ToList();

            _completedExtractionFiles = 0;
            _extractionFileCount = extractionFiles.Count;
            this.FindControl<StackPanel>("OverallProgressPanel")!.IsVisible = _extractionFileCount > 1;

            int activeExtractionFile = -1;
            var extractor = new gMKVExtract(toolPath);
            extractor.MkvExtractProgressUpdated += progress =>
            {
                int eventFileIndex = activeExtractionFile;
                Dispatcher.UIThread.Post(() =>
                {
                    if (eventFileIndex == activeExtractionFile)
                    {
                        UpdateExtractionProgress(progress);
                    }
                });
            };
            extractor.MkvExtractTrackUpdated += (filename, trackName) => Dispatcher.UIThread.Post(() =>
                SetStatus(T("status.extractingTrack", ("fileName", Path.GetFileName(filename)), ("trackName", trackName))));

            SetBusy(true);
            for (int i = 0; i < extractionFiles.Count; i++)
            {
                (string inputPath, List<gMKVSegment> selected) = extractionFiles[i];
                string extractionOutputPath = useInputFolder
                    ? Path.GetDirectoryName(inputPath) ?? ""
                    : outputPath;
                if (string.IsNullOrWhiteSpace(extractionOutputPath))
                {
                    throw new DirectoryNotFoundException(T("status.chooseOutputFolder"));
                }
                Directory.CreateDirectory(extractionOutputPath);
                activeExtractionFile = i;
                this.FindControl<TextBlock>("FileProgressLabel")!.Text = T("progress.fileForInput", ("fileName", Path.GetFileName(inputPath)));
                UpdateExtractionProgress(0);

                var parameters = new gMKVExtractSegmentsParameters
                {
                    MKVFile = inputPath,
                    MKVSegmentsToExtract = selected,
                    OutputDirectory = extractionOutputPath,
                    ChapterType = (MkvChapterTypes)this.FindControl<ComboBox>("ChapterFormatBox")!.SelectedIndex,
                    FilenamePatterns = CreateFilenamePatterns(),
                    DisableBomForTextFiles = _disableBomForTextFiles,
                    UseRawExtractionMode = _useRawExtractionMode,
                    UseFullRawExtractionMode = _useFullRawExtractionMode,
                    OverwriteExistingFile = this.FindControl<CheckBox>("OverwriteCheckBox")!.IsChecked == true
                };

                SetStatus(T("status.extractingFile", ("current", i + 1), ("total", extractionFiles.Count), ("fileName", Path.GetFileName(inputPath))));
                await Task.Run(() => extractor.ExtractMKVSegmentsThreaded(parameters));
                if (extractor.ThreadedException != null)
                {
                    throw extractor.ThreadedException;
                }

                _completedExtractionFiles++;
                activeExtractionFile = -1;
                UpdateExtractionProgress(100, currentFileCompleted: true);
            }

            SetStatus(T("extraction.complete"));
        }
        catch (Exception ex)
        {
            SetStatus(T("status.extractionFailed", ("message", ex.Message)));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (SegmentRow row in _segmentRows)
        {
            row.IsSelected = true;
        }
    }

    private void UpdateExtractionProgress(int currentFileProgress, bool currentFileCompleted = false)
    {
        int boundedProgress = Math.Clamp(currentFileProgress, 0, 100);
        double overallProgress = _extractionFileCount == 0
            ? 0
            : Math.Clamp(
                (_completedExtractionFiles + (currentFileCompleted ? 0 : boundedProgress / 100d)) / _extractionFileCount * 100,
                0,
                100);
        this.FindControl<ProgressBar>("FileProgressBar")!.Value = boundedProgress;
        this.FindControl<ProgressBar>("OverallProgressBar")!.Value = overallProgress;
        this.FindControl<TextBlock>("OverallProgressLabel")!.Text =
            T("progress.overallFiles", ("completed", _completedExtractionFiles), ("total", _extractionFileCount));
        this.FindControl<TextBlock>("FileProgressPercent")!.Text = $"{boundedProgress}%";
        this.FindControl<TextBlock>("OverallProgressPercent")!.Text = $"{Math.Round(overallProgress)}%";
    }

    private void SelectNone_Click(object? sender, RoutedEventArgs e)
    {
        foreach (SegmentRow row in _segmentRows)
        {
            row.IsSelected = false;
        }
    }

    private void SelectByType_Click(object? sender, RoutedEventArgs e)
    {
        Button selectByTypeButton = this.FindControl<Button>("SelectByTypeButton")!;
        var menu = new ContextMenu();

        int selectedCount = _segmentRows.Count(row => row.IsSelected);
        menu.Items.Add(CreateSelectAllMenuItem(T("tracks.selectAllElements", ("total", _segmentRows.Count)), _segmentRows.ToList()));
        menu.Items.Add(CreateClearSelectionMenuItem(T("tracks.clearAllElements", ("selected", selectedCount)), _segmentRows.ToList()));
        menu.Items.Add(new Separator());

        AddTrackTypeMenu(menu, MkvTrackType.video, T("tracks.type.video"));
        AddTrackTypeMenu(menu, MkvTrackType.audio, T("tracks.type.audio"));
        AddTrackTypeMenu(menu, MkvTrackType.subtitles, T("tracks.type.subtitle"));
        AddElementTypeMenu(menu, T("tracks.type.chapters"), row => row.Segment is gMKVChapter);
        AddElementTypeMenu(menu, T("tracks.type.attachments"), row => row.Segment is gMKVAttachment);

        menu.Open(selectByTypeButton);
    }

    private void AddTrackTypeMenu(ContextMenu menu, MkvTrackType trackType, string typeLabel)
    {
        string trackLabel = T("tracks.trackLabel", ("type", typeLabel));
        List<SegmentRow> rows = _segmentRows
            .Where(row => row.Segment is gMKVTrack track && track.TrackType == trackType)
            .ToList();
        int selectedCount = rows.Count(row => row.IsSelected);
        var trackMenu = new MenuItem
        {
            Header = T("tracks.trackTypeCount", ("trackLabel", trackLabel), ("selected", selectedCount), ("total", rows.Count)),
            IsEnabled = rows.Count > 0
        };

        trackMenu.Items.Add(CreateSelectAllMenuItem(
            T("tracks.allOfType", ("trackLabel", trackLabel), ("selected", selectedCount), ("total", rows.Count)), rows));
        trackMenu.Items.Add(CreateClearSelectionMenuItem(
            T("tracks.clearType", ("trackLabel", trackLabel), ("selected", selectedCount)), rows));

        foreach ((string characteristic, Func<gMKVTrack, string> selector) in GetTrackCharacteristics(trackType))
        {
            List<(string Value, List<SegmentRow> Rows)> groups = rows
                .Select(row => (Row: row, Track: (gMKVTrack)row.Segment))
                .GroupBy(item => selector(item.Track) ?? "")
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => (group.Key, group.Select(item => item.Row).ToList()))
                .ToList();
            var characteristicMenu = new MenuItem
            {
                Header = T("tracks.byCharacteristic", ("trackLabel", trackLabel), ("characteristic", characteristic), ("groupCount", groups.Count)),
                IsEnabled = groups.Count > 0
            };

            foreach ((string value, List<SegmentRow> groupRows) in groups)
            {
                string displayValue = string.IsNullOrWhiteSpace(value) ? T("tracks.unspecified") : value;
                int groupSelectedCount = groupRows.Count(row => row.IsSelected);
                characteristicMenu.Items.Add(CreateSelectAllMenuItem(
                    T("tracks.groupValue", ("value", displayValue), ("selected", groupSelectedCount), ("total", groupRows.Count)),
                    groupRows));
            }

            trackMenu.Items.Add(characteristicMenu);
        }

        menu.Items.Add(trackMenu);
    }

    private IReadOnlyList<(string Name, Func<gMKVTrack, string> Selector)> GetTrackCharacteristics(MkvTrackType trackType)
    {
        var characteristics = new List<(string Name, Func<gMKVTrack, string> Selector)>
        {
            (T("tracks.characteristic.language"), track => track.Language),
            (T("tracks.characteristic.languageIetf"), track => track.LanguageIetf),
            (T("tracks.characteristic.codec"), track => track.CodecID),
            (T("tracks.characteristic.trackName"), track => track.TrackName),
            (T("tracks.characteristic.forced"), track => track.Forced ? T("tracks.value.yes") : T("tracks.value.no"))
        };

        if (trackType == MkvTrackType.video)
        {
            characteristics.Insert(2, (T("tracks.characteristic.resolution"), track => $"{track.VideoPixelWidth}x{track.VideoPixelHeight}"));
        }
        else if (trackType == MkvTrackType.audio)
        {
            characteristics.Insert(2, (T("tracks.characteristic.channels"), track => track.AudioChannels.ToString()));
        }

        return characteristics;
    }

    private void AddElementTypeMenu(ContextMenu menu, string typeLabel, Func<SegmentRow, bool> matches)
    {
        List<SegmentRow> rows = _segmentRows.Where(matches).ToList();
        int selectedCount = rows.Count(row => row.IsSelected);
        var elementMenu = new MenuItem
        {
            Header = T("tracks.typeCount", ("typeLabel", typeLabel), ("selected", selectedCount), ("total", rows.Count)),
            IsEnabled = rows.Count > 0
        };

        elementMenu.Items.Add(CreateSelectAllMenuItem(
            T("tracks.allOfType", ("trackLabel", typeLabel), ("selected", selectedCount), ("total", rows.Count)), rows));
        elementMenu.Items.Add(CreateClearSelectionMenuItem(
            T("tracks.clearType", ("trackLabel", typeLabel), ("selected", selectedCount)), rows));
        menu.Items.Add(elementMenu);
    }

    private static MenuItem CreateSelectAllMenuItem(string header, List<SegmentRow> rows)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = rows.Any(row => !row.IsSelected)
        };
        item.Click += (_, _) =>
        {
            foreach (SegmentRow row in rows)
            {
                row.IsSelected = true;
            }
        };
        return item;
    }

    private static MenuItem CreateClearSelectionMenuItem(string header, List<SegmentRow> rows)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = rows.Any(row => row.IsSelected)
        };
        item.Click += (_, _) =>
        {
            foreach (SegmentRow row in rows)
            {
                row.IsSelected = false;
            }
        };
        return item;
    }

    private void RemoveAllInputFiles_Click(object? sender, RoutedEventArgs e)
    {
        this.FindControl<TextBox>("InputPathBox")!.Text = "";
    }

    private void RemoveSelectedInputFile_Click(object? sender, RoutedEventArgs e)
    {
        TextBox inputBox = this.FindControl<TextBox>("InputPathBox")!;
        string text = inputBox.Text ?? "";
        if (text.Length == 0)
        {
            return;
        }

        int selectionStart = Math.Clamp(inputBox.SelectionStart, 0, text.Length);
        int selectionEnd = Math.Clamp(inputBox.SelectionEnd, selectionStart, text.Length);
        int firstLineStart = selectionStart == 0 ? 0 : text.LastIndexOf('\n', selectionStart - 1) + 1;
        int lastLinePosition = selectionEnd > selectionStart ? selectionEnd - 1 : selectionStart;
        int lineEnd = text.IndexOf('\n', lastLinePosition);
        int removeLength = (lineEnd < 0 ? text.Length : lineEnd + 1) - firstLineStart;
        inputBox.Text = text.Remove(firstLineStart, removeLength);
        inputBox.CaretIndex = Math.Min(firstLineStart, inputBox.Text.Length);
    }

    private void OpenSelectedInputFile_Click(object? sender, RoutedEventArgs e)
    {
        foreach (string inputPath in GetContextInputPaths().Where(File.Exists))
        {
            try
            {
                Process.Start(new ProcessStartInfo(inputPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus(T("status.couldNotOpenFile", ("message", ex.Message)));
                return;
            }
        }
    }

    private void OpenSelectedInputFolder_Click(object? sender, RoutedEventArgs e)
    {
        string? inputPath = GetContextInputPaths().FirstOrDefault(File.Exists);
        string? directory = inputPath == null ? null : Path.GetDirectoryName(inputPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            startInfo.ArgumentList.Add(directory);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            SetStatus(T("status.couldNotOpenFolder", ("message", ex.Message)));
        }
    }

    private void ExpandInputFiles_Click(object? sender, RoutedEventArgs e)
    {
        foreach (InputFileGroup inputFile in _inputFiles)
        {
            inputFile.IsExpanded = true;
        }
    }

    private void ExpandCollapseAll_Click(object? sender, RoutedEventArgs e)
    {
        bool expandAll = _inputFiles.Any(inputFile => !inputFile.IsExpanded);
        foreach (InputFileGroup inputFile in _inputFiles)
        {
            inputFile.IsExpanded = expandAll;
        }
    }

    private void UpdateExpandCollapseAllButton() =>
        this.FindControl<Button>("ExpandCollapseAllButton")!.IsEnabled = _inputFiles.Count > 0;

    private void CollapseInputFiles_Click(object? sender, RoutedEventArgs e)
    {
        foreach (InputFileGroup inputFile in _inputFiles)
        {
            inputFile.IsExpanded = false;
        }
    }

    private void InputPath_Changed(object? sender, TextChangedEventArgs e)
    {
        _isAnalyzed = false;
        _segmentRows.Clear();
        _inputFiles.Clear();
        UpdateExpandCollapseAllButton();
        this.FindControl<StackPanel>("OverallProgressPanel")!.IsVisible = false;
        this.FindControl<ProgressBar>("FileProgressBar")!.Value = 0;
        this.FindControl<ProgressBar>("OverallProgressBar")!.Value = 0;
        this.FindControl<TextBlock>("FileProgressLabel")!.Text = T("progress.file");
        this.FindControl<TextBlock>("FileProgressPercent")!.Text = "0%";
        this.FindControl<TextBlock>("OverallProgressPercent")!.Text = "0%";
        this.FindControl<TextBlock>("ItemCountText")!.Text = T("tracks.notAnalyzed");
        this.FindControl<Button>("ExtractButton")!.IsEnabled = false;
        this.FindControl<Button>("SelectByTypeButton")!.IsEnabled = false;
    }

    private string[] GetInputPaths() => (this.FindControl<TextBox>("InputPathBox")!.Text ?? "")
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string[] GetContextInputPaths()
    {
        TextBox inputBox = this.FindControl<TextBox>("InputPathBox")!;
        string text = inputBox.Text ?? "";
        int selectionStart = Math.Clamp(inputBox.SelectionStart, 0, text.Length);
        int selectionEnd = Math.Clamp(inputBox.SelectionEnd, selectionStart, text.Length);
        int firstLineStart = selectionStart == 0 ? 0 : text.LastIndexOf('\n', selectionStart - 1) + 1;
        int lastLinePosition = selectionEnd > selectionStart ? selectionEnd - 1 : selectionStart;
        int lineEnd = text.IndexOf('\n', lastLinePosition);
        string selectedLines = text.Substring(firstLineStart, (lineEnd < 0 ? text.Length : lineEnd) - firstLineStart);
        return selectedLines.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private void SetBusy(bool isBusy)
    {
        this.FindControl<Button>("BrowseInputButton")!.IsEnabled = !isBusy;
        this.FindControl<TextBox>("InputPathBox")!.IsEnabled = !isBusy;
        this.FindControl<Button>("ExtractButton")!.IsEnabled = !isBusy && _isAnalyzed;
        this.FindControl<Button>("SelectByTypeButton")!.IsEnabled = !isBusy && _isAnalyzed;
    }

    private void SetStatus(string status) => this.FindControl<TextBlock>("StatusText")!.Text = status;

    private static bool IsExtractableSegment(gMKVSegment segment) => segment is gMKVTrack or gMKVChapter or gMKVAttachment;

    private gMKVExtractFilenamePatterns CreateDefaultFilenamePatterns() => new()
    {
        VideoTrackFilenamePattern = T("filename.videoPattern"),
        AudioTrackFilenamePattern = T("filename.audioPattern"),
        SubtitleTrackFilenamePattern = T("filename.subtitlePattern"),
        ChapterFilenamePattern = T("filename.chapterPattern"),
        AttachmentFilenamePattern = T("filename.attachmentPattern"),
        TagsFilenamePattern = T("filename.tagsPattern")
    };

    private gMKVExtractFilenamePatterns CreateFilenamePatterns() => new()
    {
        VideoTrackFilenamePattern = _filenamePatterns.VideoTrackFilenamePattern,
        AudioTrackFilenamePattern = _filenamePatterns.AudioTrackFilenamePattern,
        SubtitleTrackFilenamePattern = _filenamePatterns.SubtitleTrackFilenamePattern,
        ChapterFilenamePattern = _filenamePatterns.ChapterFilenamePattern,
        AttachmentFilenamePattern = _filenamePatterns.AttachmentFilenamePattern,
        TagsFilenamePattern = _filenamePatterns.TagsFilenamePattern
    };

    private sealed class AppPreferences
    {
        public AppPreferences()
        {
        }

        public string Theme { get; set; } = "Light";
        public bool UseInputFolder { get; set; } = true;
        public bool FilenamePatternsCustomized { get; set; }
        public gMKVExtractFilenamePatterns? FilenamePatterns { get; set; }
        public bool DisableBomForTextFiles { get; set; }
        public bool UseRawExtractionMode { get; set; }
        public bool UseFullRawExtractionMode { get; set; }
    }
}

public sealed class SegmentRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public gMKVSegment Segment { get; }
    public string Kind { get; private set; } = "";
    public string Summary { get; private set; } = "";
    public string Details { get; private set; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SegmentRow(string inputPath, gMKVSegment segment, Func<string, string> getText)
    {
        InputPath = inputPath;
        Segment = segment;
        RefreshLocalization(getText);
    }

    public void RefreshLocalization(Func<string, string> getText)
    {
        (Kind, Summary, Details) = Segment switch
        {
            gMKVTrack track => (
                getText($"track.kind.{GetTrackKind(track.TrackType)}"),
                string.IsNullOrWhiteSpace(track.TrackName)
                    ? MainWindow.FormatTemplate(getText("track.fallbackName"), ("trackNumber", track.TrackNumber))
                    : track.TrackName,
                string.Join(getText("track.detailsSeparator"), new[] { track.CodecID, track.Language, track.ExtraInfo }.Where(value => !string.IsNullOrWhiteSpace(value)))),
            gMKVChapter chapter => (
                getText("track.kind.chapters"),
                getText("track.chapterMarkers"),
                MainWindow.FormatTemplate(getText("track.chapterEntryCount"), ("count", chapter.ChapterCount))),
            gMKVAttachment attachment => (
                getText("track.kind.attachment"),
                attachment.Filename,
                MainWindow.FormatTemplate(getText("track.attachmentDetails"), ("mimeType", attachment.MimeType), ("fileSize", attachment.FileSize))),
            _ => (getText("track.kind.other"), Segment.ToString() ?? "", "")
        };

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Kind)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details)));
    }

    private static string GetTrackKind(MkvTrackType trackType) => trackType switch
    {
        MkvTrackType.video => "video",
        MkvTrackType.audio => "audio",
        MkvTrackType.subtitles => "subtitles",
        _ => "other"
    };

    public string InputPath { get; }
}

public sealed class InputFileGroup : INotifyPropertyChanged
{
    private bool _isExpanded;
    private readonly Func<string, string> _getText;

    public string FileName { get; }
    public string ItemCount => MainWindow.FormatTemplate(_getText("tracks.itemCount"), ("count", SegmentRows.Count));
    public ObservableCollection<SegmentRow> SegmentRows { get; }
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public InputFileGroup(string inputPath, IEnumerable<SegmentRow> segmentRows, Func<string, string> getText)
    {
        FileName = Path.GetFileName(inputPath);
        SegmentRows = new ObservableCollection<SegmentRow>(segmentRows);
        _getText = getText;
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ItemCount)));
}