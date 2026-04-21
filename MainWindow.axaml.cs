using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using BymlLibrary;
using MapIslandEditor.Models;
using MapIslandEditor.Services;
using Revrs;

namespace MapIslandEditor;

public partial class MainWindow : Window
{
    private const double HubWidth = 920;
    private const double HubHeight = 700;
    private const double EditorWidth = 1450;
    private const double EditorHeight = 900;
    private const double EditorMinWidth = 1150;
    private const double EditorMinHeight = 720;
    private const string ModeButtonInactiveBg = "#FFFFB30F";
    private const string ModeButtonActiveBg = "#FFFFD36A";
    private const string ModeButtonForeground = "#FF1A1A1A";
    private const uint MapSavGridHash = 2028154396u;
    private const uint MapSavGridCount = 9600u;
    private const uint MapSavObjectHashHash = 1917672272u;
    private const uint MapSavObjectXHash = 3014353243u;
    private const uint MapSavObjectYHash = 2541899808u;
    private const uint MapSavObjectRotHash = 3042461324u;
    private const uint MapSavObjectCount = 2150u;
    private const string GitHubOwner = "cakezara";
    private const string GitHubRepo = "ltd-islandeditor";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/cakezara/ltd-islandeditor/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/cakezara/ltd-islandeditor/releases";
    private const string GameBananaUrl = "https://gamebanana.com/tools/22455";

    private readonly Grid _editorRoot;
    private readonly Border _hubPanel;
    private readonly Button _hubBrowseButton;
    private readonly Button _hubBrowseMapSavButton;
    private readonly Button _githubLinkButton;
    private readonly Button _gameBananaLinkButton;
    private readonly Button _hubGameBananaPromptCloseButton;
    private readonly TextBlock _hubFooterText;
    private readonly TextBlock _hubLatestVersionText;
    private readonly TextBlock _hubGameBananaLikeText;
    private readonly TextBlock _bottomGameBananaLikeText;
    private readonly Border _hubGameBananaPrompt;
    private readonly Button _saveMapButton;
    private readonly Button _saveAsMapButton;
    private readonly Button _exitMapSavButton;
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly Button _deleteObjectButton;
    private readonly Button _moveObjectsButton;
    private readonly Button _addLandButton;
    private readonly Button _deleteLandButton;
    private readonly Button _load3DButton;
    private readonly Button _applyMapSavValueButton;
    private readonly Button _panLeft2DButton;
    private readonly Button _panRight2DButton;
    private readonly Button _panUp2DButton;
    private readonly Button _panDown2DButton;
    private readonly Button _zoomIn2DButton;
    private readonly Button _zoomOut2DButton;
    private readonly Button _resetView2DButton;
    private readonly ComboBox _paintTileTypeCombo;
    private readonly TextBox _rootPathText;
    private readonly TextBlock _mapInfoText;
    private readonly TextBlock _viewerInfoText;
    private readonly TextBlock _threeDStatusText;
    private readonly TextBlock _statusText;
    private readonly TextBlock _mapSavStatusText;
    private readonly TextBlock _mapSavSelectedPathText;
    private readonly TextBlock _mapSavSelectedTypeText;
    private readonly TextBlock _objectCatalogCountText;
    private readonly StackPanel _floorLegendPanel;
    private readonly ListBox _objectList;
    private readonly ListBox _objectCatalogList;
    private readonly ListBox _mapSavEntryList;
    private readonly TextBox _objectSearchText;
    private readonly TextBox _mapSavPathText;
    private readonly TextBox _mapSavValueText;
    private readonly TabItem _mapSavTab;
    private readonly TabControl _viewerTabs;
    private readonly MapCanvasControl _mapCanvas;
    private readonly Scene3DControl _scene3D;

    private string _gridFilePath = string.Empty;
    private string _objectFilePath = string.Empty;
    private MapRepository? _mapRepository;
    private Dictionary<uint, List<string>> _objectNameLookup = [];
    private Dictionary<uint, List<string>> _floorNameLookup = [];
    private Dictionary<uint, Color> _tileColorByHash = [];
    private MapProject? _currentMap;
    private string _gameRootPath = string.Empty;
    private uint _landHash;
    private uint _paintLandHash;
    private uint _waterHash;
    private readonly BfresReader _bfresReader = new();
    private readonly Stack<MapEditSnapshot> _undoStack = [];
    private readonly Stack<MapEditSnapshot> _redoStack = [];
    private List<MapObjectEntry> _objectClipboard = [];
    private bool _suppressHistory;
    private bool _tileStrokeUndoCaptured;
    private readonly string _versionNumber;
    private string _mapSavFilePath = string.Empty;
    private Endianness _mapSavEndianness;
    private ushort _mapSavVersion;
    private Byml? _mapSavDocument;
    private bool _mapSavHasExplicitRoot;
    private readonly List<MapSavEditableEntry> _mapSavEntries = [];
    private bool _mapSavIsRaw;
    private byte[]? _mapSavRawBytes;
    private readonly List<MapSavRawField> _mapSavRawFields = [];
    private int _mapSavRawGridStart = -1;
    private int _mapSavRawObjectHashStart = -1;
    private int _mapSavRawObjectNameHashStart = -1;
    private int _mapSavRawObjectXStart = -1;
    private int _mapSavRawObjectYStart = -1;
    private int _mapSavRawObjectRotStart = -1;
    private readonly HashSet<int> _mapSavRawLoadedObjectSlots = [];
    private bool _isUpdatingPaintTileTypeCombo;
    private bool _mapSavModeActive;
    private bool _mapSavDirty;
    private bool _mapSavSavedThisSession;
    private bool _allowWindowCloseWithoutPrompt;
    private bool _isHandlingWindowClosePrompt;
    private readonly HttpClient _httpClient = new();
    private string? _latestReleaseTag;
    private string? _latestReleaseAssetUrl;
    private string? _latestReleaseAssetName;
    private readonly Button _addObjectFromCatalogButton;
    private List<ObjectCatalogOption> _allObjectCatalogOptions = [];
    private bool _hasClickedGameBananaLikeLink;

    private sealed class MapEditSnapshot
    {
        public required uint[,] Grid { get; init; }
        public required List<MapObjectEntry> Objects { get; init; }
    }

    private sealed class MapSavEditableEntry
    {
        public required string DisplayPath { get; init; }
        public required string TypeName { get; init; }
        public required List<object> Tokens { get; init; }
    }

    private sealed class MapSavRawField
    {
        public required string DisplayPath { get; init; }
        public required string TypeName { get; init; }
        public required int ByteOffset { get; init; }
    }

    private sealed class PaintTileOption
    {
        public required uint Hash { get; init; }
        public required string Name { get; init; }
        public required int Count { get; init; }

        public override string ToString()
        {
            return $"{Name} ({Hash}) x{Count}";
        }
    }

    private sealed class ObjectCatalogOption
    {
        public required uint Hash { get; init; }
        public required string PrimaryName { get; init; }
        public required string SearchText { get; init; }

        public override string ToString()
        {
            return $"{PrimaryName} (0x{Hash:X8})";
        }
    }

    private sealed class UserPreferences
    {
        public bool HasClickedGameBananaLikeLink { get; set; }
    }

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyWindowIconFromIco();

        _editorRoot = this.FindControl<Grid>("EditorRoot")!;
        _hubPanel = this.FindControl<Border>("HubPanel")!;
        _hubBrowseButton = this.FindControl<Button>("HubBrowseButton")!;
        _hubBrowseMapSavButton = this.FindControl<Button>("HubBrowseMapSavButton")!;
        _githubLinkButton = this.FindControl<Button>("GithubLinkButton")!;
        _gameBananaLinkButton = this.FindControl<Button>("GameBananaLinkButton")!;
        _hubGameBananaPromptCloseButton = this.FindControl<Button>("HubGameBananaPromptCloseButton")!;
        _hubFooterText = this.FindControl<TextBlock>("HubFooterText")!;
        _hubLatestVersionText = this.FindControl<TextBlock>("HubLatestVersionText")!;
        _hubGameBananaLikeText = this.FindControl<TextBlock>("HubGameBananaLikeText")!;
        _bottomGameBananaLikeText = this.FindControl<TextBlock>("BottomGameBananaLikeText")!;
        _hubGameBananaPrompt = this.FindControl<Border>("HubGameBananaPrompt")!;
        _saveMapButton = this.FindControl<Button>("SaveMapButton")!;
        _saveAsMapButton = this.FindControl<Button>("SaveAsMapButton")!;
        _exitMapSavButton = this.FindControl<Button>("ExitMapSavButton")!;
        _undoButton = this.FindControl<Button>("UndoButton")!;
        _redoButton = this.FindControl<Button>("RedoButton")!;
        _deleteObjectButton = this.FindControl<Button>("DeleteObjectButton")!;
        _moveObjectsButton = this.FindControl<Button>("MoveObjectsButton")!;
        _addLandButton = this.FindControl<Button>("AddLandButton")!;
        _deleteLandButton = this.FindControl<Button>("DeleteLandButton")!;
        _load3DButton = this.FindControl<Button>("Load3DButton")!;
        _applyMapSavValueButton = this.FindControl<Button>("ApplyMapSavValueButton")!;
        _panLeft2DButton = this.FindControl<Button>("PanLeft2DButton")!;
        _panRight2DButton = this.FindControl<Button>("PanRight2DButton")!;
        _panUp2DButton = this.FindControl<Button>("PanUp2DButton")!;
        _panDown2DButton = this.FindControl<Button>("PanDown2DButton")!;
        _zoomIn2DButton = this.FindControl<Button>("ZoomIn2DButton")!;
        _zoomOut2DButton = this.FindControl<Button>("ZoomOut2DButton")!;
        _resetView2DButton = this.FindControl<Button>("ResetView2DButton")!;
        _paintTileTypeCombo = this.FindControl<ComboBox>("PaintTileTypeCombo")!;
        _rootPathText = this.FindControl<TextBox>("RootPathText")!;
        _mapInfoText = this.FindControl<TextBlock>("MapInfoText")!;
        _viewerInfoText = this.FindControl<TextBlock>("ViewerInfoText")!;
        _threeDStatusText = this.FindControl<TextBlock>("ThreeDStatusText")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _mapSavStatusText = this.FindControl<TextBlock>("MapSavStatusText")!;
        _mapSavSelectedPathText = this.FindControl<TextBlock>("MapSavSelectedPathText")!;
        _mapSavSelectedTypeText = this.FindControl<TextBlock>("MapSavSelectedTypeText")!;
        _objectCatalogCountText = this.FindControl<TextBlock>("ObjectCatalogCountText")!;
        _floorLegendPanel = this.FindControl<StackPanel>("FloorLegendPanel")!;
        _objectList = this.FindControl<ListBox>("ObjectList")!;
        _objectCatalogList = this.FindControl<ListBox>("ObjectCatalogList")!;
        _mapSavEntryList = this.FindControl<ListBox>("MapSavEntryList")!;
        _objectSearchText = this.FindControl<TextBox>("ObjectSearchText")!;
        _mapSavPathText = this.FindControl<TextBox>("MapSavPathText")!;
        _mapSavValueText = this.FindControl<TextBox>("MapSavValueText")!;
        _mapSavTab = this.FindControl<TabItem>("MapSavTab")!;
        _viewerTabs = this.FindControl<TabControl>("ViewerTabs")!;
        _mapCanvas = this.FindControl<MapCanvasControl>("MapCanvas")!;
        _scene3D = this.FindControl<Scene3DControl>("Scene3D")!;
        _addObjectFromCatalogButton = this.FindControl<Button>("AddObjectFromCatalogButton")!;

        _rootPathText.Text = "No map files selected.";
        _threeDStatusText.Text = "Open a map, then load 3D models.";
        _statusText.Text = "Ready.";
        _mapSavStatusText.Text = "Load a Map.sav to edit flags.";
        _applyMapSavValueButton.IsEnabled = false;
        _paintTileTypeCombo.IsEnabled = false;
        _addObjectFromCatalogButton.IsEnabled = false;
        _versionNumber = ReadVersionNumber();
        LoadUserPreferences();
        _hubFooterText.Text = $"cakezara - {_versionNumber}";
        _hubLatestVersionText.IsVisible = false;
        Title = $"Tomodachi Island Editor - {_versionNumber}";

