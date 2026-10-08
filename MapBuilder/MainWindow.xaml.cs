using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MapCore.Models;
using MapCore.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MapBuilder;

public partial class MainWindow : Window
{
    private MapProject _project = new();
    private readonly List<string> _panoramaFiles = new();
    private readonly List<System.Windows.Point> _renderPoints = new();
    private MapMarker? _selectedMarker;
    private string? _temporaryMapHtmlPath;
    private bool _webMessageHandlerAttached;
    private bool _isPlacingMarker;

    public MainWindow()
    {
        InitializeComponent();
        MarkerList.ItemsSource = _project.Markers;
        MapCanvas.SizeChanged += (_, _) => RenderMap();
        Loaded += (_, _) => RenderMap();
        MapWebView.NavigationCompleted += (_, args) =>
        {
            StatusText.Text = args.IsSuccess
                ? $"HTML-карту відкрито: {_project.Name}. Натисніть «Додати точку», потім клацніть потрібне місце."
                : "HTML-карта не завантажилась. Перевірте її файл і наявність WebView2 Runtime.";
        };
        Closed += (_, _) =>
        {
            if (_temporaryMapHtmlPath is not null)
            {
                try
                {
                    System.IO.File.Delete(_temporaryMapHtmlPath);
                }
                catch
                {
                }
            }
        };
        StatusText.Text = "Готово до роботи. Завантажте .conf або створіть нову карту.";
    }

    private async void LoadConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Файли карти та конфігурації (*.html;*.htm;*.txt;*.conf)|*.html;*.htm;*.txt;*.conf|Усі файли (*.*)|*.*",
            Title = "Виберіть HTML-карту або конфігурацію"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var extension = System.IO.Path.GetExtension(dialog.FileName);
        var isHtmlFile = extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                         || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
        var fileContent = System.IO.File.ReadAllText(dialog.FileName);
        var containsMapData = fileContent?.Contains("window.MAPDATA", StringComparison.OrdinalIgnoreCase) == true;

        if (isHtmlFile || containsMapData)
        {
            _project = new MapProject
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
                RootPath = System.IO.Path.GetDirectoryName(dialog.FileName) ?? string.Empty,
                SourceFilePath = System.IO.Path.GetFullPath(dialog.FileName)
            };
            _project.SourceConfig["source_type"] = "html_map_data";
            MarkerList.ItemsSource = _project.Markers;
            MapCanvas.Visibility = Visibility.Collapsed;
            MapWebView.Visibility = Visibility.Visible;
            StatusText.Text = "Відкриваю HTML-карту та її вбудовані тайли...";

