using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using HttpMonitor.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HttpMonitor;

public partial class MainWindow : Window
{
    private const int MaxLogs = 500;
    private const int MaxLogFileSize = 10 * 1024 * 1024; // 10 МБ
    private const int HttpTimeoutSeconds = 30;
    private const int MaxRequestBodySize = 1024 * 1024; // 1 МБ
    private const int MaxUrlLength = 2048;
    private const int ChartMinuteBuckets = 30;
    private const int ChartHourBuckets = 24;

    private HttpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private DateTime _serverStartTime;

    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    })
    {
        Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds)
    };

    private readonly ObservableCollection<string> _logsDisplay = new();
    private readonly List<RequestLogEntry> _allLogs = new();
    private readonly List<RequestLogEntry> _filteredLogs = new();
    private readonly ConcurrentDictionary<string, StoredMessage> _messages = new();
    private readonly object _statsLock = new();

    private int _getCount;
    private int _postCount;
    private long _totalElapsedMs;
    private int _totalRequests;

    private readonly ConcurrentDictionary<long, int> _minuteBuckets = new();
    private readonly ConcurrentDictionary<long, int> _hourBuckets = new();

    private DispatcherTimer? _uiTimer;

    public MainWindow()
    {
        InitializeComponent();
        LogsList.ItemsSource = _logsDisplay;
        ChartMode.SelectionChanged += (_, _) => DrawChart();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) =>
        {
            UpdateUptime();
            DrawChart();
        };
        _uiTimer.Start();
    }


    private void StartServer_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var portText = ServerPortBox.Text?.Trim() ?? string.Empty;

        if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
        {
            AppendLog(new RequestLogEntry
            {
                Timestamp = DateTime.Now,
                Method = "SYS",
                Url = "—",
                StatusCode = 0,
                ElapsedMs = 0,
                Body = $"[ОШИБКА] Некорректный порт: «{portText}». Допустимо: 1–65535"
            });
            return;
        }

        if (_listener != null)
        {
            AppendLog(new RequestLogEntry
            {
                Timestamp = DateTime.Now, Method = "SYS", Url = "—", StatusCode = 0, ElapsedMs = 0,
                Body = "[ОШИБКА] Сервер уже запущен"
            });
            return;
        }

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();

            _serverCts = new CancellationTokenSource();
            _serverStartTime = DateTime.Now;

            StartServerBtn.IsEnabled = false;
            StopServerBtn.IsEnabled = true;
            ServerPortBox.IsEnabled = false;
            ServerStatusDot.Fill = new SolidColorBrush(Color.Parse("#3FB950"));
            ServerStatusText.Text = $"Слушает: http://localhost:{port}/";
            SetStatus($"Сервер запущен на порту {port}");

            AppendLog(new RequestLogEntry
            {
                Timestamp = DateTime.Now, Method = "SYS", Url = $"http://localhost:{port}/",
                StatusCode = 200, ElapsedMs = 0,
                Body = $"Сервер запущен. GET → статус, POST → сохранение сообщений"
            });

            _ = Task.Run(() => ListenLoopAsync(_serverCts.Token));
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            _listener = null;
            AppendLog(new RequestLogEntry
            {
                Timestamp = DateTime.Now, Method = "SYS", Url = "—", StatusCode = 0, ElapsedMs = 0,
                Body = $"[ОШИБКА] Нет прав на порт {port}. Попробуйте порт > 1024 или запустите от администратора"
            });
        }
        catch (HttpListenerException ex)
        {
            _listener = null;
            AppendLog(new RequestLogEntry
            {
                Timestamp = DateTime.Now, Method = "SYS", Url = "—", StatusCode = 0, ElapsedMs = 0,
                Body = $"[ОШИБКА] Не удалось запустить сервер: {ex.Message} (код: {ex.ErrorCode})"
            });
        }
        catch (Exception ex)
        {
            _listener = null;
            AppendLog(new RequestLogEntry
            {
                Timestamp = DateTime.Now, Method = "SYS", Url = "—", StatusCode = 0, ElapsedMs = 0,
                Body = $"[ОШИБКА] Неожиданная ошибка: {ex.Message}"
            });
        }
    }

    private void StopServer_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        StopServer();
    }

    private void StopServer()
    {
        try { _serverCts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }

        _listener = null;
        _serverCts = null;

        Dispatcher.UIThread.Post(() =>
        {
            StartServerBtn.IsEnabled = true;
            StopServerBtn.IsEnabled = false;
            ServerPortBox.IsEnabled = true;
            ServerStatusDot.Fill = new SolidColorBrush(Color.Parse("#F85149"));
            ServerStatusText.Text = "Остановлен";
            SetStatus("Сервер остановлен");

            AppendLog(new RequestLogEntry
            {
                Timestamp = DateTime.Now, Method = "SYS", Url = "—", StatusCode = 0, ElapsedMs = 0,
                Body = "Сервер остановлен"
            });
        });
    }


    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener!.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppendLog(new RequestLogEntry
                {
                    Timestamp = DateTime.Now, Method = "SYS", Url = "—", StatusCode = 0, ElapsedMs = 0,
                    Body = $"[ОШИБКА] Ошибка получения контекста: {ex.Message}"
                });
                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(ctx), ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var sw = Stopwatch.StartNew();
        var req = ctx.Request;
        var resp = ctx.Response;
        var entry = new RequestLogEntry
        {
            Timestamp = DateTime.Now,
            Method = req.HttpMethod,
            Url = req.Url?.ToString() ?? "—"
        };

        var headersSb = new StringBuilder();
        foreach (string? key in req.Headers.Keys)
        {
            if (key != null)
                headersSb.AppendLine($"  {key}: {req.Headers[key]}");
        }
        entry.Headers = headersSb.ToString();

        try
        {
            string responseBody;
            int statusCode;

            if (req.HttpMethod == "GET")
            {
                (responseBody, statusCode) = HandleGet();
            }
            else if (req.HttpMethod == "POST")
            {
                (responseBody, statusCode) = await HandlePostAsync(req);
            }
            else
            {
                responseBody = JsonSerializer.Serialize(new { error = "Method Not Allowed" });
                statusCode = 405;
            }

            resp.StatusCode = statusCode;
            resp.ContentType = "application/json; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(responseBody);
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes);
            entry.Body = responseBody;
        }
        catch (Exception ex)
        {
            try
            {
                resp.StatusCode = 500;
                var errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = ex.Message }));
                resp.ContentLength64 = errBytes.Length;
                await resp.OutputStream.WriteAsync(errBytes);
                entry.Body = $"[ОШИБКА] {ex.Message}";
            }
            catch { }
        }
        finally
        {
            try { resp.OutputStream.Close(); } catch { }
        }

        sw.Stop();
        entry.StatusCode = resp.StatusCode;
        entry.ElapsedMs = sw.ElapsedMilliseconds;

        RecordStats(entry);
        AppendLog(entry);
    }

    private (string body, int status) HandleGet()
    {
        var now = DateTime.Now;
        var uptime = _listener != null ? (now - _serverStartTime).ToString(@"hh\:mm\:ss") : "—";

        var status = new
        {
            server = "HttpMonitor",
            status = "running",
            uptime,
            timestamp = now.ToString("yyyy-MM-dd HH:mm:ss"),
            get_count = _getCount,
            post_count = _postCount,
            total_requests = _totalRequests,
            average_response_ms = _totalRequests > 0 ? _totalElapsedMs / _totalRequests : 0,
            stored_messages = _messages.Count
        };

        return (JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }), 200);
    }

    private async Task<(string body, int status)> HandlePostAsync(HttpListenerRequest req)
    {
        if (req.ContentLength64 > MaxRequestBodySize)
            return (JsonSerializer.Serialize(new { error = "Request body too large" }), 413);

        string rawBody;
        using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8,
                                              detectEncodingFromByteOrderMarks: false,
                                              bufferSize: 4096, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(rawBody))
            return (JsonSerializer.Serialize(new { error = "Empty body" }), 400);

        JsonElement doc;
        try
        {
            doc = JsonSerializer.Deserialize<JsonElement>(rawBody);
        }
        catch (JsonException)
        {
            return (JsonSerializer.Serialize(new { error = "Invalid JSON" }), 400);
        }

        if (!doc.TryGetProperty("message", out var msgProp))
            return (JsonSerializer.Serialize(new { error = "Field 'message' is required" }), 400);

        var messageText = msgProp.GetString();
        if (string.IsNullOrWhiteSpace(messageText))
            return (JsonSerializer.Serialize(new { error = "Field 'message' cannot be empty" }), 400);

        if (messageText.Length > 10_000)
            return (JsonSerializer.Serialize(new { error = "Message too long (max 10000 chars)" }), 400);

        var id = Guid.NewGuid().ToString("N")[..8];
        var stored = new StoredMessage
        {
            Id = id,
            Message = messageText,
            ReceivedAt = DateTime.Now
        };
        _messages[id] = stored;

        var response = new { id, message = messageText, received_at = stored.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss") };
        return (JsonSerializer.Serialize(response), 201);
    }


    private void RecordStats(RequestLogEntry entry)
    {
        lock (_statsLock)
        {
            if (entry.Method == "GET") Interlocked.Increment(ref _getCount);
            else if (entry.Method == "POST") Interlocked.Increment(ref _postCount);

            if (entry.Method != "SYS")
            {
                Interlocked.Increment(ref _totalRequests);
                Interlocked.Add(ref _totalElapsedMs, entry.ElapsedMs);
            }
        }

        var minuteKey = entry.Timestamp.Ticks / TimeSpan.TicksPerMinute;
        var hourKey = entry.Timestamp.Ticks / TimeSpan.TicksPerHour;
        _minuteBuckets.AddOrUpdate(minuteKey, 1, (_, v) => v + 1);
        _hourBuckets.AddOrUpdate(hourKey, 1, (_, v) => v + 1);

        Dispatcher.UIThread.Post(UpdateStats);
    }

    private void UpdateStats()
    {
        StatGet.Text = _getCount.ToString();
        StatPost.Text = _postCount.ToString();
        var avg = _totalRequests > 0 ? _totalElapsedMs / _totalRequests : 0;
        StatAvgTime.Text = $"{avg} мс";
    }

    private void UpdateUptime()
    {
        if (_listener != null)
        {
            var uptime = DateTime.Now - _serverStartTime;
            StatUptime.Text = uptime.ToString(@"hh\:mm\:ss");
        }
    }


    private void DrawChart()
    {
        var canvas = LoadChart;
        canvas.Children.Clear();

        double w = canvas.Bounds.Width;
        double h = canvas.Bounds.Height;
        if (w < 10 || h < 10) return;

        bool isMinute = (ChartMode.SelectedIndex == 0);
        int buckets = isMinute ? ChartMinuteBuckets : ChartHourBuckets;

        var now = DateTime.Now;
        var data = new int[buckets];

        if (isMinute)
        {
            long nowBucket = now.Ticks / TimeSpan.TicksPerMinute;
            for (int i = 0; i < buckets; i++)
            {
                long key = nowBucket - (buckets - 1 - i);
                _minuteBuckets.TryGetValue(key, out data[i]);
            }
        }
        else
        {
            long nowBucket = now.Ticks / TimeSpan.TicksPerHour;
            for (int i = 0; i < buckets; i++)
            {
                long key = nowBucket - (buckets - 1 - i);
                _hourBuckets.TryGetValue(key, out data[i]);
            }
        }

        int maxVal = data.Max();
        if (maxVal == 0) maxVal = 1;

        double padL = 30, padR = 6, padT = 6, padB = 20;
        double chartW = w - padL - padR;
        double chartH = h - padT - padB;
        double barW = chartW / buckets - 2;

        for (int g = 0; g <= 4; g++)
        {
            double y = padT + chartH * g / 4;
            var line = new Line
            {
                StartPoint = new Avalonia.Point(padL, y),
                EndPoint = new Avalonia.Point(w - padR, y),
                Stroke = new SolidColorBrush(Color.Parse("#30363D")),
                StrokeThickness = 1
            };
            canvas.Children.Add(line);

            int val = maxVal - maxVal * g / 4;
            var label = new TextBlock
            {
                Text = val.ToString(),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.Parse("#8B949E")),
                FontFamily = new FontFamily("Consolas, Menlo, monospace")
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 6);
            canvas.Children.Add(label);
        }

        for (int i = 0; i < buckets; i++)
        {
            double barH = chartH * data[i] / maxVal;
            double x = padL + i * (chartW / buckets) + 1;
            double y = padT + chartH - barH;

            var bar = new Rectangle
            {
                Width = Math.Max(barW, 1),
                Height = Math.Max(barH, 0),
                Fill = new SolidColorBrush(Color.Parse("#1F6FEB")),
                RadiusX = 2,
                RadiusY = 2,
                Opacity = 0.85
            };
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
            canvas.Children.Add(bar);
        }

        string labelLeft = isMinute
            ? now.AddMinutes(-(buckets - 1)).ToString("HH:mm")
            : now.AddHours(-(buckets - 1)).ToString("HH:00");
        string labelRight = isMinute ? now.ToString("HH:mm") : now.ToString("HH:00");

        var tbLeft = new TextBlock
        {
            Text = labelLeft, FontSize = 9,
            Foreground = new SolidColorBrush(Color.Parse("#8B949E")),
            FontFamily = new FontFamily("Consolas, Menlo, monospace")
        };
        Canvas.SetLeft(tbLeft, padL);
        Canvas.SetTop(tbLeft, h - 14);
        canvas.Children.Add(tbLeft);

        var tbRight = new TextBlock
        {
            Text = labelRight, FontSize = 9,
            Foreground = new SolidColorBrush(Color.Parse("#8B949E")),
            FontFamily = new FontFamily("Consolas, Menlo, monospace")
        };
        Canvas.SetLeft(tbRight, w - padR - 34);
        Canvas.SetTop(tbRight, h - 14);
        canvas.Children.Add(tbRight);
    }


    private void AppendLog(RequestLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _allLogs.Add(entry);
            if (_allLogs.Count > MaxLogs)
                _allLogs.RemoveAt(0);

            ApplyFilter();
        });
    }

    private void ApplyFilter()
    {
        _filteredLogs.Clear();
        _logsDisplay.Clear();

        var methodFilter = (FilterMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Все";
        var statusFilter = (FilterStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Все";

        foreach (var e in _allLogs)
        {
            if (methodFilter != "Все" && e.Method != methodFilter) continue;

            if (statusFilter != "Все")
            {
                if (statusFilter.StartsWith("2") && (e.StatusCode < 200 || e.StatusCode > 299)) continue;
                if (statusFilter.StartsWith("4") && (e.StatusCode < 400 || e.StatusCode > 499)) continue;
                if (statusFilter.StartsWith("5") && (e.StatusCode < 500 || e.StatusCode > 599)) continue;
            }

            _filteredLogs.Add(e);
        }

        foreach (var e in _filteredLogs)
            _logsDisplay.Add(e.ToString());
    }

    private void ApplyFilter_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ApplyFilter();
    private void ResetFilter_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        FilterMethod.SelectedIndex = 0;
        FilterStatus.SelectedIndex = 0;
        ApplyFilter();
    }

    private void ClearLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _allLogs.Clear();
        _filteredLogs.Clear();
        _logsDisplay.Clear();
        RequestDetailsBox.Text = string.Empty;
        SetStatus("Логи очищены");
    }

    private void LogsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = LogsList.SelectedIndex;
        if (idx < 0 || idx >= _filteredLogs.Count)
        {
            RequestDetailsBox.Text = string.Empty;
            return;
        }
        var entry = _filteredLogs[idx];
        var sb = new StringBuilder();
        sb.AppendLine($"Время:   {entry.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Метод:   {entry.Method}");
        sb.AppendLine($"URL:     {entry.Url}");
        sb.AppendLine($"Статус:  {entry.StatusCode}");
        sb.AppendLine($"Время:   {entry.ElapsedMs} мс");
        if (!string.IsNullOrWhiteSpace(entry.Headers))
        {
            sb.AppendLine("Заголовки:");
            sb.Append(entry.Headers);
        }
        if (!string.IsNullOrWhiteSpace(entry.Body))
        {
            sb.AppendLine("Тело:");
            sb.AppendLine(entry.Body.Length > 500 ? entry.Body[..500] + "…" : entry.Body);
        }
        RequestDetailsBox.Text = sb.ToString();
    }


    private void SaveLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = LogFilePathBox.Text?.Trim() ?? "logs.txt";

        if (string.IsNullOrWhiteSpace(path))
        {
            SaveStatusText.Text = "[ОШИБКА] Укажите путь к файлу";
            SaveStatusText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
            return;
        }

        if (path.Length > 260)
        {
            SaveStatusText.Text = "[ОШИБКА] Путь слишком длинный";
            SaveStatusText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
            return;
        }

        var invalidChars = System.IO.Path.GetInvalidPathChars();
        if (path.Any(c => invalidChars.Contains(c)))
        {
            SaveStatusText.Text = "[ОШИБКА] Путь содержит недопустимые символы";
            SaveStatusText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if (info.Length > MaxLogFileSize)
                {
                    SaveStatusText.Text = $"[ОШИБКА] Файл превышает {MaxLogFileSize / 1024 / 1024} МБ";
                    SaveStatusText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
                    return;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# HTTP Monitor Logs — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# Всего записей: {_allLogs.Count}");
            sb.AppendLine(new string('=', 80));

            foreach (var entry in _allLogs)
            {
                sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Method,-4} {entry.Url}");
                sb.AppendLine($"  Статус: {entry.StatusCode} | Время: {entry.ElapsedMs}мс");
                if (!string.IsNullOrWhiteSpace(entry.Body))
                    sb.AppendLine($"  Тело: {(entry.Body.Length > 200 ? entry.Body[..200] + "…" : entry.Body)}");
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            SaveStatusText.Text = $"✓ Сохранено {_allLogs.Count} записей";
            SaveStatusText.Foreground = new SolidColorBrush(Color.Parse("#3FB950"));
            SetStatus($"Логи сохранены в {path}");
        }
        catch (UnauthorizedAccessException)
        {
            SaveStatusText.Text = "[ОШИБКА] Нет прав на запись";
            SaveStatusText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
        }
        catch (DirectoryNotFoundException)
        {
            SaveStatusText.Text = "[ОШИБКА] Директория не существует";
            SaveStatusText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
        }
        catch (IOException ex)
        {
            SaveStatusText.Text = $"[ОШИБКА] IO: {ex.Message}";
            SaveStatusText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
        }
        catch (Exception ex)
        {
            SaveStatusText.Text = $"[ОШИБКА] {ex.Message}";
            SaveStatusText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
        }
    }


    private async void SendRequest_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var url = ClientUrlBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            ResponseStatusText.Text = "[ОШИБКА] Введите URL";
            ResponseBodyBox.Text = string.Empty;
            return;
        }

        if (url.Length > MaxUrlLength)
        {
            ResponseStatusText.Text = $"[ОШИБКА] URL слишком длинный";
            ResponseBodyBox.Text = string.Empty;
            return;
        }

        if (url.Any(c => c < 0x20))
        {
            ResponseStatusText.Text = "[ОШИБКА] URL содержит недопустимые символы";
            ResponseBodyBox.Text = string.Empty;
            return;
        }

        if (!url.Contains("://"))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            ResponseStatusText.Text = "[ОШИБКА] Некорректный URL";
            ResponseBodyBox.Text = string.Empty;
            return;
        }

        var allowedSchemes = new[] { "http", "https" };
        if (!allowedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
        {
            ResponseStatusText.Text = $"[ОШИБКА] Схема «{uri.Scheme}» не поддерживается";
            ResponseBodyBox.Text = string.Empty;
            return;
        }

        var method = (ClientMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
        var body = ClientBodyBox.Text?.Trim() ?? string.Empty;

        if (method == "POST" && !string.IsNullOrWhiteSpace(body))
        {
            try { JsonSerializer.Deserialize<JsonElement>(body); }
            catch (JsonException)
            {
                ResponseStatusText.Text = "[ОШИБКА] Тело запроса — невалидный JSON";
                ResponseBodyBox.Text = string.Empty;
                return;
            }
        }

        ResponseStatusText.Text = "Отправка…";
        ResponseTimeText.Text = "—";
        ResponseBodyBox.Text = string.Empty;
        SetStatus("Отправка запроса...");

        var sw = Stopwatch.StartNew();
        try
        {
            HttpResponseMessage response;

            if (method == "GET")
            {
                response = await _httpClient.GetAsync(uri);
            }
            else // POST
            {
                var content = new StringContent(
                    string.IsNullOrWhiteSpace(body) ? "{}" : body,
                    Encoding.UTF8,
                    "application/json");
                response = await _httpClient.PostAsync(uri, content);
            }

            sw.Stop();
            var responseText = await response.Content.ReadAsStringAsync();

            try
            {
                var parsed = JsonSerializer.Deserialize<JsonElement>(responseText);
                responseText = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });
            }
            catch {}

            if (responseText.Length > 20_000)
                responseText = responseText[..20_000] + "\n… (обрезано)";

            ResponseStatusText.Text = $"{(int)response.StatusCode} {response.StatusCode}";
            ResponseStatusText.Foreground = (int)response.StatusCode < 400
                ? new SolidColorBrush(Color.Parse("#3FB950"))
                : new SolidColorBrush(Color.Parse("#F85149"));
            ResponseTimeText.Text = $"{sw.ElapsedMilliseconds} мс";
            ResponseBodyBox.Text = responseText;

            SetStatus($"Ответ получен: {(int)response.StatusCode} за {sw.ElapsedMilliseconds}мс");
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException se)
        {
            sw.Stop();
            ResponseStatusText.Text = "[ОШИБКА] Сетевая ошибка";
            ResponseBodyBox.Text = $"Ошибка сокета: {se.Message} (код: {se.SocketErrorCode})";
            SetStatus("Ошибка соединения");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            ResponseStatusText.Text = "[ОШИБКА]";
            ResponseBodyBox.Text = $"HTTP ошибка: {ex.Message}";
            SetStatus("Ошибка запроса");
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            ResponseStatusText.Text = "[ОШИБКА] Таймаут";
            ResponseBodyBox.Text = $"Запрос превысил таймаут ({HttpTimeoutSeconds}с)";
            SetStatus("Таймаут запроса");
        }
        catch (UriFormatException ex)
        {
            sw.Stop();
            ResponseStatusText.Text = "[ОШИБКА] URL";
            ResponseBodyBox.Text = $"Некорректный URL: {ex.Message}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            ResponseStatusText.Text = "[ОШИБКА]";
            ResponseBodyBox.Text = $"Неожиданная ошибка: {ex.Message}";
            SetStatus("Ошибка");
        }
    }


    private void SetStatus(string message) =>
        Dispatcher.UIThread.Post(() => StatusBar.Text = message);

    protected override void OnClosing(Avalonia.Controls.WindowClosingEventArgs e)
    {
        _uiTimer?.Stop();
        StopServer();
        base.OnClosing(e);
    }
}