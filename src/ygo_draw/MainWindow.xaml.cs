using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ygo_draw.models;
using ygo_draw.services;
using MessageBox = System.Windows.MessageBox;

namespace ygo_draw;

public partial class MainWindow : Window
{
    private const double CardAspectRatio = 59.0 / 86.0;
    private const double MinMeasuredCardAspectRatio = 0.55;
    private const double MaxMeasuredCardAspectRatio = 0.85;
    private const double ContentColumnGap = 12.0;
    private const double PreviewZoom = 1;

    private readonly ProjectPaths _paths = new();
    private readonly ProcessService _processService = new();
    private readonly DatabaseConnectionSettings _databaseSettings = new();
    private readonly DispatcherTimer _searchTimer;
    private ResourceStatus _lastResourceStatus = new(false, "正在检查资源...");
    private readonly List<CardSummary> _previewHistory = [];
    private int _previewHistoryIndex = -1;
    private bool _isApplyingHistorySelection;
    private CancellationTokenSource? _previewCts;
    private CardCatalogService _catalog = null!;
    private ResourceIntegrityService _integrity = null!;
    private CardRenderService _renderer = null!;
    private bool _isApplyingPreviewColumnWidth;
    private double _previewContentAspectRatio = CardAspectRatio;

    public MainWindow()
    {
        InitializeComponent();
        SearchBox.Text = string.Empty;
        _searchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplySearch();
        };
        UpdatePreviewHistoryButtons();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_paths.AppIconPath))
        {
            Icon = new BitmapImage(new Uri(_paths.AppIconPath));
        }

        _catalog = new CardCatalogService(_databaseSettings);
        _integrity = new ResourceIntegrityService(_paths);
        _renderer = CreateRenderer();
        await RefreshResourcesAndCardsAsync();
    }

    private async Task RefreshResourcesAndCardsAsync()
    {
        _lastResourceStatus = _integrity.Check();
        StatusText.Text = _lastResourceStatus.Message;
        if (!_lastResourceStatus.IsReady)
        {
            MessageBox.Show(this, _lastResourceStatus.Message, "资源需要更新", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        try
        {
            await _catalog.LoadAsync();
            ApplySearch();
        }
        catch (Exception ex)
        {
            ResultsList.ItemsSource = Array.Empty<CardSummary>();
            StatusText.Text = "无法从数据库加载卡片，请检查 PostgreSQL 连接或先执行下载导入。";
            MessageBox.Show(this, ex.Message, "数据库连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_catalog is not null)
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    private void ApplySearch()
    {
        var results = _catalog.Search(SearchBox.Text).ToList();
        ResultsList.ItemsSource = results;
        StatusText.Text = $"{(_lastResourceStatus.IsReady ? "资源已就绪" : "资源不完整")}，已载入 {_catalog.Cards.Count} 张卡，显示 {results.Count} 条结果";
    }

    private void ResultsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not CardSummary card)
        {
            PreviewText.Text = "请选择一张卡";
            return;
        }

        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewText.Visibility = Visibility.Visible;
        PreviewText.Text = "预览渲染待接入";

        if (ResultsList.IsKeyboardFocusWithin)
        {
            _ = PreviewCardAsync(card, addToHistory: true);
        }
    }

    private async void ResultsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is not CardSummary card)
        {
            return;
        }

        await PreviewCardAsync(card, addToHistory: true);
    }

    private async Task PreviewCardAsync(CardSummary card, bool addToHistory)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var cancellationToken = _previewCts.Token;

        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewText.Visibility = Visibility.Visible;
        PreviewText.Text = "正在渲染预览...";
        StatusText.Text = $"正在渲染 {card.Name}";

        try
        {
            await _renderer.RenderPreviewAsync(
                card,
                line => Dispatcher.BeginInvoke(() => StatusText.Text = line),
                cancellationToken);
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Collapsed;
            PreviewBrowser.Visibility = Visibility.Visible;
            await PreviewBrowser.EnsureCoreWebView2Async();
            PreviewBrowser.ZoomFactor = PreviewZoom;
            await Task.Delay(350);
            PreviewBrowser.CoreWebView2.Navigate(TinymistPreviewService.PreviewUrl);
            await Task.Delay(700);
            PreviewBrowser.ZoomFactor = PreviewZoom;
            await ApplyPreviewPageChromeFixAsync();
            await TryUpdatePreviewContentAspectRatioAsync();
            UpdatePreviewColumnWidth();
            StatusText.Text = $"已启动预览 {card.Name}";
            if (addToHistory && !_isApplyingHistorySelection)
            {
                AddPreviewHistory(card);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Visible;
            PreviewText.Text = "预览渲染失败";
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "预览渲染失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ApplyPreviewPageChromeFixAsync()
    {
        if (PreviewBrowser.CoreWebView2 is null)
        {
            return;
        }

        const string script = """
            (() => {
                let style = document.getElementById("ygo-draw-preview-fit");
                if (!style) {
                    style = document.createElement("style");
                    style.id = "ygo-draw-preview-fit";
                    document.head.appendChild(style);
                }
                style.textContent = `
                    html, body {
                        margin: 0 !important;
                        padding: 0 !important;
                    }
                `;
                window.scrollTo(0, 0);
            })();
            """;
        await PreviewBrowser.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async Task TryUpdatePreviewContentAspectRatioAsync()
    {
        if (PreviewBrowser.CoreWebView2 is null)
        {
            return;
        }

        const string script = """
            (() => {
                const rectOf = node => {
                    const rect = node.getBoundingClientRect();
                    return {
                        width: rect.width,
                        height: rect.height,
                        ratio: rect.width / Math.max(rect.height, 1),
                        area: rect.width * rect.height
                    };
                };
                const explicitNodes = Array.from(document.querySelectorAll(
                    "svg, canvas, img, .typst-page, [data-page]"
                ));
                const explicit = explicitNodes
                    .map(rectOf)
                    .filter(item => item.width > 40 && item.height > 40)
                    .filter(item => item.ratio > 0.5 && item.ratio < 0.9)
                    .sort((left, right) => right.area - left.area);
                const fallback = Array.from(document.body.children)
                    .map(rectOf)
                    .filter(item => item.width > 40 && item.height > 40)
                    .filter(item => item.ratio > 0.5 && item.ratio < 0.9)
                    .sort((left, right) => right.area - left.area);
                const item = explicit[0] || fallback[0];
                if (item) {
                    return { width: item.width, height: item.height };
                }
                const scroll = document.scrollingElement || document.documentElement;
                return {
                    width: scroll.scrollWidth,
                    height: scroll.scrollHeight
                };
            })()
            """;

        try
        {
            var raw = await PreviewBrowser.CoreWebView2.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(raw) || raw == "null")
            {
                return;
            }

            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var width = root.GetProperty("width").GetDouble();
            var height = root.GetProperty("height").GetDouble();
            var aspectRatio = width / height;
            if (aspectRatio is >= MinMeasuredCardAspectRatio and <= MaxMeasuredCardAspectRatio)
            {
                _previewContentAspectRatio = aspectRatio;
            }
        }
        catch
        {
        }
    }

    private void MainContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewColumnWidth();
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewColumnWidth();
    }

    private void UpdatePreviewColumnWidth()
    {
        if (_isApplyingPreviewColumnWidth)
        {
            return;
        }

        var viewportHeight = PreviewViewport.ActualHeight;
        var totalWidth = MainContentGrid.ActualWidth;
        if (viewportHeight <= 0 || totalWidth <= 0)
        {
            return;
        }

        var maxPreviewWidth = Math.Max(
            1.0,
            totalWidth - ResultsColumn.MinWidth - ContentColumnGap);
        var desiredPreviewWidth = Math.Ceiling(viewportHeight * _previewContentAspectRatio);
        var previewWidth = Math.Min(desiredPreviewWidth, maxPreviewWidth);

        if (Math.Abs(PreviewColumn.ActualWidth - previewWidth) < 0.5)
        {
            return;
        }

        try
        {
            _isApplyingPreviewColumnWidth = true;
            PreviewColumn.Width = new GridLength(previewWidth, GridUnitType.Pixel);
        }
        finally
        {
            _isApplyingPreviewColumnWidth = false;
        }
    }

    private void AddPreviewHistory(CardSummary card)
    {
        if (_previewHistoryIndex >= 0 &&
            _previewHistoryIndex < _previewHistory.Count &&
            SameCard(_previewHistory[_previewHistoryIndex], card))
        {
            return;
        }

        if (_previewHistoryIndex < _previewHistory.Count - 1)
        {
            _previewHistory.RemoveRange(_previewHistoryIndex + 1, _previewHistory.Count - _previewHistoryIndex - 1);
        }

        _previewHistory.Add(card);
        _previewHistoryIndex = _previewHistory.Count - 1;
        UpdatePreviewHistoryButtons();
    }

    private async Task NavigatePreviewHistoryAsync(int offset)
    {
        var nextIndex = _previewHistoryIndex + offset;
        if (nextIndex < 0 || nextIndex >= _previewHistory.Count)
        {
            return;
        }

        _previewHistoryIndex = nextIndex;
        UpdatePreviewHistoryButtons();
        var card = _previewHistory[_previewHistoryIndex];

        _isApplyingHistorySelection = true;
        var listItem = ResultsList.Items
            .OfType<CardSummary>()
            .FirstOrDefault(item => SameCard(item, card));
        if (listItem is not null)
        {
            ResultsList.SelectedItem = listItem;
            ResultsList.ScrollIntoView(listItem);
        }
        _isApplyingHistorySelection = false;

        await PreviewCardAsync(card, addToHistory: false);
    }

    private void UpdatePreviewHistoryButtons()
    {
        PreviewBackButton.IsEnabled = _previewHistoryIndex > 0;
        PreviewForwardButton.IsEnabled = _previewHistoryIndex >= 0 && _previewHistoryIndex < _previewHistory.Count - 1;
        PreviewHistoryText.Text = _previewHistoryIndex >= 0
            ? $"{_previewHistoryIndex + 1}/{_previewHistory.Count}"
            : string.Empty;
    }

    private async void PreviewBackButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigatePreviewHistoryAsync(-1);
    }

    private async void PreviewForwardButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigatePreviewHistoryAsync(1);
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            e.Handled = true;
            await NavigatePreviewHistoryAsync(-1);
            return;
        }

        if (e.Key == Key.Right)
        {
            e.Handled = true;
            await NavigatePreviewHistoryAsync(1);
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Project: https://github.com/arshtyi/ygo_draw\nMore: https://github.com/arshtyi/my-ygo",
            "关于",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DownloadDialog(_paths, _processService, _databaseSettings) { Owner = this };
        var result = dialog.ShowDialog();
        if (result == true)
        {
            await RefreshResourcesAndCardsAsync();
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExportDialog(_paths.ProjectRoot) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var cards = GetExportCards(dialog.Scope);
        if (cards.Count == 0)
        {
            MessageBox.Show(this, "没有可导出的卡片。", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _ = ExportCardsAsync(cards, dialog.OutputDirectory);
    }

    private IReadOnlyList<CardSummary> GetExportCards(ExportScope scope)
    {
        return scope switch
        {
            ExportScope.Current => _previewHistoryIndex >= 0 && _previewHistoryIndex < _previewHistory.Count
                ? [_previewHistory[_previewHistoryIndex]]
                : ResultsList.SelectedItem is CardSummary selected
                    ? [selected]
                    : [],
            ExportScope.History => _previewHistory
                .GroupBy(CardIdentity)
                .Select(group => group.Last())
                .ToList(),
            ExportScope.List => ResultsList.Items
                .OfType<CardSummary>()
                .GroupBy(CardIdentity)
                .Select(group => group.First())
                .ToList(),
            _ => []
        };
    }

    private async Task ExportCardsAsync(IReadOnlyList<CardSummary> cards, string outputDirectory)
    {
        IsEnabled = false;
        var completed = 0;
        var failed = 0;
        var maxParallelism = cards.Count <= 1 ? 1 : 2;
        using var throttler = new SemaphoreSlim(maxParallelism);

        try
        {
            var tasks = cards.Select(async card =>
            {
                await throttler.WaitAsync();
                try
                {
                    await _renderer.ExportAsync(
                        card,
                        outputDirectory,
                        line => _ = Dispatcher.BeginInvoke(() => StatusText.Text = line));
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    _ = Dispatcher.BeginInvoke(() => StatusText.Text = ex.Message);
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    _ = Dispatcher.BeginInvoke(() => StatusText.Text = $"导出进度 {done}/{cards.Count}");
                    throttler.Release();
                }
            });

            await Task.WhenAll(tasks);
            MessageBox.Show(
                this,
                failed == 0
                    ? $"Export completed: {cards.Count} file(s)."
                    : $"Export completed with {failed} failure(s).",
                "导出",
                MessageBoxButton.OK,
                failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
            StatusText.Text = failed == 0 ? $"导出完成: {cards.Count}" : $"导出完成，失败 {failed}";
        }
    }

    private async void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "This will delete .cache/, assets/, and lib/. Downloaded resources, generated previews, card images, and typst-ygo code will be removed.",
            "Confirm cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        IsEnabled = false;
        StatusText.Text = "正在清理资源...";
        try
        {
            _renderer?.Dispose();
            _renderer = CreateRenderer();
            PreviewBrowser.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Visible;
            PreviewText.Text = "资源已清理";

            await Task.Run(() =>
            {
                DeleteDirectoryIfExists(_paths.CacheRoot);
                DeleteDirectoryIfExists(_paths.AssetsRoot);
                DeleteDirectoryIfExists(_paths.TypstLibRoot);
                Directory.CreateDirectory(_paths.CacheRoot);
                Directory.CreateDirectory(_paths.AssetsRoot);
            });

            _lastResourceStatus = new ResourceStatus(false, "资源已清理，请点击“下载”重新获取。");
            StatusText.Text = _lastResourceStatus.Message;
            ResultsList.ItemsSource = Array.Empty<CardSummary>();
            MessageBox.Show(this, "Cleanup completed.", "Cleanup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Cleanup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _renderer?.Dispose();
    }

    private CardRenderService CreateRenderer()
    {
        var imageService = new CardImageService(_paths, _processService);
        var previewService = new TinymistPreviewService(_paths);
        return new CardRenderService(_paths, _processService, imageService, previewService);
    }

    private static string CardIdentity(CardSummary card)
    {
        return $"{card.Series}:{card.Id}";
    }

    private static bool SameCard(CardSummary left, CardSummary right)
    {
        return string.Equals(left.Series, right.Series, StringComparison.Ordinal) &&
               string.Equals(left.Id, right.Id, StringComparison.Ordinal);
    }
}