            try
            {
                await MapWebView.EnsureCoreWebView2Async();
                if (!_webMessageHandlerAttached)
                {
                    MapWebView.CoreWebView2.WebMessageReceived += MapWebView_WebMessageReceived;
                    _webMessageHandlerAttached = true;
                }

                if (containsMapData)
                {
                    var sourceDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(dialog.FileName))
                                          ?? Environment.CurrentDirectory;
                    var baseUri = new Uri(sourceDirectory + System.IO.Path.DirectorySeparatorChar).AbsoluteUri;
                    var htmlContent = fileContent!;
                    var headStart = htmlContent.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
                    var headEnd = headStart >= 0 ? htmlContent.IndexOf('>', headStart) : -1;
                    if (headEnd >= 0 && !htmlContent.Contains("<base", StringComparison.OrdinalIgnoreCase))
                    {
                        htmlContent = htmlContent.Insert(headEnd + 1, $"<base href=\"{baseUri}\">");
                    }

                    htmlContent = AddMapBuilderBridge(htmlContent);
                    var temporaryDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MapBuilder");
                    System.IO.Directory.CreateDirectory(temporaryDirectory);
                    _temporaryMapHtmlPath = System.IO.Path.Combine(temporaryDirectory, $"map-{Environment.ProcessId}.html");
                    await System.IO.File.WriteAllTextAsync(_temporaryMapHtmlPath, htmlContent);
                    MapWebView.CoreWebView2.Navigate(new Uri(_temporaryMapHtmlPath).AbsoluteUri);
                }
                else
                {
                    MapWebView.CoreWebView2.Navigate(new Uri(System.IO.Path.GetFullPath(dialog.FileName)).AbsoluteUri);
                }
            }
            catch (Exception ex)
            {
                MapWebView.Visibility = Visibility.Collapsed;
                MapCanvas.Visibility = Visibility.Visible;
                StatusText.Text = $"Не вдалося запустити HTML-карту: {ex.Message}";
            }

            return;
        }

        MapWebView.Visibility = Visibility.Collapsed;
        MapCanvas.Visibility = Visibility.Visible;
        _project = MapConfigParser.Parse(dialog.FileName);
        _project.Layers.Add(new MapLayer { Name = "Основний шар", Color = "#60a5fa" });

        if (_project.Markers.Count == 0)
        {
            _project.Markers.Add(new MapMarker { Name = "Старт", X = 0, Y = 0, Note = "Початкова точка" });
        }

        MarkerList.ItemsSource = _project.Markers;
        RenderMap();
        var tileCount = _project.SourceConfig.Keys.Count(key => key.StartsWith("hmap3_", StringComparison.OrdinalIgnoreCase));
        StatusText.Text = tileCount > 0
            ? $"Конфіг завантажено: {_project.Name}. Знайдено {tileCount} блоків hmap3; відображення цих блоків на карті ще не реалізоване."
            : $"Конфіг завантажено: {_project.Name}. У ньому не знайдено блоків hmap3; відкрийте HTML-експорт карти.";
    }

    private void BuildMap_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_project.Name))
        {
            _project = new MapProject { Name = "Карта" };
        }

        if (_project.Markers.Count == 0)
        {
            _project.Markers.Add(new MapMarker { Name = "Карта", X = 0, Y = 0, Note = "Стандартна точка" });
        }

        RenderMap();
        StatusText.Text = $"Карта зібрана. Всього маркерів: {_project.Markers.Count}. Панорам: {_panoramaFiles.Count}.";
    }

    private async void AddMarker_Click(object sender, RoutedEventArgs e)
    {
        if (MapWebView.Visibility == Visibility.Visible)
        {
            _isPlacingMarker = true;
            await SetHtmlPlacementModeAsync(true);
            StatusText.Text = "Режим додавання точки: клацніть її місце на карті.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(MarkerNameText.Text) ? "Marker" : MarkerNameText.Text.Trim();
        var marker = new MapMarker
        {
            Name = name,
            X = _renderPoints.Count > 0 ? _renderPoints[^1].X / 10 : 0,
            Y = _renderPoints.Count > 0 ? _renderPoints[^1].Y / 10 : 0,
            Note = MarkerNoteText.Text,
            Heading = 0,
            Pitch = 0
        };

        _project.Markers.Add(marker);
        MarkerList.ItemsSource = null;
        MarkerList.ItemsSource = _project.Markers;
        RenderMap();
    }

    private void LoadPanoramaFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All files (*.*)|*.*",
            Multiselect = true,
            Title = "Виберіть 22 фото для панорами 360"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _panoramaFiles.Clear();
        _panoramaFiles.AddRange(dialog.FileNames);
        _project.PanoramaFiles = _panoramaFiles.ToList();

        StatusText.Text = $"Завантажено {_panoramaFiles.Count} фото. Для повної панорами рекомендується 22 знімки.";
    }

    private void ExportProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Виберіть папку для експорту карти"
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        ExportService.ExportProject(_project, dialog.SelectedPath);
        StatusText.Text = $"Експорт завершено: {dialog.SelectedPath}";
    }

    private async void GitHubPublish_Click(object sender, RoutedEventArgs e)
    {
        var owner = new System.Windows.Controls.TextBox();
        var repository = new System.Windows.Controls.TextBox();
        var branch = new System.Windows.Controls.TextBox { Text = "main" };
        var remoteDirectory = new System.Windows.Controls.TextBox { Text = $"maps/{_project.Id[..8]}" };
        var commitMessage = new System.Windows.Controls.TextBox { Text = $"Update map: {_project.Name}" };
        var token = new PasswordBox();
        var form = new StackPanel { Margin = new Thickness(20), Width = 430 };

        void AddField(string label, System.Windows.Controls.Control control)
        {
            form.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
            control.Height = 30;
            form.Children.Add(control);
        }

        AddField("Власник GitHub (користувач або організація)", owner);
        AddField("Назва репозиторію", repository);
        AddField("Гілка (залиште порожнім для гілки за замовчуванням)", branch);
        AddField("Папка в репозиторії", remoteDirectory);
        AddField("Повідомлення коміту", commitMessage);
        AddField("Fine-grained token із Contents: Read and write", token);
        form.Children.Add(new TextBlock
        {
            Text = "Token використовується лише для цієї публікації й не зберігається.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancelButton = new System.Windows.Controls.Button { Content = "Скасувати", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var publishButton = new System.Windows.Controls.Button { Content = "Опублікувати", MinWidth = 110, IsDefault = true };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(publishButton);
        form.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Публікація карти на GitHub",
            Content = form,
            SizeToContent = SizeToContent.Height,
            Width = 480,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;
        publishButton.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true)
        {
            token.Clear();
            return;
        }

        if (string.IsNullOrWhiteSpace(owner.Text)
            || string.IsNullOrWhiteSpace(repository.Text)
            || string.IsNullOrWhiteSpace(token.Password))
        {
            System.Windows.MessageBox.Show(this, "Заповніть власника, репозиторій і token.", "Не всі поля заповнені");
            token.Clear();
            return;
        }

        var stagingDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "MapBuilder", $"publish-{Guid.NewGuid():N}");
        var progress = new Progress<string>(message => StatusText.Text = message);
        try
        {
            _project.PanoramaFiles = _panoramaFiles.ToList();
            ExportService.ExportProject(_project, stagingDirectory);
            StatusText.Text = "Підготовка файлів для GitHub...";
            var publisher = new GitHubPublisher();
            await publisher.PublishDirectoryAsync(
                owner.Text.Trim(),
                repository.Text.Trim(),
                branch.Text.Trim(),
                remoteDirectory.Text.Trim(),
                string.IsNullOrWhiteSpace(commitMessage.Text) ? $"Update map: {_project.Name}" : commitMessage.Text.Trim(),
                token.Password.Trim(),
                stagingDirectory,
                progress);

            StatusText.Text = "Карту опубліковано на GitHub.";
            System.Windows.MessageBox.Show(this, "Файли карти успішно відправлені в GitHub.", "Публікацію завершено");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Помилка публікації: {ex.Message}";
            System.Windows.MessageBox.Show(this, ex.Message, "Не вдалося опублікувати карту");
        }
        finally
        {
            token.Clear();
            try
            {
                if (System.IO.Directory.Exists(stagingDirectory))
                {
                    System.IO.Directory.Delete(stagingDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MapCanvas);

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var hitMarker = FindMarkerAtPosition(pos);
            if (hitMarker != null)
            {
                DeleteMarker(hitMarker);
                return;
            }
        }

        var marker = new MapMarker
        {
            Name = string.IsNullOrWhiteSpace(MarkerNameText.Text) ? "Точка" : MarkerNameText.Text.Trim(),
            X = pos.X / 20,
            Y = pos.Y / 20,
            Note = MarkerNoteText.Text,
            Heading = 0,
            Pitch = 0
        };

        _project.Markers.Add(marker);
        MarkerList.ItemsSource = null;
        MarkerList.ItemsSource = _project.Markers;
        RenderMap();
    }

    private void MarkerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMarker = MarkerList.SelectedItem as MapMarker;
        if (_selectedMarker != null)
        {
            StatusText.Text = $"Вибрано точку: {_selectedMarker.Name} ({_selectedMarker.X}, {_selectedMarker.Y})";
        }
    }

    private void DeleteSelectedMarker_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is not MapMarker selected)
        {
            StatusText.Text = "Спочатку виберіть точку зі списку або клацніть по ній з Ctrl.";
            return;
        }

        DeleteMarker(selected);
    }

    private void DeleteMarker(MapMarker marker)
    {
        if (_project.Markers.Remove(marker))
        {
            _selectedMarker = null;
            MarkerList.SelectedIndex = -1;
            MarkerList.ItemsSource = null;
            MarkerList.ItemsSource = _project.Markers;
            if (MapWebView.Visibility == Visibility.Visible)
            {
                _ = RefreshHtmlMarkersAsync();
            }
            else
            {
                RenderMap();
            }

            StatusText.Text = $"Точка видалена: {marker.Name}";
        }
    }

    private async void MapWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "mapReady")
            {
                await RefreshHtmlMarkersAsync();
                await SetHtmlPlacementModeAsync(_isPlacingMarker);
                return;
            }

            if (type != "mapClick" || !_isPlacingMarker)
            {
                return;
            }

            var marker = new MapMarker
            {
                Name = string.IsNullOrWhiteSpace(MarkerNameText.Text) ? "Точка" : MarkerNameText.Text.Trim(),
                X = root.GetProperty("x").GetDouble(),
                Y = root.GetProperty("z").GetDouble(),
                Note = MarkerNoteText.Text
            };
            _project.Markers.Add(marker);
            MarkerList.ItemsSource = null;
            MarkerList.ItemsSource = _project.Markers;
            _isPlacingMarker = false;
            await RefreshHtmlMarkersAsync();
            await SetHtmlPlacementModeAsync(false);
            StatusText.Text = $"Точку «{marker.Name}» додано: X={marker.X}, Z={marker.Y}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Не вдалося обробити дію на карті: {ex.Message}";
        }
    }

    private async Task RefreshHtmlMarkersAsync()
    {
        if (MapWebView.CoreWebView2 is null || MapWebView.Visibility != Visibility.Visible)
        {
            return;
        }

        var markers = _project.Markers.Select(marker => new
        {
            name = marker.Name,
            x = marker.X,
            z = marker.Y
        });
        var markersJson = JsonSerializer.Serialize(markers);
        await MapWebView.ExecuteScriptAsync($"window.mapBuilderSetMarkers({markersJson});");
    }

    private async Task SetHtmlPlacementModeAsync(bool enabled)
    {
        if (MapWebView.CoreWebView2 is null || MapWebView.Visibility != Visibility.Visible)
        {
            return;
        }

        await MapWebView.ExecuteScriptAsync($"window.mapBuilderSetPlacing({enabled.ToString().ToLowerInvariant()});");
    }

    private static string AddMapBuilderBridge(string htmlContent)
    {
        const string startup = "window.addEventListener('resize',size);size();fit();";
        var insertionPoint = htmlContent.LastIndexOf(startup, StringComparison.Ordinal);
        if (insertionPoint < 0)
        {
            throw new System.IO.InvalidDataException("HTML-карта не містить очікуваного коду mapV3.");
        }

        const string bridge = """
            let mapBuilderPlacing = false;
            const mapBuilderMarkers = [];
            const mapBuilderDraw = draw;
            draw = function() {
                mapBuilderDraw();
                ctx.save();
                for (const marker of mapBuilderMarkers) {
                    const x = sx(marker.x);
                    const y = sy(marker.z + 1);
                    ctx.beginPath();
                    ctx.arc(x, y, 8, 0, Math.PI * 2);
                    ctx.fillStyle = '#e5484d';
                    ctx.fill();
                    ctx.lineWidth = 2;
                    ctx.strokeStyle = '#fff';
                    ctx.stroke();
                    label(marker.name, x + 12, y - 10);
                }
                ctx.restore();
            };
            window.mapBuilderSetMarkers = markers => {
                mapBuilderMarkers.splice(0, mapBuilderMarkers.length, ...markers);
                req();
            };
            window.mapBuilderSetPlacing = enabled => {
                mapBuilderPlacing = enabled;
                cv.style.cursor = enabled ? 'crosshair' : '';
            };
            cv.addEventListener('click', event => {
                if (!mapBuilderPlacing) return;
                mapBuilderPlacing = false;
                const point = pos(event);
                window.chrome.webview.postMessage({
                    type: 'mapClick',
                    x: Math.floor(bx(point.x)),
                    z: Math.floor(bz(point.y))
                });
            });
            window.chrome.webview.postMessage({type: 'mapReady'});
            """;

        return htmlContent.Insert(insertionPoint, bridge);
    }

    private MapMarker? FindMarkerAtPosition(System.Windows.Point point)
    {
        foreach (var marker in _project.Markers)
        {
            var x = Math.Max(20, Math.Min(MapCanvas.ActualWidth - 20, 160 + marker.X * 20));
            var y = Math.Max(20, Math.Min(MapCanvas.ActualHeight - 20, 120 + marker.Y * 20));
            var dx = point.X - x;
            var dy = point.Y - y;
            if (dx * dx + dy * dy <= 225)
            {
                return marker;
            }
        }

        return null;
    }

    private void MapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderMap();
    }

    private void RenderMap()
    {
        if (MapCanvas.ActualWidth <= 0 || MapCanvas.ActualHeight <= 0)
        {
            return;
        }

        MapCanvas.Children.Clear();
        _renderPoints.Clear();

        var mapWidth = MapCanvas.ActualWidth;
        var mapHeight = MapCanvas.ActualHeight;
        var pad = 20d;
        var worldW = Math.Max(1, _project.Bounds.MaxX - _project.Bounds.MinX);
        var worldH = Math.Max(1, _project.Bounds.MaxY - _project.Bounds.MinY);

        foreach (var marker in _project.Markers)
        {
            var x = pad + (marker.X - _project.Bounds.MinX) / worldW * (mapWidth - pad * 2);
            var y = pad + (1 - (marker.Y - _project.Bounds.MinY) / worldH) * (mapHeight - pad * 2);
            x = Math.Max(20, Math.Min(mapWidth - 20, x));
            y = Math.Max(20, Math.Min(mapHeight - 20, y));
            _renderPoints.Add(new System.Windows.Point(x, y));

            var ellipse = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248)),
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 2
            };

            Canvas.SetLeft(ellipse, x - 7);
            Canvas.SetTop(ellipse, y - 7);
            MapCanvas.Children.Add(ellipse);

            var label = new TextBlock
            {
                Text = marker.Name,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                Margin = new Thickness(0)
            };
            Canvas.SetLeft(label, x + 10);
            Canvas.SetTop(label, y - 12);
            MapCanvas.Children.Add(label);
        }
    }
}