        _hubBrowseButton.Click += BrowseRootButtonOnClick;
        _hubBrowseMapSavButton.Click += BrowseMapSavButtonOnClick;
        _githubLinkButton.Click += (_, _) => OpenExternalUrl("https://github.com/cakezara/ltd-islandeditor", "GitHub");
        _gameBananaLinkButton.Click += (_, _) => OpenGameBananaLikeLink();
        _hubGameBananaPromptCloseButton.Click += HubGameBananaPromptCloseButtonOnClick;
        _hubGameBananaLikeText.PointerPressed += GameBananaLikeTextOnPointerPressed;
        _bottomGameBananaLikeText.PointerPressed += GameBananaLikeTextOnPointerPressed;
        _hubLatestVersionText.PointerPressed += HubLatestVersionTextOnPointerPressed;
        _saveMapButton.Click += SaveMapButtonOnClick;
        _saveAsMapButton.Click += SaveAsMapButtonOnClick;
        _undoButton.Click += UndoButtonOnClick;
        _redoButton.Click += RedoButtonOnClick;
        _deleteObjectButton.Click += DeleteObjectButtonOnClick;
        _moveObjectsButton.Click += MoveObjectsButtonOnClick;
        _addLandButton.Click += AddLandButtonOnClick;
        _deleteLandButton.Click += DeleteLandButtonOnClick;
        _load3DButton.Click += Load3DButtonOnClick;
        _exitMapSavButton.Click += ExitMapSavButtonOnClick;
        _applyMapSavValueButton.Click += ApplyMapSavValueButtonOnClick;
        _panLeft2DButton.Click += (_, _) => _mapCanvas.PanBy(40, 0);
        _panRight2DButton.Click += (_, _) => _mapCanvas.PanBy(-40, 0);
        _panUp2DButton.Click += (_, _) => _mapCanvas.PanBy(0, 40);
        _panDown2DButton.Click += (_, _) => _mapCanvas.PanBy(0, -40);
        _zoomIn2DButton.Click += (_, _) => _mapCanvas.ZoomBy(1.15);
        _zoomOut2DButton.Click += (_, _) => _mapCanvas.ZoomBy(0.87);
        _resetView2DButton.Click += (_, _) => _mapCanvas.ResetView();
        _paintTileTypeCombo.SelectionChanged += PaintTileTypeComboOnSelectionChanged;
        _objectList.SelectionChanged += ObjectListOnSelectionChanged;
        _objectList.PointerPressed += ObjectListOnPointerPressed;
        _mapSavEntryList.SelectionChanged += MapSavEntryListOnSelectionChanged;
        _objectSearchText.TextChanged += ObjectSearchTextOnTextChanged;
        _objectCatalogList.SelectionChanged += ObjectCatalogListOnSelectionChanged;
        _addObjectFromCatalogButton.Click += AddObjectFromCatalogButtonOnClick;
        _mapCanvas.ObjectMoved += MapCanvasOnObjectMoved;
        _mapCanvas.ObjectSelected += MapCanvasOnObjectSelected;
        _mapCanvas.TilePainted += MapCanvasOnTilePainted;
        _mapCanvas.TilePaintStrokeStarted += MapCanvasOnTilePaintStrokeStarted;
        _mapCanvas.TilePaintStrokeCompleted += MapCanvasOnTilePaintStrokeCompleted;
        BuildObjectContextMenus();
        KeyDown += MainWindowOnKeyDown;
        Closing += MainWindowOnClosing;
        UpdateUndoRedoButtons();
        UpdateEditModeButtons();
        UpdateMapSavToolbarState();
        ShowHub(true);
        TryAutoLoadMapSav();
        _ = CheckForUpdatesOnBootAsync();
        ApplyObjectCatalogFilter();
    }

    private void HubGameBananaPromptCloseButtonOnClick(object? sender, RoutedEventArgs e)
    {
        _hubGameBananaPrompt.IsVisible = false;
    }

    private void GameBananaLikeTextOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        OpenGameBananaLikeLink();
    }

    private void OpenGameBananaLikeLink()
    {
        if (!OpenExternalUrl(GameBananaUrl, "GameBanana"))
        {
            return;
        }

        if (_hasClickedGameBananaLikeLink)
        {
            return;
        }

        _hasClickedGameBananaLikeLink = true;
        _hubGameBananaPrompt.IsVisible = false;
        SaveUserPreferences();
    }

    private void ApplyWindowIconFromIco()
    {
        var iconUri = new Uri("avares://MapIslandEditor/IE.ico");
        if (!AssetLoader.Exists(iconUri))
        {
            return;
        }

        using var iconStream = AssetLoader.Open(iconUri);
        Icon = new WindowIcon(iconStream);
    }

    private static string ReadVersionNumber()
    {
        try
        {
            var versionPath = Path.Combine(AppContext.BaseDirectory, "Version.txt");
            if (!File.Exists(versionPath))
            {
                return "dev";
            }

            var version = File.ReadAllText(versionPath).Trim();
            return string.IsNullOrWhiteSpace(version) ? "dev" : version;
        }
        catch
        {
            return "dev";
        }
    }

    private static string GetUserPreferencesFilePath()
    {
        var preferencesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapIslandEditor");
        return Path.Combine(preferencesDir, "preferences.json");
    }

    private void LoadUserPreferences()
    {
        try
        {
            var path = GetUserPreferencesFilePath();
            if (!File.Exists(path))
            {
                _hasClickedGameBananaLikeLink = false;
                return;
            }

            var json = File.ReadAllText(path);
            var preferences = JsonSerializer.Deserialize<UserPreferences>(json);
            _hasClickedGameBananaLikeLink = preferences?.HasClickedGameBananaLikeLink ?? false;
        }
        catch
        {
            _hasClickedGameBananaLikeLink = false;
        }
    }

    private void SaveUserPreferences()
    {
        try
        {
            var path = GetUserPreferencesFilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(new UserPreferences
            {
                HasClickedGameBananaLikeLink = _hasClickedGameBananaLikeLink
            });
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    private async void HubLatestVersionTextOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        await StartUpdateFromHubAsync();
    }

    private async Task CheckForUpdatesOnBootAsync()
    {
        try
        {
            var updateAvailable = await CheckForLatestReleaseAsync();
            if (!updateAvailable)
            {
                return;
            }

            if (!await AskYesNoAsync("Update Available", $"A new version ({_latestReleaseTag}) is available.\nUpdate now?"))
            {
                return;
            }

            await StartUpdateAsync();
        }
        catch
        {
        }
    }

    private async Task StartUpdateFromHubAsync()
    {
        if (string.IsNullOrWhiteSpace(_latestReleaseTag))
        {
            var updateAvailable = await CheckForLatestReleaseAsync();
            if (!updateAvailable)
            {
                return;
            }
        }

        await StartUpdateAsync();
    }

    private async Task<bool> CheckForLatestReleaseAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            request.Headers.UserAgent.ParseAdd("TomodachiIslandEditor");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagNode))
            {
                return false;
            }

            var latestTag = tagNode.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(latestTag))
            {
                return false;
            }

            _latestReleaseTag = latestTag;
            _latestReleaseAssetUrl = null;
            _latestReleaseAssetName = null;

            if (root.TryGetProperty("assets", out var assetsNode) && assetsNode.ValueKind == JsonValueKind.Array)
            {
                var bestScore = int.MinValue;
                foreach (var asset in assetsNode.EnumerateArray())
                {
                    if (!asset.TryGetProperty("browser_download_url", out var urlNode) ||
                        !asset.TryGetProperty("name", out var nameNode))
                    {
                        continue;
                    }

                    var url = urlNode.GetString();
                    var name = nameNode.GetString();
                    if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var score = ScoreReleaseAsset(name);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        _latestReleaseAssetUrl = url;
                        _latestReleaseAssetName = name;
                    }
                }
            }

            var hasUpdate = IsRemoteVersionNewer(_versionNumber, latestTag);
            UpdateHubLatestVersionUi(hasUpdate, latestTag);
            return hasUpdate;
        }
        catch
        {
            return false;
        }
    }

    private static int ScoreReleaseAsset(string name)
    {
        var lower = name.ToLowerInvariant();
        var score = 0;
        if (lower.EndsWith(".zip", StringComparison.Ordinal))
        {
            score += 80;
        }
        if (lower.EndsWith(".exe", StringComparison.Ordinal))
        {
            score += 60;
        }
        if (lower.EndsWith(".msi", StringComparison.Ordinal))
        {
            score += 50;
        }
        if (lower.Contains("win", StringComparison.Ordinal))
        {
            score += 25;
        }
        if (lower.Contains("x64", StringComparison.Ordinal))
        {
            score += 20;
        }
        if (lower.Contains("portable", StringComparison.Ordinal))
        {
            score += 10;
        }
        return score;
    }

    private void UpdateHubLatestVersionUi(bool hasUpdate, string latestTag)
    {
        if (!hasUpdate)
        {
            _hubLatestVersionText.IsVisible = false;
            _hubLatestVersionText.Text = string.Empty;
            return;
        }

        _hubLatestVersionText.Text = $"Latest: {latestTag} (click to update)";
        _hubLatestVersionText.IsVisible = true;
    }

    private static bool IsRemoteVersionNewer(string currentVersion, string remoteTag)
    {
        var current = ParseVersion(currentVersion);
        var remote = ParseVersion(remoteTag);
        if (current is null || remote is null)
        {
            return !string.Equals(NormalizeTag(currentVersion), NormalizeTag(remoteTag), StringComparison.OrdinalIgnoreCase);
        }

        return remote > current;
    }

    private static Version? ParseVersion(string value)
    {
        var normalized = NormalizeTag(value);
        var match = Regex.Match(normalized, @"^\d+(\.\d+){0,3}");
        if (!match.Success)
        {
            return null;
        }

        return Version.TryParse(match.Value, out var parsed) ? parsed : null;
    }

    private static string NormalizeTag(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? trimmed[1..] : trimmed;
    }

    private async Task StartUpdateAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            OpenExternalUrl(ReleasesPageUrl, "Releases");
            return;
        }

        if (string.IsNullOrWhiteSpace(_latestReleaseTag))
        {
            if (!await CheckForLatestReleaseAsync())
            {
                await ShowInfoDialogAsync("Updater", "No update information was found.");
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(_latestReleaseAssetUrl) || string.IsNullOrWhiteSpace(_latestReleaseAssetName))
        {
            OpenExternalUrl(ReleasesPageUrl, "Releases");
            return;
        }

        var tag = _latestReleaseTag!;
        var assetUrl = _latestReleaseAssetUrl!;
        var assetName = _latestReleaseAssetName!;
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentPid = Environment.ProcessId;
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"ltd-islandeditor-update-{Guid.NewGuid():N}.cmd");
        var script = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("setlocal EnableExtensions")
            .AppendLine($"set \"pidToWait={currentPid}\"")
            .AppendLine($"set \"downloadUrl={EscapeForCmdLiteral(assetUrl)}\"")
            .AppendLine($"set \"downloadName={EscapeForCmdLiteral(assetName)}\"")
            .AppendLine($"set \"installDir={EscapeForCmdLiteral(installDir)}\"")
            .AppendLine($"set \"tag={EscapeForCmdLiteral(tag)}\"")
            .AppendLine("set \"tmp=%TEMP%\\ltd-islandeditor-update-%RANDOM%%RANDOM%\"")
            .AppendLine("mkdir \"%tmp%\" >nul 2>nul")
            .AppendLine(":waitloop")
            .AppendLine("tasklist /FI \"PID eq %pidToWait%\" 2>nul | find \"%pidToWait%\" >nul")
            .AppendLine("if not errorlevel 1 (")
            .AppendLine("  timeout /t 1 /nobreak >nul")
            .AppendLine("  goto waitloop")
            .AppendLine(")")
            .AppendLine("set \"downloadPath=%tmp%\\%downloadName%\"")
            .AppendLine("curl.exe -L -o \"%downloadPath%\" \"%downloadUrl%\"")
            .AppendLine("if errorlevel 1 goto failed")
            .AppendLine("set \"ext=%downloadPath:~-4%\"")
            .AppendLine("if /I \"%ext%\"==\".zip\" goto zip")
            .AppendLine("if /I \"%ext%\"==\".exe\" goto installer")
            .AppendLine("if /I \"%ext%\"==\".msi\" goto installer")
            .AppendLine($"start \"\" \"{EscapeForCmdLiteral(ReleasesPageUrl)}\"")
            .AppendLine("goto done")
            .AppendLine(":zip")
            .AppendLine("set \"extractPath=%tmp%\\extract\"")
            .AppendLine("mkdir \"%extractPath%\" >nul 2>nul")
            .AppendLine("tar.exe -xf \"%downloadPath%\" -C \"%extractPath%\"")
            .AppendLine("if errorlevel 1 goto failed")
            .AppendLine("xcopy \"%extractPath%\\*\" \"%installDir%\\\" /E /I /Y >nul")
            .AppendLine("if errorlevel 1 goto failed")
            .AppendLine("> \"%installDir%\\Version.txt\" echo %tag%")
            .AppendLine("if exist \"%installDir%\\MapIslandEditor.exe\" start \"\" \"%installDir%\\MapIslandEditor.exe\"")
            .AppendLine("goto done")
            .AppendLine(":installer")
            .AppendLine("> \"%installDir%\\Version.txt\" echo %tag%")
            .AppendLine("start \"\" \"%downloadPath%\"")
            .AppendLine("goto done")
            .AppendLine(":failed")
            .AppendLine("echo Update failed. Opening releases page...")
            .AppendLine($"start \"\" \"{EscapeForCmdLiteral(ReleasesPageUrl)}\"")
            .AppendLine(":done")
            .AppendLine("echo Update script complete.")
            .ToString();

        File.WriteAllText(tempScriptPath, script, new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/k \"\"{tempScriptPath}\"\"",
            WorkingDirectory = installDir,
            UseShellExecute = true
        });

        Close();
    }

    private static string EscapeForCmdLiteral(string value)
    {
        return value.Replace("%", "%%", StringComparison.Ordinal)
            .Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    private async Task<bool> AskYesNoAsync(string title, string message)
    {
        if (OperatingSystem.IsWindows())
        {
            var result = MessageBoxW(IntPtr.Zero, message, title, MbYesNo | MbIconQuestion);
            return result == IdYes;
        }

        var decision = false;
        var yesButton = new Button { Content = "Yes", MinWidth = 96, Padding = new Thickness(16, 6) };
        var noButton = new Button { Content = "No", MinWidth = 96, Padding = new Thickness(16, 6) };
        var dialog = CreateStandardDialog(title, 520, 180);
        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { yesButton, noButton }
                }
            }
        };

        yesButton.Click += (_, _) =>
        {
            decision = true;
            dialog.Close();
        };
        noButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return decision;
    }

    private async void BrowseRootButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (_mapSavModeActive && !await EnsureMapSavExitApprovedAsync())
        {
            return;
        }

        _mapSavModeActive = false;
        UpdateMapSavToolbarState();

        var top = GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            return;
        }

        var bymlFilter = new FilePickerFileType("BYML files")
        {
            Patterns = ["*.byml", "*.bgyml"]
        };
        var allFilter = new FilePickerFileType("All files")
        {
            Patterns = ["*"]
        };

        var gridFiles = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select MapGrid BYML file",
            AllowMultiple = false,
            FileTypeFilter = [bymlFilter, allFilter]
        });

        var gridFile = gridFiles.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(gridFile))
        {
            return;
        }

        var objectFiles = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select MapObject BYML file",
            AllowMultiple = false,
            FileTypeFilter = [bymlFilter, allFilter]
        });

        var objectFile = objectFiles.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(objectFile))
        {
            return;
        }

        _gridFilePath = gridFile;
        _objectFilePath = objectFile;
        _rootPathText.Text = $"{Path.GetFileName(_gridFilePath)} | {Path.GetFileName(_objectFilePath)}";
        LoadSelectedMapFiles();
    }

    private void LoadSelectedMapFiles()
    {
        if (!File.Exists(_gridFilePath) || !File.Exists(_objectFilePath))
        {
            _statusText.Text = "Pick both MapGrid and MapObject files.";
            return;
        }

        try
        {
            _gameRootPath = GuessGameRootPath(_gridFilePath, _objectFilePath);
            _mapRepository = new MapRepository(_gameRootPath);
            TryAutoLoadMapSavFromRoot(_gameRootPath);
            _objectNameLookup = _mapRepository.LoadObjectNameLookup();
            _floorNameLookup = _mapRepository.LoadFloorNameLookup();
            RebuildObjectCatalog();
            var map = _mapRepository.LoadMap(_gridFilePath, _objectFilePath);
            if (map is null)
            {
                _statusText.Text = "Could not load the selected map files.";
                return;
            }

            SelectMap(map);
            _mapSavModeActive = false;
            UpdateMapSavToolbarState();
            ShowHub(false);
            _statusText.Text = "Loaded selected map files.";
        }
        catch (Exception ex)
        {
            _statusText.Text = ex.Message;
        }
    }

    private async void BrowseMapSavButtonOnClick(object? sender, RoutedEventArgs e)
    {
        var top = GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            return;
        }

        var savFilter = new FilePickerFileType("Save files")
        {
            Patterns = ["*.sav"]
        };
        var allFilter = new FilePickerFileType("All files")
        {
            Patterns = ["*"]
        };

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Map.sav file",
            AllowMultiple = false,
            FileTypeFilter = [savFilter, allFilter]
        });

        var file = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        _mapSavModeActive = true;
        var loaded = LoadMapSav(file);
        if (!loaded)
        {
            _mapSavModeActive = false;
            return;
        }

        UpdateMapSavToolbarState();
        ShowHub(false);
        _viewerTabs.SelectedIndex = 0;
        _viewerInfoText.Text = Path.GetFileName(file);
        _statusText.Text = $"Loaded Map.sav: {Path.GetFileName(file)}";
    }

    private async void ExitMapSavButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureMapSavExitApprovedAsync())
        {
            return;
        }

        _mapSavModeActive = false;
        UpdateMapSavToolbarState();
        ShowHub(true);
    }

    private void ApplyMapSavValueButtonOnClick(object? sender, RoutedEventArgs e)
    {
        ApplySelectedMapSavValue();
    }

    private void MapSavEntryListOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = _mapSavEntryList.SelectedIndex;
        if (_mapSavIsRaw)
        {
            if (index < 0 || index >= _mapSavRawFields.Count || _mapSavRawBytes is null)
            {
                _mapSavSelectedPathText.Text = "-";
                _mapSavSelectedTypeText.Text = "Type: -";
                _mapSavValueText.Text = string.Empty;
                _applyMapSavValueButton.IsEnabled = false;
                return;
            }

            var field = _mapSavRawFields[index];
            _mapSavSelectedPathText.Text = field.DisplayPath;
            _mapSavSelectedTypeText.Text = $"Type: {field.TypeName}";
            _mapSavValueText.Text = ReadRawMapSavValue(field);
            _applyMapSavValueButton.IsEnabled = true;
            return;
        }

        if (index < 0 || index >= _mapSavEntries.Count || _mapSavDocument is null)
        {
            _mapSavSelectedPathText.Text = "-";
            _mapSavSelectedTypeText.Text = "Type: -";
            _mapSavValueText.Text = string.Empty;
            _applyMapSavValueButton.IsEnabled = false;
            return;
        }

        var entry = _mapSavEntries[index];
        _mapSavSelectedPathText.Text = entry.DisplayPath;
        _mapSavSelectedTypeText.Text = $"Type: {entry.TypeName}";
        var root = GetMapSavRootNode(_mapSavDocument, _mapSavHasExplicitRoot);
        if (TryGetBymlNodeAtPath(root, entry.Tokens, out var node))
        {
            _mapSavValueText.Text = GetBymlValueDisplay(node);
        }
        _applyMapSavValueButton.IsEnabled = true;
    }

    private void TryAutoLoadMapSav()
    {
        var path = Path.Combine(Environment.CurrentDirectory, "Map.sav");
        if (File.Exists(path))
        {
            _ = LoadMapSav(path);
        }
    }

    private void TryAutoLoadMapSavFromRoot(string rootPath)
    {
        var path = Path.Combine(rootPath, "Map.sav");
        if (!File.Exists(path))
        {
            return;
        }

        if (string.Equals(_mapSavFilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = LoadMapSav(path);
    }

    private bool LoadMapSav(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            _mapSavIsRaw = false;
            _mapSavRawBytes = null;
            _mapSavRawFields.Clear();
            _mapSavRawGridStart = -1;
            _mapSavRawObjectHashStart = -1;
            _mapSavRawObjectNameHashStart = -1;
            _mapSavRawObjectXStart = -1;
            _mapSavRawObjectYStart = -1;
            _mapSavRawObjectRotStart = -1;
            _mapSavRawLoadedObjectSlots.Clear();
            try
            {
                var byml = Byml.FromBinary(bytes, out var endianness, out var version);
                if (!TryGetRootNode(byml, out var rootNode, out var hasExplicitRoot))
                {
                    _mapSavStatusText.Text = "Map.sav could not be parsed as BYML.";
                    return false;
                }

                _mapSavFilePath = filePath;
                _mapSavPathText.Text = filePath;
                _mapSavDocument = byml;
                _mapSavEndianness = endianness;
                _mapSavVersion = version;
                _mapSavHasExplicitRoot = hasExplicitRoot;
                RebuildMapSavEntryList(rootNode);
                _mapSavStatusText.Text = $"Loaded Map.sav BYML ({_mapSavEntries.Count} editable values).";
                _mapSavDirty = false;
                _mapSavSavedThisSession = false;
                return true;
            }
            catch
            {
                if (TryLoadRawMapSav(filePath, bytes))
                {
                    _mapSavDirty = false;
                    _mapSavSavedThisSession = false;
                    return true;
                }
                _mapSavStatusText.Text = "Map.sav format is not supported yet.";
                return false;
            }
        }
        catch (Exception ex)
        {
            _mapSavStatusText.Text = ex.Message;
            return false;
        }
    }

    private async Task<bool> SaveMapSavAsync()
    {
        if ((_mapSavDocument is null && !_mapSavIsRaw) || string.IsNullOrWhiteSpace(_mapSavFilePath))
        {
            _mapSavStatusText.Text = "Load a Map.sav first.";
            return false;
        }

        try
        {
            if (_mapSavIsRaw)
            {
                if (_mapSavRawBytes is null)
                {
                    _mapSavStatusText.Text = "Raw Map.sav buffer is missing.";
                    return false;
                }

                if (_currentMap is not null &&
                    string.Equals(_currentMap.MapKey, "MapSavRaw", StringComparison.Ordinal))
                {
                    SyncRawMapSavFromCurrentMap(_currentMap);
                }

                File.WriteAllBytes(_mapSavFilePath, _mapSavRawBytes);
                if (_currentMap is not null &&
                    string.Equals(_currentMap.MapKey, "MapSavRaw", StringComparison.Ordinal))
                {
                    TryRefreshMapSavRawPreviewMap();
                }
            }
            else
            {
                var bytes = _mapSavDocument!.ToBinary(_mapSavEndianness, _mapSavVersion);
                File.WriteAllBytes(_mapSavFilePath, bytes);
            }
            _mapSavStatusText.Text = $"Saved Map.sav: {_mapSavFilePath}";
            await ShowMapSavSavedDialogAsync(_mapSavFilePath);
            _mapSavDirty = false;
            _mapSavSavedThisSession = true;
            return true;
        }
        catch (Exception ex)
        {
            _mapSavStatusText.Text = ex.Message;
            return false;
        }
    }

    private void SyncRawMapSavFromCurrentMap(MapProject map)
    {
        if (_mapSavRawBytes is null || _mapSavRawGridStart < 0)
        {
            return;
        }

        var width = map.Width;
        var height = map.Height;
        if (width <= 0 || height <= 0 || width * height != (int)MapSavGridCount)
        {
            return;
        }

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var i = (x * height) + y;
                var offset = _mapSavRawGridStart + (i * 4);
                if (offset < 0 || offset + 4 > _mapSavRawBytes.Length)
                {
                    return;
                }

                BinaryPrimitives.WriteUInt32LittleEndian(_mapSavRawBytes.AsSpan(offset, 4), map.Grid[x, y]);
            }
        }

        if (_mapSavRawObjectHashStart < 0 || _mapSavRawObjectXStart < 0 || _mapSavRawObjectYStart < 0)
        {
            return;
        }

        var slotsToClear = _mapSavRawLoadedObjectSlots.Count > 0
            ? _mapSavRawLoadedObjectSlots
            : Enumerable.Range(0, (int)MapSavObjectCount).ToHashSet();
        foreach (var i in slotsToClear)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectHashStart + (i * 4), 4), 0u);
            BinaryPrimitives.WriteInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectXStart + (i * 4), 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectYStart + (i * 4), 4), 0);
            if (_mapSavRawObjectRotStart >= 0)
            {
                BinaryPrimitives.WriteInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectRotStart + (i * 4), 4), 0);
            }
        }

        foreach (var obj in map.Objects)
        {
            var slot = TryParseMapSavObjectIndex(obj.Id);
            if (slot < 0 || slot >= (int)MapSavObjectCount)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectHashStart + (slot * 4), 4), obj.Hash);
            BinaryPrimitives.WriteInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectXStart + (slot * 4), 4), obj.GridPosX);
            BinaryPrimitives.WriteInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectYStart + (slot * 4), 4), obj.GridPosY);
            if (_mapSavRawObjectRotStart >= 0)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    _mapSavRawBytes.AsSpan(_mapSavRawObjectRotStart + (slot * 4), 4),
                    BitConverter.SingleToInt32Bits(obj.RotY));
            }
        }
    }

    private static int TryParseMapSavObjectIndex(string id)
    {
        if (id.StartsWith("Object_", StringComparison.Ordinal) && int.TryParse(id[7..], out var index))
        {
            return index;
        }

        return -1;
    }

    private async Task<bool> SaveMapSavAsAsync()
    {
        if (_mapSavDocument is null && !_mapSavIsRaw)
        {
            _mapSavStatusText.Text = "Load a Map.sav first.";
            return false;
        }

        var top = GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            return false;
        }

        var savFilter = new FilePickerFileType("Save files")
        {
            Patterns = ["*.sav"]
        };

        var savePath = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Map.sav As",
            SuggestedFileName = string.IsNullOrWhiteSpace(_mapSavFilePath) ? "Map.sav" : Path.GetFileName(_mapSavFilePath),
            FileTypeChoices = [savFilter],
            DefaultExtension = "sav"
        });

        var path = savePath?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        _mapSavFilePath = path;
        _mapSavPathText.Text = path;
        return await SaveMapSavAsync();
    }

    private enum SavePromptDecision
    {
        Save,
        DontSave,
        Cancel
    }

    private async Task<SavePromptDecision> ShowMapSavUnsavedPromptAsync(string title, string message)
    {
        if (OperatingSystem.IsWindows())
        {
            var result = MessageBoxW(IntPtr.Zero, message, title, MbYesNoCancel | MbIconQuestion);
            return result switch
            {
                IdYes => SavePromptDecision.Save,
                IdNo => SavePromptDecision.DontSave,
                _ => SavePromptDecision.Cancel
            };
        }

        SavePromptDecision decision = SavePromptDecision.Cancel;
        var saveButton = new Button { Content = "Save", MinWidth = 96, Padding = new Thickness(16, 6) };
        var dontSaveButton = new Button { Content = "Don't Save", MinWidth = 96, Padding = new Thickness(16, 6) };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 96, Padding = new Thickness(16, 6) };

        var dialog = CreateStandardDialog(title, 520, 180);
        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { saveButton, dontSaveButton, cancelButton }
                }
            }
        };

        saveButton.Click += (_, _) =>
        {
            decision = SavePromptDecision.Save;
            dialog.Close();
        };
        dontSaveButton.Click += (_, _) =>
        {
            decision = SavePromptDecision.DontSave;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            decision = SavePromptDecision.Cancel;
            dialog.Close();
        };

        await dialog.ShowDialog(this);
        return decision;
    }

    private async Task<bool> EnsureMapSavExitApprovedAsync()
    {
        if (!ShouldPromptOnMapSavExit())
        {
            return true;
        }

        var message = _mapSavDirty
            ? "You have unsaved Map.sav changes. Do you want to save before exiting?"
            : "Do you want to save Map.sav before exiting?";
        var decision = await ShowMapSavUnsavedPromptAsync("Map.sav", message);

        switch (decision)
        {
            case SavePromptDecision.Save:
                return await SaveMapSavAsync();
            case SavePromptDecision.DontSave:
                return true;
            default:
                return false;
        }
    }

    private void MainWindowOnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowCloseWithoutPrompt || _isHandlingWindowClosePrompt || !ShouldPromptOnMapSavExit())
        {
            return;
        }

        e.Cancel = true;
        _ = HandleWindowClosingPromptAsync();
    }

    private async Task HandleWindowClosingPromptAsync()
    {
        if (_isHandlingWindowClosePrompt)
        {
            return;
        }

        _isHandlingWindowClosePrompt = true;
        try
        {
            if (await EnsureMapSavExitApprovedAsync())
            {
                _allowWindowCloseWithoutPrompt = true;
                Close();
            }
        }
        finally
        {
            _isHandlingWindowClosePrompt = false;
        }
    }

    private async Task ShowMapSavSavedDialogAsync(string savePath)
    {
        await ShowInfoDialogAsync("Map.sav Save Complete", $"Map.sav saved successfully.\n{savePath}");
    }

    private bool ShouldPromptOnMapSavExit()
    {
        return _mapSavModeActive &&
               !string.IsNullOrWhiteSpace(_mapSavFilePath) &&
               (_mapSavDirty || !_mapSavSavedThisSession);
    }

    private async Task ShowInfoDialogAsync(string title, string message)
    {
        if (OperatingSystem.IsWindows())
        {
            MessageBoxW(IntPtr.Zero, message, title, MbOk | MbIconInformation);
            return;
        }

        var closeButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6),
            MinWidth = 96
        };

        var dialog = CreateStandardDialog(title, 680, 190);
        dialog.Content = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
                closeButton
            }
        };

        closeButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private static Window CreateStandardDialog(string title, double width, double height)
    {
        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
    }

    private const uint MbOk = 0x00000000;
    private const uint MbYesNo = 0x00000004;
    private const uint MbYesNoCancel = 0x00000003;
    private const uint MbIconQuestion = 0x00000020;
    private const uint MbIconInformation = 0x00000040;
    private const int IdYes = 6;
    private const int IdNo = 7;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private void RebuildMapSavEntryList(Byml rootNode)
    {
        _mapSavEntries.Clear();
        CollectMapSavEditableEntries(rootNode, [], _mapSavEntries);
        var items = _mapSavEntries.Select(entry =>
        {
            if (TryGetBymlNodeAtPath(rootNode, entry.Tokens, out var node))
            {
                return $"{entry.DisplayPath} = {GetBymlValueDisplay(node)}";
            }

            return $"{entry.DisplayPath} = ?";
        }).ToList();
        _mapSavEntryList.ItemsSource = items;
        _mapSavSelectedPathText.Text = "-";
        _mapSavSelectedTypeText.Text = "Type: -";
        _mapSavValueText.Text = string.Empty;
        _applyMapSavValueButton.IsEnabled = false;
    }

    private void ApplySelectedMapSavValue()
    {
        if (_mapSavIsRaw)
        {
            if (_mapSavRawBytes is null)
            {
                _mapSavStatusText.Text = "Load a Map.sav first.";
                return;
            }

            var rawIndex = _mapSavEntryList.SelectedIndex;
            if (rawIndex < 0 || rawIndex >= _mapSavRawFields.Count)
            {
                _mapSavStatusText.Text = "Select a Map.sav value first.";
                return;
            }

            var rawField = _mapSavRawFields[rawIndex];
            if (!TryWriteRawMapSavValue(rawField, _mapSavValueText.Text ?? string.Empty))
            {
                _mapSavStatusText.Text = $"Invalid value for type {rawField.TypeName}.";
                return;
            }

            RebuildRawMapSavEntryList();
            _mapSavEntryList.SelectedIndex = rawIndex;
            _mapSavStatusText.Text = $"Updated {rawField.DisplayPath}.";
            _mapSavDirty = true;
            TryRefreshMapSavRawPreviewMap();
            return;
        }

        if (_mapSavDocument is null)
        {
            _mapSavStatusText.Text = "Load a Map.sav first.";
            return;
        }

        var index = _mapSavEntryList.SelectedIndex;
        if (index < 0 || index >= _mapSavEntries.Count)
        {
            _mapSavStatusText.Text = "Select a Map.sav value first.";
            return;
        }

        var entry = _mapSavEntries[index];
        if (!TryParseBymlValue(_mapSavValueText.Text ?? string.Empty, entry.TypeName, out var parsedNode))
        {
            _mapSavStatusText.Text = $"Invalid value for type {entry.TypeName}.";
            return;
        }

        var root = GetMapSavRootNode(_mapSavDocument, _mapSavHasExplicitRoot);
        var updatedRoot = SetBymlNodeAtPath(root, entry.Tokens, 0, parsedNode);
        if (_mapSavHasExplicitRoot)
        {
            var top = _mapSavDocument.GetMap();
            top["root"] = updatedRoot;
            _mapSavDocument = new Byml(top);
        }
        else
        {
            _mapSavDocument = updatedRoot;
        }

        RebuildMapSavEntryList(updatedRoot);
        _mapSavEntryList.SelectedIndex = index;
        _mapSavStatusText.Text = $"Updated {entry.DisplayPath}.";
        _mapSavDirty = true;
    }

    private bool TryLoadRawMapSav(string filePath, byte[] bytes)
    {
        _mapSavRawFields.Clear();
        _mapSavRawBytes = [.. bytes];
        _mapSavDocument = null;
        _mapSavHasExplicitRoot = false;
        _mapSavEntries.Clear();
        _mapSavRawGridStart = -1;
        _mapSavRawObjectHashStart = -1;
        _mapSavRawObjectNameHashStart = -1;
        _mapSavRawObjectXStart = -1;
        _mapSavRawObjectYStart = -1;
        _mapSavRawObjectRotStart = -1;
        _mapSavRawLoadedObjectSlots.Clear();

        if (_mapSavRawBytes is null)
        {
            return false;
        }

        EnsureMapSavLookupsLoaded(filePath);

        var gridStarts = FindHashedArrayDataStarts(_mapSavRawBytes, MapSavGridHash, MapSavGridCount);
        if (gridStarts.Count > 0)
        {
            _mapSavRawGridStart = gridStarts[0];
            AddRawMapSavArrayFields(_mapSavRawGridStart, MapSavGridCount, "MapGrid", "UInt32");
        }

        var objectHashStarts = FindHashedArrayDataStarts(_mapSavRawBytes, MapSavObjectHashHash, MapSavObjectCount);
        var objectXStarts = FindHashedArrayDataStarts(_mapSavRawBytes, MapSavObjectXHash, MapSavObjectCount);
        var objectYStarts = FindHashedArrayDataStarts(_mapSavRawBytes, MapSavObjectYHash, MapSavObjectCount);
        var objectRotStarts = FindHashedArrayDataStarts(_mapSavRawBytes, MapSavObjectRotHash, MapSavObjectCount);
        if (TrySelectBestObjectArrayBundle(
            _mapSavRawBytes,
            objectHashStarts,
            objectXStarts,
            objectYStarts,
            objectRotStarts,
            _objectNameLookup,
            out var objectHashStart,
            out var objectXStart,
            out var objectYStart,
            out var objectRotStart))
        {
            _mapSavRawObjectHashStart = objectHashStart;
            _mapSavRawObjectXStart = objectXStart;
            _mapSavRawObjectYStart = objectYStart;
            _mapSavRawObjectRotStart = objectRotStart;

            AddRawMapSavArrayFields(_mapSavRawObjectHashStart, MapSavObjectCount, "ObjectHashes", "UInt32");
            AddRawMapSavArrayFields(_mapSavRawObjectXStart, MapSavObjectCount, "ObjectPosX", "Int32");
            AddRawMapSavArrayFields(_mapSavRawObjectYStart, MapSavObjectCount, "ObjectPosY", "Int32");
            if (_mapSavRawObjectRotStart >= 0)
            {
                AddRawMapSavArrayFields(_mapSavRawObjectRotStart, MapSavObjectCount, "ObjectRotY", "Single");
            }

            _mapSavRawObjectNameHashStart = SelectBestNameHashStartForAxes(
                _mapSavRawBytes,
                objectHashStarts,
                _mapSavRawObjectXStart,
                _mapSavRawObjectYStart,
                _objectNameLookup);
            if (_mapSavRawObjectNameHashStart < 0)
            {
                _mapSavRawObjectNameHashStart = _mapSavRawObjectHashStart;
            }
        }

        if (_mapSavRawFields.Count == 0)
        {
            _mapSavRawBytes = null;
            return false;
        }

        _mapSavIsRaw = true;
        _mapSavFilePath = filePath;
        _mapSavPathText.Text = filePath;
        RebuildRawMapSavEntryList();
        TryRefreshMapSavRawPreviewMap();
        _viewerTabs.SelectedIndex = 0;
        _mapSavStatusText.Text = $"Loaded raw Map.sav ({_mapSavRawFields.Count} editable values).";
        return true;
    }

    private void AddRawMapSavArrayFields(int dataStart, uint expectedCount, string displayName, string typeName)
    {
        if (_mapSavRawBytes is null)
        {
            return;
        }

        for (var i = 0; i < expectedCount; i++)
        {
            _mapSavRawFields.Add(new MapSavRawField
            {
                DisplayPath = $"{displayName}[{i}]",
                TypeName = typeName,
                ByteOffset = dataStart + (int)(i * 4u)
            });
        }
    }

    private static List<int> FindHashedArrayDataStarts(byte[] data, uint targetHash, uint expectedCount)
    {
        var starts = new List<int>();
        for (var i = 0; i <= data.Length - 8; i += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4)) != targetHash)
            {
                continue;
            }

            var candidateOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i + 4, 4));
            if (candidateOffset < 0 || candidateOffset > data.Length - 4)
            {
                continue;
            }

            var count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(candidateOffset, 4));
            if (count != expectedCount)
            {
                continue;
            }

            var candidateDataStart = candidateOffset + 4;
            var bytesNeeded = (long)count * 4L;
            if (candidateDataStart < 0 || candidateDataStart + bytesNeeded > data.Length)
            {
                continue;
            }

            starts.Add(candidateDataStart);
        }

        return starts.Distinct().ToList();
    }

    private static bool TrySelectBestObjectArrayBundle(
        byte[] data,
        IReadOnlyList<int> hashStarts,
        IReadOnlyList<int> xStarts,
        IReadOnlyList<int> yStarts,
        IReadOnlyList<int> rotStarts,
        IReadOnlyDictionary<uint, List<string>> objectNameLookup,
        out int hashStart,
        out int xStart,
        out int yStart,
        out int rotStart)
    {
        hashStart = -1;
        xStart = -1;
        yStart = -1;
        rotStart = -1;
        if (hashStarts.Count == 0 || xStarts.Count == 0 || yStarts.Count == 0)
        {
            return false;
        }

        var bestHashStart = -1;
        var bestXStart = -1;
        var bestYStart = -1;
        var bestRotStart = -1;
        var bestScore = long.MinValue;

        foreach (var hashCandidate in hashStarts)
        {
            var xCandidate = SelectNearestArrayStart(xStarts, hashCandidate);
            var yCandidate = SelectNearestArrayStart(yStarts, hashCandidate);
            var rotCandidate = rotStarts.Count > 0 ? SelectNearestArrayStart(rotStarts, hashCandidate) : -1;
            if (xCandidate < 0 || yCandidate < 0)
            {
                continue;
            }

            var score = ScoreObjectArrayBundle(data, hashCandidate, xCandidate, yCandidate, objectNameLookup);
            if (score > bestScore)
            {
                bestScore = score;
                bestHashStart = hashCandidate;
                bestXStart = xCandidate;
                bestYStart = yCandidate;
                bestRotStart = rotCandidate;
            }
        }

        if (bestHashStart < 0 || bestXStart < 0 || bestYStart < 0)
        {
            return false;
        }

        hashStart = bestHashStart;
        xStart = bestXStart;
        yStart = bestYStart;
        rotStart = bestRotStart;
        return true;
    }

    private static long ScoreObjectArrayBundle(
        byte[] data,
        int hashStart,
        int xStart,
        int yStart,
        IReadOnlyDictionary<uint, List<string>> objectNameLookup)
    {
        long score = 0;
        var limit = Math.Min((int)MapSavObjectCount, 1200);
        for (var i = 0; i < limit; i++)
        {
            var hashOffset = hashStart + (i * 4);
            var xOffset = xStart + (i * 4);
            var yOffset = yStart + (i * 4);
            if (hashOffset < 0 || xOffset < 0 || yOffset < 0 ||
                hashOffset + 4 > data.Length || xOffset + 4 > data.Length || yOffset + 4 > data.Length)
            {
                break;
            }

            var hash = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(hashOffset, 4));
            if (hash == 0)
            {
                continue;
            }

            var x = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(xOffset, 4));
            var y = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(yOffset, 4));

            score += 2;
            if (hash > 0xFFFF)
            {
                score += 3;
            }
            else
            {
                score -= 8;
            }

            if (objectNameLookup.Count > 0)
            {
                if (objectNameLookup.ContainsKey(hash))
                {
                    score += 10;
                }
                else
                {
                    score -= 1;
                }
            }

            if (x >= 0 && x < 120 && y >= 0 && y < 80)
            {
                score += 7;
            }
            else if (x >= -64 && x <= 256 && y >= -64 && y <= 256)
            {
                score += 1;
            }
            else
            {
                score -= 5;
            }
        }

        return score;
    }

    private static int SelectBestNameHashStartForAxes(
        byte[] data,
        IReadOnlyList<int> hashStarts,
        int xStart,
        int yStart,
        IReadOnlyDictionary<uint, List<string>> objectNameLookup)
    {
        if (hashStarts.Count == 0 || xStart < 0 || yStart < 0 || objectNameLookup.Count == 0)
        {
            return -1;
        }

        var bestHashStart = -1;
        var bestScore = long.MinValue;
        foreach (var hashStart in hashStarts)
        {
            long score = 0;
            var limit = Math.Min((int)MapSavObjectCount, 1200);
            for (var i = 0; i < limit; i++)
            {
                var hashOffset = hashStart + (i * 4);
                var xOffset = xStart + (i * 4);
                var yOffset = yStart + (i * 4);
                if (hashOffset < 0 || xOffset < 0 || yOffset < 0 ||
                    hashOffset + 4 > data.Length || xOffset + 4 > data.Length || yOffset + 4 > data.Length)
                {
                    break;
                }

                var hash = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(hashOffset, 4));
                if (hash == 0)
                {
                    continue;
                }

                var x = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(xOffset, 4));
                var y = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(yOffset, 4));
                if (x < 0 || x >= 120 || y < 0 || y >= 80)
                {
                    continue;
                }

                if (objectNameLookup.ContainsKey(hash))
                {
                    score += 10;
                }
                else
                {
                    score -= 2;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestHashStart = hashStart;
            }
        }

        return bestHashStart;
    }

    private static int SelectNearestArrayStart(IReadOnlyList<int> starts, int anchorStart)
    {
        if (starts.Count == 0)
        {
            return -1;
        }

        var best = starts[0];
        var bestDistance = Math.Abs(starts[0] - anchorStart);
        for (var i = 1; i < starts.Count; i++)
        {
            var distance = Math.Abs(starts[i] - anchorStart);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = starts[i];
            }
        }

        return best;
    }

    private void RebuildRawMapSavEntryList()
    {
        if (_mapSavRawBytes is null)
        {
            _mapSavEntryList.ItemsSource = null;
            _mapSavSelectedPathText.Text = "-";
            _mapSavSelectedTypeText.Text = "Type: -";
            _mapSavValueText.Text = string.Empty;
            _applyMapSavValueButton.IsEnabled = false;
            return;
        }

        _mapSavEntryList.ItemsSource = _mapSavRawFields.Select(field =>
        {
            var value = ReadRawMapSavValue(field);
            return $"{field.DisplayPath} ({field.TypeName}) = {value}";
        }).ToList();
        _mapSavEntryList.SelectedIndex = -1;
        _mapSavSelectedPathText.Text = "-";
        _mapSavSelectedTypeText.Text = "Type: -";
        _mapSavValueText.Text = string.Empty;
        _applyMapSavValueButton.IsEnabled = false;
    }

    private string ReadRawMapSavValue(MapSavRawField field)
    {
        if (_mapSavRawBytes is null || field.ByteOffset < 0 || field.ByteOffset + 4 > _mapSavRawBytes.Length)
        {
            return string.Empty;
        }

        var slice = _mapSavRawBytes.AsSpan(field.ByteOffset, 4);
        return field.TypeName switch
        {
            "UInt32" => BinaryPrimitives.ReadUInt32LittleEndian(slice).ToString(CultureInfo.InvariantCulture),
            "Int32" => BinaryPrimitives.ReadInt32LittleEndian(slice).ToString(CultureInfo.InvariantCulture),
            "Single" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(slice)).ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
    }

    private bool TryWriteRawMapSavValue(MapSavRawField field, string text)
    {
        if (_mapSavRawBytes is null || field.ByteOffset < 0 || field.ByteOffset + 4 > _mapSavRawBytes.Length)
        {
            return false;
        }

        var destination = _mapSavRawBytes.AsSpan(field.ByteOffset, 4);
        switch (field.TypeName)
        {
            case "UInt32":
                if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u32))
                {
                    return false;
                }
                BinaryPrimitives.WriteUInt32LittleEndian(destination, u32);
                return true;
            case "Int32":
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i32))
                {
                    return false;
                }
                BinaryPrimitives.WriteInt32LittleEndian(destination, i32);
                return true;
            case "Single":
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f32))
                {
                    return false;
                }
                BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(f32));
                return true;
            default:
                return false;
        }
    }

    private void TryRefreshMapSavRawPreviewMap()
    {
        if (!_mapSavIsRaw || _mapSavRawBytes is null)
        {
            return;
        }

        EnsureMapSavLookupsLoaded(_mapSavFilePath);

        if (!TryBuildMapSavRawPreviewMap(out var map))
        {
            return;
        }

        SelectMap(map);
        _statusText.Text = $"Loaded Map.sav preview ({map.Width}x{map.Height}, {map.Objects.Count} objects).";
    }

    private void EnsureMapSavLookupsLoaded(string? mapSavPath)
    {
        var guessedRoot = string.Empty;
        if (!string.IsNullOrWhiteSpace(mapSavPath))
        {
            var folder = Path.GetDirectoryName(mapSavPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                guessedRoot = FindRootByWalkingParents(folder) ?? string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(guessedRoot))
        {
            guessedRoot = FindRootByWalkingParents(Environment.CurrentDirectory) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(guessedRoot))
        {
            return;
        }

        var shouldReloadLookups =
            !string.Equals(_gameRootPath, guessedRoot, StringComparison.OrdinalIgnoreCase) ||
            _objectNameLookup.Count == 0 ||
            _floorNameLookup.Count == 0;

        if (!shouldReloadLookups)
        {
            return;
        }

        _gameRootPath = guessedRoot;
        _mapRepository = new MapRepository(_gameRootPath);
        _objectNameLookup = _mapRepository.LoadObjectNameLookup();
        _floorNameLookup = _mapRepository.LoadFloorNameLookup();
        RebuildObjectCatalog();
    }

    private bool TryBuildMapSavRawPreviewMap(out MapProject map)
    {
        map = new MapProject();
        if (_mapSavRawBytes is null || _mapSavRawGridStart < 0)
        {
            return false;
        }

        int width;
        int height;
        uint[,] grid;
        if (TryLoadMapSavBaselineGrid(out var baselineGrid, out var baselineWidth, out var baselineHeight))
        {
            width = baselineWidth;
            height = baselineHeight;
            grid = baselineGrid;
        }
        else
        {
            var resolved = ResolveMapSavRawDimensions();
            width = resolved.Width;
            height = resolved.Height;
            if (width <= 0 || height <= 0 || width * height != (int)MapSavGridCount)
            {
                return false;
            }

            grid = new uint[width, height];
        }

        for (var i = 0; i < (int)MapSavGridCount; i++)
        {
            var x = i / height;
            var y = i % height;
            var offset = _mapSavRawGridStart + (i * 4);
            if (offset < 0 || offset + 4 > _mapSavRawBytes.Length)
            {
                return false;
            }
            grid[x, y] = BinaryPrimitives.ReadUInt32LittleEndian(_mapSavRawBytes.AsSpan(offset, 4));
        }

        var objects = new List<MapObjectEntry>();
        _mapSavRawLoadedObjectSlots.Clear();
        if (_mapSavRawObjectHashStart >= 0 && _mapSavRawObjectXStart >= 0 && _mapSavRawObjectYStart >= 0)
        {
            for (var i = 0; i < (int)MapSavObjectCount; i++)
            {
                var hashOffset = _mapSavRawObjectHashStart + (i * 4);
                var xOffset = _mapSavRawObjectXStart + (i * 4);
                var yOffset = _mapSavRawObjectYStart + (i * 4);
                if (hashOffset + 4 > _mapSavRawBytes.Length || xOffset + 4 > _mapSavRawBytes.Length || yOffset + 4 > _mapSavRawBytes.Length)
                {
                    break;
                }

                var hash = BinaryPrimitives.ReadUInt32LittleEndian(_mapSavRawBytes.AsSpan(hashOffset, 4));
                if (hash == 0)
                {
                    continue;
                }
                _mapSavRawLoadedObjectSlots.Add(i);

                var x = BinaryPrimitives.ReadInt32LittleEndian(_mapSavRawBytes.AsSpan(xOffset, 4));
                var y = BinaryPrimitives.ReadInt32LittleEndian(_mapSavRawBytes.AsSpan(yOffset, 4));
                var rotY = 0f;
                if (_mapSavRawObjectRotStart >= 0)
                {
                    var rotOffset = _mapSavRawObjectRotStart + (i * 4);
                    if (rotOffset + 4 <= _mapSavRawBytes.Length)
                    {
                        rotY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_mapSavRawBytes.AsSpan(rotOffset, 4)));
                    }
                }

                if (x < 0 || y < 0 || x >= width || y >= height)
                {
                    continue;
                }

                objects.Add(new MapObjectEntry
                {
                    Id = $"Object_{i:0000}",
                    Hash = hash,
                    GridPosX = x,
                    GridPosY = y,
                    RotY = rotY
                });
            }
        }

        map = new MapProject
        {
            Name = "Map.sav",
            MapKey = "MapSavRaw",
            GridFilePath = _mapSavFilePath,
            ObjectFilePath = _mapSavFilePath,
            Width = width,
            Height = height,
            Grid = grid,
            UgcFloor = new uint[width, height],
            InvalidGridFlag = null,
            GridSizeType = $"{width}x{height}",
            CanBeFocus = true,
            CanEnterSequence = true,
            HasInvalidGridFlag = false,
            GridEndianness = Endianness.Little,
            GridVersion = 0,
            ObjectEndianness = Endianness.Little,
            ObjectVersion = 0,
            Objects = objects
        };
        return true;
    }

    private bool TryLoadMapSavBaselineGrid(out uint[,] grid, out int width, out int height)
    {
        grid = new uint[0, 0];
        width = 0;
        height = 0;

        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(_gameRootPath))
        {
            roots.Add(_gameRootPath);
        }

        if (!string.IsNullOrWhiteSpace(_mapSavFilePath))
        {
            var folder = Path.GetDirectoryName(_mapSavFilePath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                var root = FindRootByWalkingParents(folder);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    roots.Add(root);
                }
            }
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var gridPath = Path.Combine(root, "MapFile", "FirstIsland10_MapGrid_MapGrid.byml");
            var objectPath = Path.Combine(root, "MapFile", "FirstIsland10_MapGrid_MapObject.byml");
            if (!File.Exists(gridPath) || !File.Exists(objectPath))
            {
                continue;
            }

            var repo = new MapRepository(root);
            var map = repo.LoadMap(gridPath, objectPath);
            if (map is null || map.Width * map.Height != (int)MapSavGridCount)
            {
                continue;
            }

            var copied = new uint[map.Width, map.Height];
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    copied[x, y] = map.Grid[x, y];
                }
            }

            grid = copied;
            width = map.Width;
            height = map.Height;
            return true;
        }

        return false;
    }

    private (int Width, int Height) ResolveMapSavRawDimensions()
    {
        if (_currentMap is not null && _currentMap.Width > 0 && _currentMap.Height > 0 &&
            _currentMap.Width * _currentMap.Height == (int)MapSavGridCount)
        {
            return (_currentMap.Width, _currentMap.Height);
        }

        if (_mapSavRawBytes is not null &&
            _mapSavRawObjectHashStart >= 0 &&
            _mapSavRawObjectXStart >= 0 &&
            _mapSavRawObjectYStart >= 0)
        {
            var maxX = -1;
            var maxY = -1;
            for (var i = 0; i < (int)MapSavObjectCount; i++)
            {
                var hash = BinaryPrimitives.ReadUInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectHashStart + (i * 4), 4));
                if (hash == 0)
                {
                    continue;
                }

                var x = BinaryPrimitives.ReadInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectXStart + (i * 4), 4));
                var y = BinaryPrimitives.ReadInt32LittleEndian(_mapSavRawBytes.AsSpan(_mapSavRawObjectYStart + (i * 4), 4));
                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }

            var bestWidth = 120;
            var bestHeight = 80;
            var bestScore = int.MaxValue;
            for (var h = 1; h * h <= (int)MapSavGridCount; h++)
            {
                if ((int)MapSavGridCount % h != 0)
                {
                    continue;
                }

                var w = (int)MapSavGridCount / h;
                if (w < h)
                {
                    continue;
                }

                if (maxX >= 0 && maxY >= 0 && (maxX >= w || maxY >= h))
                {
                    continue;
                }

                var score = Math.Abs(w - 120) + Math.Abs(h - 80);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestWidth = w;
                    bestHeight = h;
                }
            }

            return (bestWidth, bestHeight);
        }

        return (120, 80);
    }

    private static string GuessGameRootPath(string gridFilePath, string objectFilePath)
    {
        var candidates = new[]
        {
            Path.GetDirectoryName(gridFilePath),
            Path.GetDirectoryName(objectFilePath)
        }
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var start in candidates)
        {
            var found = FindRootByWalkingParents(start);
            if (!string.IsNullOrWhiteSpace(found))
            {
                return found;
            }
        }

        var cwdRoot = FindRootByWalkingParents(Environment.CurrentDirectory);
        if (!string.IsNullOrWhiteSpace(cwdRoot))
        {
            return cwdRoot;
        }

        var fallback = candidates.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return AppContext.BaseDirectory;
    }

    private static string? FindRootByWalkingParents(string path)
    {
        var dir = new DirectoryInfo(path);
        while (dir is not null)
        {
            var hasModel = Directory.Exists(Path.Combine(dir.FullName, "Model"));
            var hasMap = Directory.Exists(Path.Combine(dir.FullName, "MapFile"));
            var hasTex = Directory.Exists(Path.Combine(dir.FullName, "Tex"));
            if (hasModel && hasMap && hasTex)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool TryGetRootNode(Byml byml, out Byml rootNode, out bool hasExplicitRoot)
    {
        rootNode = new Byml();
        hasExplicitRoot = false;
        if (byml.Type.ToString() != "Map")
        {
            return false;
        }

        var map = byml.GetMap();
        if (map.TryGetValue("root", out var explicitRootNode) && explicitRootNode.Type.ToString() == "Map")
        {
            rootNode = explicitRootNode;
            hasExplicitRoot = true;
            return true;
        }

        rootNode = byml;
        return true;
    }

    private static Byml GetMapSavRootNode(Byml document, bool hasExplicitRoot)
    {
        if (!hasExplicitRoot)
        {
            return document;
        }

        var map = document.GetMap();
        return map.TryGetValue("root", out var root) ? root : document;
    }

    private static void CollectMapSavEditableEntries(Byml node, List<object> path, List<MapSavEditableEntry> output)
    {
        var type = node.Type.ToString();
        if (IsEditablePrimitiveType(type))
        {
            output.Add(new MapSavEditableEntry
            {
                DisplayPath = BuildPathDisplay(path),
                TypeName = type,
                Tokens = [.. path]
            });
            return;
        }

        if (type == "Map")
        {
            foreach (var kv in node.GetMap().OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                path.Add(kv.Key);
                CollectMapSavEditableEntries(kv.Value, path, output);
                path.RemoveAt(path.Count - 1);
            }
            return;
        }

        if (type == "Array")
        {
            var array = node.GetArray();
            for (var i = 0; i < array.Count; i++)
            {
                path.Add(i);
                CollectMapSavEditableEntries(array[i], path, output);
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    private static string BuildPathDisplay(List<object> tokens)
    {
        if (tokens.Count == 0)
        {
            return "root";
        }

        var parts = new List<string>();
        foreach (var token in tokens)
        {
            if (token is string key)
            {
                if (parts.Count == 0)
                {
                    parts.Add(key);
                }
                else
                {
                    parts.Add($".{key}");
                }
            }
            else if (token is int index)
            {
                parts.Add($"[{index}]");
            }
        }

        return string.Concat(parts);
    }

    private static bool IsEditablePrimitiveType(string type)
    {
        return type is "Bool" or "String" or "Int" or "UInt32" or "Float" or "Double";
    }

    private static bool TryGetBymlNodeAtPath(Byml root, List<object> tokens, out Byml node)
    {
        node = root;
        foreach (var token in tokens)
        {
            if (token is string key)
            {
                if (node.Type.ToString() != "Map")
                {
                    return false;
                }

                var map = node.GetMap();
                if (!map.TryGetValue(key, out node))
                {
                    return false;
                }
            }
            else if (token is int index)
            {
                if (node.Type.ToString() != "Array")
                {
                    return false;
                }

                var array = node.GetArray();
                if (index < 0 || index >= array.Count)
                {
                    return false;
                }

                node = array[index];
            }
        }

        return true;
    }

    private static Byml SetBymlNodeAtPath(Byml node, List<object> tokens, int depth, Byml value)
    {
        if (depth >= tokens.Count)
        {
            return value;
        }

        var token = tokens[depth];
        if (token is string key)
        {
            var map = node.GetMap();
            if (!map.TryGetValue(key, out var child))
            {
                return node;
            }

            map[key] = SetBymlNodeAtPath(child, tokens, depth + 1, value);
            return new Byml(map);
        }

        if (token is int index)
        {
            var array = node.GetArray();
            if (index < 0 || index >= array.Count)
            {
                return node;
            }

            array[index] = SetBymlNodeAtPath(array[index], tokens, depth + 1, value);
            return new Byml(array);
        }

        return node;
    }

    private static string GetBymlValueDisplay(Byml node)
    {
        var type = node.Type.ToString();
        return type switch
        {
            "Bool" => node.GetBool().ToString(),
            "String" => node.GetString(),
            "Int" => node.GetInt().ToString(CultureInfo.InvariantCulture),
            "UInt32" => node.GetUInt32().ToString(CultureInfo.InvariantCulture),
            "Float" => node.GetFloat().ToString(CultureInfo.InvariantCulture),
            "Double" => node.GetDouble().ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
    }

    private static bool TryParseBymlValue(string text, string type, out Byml value)
    {
        value = new Byml();
        var raw = text.Trim();
        switch (type)
        {
            case "Bool":
                if (bool.TryParse(raw, out var b))
                {
                    value = new Byml(b);
                    return true;
                }
                if (raw == "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    value = new Byml(true);
                    return true;
                }
                if (raw == "0" || raw.Equals("no", StringComparison.OrdinalIgnoreCase))
                {
                    value = new Byml(false);
                    return true;
                }
                return false;
            case "String":
                value = new Byml(raw);
                return true;
            case "Int":
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i32))
                {
                    value = new Byml(i32);
                    return true;
                }
                return false;
            case "UInt32":
                if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u32))
                {
                    value = new Byml(u32);
                    return true;
                }
                return false;
            case "Float":
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f32))
                {
                    value = new Byml(f32);
                    return true;
                }
                return false;
            case "Double":
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f64))
                {
                    value = new Byml(f64);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private void SelectMap(MapProject map)
    {
        _currentMap = map;
        _undoStack.Clear();
        _redoStack.Clear();
        _tileStrokeUndoCaptured = false;
        UpdateUndoRedoButtons();
        DetermineTerrainHashes(map, out _landHash, out _waterHash);
        _paintLandHash = _landHash;
        _mapCanvas.SetTerrainHashes(_paintLandHash, _waterHash);
        _mapCanvas.EditMode = MapEditMode.MoveObjects;
        UpdateEditModeButtons();
        BuildTileColorMap(map);
        _mapCanvas.SetTileColorMap(_tileColorByHash);

        _mapInfoText.Text =
            $"MapKey: {map.MapKey}\n" +
            $"Grid: {Path.GetFileName(map.GridFilePath)}\n" +
            $"Object: {Path.GetFileName(map.ObjectFilePath)}\n" +
            $"Size: {map.Width}x{map.Height}\n" +
            $"GridSizeType: {map.GridSizeType}\n" +
            $"CanBeFocus: {map.CanBeFocus}\n" +
            $"CanEnterSequence: {map.CanEnterSequence}\n" +
            $"HasInvalidGridFlag: {map.HasInvalidGridFlag}\n" +
            $"ObjectCount: {map.Objects.Count}";

        RefreshObjectList(map);
        _mapCanvas.SetMap(map, ResolveObjectName);
        _scene3D.SetScene([]);
        _threeDStatusText.Text = "Ready to load 3D models.";
        RefreshFloorLegend(map);
        RefreshPaintTileTypeOptions(map);
        UpdateViewerInfoText(map.Name);
        UpdateObjectCatalogAddButtonState();
        _statusText.Text = $"Move mode | land hash {_landHash} | water hash {_waterHash}";
    }

    private void ObjectSearchTextOnTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyObjectCatalogFilter();
    }

    private void ObjectCatalogListOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateObjectCatalogAddButtonState();
    }

    private void AddObjectFromCatalogButtonOnClick(object? sender, RoutedEventArgs e)
    {
        AddSelectedCatalogObject();
    }

    private void RebuildObjectCatalog()
    {
        _allObjectCatalogOptions = _objectNameLookup
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => new ObjectCatalogOption
            {
                Hash = kv.Key,
                PrimaryName = kv.Value[0],
                SearchText = BuildObjectCatalogSearchText(kv.Key, kv.Value)
            })
            .OrderBy(o => o.PrimaryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Hash)
            .ToList();

        ApplyObjectCatalogFilter();
    }

    private static string BuildObjectCatalogSearchText(uint hash, IReadOnlyCollection<string> names)
    {
        var allNames = string.Join(' ', names);
        return $"{allNames} {hash} 0x{hash:X8}";
    }

    private void ApplyObjectCatalogFilter()
    {
        var query = (_objectSearchText.Text ?? string.Empty).Trim();
        IEnumerable<ObjectCatalogOption> filtered = _allObjectCatalogOptions;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(o =>
                o.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase));

            if (TryParseObjectHashQuery(query, out var hashQuery))
            {
                filtered = filtered
                    .Concat(_allObjectCatalogOptions.Where(o => o.Hash == hashQuery))
                    .GroupBy(o => o.Hash)
                    .Select(g => g.First());
            }
        }

        var selectedHash = (_objectCatalogList.SelectedItem as ObjectCatalogOption)?.Hash;
        var list = filtered.ToList();
        _objectCatalogList.ItemsSource = list;
        _objectCatalogCountText.Text = $"{list.Count} object types";

        if (selectedHash.HasValue)
        {
            var reselect = list.FirstOrDefault(o => o.Hash == selectedHash.Value);
            if (reselect is not null)
            {
                _objectCatalogList.SelectedItem = reselect;
            }
        }

        UpdateObjectCatalogAddButtonState();
    }

    private static bool TryParseObjectHashQuery(string value, out uint hash)
    {
        hash = 0;
        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash);
        }

        return uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out hash);
    }

    private void UpdateObjectCatalogAddButtonState()
    {
        _addObjectFromCatalogButton.IsEnabled = _currentMap is not null && _objectCatalogList.SelectedItem is ObjectCatalogOption;
    }

    private void AddSelectedCatalogObject()
    {
        if (_currentMap is null)
        {
            _statusText.Text = "Load a map first.";
            return;
        }

        if (_objectCatalogList.SelectedItem is not ObjectCatalogOption selected)
        {
            _statusText.Text = "Select an object from search first.";
            return;
        }

        if (!_suppressHistory)
        {
            PushUndoSnapshot(CaptureSnapshot(_currentMap));
        }

        var x = _currentMap.Width / 2;
        var y = _currentMap.Height / 2;
        if (_mapCanvas.TryGetGridAtLastPointer(out var cursorX, out var cursorY))
        {
            x = cursorX;
            y = cursorY;
        }
        else
        {
            var selectedIndex = GetSelectedObjectIndex();
            if (selectedIndex >= 0 && selectedIndex < _currentMap.Objects.Count)
            {
                x = _currentMap.Objects[selectedIndex].GridPosX;
                y = _currentMap.Objects[selectedIndex].GridPosY;
            }
        }

        x = Math.Clamp(x, 0, _currentMap.Width - 1);
        y = Math.Clamp(y, 0, _currentMap.Height - 1);

        var added = new MapObjectEntry
        {
            Id = BuildNextObjectId(_currentMap),
            Hash = selected.Hash,
            GridPosX = x,
            GridPosY = y,
            RotY = 0f
        };

        _currentMap.Objects.Add(added);
        var addedIndex = _currentMap.Objects.Count - 1;
        RefreshObjectList(_currentMap, addedIndex);
        _mapCanvas.InvalidateVisual();
        _statusText.Text = $"Added {selected.PrimaryName} (0x{selected.Hash:X8}) at ({x},{y}).";
    }

    private void ObjectListOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _mapCanvas.SelectedObjectIndex = _objectList.SelectedIndex;
    }

    private void ObjectListOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_objectList).Properties.IsRightButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(_objectList);
        for (var i = 0; i < _objectList.ItemCount; i++)
        {
            if (_objectList.ContainerFromIndex(i) is not Control container)
            {
                continue;
            }

            var topLeft = container.TranslatePoint(default, _objectList);
            if (topLeft is null)
            {
                continue;
            }

            var bounds = new Rect(topLeft.Value, container.Bounds.Size);
            if (bounds.Contains(point))
            {
                _objectList.SelectedIndex = i;
                break;
            }
        }
    }

    private void MainWindowOnKeyDown(object? sender, KeyEventArgs e)
    {
        var primaryModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (primaryModifier && e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Redo();
            e.Handled = true;
            return;
        }

        if (primaryModifier && e.Key == Key.Z)
        {
            Undo();
            e.Handled = true;
            return;
        }

        if (primaryModifier && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
            return;
        }

        if (primaryModifier && e.Key == Key.C)
        {
            CopySelectedObject();
            e.Handled = true;
            return;
        }

        if (primaryModifier && e.Key == Key.X)
        {
            CutSelectedObject();
            e.Handled = true;
            return;
        }

        if (primaryModifier && e.Key == Key.V)
        {
            PasteObject();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            _mapCanvas.PanBy(24, 0);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            _mapCanvas.PanBy(-24, 0);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _mapCanvas.PanBy(0, 24);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _mapCanvas.PanBy(0, -24);
            e.Handled = true;
        }
        else if (e.Key is Key.OemPlus or Key.Add)
        {
            _mapCanvas.ZoomBy(1.1);
            e.Handled = true;
        }
        else if (e.Key is Key.OemMinus or Key.Subtract)
        {
            _mapCanvas.ZoomBy(0.9);
            e.Handled = true;
        }
        else if (e.Key is Key.Delete)
        {
            DeleteSelectedObject();
            e.Handled = true;
        }
    }

    private async void SaveMapButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (_mapSavModeActive)
        {
            await SaveMapSavAsync();
            return;
        }

        await SaveCurrentMapAsync();
    }

    private async void SaveAsMapButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (_mapSavModeActive)
        {
            await SaveMapSavAsAsync();
            return;
        }

        await SaveCurrentMapAsAsync();
    }

    private void UndoButtonOnClick(object? sender, RoutedEventArgs e)
    {
        Undo();
    }

    private void RedoButtonOnClick(object? sender, RoutedEventArgs e)
    {
        Redo();
    }

    private void DeleteObjectButtonOnClick(object? sender, RoutedEventArgs e)
    {
        DeleteSelectedObject();
    }

    private async void Load3DButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await Load3DSceneAsync();
    }

    private void MoveObjectsButtonOnClick(object? sender, RoutedEventArgs e)
    {
        _mapCanvas.EditMode = MapEditMode.MoveObjects;
        UpdateEditModeButtons();
        _statusText.Text = "Move mode enabled.";
    }

    private void AddLandButtonOnClick(object? sender, RoutedEventArgs e)
    {
        _mapCanvas.EditMode = MapEditMode.AddLand;
        UpdateEditModeButtons();
        _statusText.Text = $"Add tile mode enabled (hash {_paintLandHash}). Drag on tiles to paint.";
    }

    private void DeleteLandButtonOnClick(object? sender, RoutedEventArgs e)
    {
        _mapCanvas.EditMode = MapEditMode.DeleteLand;
        UpdateEditModeButtons();
        _statusText.Text = $"Delete tiles mode enabled (hash {_waterHash}). Drag on tiles to erase.";
    }

    private void UpdateEditModeButtons()
    {
        ApplyModeButtonStyle(_moveObjectsButton, _mapCanvas.EditMode == MapEditMode.MoveObjects);
        ApplyModeButtonStyle(_addLandButton, _mapCanvas.EditMode == MapEditMode.AddLand);
        ApplyModeButtonStyle(_deleteLandButton, _mapCanvas.EditMode == MapEditMode.DeleteLand);
    }

    private static void ApplyModeButtonStyle(Button button, bool isActive)
    {
        button.Background = new SolidColorBrush(Color.Parse(isActive ? ModeButtonActiveBg : ModeButtonInactiveBg));
        button.Foreground = new SolidColorBrush(Color.Parse(ModeButtonForeground));
    }

    private void MapCanvasOnObjectMoved(object? sender, MapObjectMovedEventArgs e)
    {
        if (_currentMap is null || e.Index < 0 || e.Index >= _currentMap.Objects.Count)
        {
            return;
        }

        if (!_suppressHistory && (e.OldGridPosX != e.GridPosX || e.OldGridPosY != e.GridPosY))
        {
            PushUndoSnapshot(CreateSnapshotWithObjectAt(_currentMap, e.Index, e.OldGridPosX, e.OldGridPosY));
        }

        _currentMap.Objects[e.Index].GridPosX = e.GridPosX;
        _currentMap.Objects[e.Index].GridPosY = e.GridPosY;
        RefreshObjectList(_currentMap, e.Index);
        _statusText.Text = $"Moved {_currentMap.Objects[e.Index].Id} to ({e.GridPosX},{e.GridPosY}).";
    }

    private void MapCanvasOnObjectSelected(object? sender, ObjectSelectedEventArgs e)
    {
        if (_currentMap is null || e.Index < 0 || e.Index >= _currentMap.Objects.Count)
        {
            return;
        }

        _objectList.SelectedIndex = e.Index;
        _mapCanvas.SelectedObjectIndex = e.Index;
    }

    private void MapCanvasOnTilePainted(object? sender, EventArgs e)
    {
        if (_currentMap is not null)
        {
            BuildTileColorMap(_currentMap);
            _mapCanvas.SetTileColorMap(_tileColorByHash);
            RefreshFloorLegend(_currentMap);
            RefreshPaintTileTypeOptions(_currentMap);
            _mapCanvas.InvalidateVisual();
        }
    }

    private void MapCanvasOnTilePaintStrokeStarted(object? sender, EventArgs e)
    {
        if (_currentMap is null || _suppressHistory || _tileStrokeUndoCaptured)
        {
            return;
        }

        PushUndoSnapshot(CaptureSnapshot(_currentMap));
        _tileStrokeUndoCaptured = true;
    }

    private void MapCanvasOnTilePaintStrokeCompleted(object? sender, EventArgs e)
    {
        _tileStrokeUndoCaptured = false;
    }

    private async Task Load3DSceneAsync()
    {
        var map = _currentMap;
        if (map is null)
        {
            _threeDStatusText.Text = "Load a map first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_gameRootPath) ||
            !Directory.Exists(Path.Combine(_gameRootPath, "Model")) ||
            !Directory.Exists(Path.Combine(_gameRootPath, "Tex")))
        {
            _threeDStatusText.Text = "Game root is missing Model and/or Tex folders.";
            return;
        }

        _load3DButton.IsEnabled = false;
        _threeDStatusText.Text = "Resolving and loading models...";

        try
        {
            var resolver = new ModelFileResolver(_gameRootPath);
            var distinctHashes = map.Objects.Select(o => o.Hash).Distinct().ToList();
            var loadedByHash = new Dictionary<uint, BfresMeshData>();
            var failedLoads = 0;
            var resolvedNames = 0;

            await Task.Run(() =>
            {
                foreach (var hash in distinctHashes)
                {
                    if (!TryResolveModelPathForHash(resolver, hash, out var modelPath))
                    {
                        continue;
                    }

                    resolvedNames++;

                    try
                    {
                        var mesh = _bfresReader.ReadMesh(modelPath!);
                        if (mesh.VertexCount > 0)
                        {
                            loadedByHash[hash] = mesh;
                        }
                    }
                    catch
                    {
                        failedLoads++;
                    }
                }
            });

            var instances = map.Objects
                .Where(o => loadedByHash.ContainsKey(o.Hash))
                .Select(o => new SceneMeshInstance
                {
                    Mesh = loadedByHash[o.Hash],
                    Name = ResolveObjectName(o.Hash),
                    Hash = o.Hash,
                    GridX = o.GridPosX - (map.Width * 0.5f),
                    GridY = o.GridPosY - (map.Height * 0.5f),
                    RotY = o.RotY
                })
                .ToList();

            _scene3D.SetScene(instances);
            _threeDStatusText.Text = $"Root: {_gameRootPath} | names matched {resolvedNames} | model types {loadedByHash.Count} | instances {instances.Count} | failed {failedLoads}.";
        }
        catch (Exception ex)
        {
            _threeDStatusText.Text = ex.Message;
        }
        finally
        {
            _load3DButton.IsEnabled = true;
        }
    }

    private bool TryResolveModelPathForHash(ModelFileResolver resolver, uint hash, out string? modelPath)
    {
        modelPath = null;

        if (_objectNameLookup.TryGetValue(hash, out var names))
        {
            foreach (var name in names)
            {
                var resolved = resolver.ResolveFromObjectName(name);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    modelPath = resolved;
                    return true;
                }
            }
        }

        var fallback = ResolveObjectName(hash);
        if (fallback != "(unknown)")
        {
            var resolved = resolver.ResolveFromObjectName(fallback);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                modelPath = resolved;
                return true;
            }
        }

        return false;
    }

    private void RefreshObjectList(MapProject map, int selectedIndex = -1)
    {
        var rows = map.Objects
            .Select(o =>
            {
                var name = ResolveObjectDisplayName(o);
                var displayName = name == "(unknown)" ? $"0x{o.Hash:X8}" : name;
                var id = o.Id.StartsWith("Object_", StringComparison.Ordinal) ? o.Id[7..] : o.Id;
                return $"{id}  {displayName}  {o.GridPosX},{o.GridPosY}";
            })
            .ToList();

        _objectList.ItemsSource = rows;
        if (selectedIndex >= 0 && selectedIndex < rows.Count)
        {
            _objectList.SelectedIndex = selectedIndex;
            _mapCanvas.SelectedObjectIndex = selectedIndex;
        }
    }

    private string ResolveObjectName(uint hash)
    {
        if (_objectNameLookup.TryGetValue(hash, out var names) && names.Count > 0)
        {
            return names[0];
        }

        return "(unknown)";
    }

    private string ResolveObjectDisplayName(MapObjectEntry entry)
    {
        var direct = ResolveObjectName(entry.Hash);
        if (direct != "(unknown)")
        {
            return direct;
        }

        if (!_mapSavIsRaw || _mapSavRawBytes is null || _mapSavRawObjectNameHashStart < 0)
        {
            return direct;
        }

        var slot = TryParseMapSavObjectIndex(entry.Id);
        if (slot < 0 || slot >= (int)MapSavObjectCount)
        {
            return direct;
        }

        var offset = _mapSavRawObjectNameHashStart + (slot * 4);
        if (offset < 0 || offset + 4 > _mapSavRawBytes.Length)
        {
            return direct;
        }

        var altHash = BinaryPrimitives.ReadUInt32LittleEndian(_mapSavRawBytes.AsSpan(offset, 4));
        if (_objectNameLookup.TryGetValue(altHash, out var names) && names.Count > 0)
        {
            return names[0];
        }

        return direct;
    }

    private void DeleteSelectedObject()
    {
        if (_currentMap is null)
        {
            return;
        }

        var indices = GetSelectedObjectIndices(_currentMap);
        if (indices.Count == 0)
        {
            _statusText.Text = "Select an object first.";
            return;
        }

        if (!_suppressHistory)
        {
            PushUndoSnapshot(CaptureSnapshot(_currentMap));
        }

        var removedIds = new List<string>();
        foreach (var index in indices.OrderByDescending(i => i))
        {
            removedIds.Add(_currentMap.Objects[index].Id);
            _currentMap.Objects.RemoveAt(index);
        }

        _mapCanvas.InvalidateVisual();
        var nextIndex = _currentMap.Objects.Count > 0 ? Math.Min(indices.Min(), _currentMap.Objects.Count - 1) : -1;
        RefreshObjectList(_currentMap, nextIndex);
        _statusText.Text = removedIds.Count == 1
            ? $"Deleted {removedIds[0]}."
            : $"Deleted {removedIds.Count} objects.";
    }

    private void CopySelectedObject()
    {
        if (_currentMap is null)
        {
            return;
        }

        var indices = GetSelectedObjectIndices(_currentMap);
        if (indices.Count == 0)
        {
            _statusText.Text = "Select an object first.";
            return;
        }

        _objectClipboard = indices
            .Select(i => CloneObject(_currentMap.Objects[i]))
            .ToList();
        _statusText.Text = _objectClipboard.Count == 1
            ? $"Copied {_objectClipboard[0].Id}."
            : $"Copied {_objectClipboard.Count} objects.";
    }

    private void CutSelectedObject()
    {
        if (_currentMap is null)
        {
            return;
        }

        var indices = GetSelectedObjectIndices(_currentMap);
        if (indices.Count == 0)
        {
            _statusText.Text = "Select an object first.";
            return;
        }

        _objectClipboard = indices
            .Select(i => CloneObject(_currentMap.Objects[i]))
            .ToList();
        DeleteSelectedObject();
    }

    private void PasteObject()
    {
        if (_currentMap is null)
        {
            return;
        }

        if (_objectClipboard.Count == 0)
        {
            _statusText.Text = "Nothing to paste.";
            return;
        }

        if (!_suppressHistory)
        {
            PushUndoSnapshot(CaptureSnapshot(_currentMap));
        }

        var sourceMinX = _objectClipboard.Min(o => o.GridPosX);
        var sourceMinY = _objectClipboard.Min(o => o.GridPosY);
        var anchorX = sourceMinX + 1;
        var anchorY = sourceMinY + 1;
        if (_mapCanvas.TryGetGridAtLastPointer(out var cursorX, out var cursorY))
        {
            anchorX = cursorX;
            anchorY = cursorY;
        }
        else
        {
            var selectedIndices = GetSelectedObjectIndices(_currentMap);
            if (selectedIndices.Count > 0)
            {
                var first = _currentMap.Objects[selectedIndices[0]];
                anchorX = first.GridPosX + 1;
                anchorY = first.GridPosY + 1;
            }
        }

        var addedIndices = new List<int>();
        foreach (var source in _objectClipboard)
        {
            var target = CloneObject(source);
            target.Id = BuildNextObjectId(_currentMap);
            target.GridPosX = Math.Clamp(anchorX + (source.GridPosX - sourceMinX), 0, _currentMap.Width - 1);
            target.GridPosY = Math.Clamp(anchorY + (source.GridPosY - sourceMinY), 0, _currentMap.Height - 1);
            _currentMap.Objects.Add(target);
            addedIndices.Add(_currentMap.Objects.Count - 1);
        }

        RefreshObjectList(_currentMap, addedIndices.Count > 0 ? addedIndices[0] : -1);
        _objectList.SelectedItems.Clear();
        var rows = (_objectList.ItemsSource as IEnumerable<object>)?.ToList() ?? [];
        foreach (var idx in addedIndices.Where(i => i >= 0 && i < rows.Count))
        {
            _objectList.SelectedItems.Add(rows[idx]);
        }
        _mapCanvas.SelectedObjectIndex = addedIndices.Count > 0 ? addedIndices[0] : -1;
        _mapCanvas.InvalidateVisual();
        _statusText.Text = _objectClipboard.Count == 1
            ? $"Pasted {_objectClipboard[0].Id}."
            : $"Pasted {_objectClipboard.Count} objects.";
    }

    private int GetSelectedObjectIndex()
    {
        if (_currentMap is null)
        {
            return -1;
        }

        var indices = GetSelectedObjectIndices(_currentMap);
        if (indices.Count > 0)
        {
            return indices[0];
        }

        var index = _mapCanvas.SelectedObjectIndex;
        return index >= 0 && index < _currentMap.Objects.Count ? index : -1;
    }

    private List<int> GetSelectedObjectIndices(MapProject map)
    {
        var selected = new List<int>();
        var selectedItems = _objectList.SelectedItems;
        if (selectedItems is not null)
        {
            var rowToIndex = ((_objectList.ItemsSource as IEnumerable<object>) ?? [])
                .Select((row, index) => new { row, index })
                .ToDictionary(x => x.row, x => x.index);
            foreach (var item in selectedItems)
            {
                if (item is not null && rowToIndex.TryGetValue(item, out var idx) && idx >= 0 && idx < map.Objects.Count)
                {
                    selected.Add(idx);
                }
            }
        }

        if (selected.Count == 0)
        {
            var single = _objectList.SelectedIndex;
            if (single >= 0 && single < map.Objects.Count)
            {
                selected.Add(single);
            }
        }

        return selected.Distinct().OrderBy(i => i).ToList();
    }

    private static string BuildNextObjectId(MapProject map)
    {
        var max = map.Objects
            .Select(o =>
            {
                if (o.Id.StartsWith("Object_", StringComparison.Ordinal) &&
                    int.TryParse(o.Id[7..], out var n))
                {
                    return n;
                }

                return -1;
            })
            .DefaultIfEmpty(-1)
            .Max();

        return $"Object_{max + 1:000}";
    }

    private void BuildObjectContextMenus()
    {
        var listMenu = CreateObjectContextMenu();
        var canvasMenu = CreateObjectContextMenu();
        _objectList.ContextMenu = listMenu;
        _mapCanvas.ContextMenu = canvasMenu;
    }

    private ContextMenu CreateObjectContextMenu()
    {
        var deleteItem = new MenuItem { Header = "Delete" };
        var copyItem = new MenuItem { Header = "Copy" };
        var cutItem = new MenuItem { Header = "Cut" };
        var pasteItem = new MenuItem { Header = "Paste" };

        deleteItem.Click += (_, _) => DeleteSelectedObject();
        copyItem.Click += (_, _) => CopySelectedObject();
        cutItem.Click += (_, _) => CutSelectedObject();
        pasteItem.Click += (_, _) => PasteObject();

        return new ContextMenu
        {
            ItemsSource = new object[]
            {
                deleteItem,
                copyItem,
                cutItem,
                new Separator(),
                pasteItem
            }
        };
    }

    private async Task SaveCurrentMapAsync()
    {
        if (_mapRepository is null || _currentMap is null)
        {
            _statusText.Text = "Load a map first.";
            return;
        }

        try
        {
            _mapRepository.SaveMap(_currentMap);
            _statusText.Text = $"Saved {Path.GetFileName(_currentMap.GridFilePath)} and {Path.GetFileName(_currentMap.ObjectFilePath)}.";
            await ShowSaveSuccessDialogAsync("Save Complete", _currentMap.GridFilePath, _currentMap.ObjectFilePath);
        }
        catch (Exception ex)
        {
            _statusText.Text = ex.Message;
        }
    }

    private void ShowHub(bool showHub)
    {
        _hubPanel.IsVisible = showHub;
        _editorRoot.IsVisible = !showHub;
        if (showHub)
        {
            Width = HubWidth;
            Height = HubHeight;
            MinWidth = HubWidth;
            MaxWidth = HubWidth;
            MinHeight = HubHeight;
            MaxHeight = HubHeight;
            CanResize = false;
            _hubGameBananaPrompt.IsVisible = !_hasClickedGameBananaLikeLink;
        }
        else
        {
            Width = EditorWidth;
            Height = EditorHeight;
            MinWidth = EditorMinWidth;
            MinHeight = EditorMinHeight;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            CanResize = true;
            _hubGameBananaPrompt.IsVisible = false;
        }
    }

    private void UpdateMapSavToolbarState()
    {
        _exitMapSavButton.IsEnabled = true;
        _mapSavTab.IsVisible = _mapSavModeActive;
        if (!_mapSavModeActive && _viewerTabs.SelectedIndex == 2)
        {
            _viewerTabs.SelectedIndex = 0;
        }
    }

    private bool OpenExternalUrl(string url, string label)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Could not open {label}: {ex.Message}";
            return false;
        }
    }

    private async Task SaveCurrentMapAsAsync()
    {
        if (_mapRepository is null || _currentMap is null)
        {
            _statusText.Text = "Load a map first.";
            return;
        }

        var top = GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            return;
        }

        var bymlFilter = new FilePickerFileType("BYML files")
        {
            Patterns = ["*.byml", "*.bgyml"]
        };

        var gridPath = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save MapGrid BYML As",
            SuggestedFileName = Path.GetFileName(_currentMap.GridFilePath),
            FileTypeChoices = [bymlFilter],
            DefaultExtension = "byml"
        });
        var gridFile = gridPath?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(gridFile))
        {
            return;
        }

        var objectPath = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save MapObject BYML As",
            SuggestedFileName = Path.GetFileName(_currentMap.ObjectFilePath),
            FileTypeChoices = [bymlFilter],
            DefaultExtension = "byml"
        });
        var objectFile = objectPath?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(objectFile))
        {
            return;
        }

        try
        {
            _mapRepository.SaveMap(_currentMap, gridFile, objectFile);
            _currentMap.GridFilePath = gridFile;
            _currentMap.ObjectFilePath = objectFile;
            _rootPathText.Text = $"{Path.GetFileName(gridFile)} | {Path.GetFileName(objectFile)}";
            _statusText.Text = $"Saved as {Path.GetFileName(gridFile)} and {Path.GetFileName(objectFile)}.";
            await ShowSaveSuccessDialogAsync("Save As Complete", gridFile, objectFile);
        }
        catch (Exception ex)
        {
            _statusText.Text = ex.Message;
        }
    }

    private async Task ShowSaveSuccessDialogAsync(string title, string gridPath, string objectPath)
    {
        await ShowInfoDialogAsync(title, $"Map files saved successfully.\nMapGrid: {gridPath}\nMapObject: {objectPath}");
    }

    private void PushUndoSnapshot(MapEditSnapshot snapshot)
    {
        if (_suppressHistory)
        {
            return;
        }

        _undoStack.Push(snapshot);
        _redoStack.Clear();
        UpdateUndoRedoButtons();
        if (_mapSavModeActive)
        {
            _mapSavDirty = true;
        }
    }

    private void Undo()
    {
        if (_currentMap is null || _undoStack.Count == 0)
        {
            return;
        }

        var current = CaptureSnapshot(_currentMap);
        var previous = _undoStack.Pop();
        _redoStack.Push(current);
        ApplySnapshot(_currentMap, previous);
        UpdateUndoRedoButtons();
        _statusText.Text = "Undo applied.";
    }

    private void Redo()
    {
        if (_currentMap is null || _redoStack.Count == 0)
        {
            return;
        }

        var current = CaptureSnapshot(_currentMap);
        var next = _redoStack.Pop();
        _undoStack.Push(current);
        ApplySnapshot(_currentMap, next);
        UpdateUndoRedoButtons();
        _statusText.Text = "Redo applied.";
    }

    private void UpdateUndoRedoButtons()
    {
        _undoButton.IsEnabled = _undoStack.Count > 0;
        _redoButton.IsEnabled = _redoStack.Count > 0;
    }

    private MapEditSnapshot CaptureSnapshot(MapProject map)
    {
        var width = map.Width;
        var height = map.Height;
        var grid = new uint[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                grid[x, y] = map.Grid[x, y];
            }
        }

        var objects = map.Objects.Select(CloneObject).ToList();
        return new MapEditSnapshot
        {
            Grid = grid,
            Objects = objects
        };
    }

    private MapEditSnapshot CreateSnapshotWithObjectAt(MapProject map, int index, int oldX, int oldY)
    {
        var snapshot = CaptureSnapshot(map);
        if (index >= 0 && index < snapshot.Objects.Count)
        {
            snapshot.Objects[index].GridPosX = oldX;
            snapshot.Objects[index].GridPosY = oldY;
        }

        return snapshot;
    }

    private void ApplySnapshot(MapProject map, MapEditSnapshot snapshot)
    {
        _suppressHistory = true;
        try
        {
            map.Grid = snapshot.Grid;
            map.Objects = snapshot.Objects.Select(CloneObject).ToList();
            DetermineTerrainHashes(map, out _landHash, out _waterHash);
            _paintLandHash = _landHash;
            BuildTileColorMap(map);
            _mapCanvas.SetTileColorMap(_tileColorByHash);
            RefreshFloorLegend(map);
            RefreshPaintTileTypeOptions(map);
            RefreshObjectList(map, -1);
            _mapCanvas.SetMap(map, ResolveObjectName);
            _mapCanvas.SetTerrainHashes(_paintLandHash, _waterHash);
            UpdateViewerInfoText(map.Name);
        }
        finally
        {
            _suppressHistory = false;
        }
    }

    private void UpdateViewerInfoText(string fallbackMapName)
    {
        if (_mapSavModeActive && !string.IsNullOrWhiteSpace(_mapSavFilePath))
        {
            _viewerInfoText.Text = Path.GetFileName(_mapSavFilePath);
            return;
        }

        _viewerInfoText.Text = string.IsNullOrWhiteSpace(fallbackMapName) ? "Map.sav" : fallbackMapName;
    }

    private static MapObjectEntry CloneObject(MapObjectEntry entry)
    {
        return new MapObjectEntry
        {
            Id = entry.Id,
            Hash = entry.Hash,
            GridPosX = entry.GridPosX,
            GridPosY = entry.GridPosY,
            RotY = entry.RotY
        };
    }

    private static void DetermineTerrainHashes(MapProject map, out uint landHash, out uint waterHash)
    {
        var counts = new Dictionary<uint, int>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var hash = map.Grid[x, y];
                counts[hash] = counts.TryGetValue(hash, out var n) ? n + 1 : 1;
            }
        }

        if (counts.Count == 0)
        {
            landHash = 0;
            waterHash = 0;
            return;
        }

        waterHash = counts.ContainsKey(3525010870u)
            ? 3525010870u
            : counts.OrderByDescending(kv => kv.Value).First().Key;

        var water = waterHash;
        landHash = counts
            .Where(kv => kv.Key != water)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault(water);
    }

    private void RefreshPaintTileTypeOptions(MapProject map)
    {
        var counts = new Dictionary<uint, int>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var hash = map.Grid[x, y];
                counts[hash] = counts.TryGetValue(hash, out var n) ? n + 1 : 1;
            }
        }

        foreach (var hash in GetKnownTileHashes())
        {
            if (!counts.ContainsKey(hash))
            {
                counts[hash] = 0;
            }
        }

        foreach (var hash in _floorNameLookup.Keys)
        {
            if (!counts.ContainsKey(hash))
            {
                counts[hash] = 0;
            }
        }

        var options = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => ResolveFloorName(kv.Key), StringComparer.OrdinalIgnoreCase)
            .Select(kv => new PaintTileOption
            {
                Hash = kv.Key,
                Name = ResolveFloorName(kv.Key),
                Count = kv.Value
            })
            .ToList();

        _isUpdatingPaintTileTypeCombo = true;
        try
        {
            _paintTileTypeCombo.ItemsSource = options;
            _paintTileTypeCombo.IsEnabled = options.Count > 0;
            if (options.Count == 0)
            {
                return;
            }

            var selected = options.FirstOrDefault(o => o.Hash == _paintLandHash)
                ?? options.FirstOrDefault(o => o.Hash == _landHash)
                ?? options[0];

            _paintLandHash = selected.Hash;
            _paintTileTypeCombo.SelectedItem = selected;
        }
        finally
        {
            _isUpdatingPaintTileTypeCombo = false;
        }

        _mapCanvas.SetTerrainHashes(_paintLandHash, _waterHash);
    }

    private void PaintTileTypeComboOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPaintTileTypeCombo || _currentMap is null || _paintTileTypeCombo.SelectedItem is not PaintTileOption option)
        {
            return;
        }

        _paintLandHash = option.Hash;
        _mapCanvas.SetTerrainHashes(_paintLandHash, _waterHash);
        if (_mapCanvas.EditMode == MapEditMode.AddLand)
        {
            _statusText.Text = $"Add tile mode enabled (hash {_paintLandHash}). Drag on tiles to paint.";
        }
    }

    private void RefreshFloorLegend(MapProject map)
    {
        _floorLegendPanel.Children.Clear();
        var counts = new Dictionary<uint, int>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var hash = map.Grid[x, y];
                counts[hash] = counts.TryGetValue(hash, out var n) ? n + 1 : 1;
            }
        }

        foreach (var kv in counts.OrderByDescending(k => k.Value).Take(14))
        {
            var floorHash = kv.Key;
            var floorName = ResolveFloorName(floorHash);
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            row.PointerPressed += async (_, _) => await CopyFloorHashToClipboardAsync(floorHash, floorName);

            row.Children.Add(new Border
            {
                Width = 12,
                Height = 12,
                Background = new SolidColorBrush(GetTileColor(floorHash)),
                BorderBrush = new SolidColorBrush(Color.Parse("#FF6A6A6A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });

            row.Children.Add(new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#FFE6E6E6")),
                Text = $"{floorName}  ({floorHash}) x{kv.Value}"
            });

            _floorLegendPanel.Children.Add(row);
        }
    }

    private async Task CopyFloorHashToClipboardAsync(uint hash, string name)
    {
        var top = GetTopLevel(this);
        if (top?.Clipboard is null)
        {
            _statusText.Text = "Clipboard is not available.";
            return;
        }

        await top.Clipboard.SetTextAsync(hash.ToString(CultureInfo.InvariantCulture));
        _statusText.Text = $"Copied {name} tile id: {hash}";
    }

    private void BuildTileColorMap(MapProject map)
    {
        _tileColorByHash.Clear();
        var unique = new HashSet<uint>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                unique.Add(map.Grid[x, y]);
            }
        }

        foreach (var hash in unique)
        {
            _tileColorByHash[hash] = ResolveSemanticTileColor(hash);
        }
    }

    private Color GetTileColor(uint hash)
    {
        return _tileColorByHash.TryGetValue(hash, out var color)
            ? color
            : ResolveSemanticTileColor(hash);
    }

    private string ResolveFloorName(uint hash)
    {
        if (_floorNameLookup.TryGetValue(hash, out var names) && names.Count > 0)
        {
            return names[0].Replace('_', ' ');
        }

        switch (hash)
        {
            case 0x5E52F4DEu:
                return "Path";
            case 0x91DFA1EAu:
                return "Path Road";
            case 0xD7E5E4E0u:
                return "Asphalt";
            case 0xDE578684u:
                return "Asphalt Road";
            case 0xB6D76A62u:
                return "Beach";
            case 0xB53F5F3Du:
                return "Seaside";
            case 0xA27341EDu:
                return "Clover";
            case 0xAFA5B5ABu:
                return "Clover Road";
            case 0xB019EFF9u:
                return "Cobblestone";
            case 0xA442959Eu:
                return "Cobblestone Road";
            case 0xCF83CF1Fu:
                return "Concrete";
            case 0x318904D8u:
                return "Concrete Road";
            case 0x54AE7E98u:
                return "CherryBlossom";
            case 0xFF4AE68Au:
                return "Grass";
            case 0x2EF21057u:
                return "Grass Road";
            case 0xD1B37F49u:
                return "CherryBlossom Road";
            case 0x8A58EB7Du:
                return "FallenLeaves";
            case 0x923CFBD7u:
                return "FallenLeaves Road";
            case 0x3948DC33u:
                return "Gold";
            case 0x8698C8B7u:
                return "Gold Road";
            case 0x1FB9379Du:
                return "Iron";
            case 0x8B39F8D2u:
                return "Iron Road";
            case 0xA4AFD856u:
                return "Pebble";
            case 0xCA11E25Au:
                return "Pebble Road";
            case 0x17EE09E8u:
                return "Room Invalid";
            case 0x9999D173u:
                return "Soil";
            case 0x62F90493u:
                return "Soil Road";
            case 0x122A7D23u:
                return "Sand";
            case 0x2B9B8582u:
                return "Sand Road";
            case 0x47F627BDu:
                return "Snow";
            case 0xE9473287u:
                return "Snow Road";
            case 0x10F7EE55u:
                return "Stone";
            case 0xC67A3C6Cu:
                return "Stone Road";
            case 0xA155274Bu:
                return "Tile";
            case 0xA281DB34u:
                return "Tile Road";
            case 0x69FFF2F1u:
                return "UGC";
            case 0xD21B65B6u:
                return "Water";
            case 0xEB213538u:
                return "Wood";
            case 0x5E35B65Fu:
                return "Wood Road";
        }

        if (hash == _waterHash)
        {
            return "Water";
        }

        if (hash == _landHash)
        {
            return "Land";
        }

        return "Unknown";
    }

    private static IReadOnlyList<uint> GetKnownTileHashes()
    {
        return
        [
            0x5E52F4DEu, 0x91DFA1EAu, 0xD7E5E4E0u, 0xDE578684u, 0xB6D76A62u, 0x54AE7E98u,
            0xD1B37F49u, 0xA27341EDu, 0xAFA5B5ABu, 0xB019EFF9u, 0xA442959Eu, 0xCF83CF1Fu,
            0x318904D8u, 0x8A58EB7Du, 0x923CFBD7u, 0x3948DC33u, 0x8698C8B7u, 0xFF4AE68Au,
            0x2EF21057u, 0x1FB9379Du, 0x8B39F8D2u, 0xA4AFD856u, 0xCA11E25Au, 0x17EE09E8u,
            0x122A7D23u, 0x2B9B8582u, 0xB53F5F3Du, 0x47F627BDu, 0xE9473287u, 0x9999D173u,
            0x62F90493u, 0x10F7EE55u, 0xC67A3C6Cu, 0xA155274Bu, 0xA281DB34u, 0x69FFF2F1u,
            0xD21B65B6u, 0xEB213538u, 0x5E35B65Fu
        ];
    }

    private Color ResolveSemanticTileColor(uint hash)
    {
        switch (hash)
        {
            case 0x5E52F4DEu: return Color.Parse("#A3917A");
            case 0x91DFA1EAu: return Color.Parse("#8D7564");
            case 0xD7E5E4E0u: return Color.Parse("#3C4147");
            case 0xDE578684u: return Color.Parse("#2A2E33");
            case 0xB6D76A62u: return Color.Parse("#E4CC9A");
            case 0xB53F5F3Du: return Color.Parse("#3A6EA7");
            case 0xA27341EDu: return Color.Parse("#5E9E4A");
            case 0xAFA5B5ABu: return Color.Parse("#4A8F56");
            case 0xB019EFF9u: return Color.Parse("#7A8088");
            case 0xA442959Eu: return Color.Parse("#6A7380");
            case 0xCF83CF1Fu: return Color.Parse("#F2F2F2");
            case 0x318904D8u: return Color.Parse("#D0D0D0");
            case 0x54AE7E98u: return Color.Parse("#EFA7C8");
            case 0xD1B37F49u: return Color.Parse("#DB7DB0");
            case 0xFF4AE68Au: return Color.Parse("#67B86A");
            case 0x2EF21057u: return Color.Parse("#4D9B59");
            case 0x8A58EB7Du: return Color.Parse("#B66A3A");
            case 0x923CFBD7u: return Color.Parse("#A15E3A");
            case 0x3948DC33u: return Color.Parse("#D9B23B");
            case 0x8698C8B7u: return Color.Parse("#C19B34");
            case 0x1FB9379Du: return Color.Parse("#8A8F99");
            case 0x8B39F8D2u: return Color.Parse("#6C7483");
            case 0xA4AFD856u: return Color.Parse("#9A958A");
            case 0xCA11E25Au: return Color.Parse("#878072");
            case 0x17EE09E8u: return Color.Parse("#E05A5A");
            case 0x9999D173u: return Color.Parse("#8B5A2B");
            case 0x62F90493u: return Color.Parse("#70502D");
            case 0x122A7D23u: return Color.Parse("#D8C089");
            case 0x2B9B8582u: return Color.Parse("#CCB27D");
            case 0x47F627BDu: return Color.Parse("#E9F3FF");
            case 0xE9473287u: return Color.Parse("#DCE7F2");
            case 0x10F7EE55u: return Color.Parse("#87817A");
            case 0xC67A3C6Cu: return Color.Parse("#7A746D");
            case 0xA155274Bu: return Color.Parse("#B8B5C9");
            case 0xA281DB34u: return Color.Parse("#A9A5BE");
            case 0x69FFF2F1u: return Color.Parse("#C06BFF");
            case 0xD21B65B6u: return Color.Parse("#4A90E2");
            case 0xEB213538u: return Color.Parse("#9B6B3D");
            case 0x5E35B65Fu: return Color.Parse("#8A603B");
        }

        var name = ResolveFloorName(hash).ToLowerInvariant();
        if (hash == _waterHash || name.Contains("water"))
        {
            return Color.Parse("#4A90E2");
        }

        if (name.Contains("seaside"))
        {
            return Color.Parse("#2F5F9E");
        }

        if (name.Contains("beach") || name.Contains("sand"))
        {
            return Color.Parse("#D8C089");
        }

        if (name.Contains("snow"))
        {
            return Color.Parse("#E9F3FF");
        }

        if (name.Contains("road"))
        {
            return Color.Parse("#8C8C8C");
        }

        if (name.Contains("soil") || name.Contains("dirt") || name.Contains("earth") || name.Contains("mud"))
        {
            return Color.Parse("#8B5A2B");
        }

        if (name.Contains("clover"))
        {
            return Color.Parse("#3E7F3A");
        }

        if (hash == _landHash || name.Contains("grass") || name.Contains("land"))
        {
            return Color.Parse("#67B86A");
        }

        return Color.Parse(HashToColor(hash));
    }

    private static string HashToColor(uint value)
    {
        var r = (int)((value >> 16) & 0x7F) + 64;
        var g = (int)((value >> 8) & 0x7F) + 64;
        var b = (int)(value & 0x7F) + 64;
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
