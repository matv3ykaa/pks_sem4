using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using NetworkAnalyzer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NetworkAnalyzer;

public partial class MainWindow : Window
{
    // Максимальная длина URL для валидации
    private const int MaxUrlLength = 2048;
    // Максимальное количество записей в истории
    private const int MaxHistoryCount = 100;
    // Таймаут ping в миллисекундах
    private const int PingTimeoutMs = 3000;
    // Максимальная длина фильтра
    private const int MaxFilterLength = 100;

    private readonly ObservableCollection<string> _interfaceItems = new();
    private readonly ObservableCollection<string> _historyItems = new();

    // Хранение полных объектов интерфейсов для отображения по индексу
    private List<NetworkInterface> _allInterfaces = new();
    private List<NetworkInterface> _filteredInterfaces = new();

    // Полная история с метаданными
    private readonly List<UrlHistoryEntry> _history = new();

    public MainWindow()
    {
        InitializeComponent();
        InterfaceList.ItemsSource = _interfaceItems;
        HistoryList.ItemsSource = _historyItems;

        LoadInterfaces();
    }

    // ─── Загрузка интерфейсов ────────────────────────────────────────────────

    private void LoadInterfaces()
    {
        try
        {
            _allInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .OrderBy(n => n.OperationalStatus == OperationalStatus.Up ? 0 : 1)
                .ThenBy(n => n.Name)
                .ToList();

            ApplyFilter(FilterBox?.Text ?? string.Empty);
            UpdateStats();
            SetStatus("Интерфейсы обновлены");
        }
        catch (NetworkInformationException ex)
        {
            AppendResult($"[ОШИБКА] Не удалось получить список интерфейсов: {ex.Message}");
            SetStatus("Ошибка при загрузке интерфейсов");
        }
        catch (Exception ex)
        {
            AppendResult($"[ОШИБКА] Неожиданная ошибка при загрузке: {ex.Message}");
            SetStatus("Ошибка");
        }
    }

    private void ApplyFilter(string? filter)
    {
        filter = (filter ?? string.Empty).Trim();

        if (filter.Length > MaxFilterLength)
            filter = filter[..MaxFilterLength];

        _filteredInterfaces = string.IsNullOrEmpty(filter)
            ? _allInterfaces.ToList()
            : _allInterfaces
                .Where(n => n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                         || n.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _interfaceItems.Clear();
        foreach (var ni in _filteredInterfaces)
        {
            var statusIcon = ni.OperationalStatus == OperationalStatus.Up ? "●" : "○";
            _interfaceItems.Add($"{statusIcon}  {ni.Name}");
        }
    }

    private void UpdateStats()
    {
        var total = _allInterfaces.Count;
        var up    = _allInterfaces.Count(n => n.OperationalStatus == OperationalStatus.Up);
        var down  = total - up;

        StatsTotal.Text = $"Всего: {total}";
        StatsUp.Text    = $"Активных: {up}";
        StatsDown.Text  = $"Неактивных: {down}";
    }

    // ─── Отображение информации об интерфейсе ───────────────────────────────

    private void ShowInterfaceInfo(NetworkInterface ni)
    {
        try
        {
            InfoName.Text = TruncateSafe(ni.Name, 40);

            // IP-адреса
            var ipProps = ni.GetIPProperties();
            var ipv4 = ipProps.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .ToList();
            var ipv6 = ipProps.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(a => a.Address.ToString())
                .ToList();

            InfoIp.Text   = ipv4.Count > 0 ? string.Join(", ", ipv4) : "Нет IPv4";
            InfoIpv6.Text = ipv6.Count > 0 ? TruncateSafe(string.Join(", ", ipv6), 80) : "Нет IPv6";

            // Маска подсети (берём первый unicast IPv4)
            var firstUnicast = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            if (firstUnicast != null)
            {
                try
                {
                    InfoMask.Text = firstUnicast.IPv4Mask.ToString();
                }
                catch
                {
                    InfoMask.Text = "Н/Д";
                }
            }
            else
            {
                InfoMask.Text = "—";
            }

            // MAC-адрес
            var mac = ni.GetPhysicalAddress();
            var macBytes = mac.GetAddressBytes();
            InfoMac.Text = macBytes.Length > 0
                ? string.Join(":", macBytes.Select(b => b.ToString("X2")))
                : "Нет MAC";

            // Состояние
            InfoStatus.Text = ni.OperationalStatus switch
            {
                OperationalStatus.Up             => "▲ Активен",
                OperationalStatus.Down           => "▼ Неактивен",
                OperationalStatus.Testing        => "⚙ Тестирование",
                OperationalStatus.Dormant        => "⏸ Спящий",
                OperationalStatus.NotPresent     => "✗ Отсутствует",
                OperationalStatus.LowerLayerDown => "↓ Нет нижнего уровня",
                _                                => "? Неизвестно"
            };
            InfoStatus.Foreground = ni.OperationalStatus == OperationalStatus.Up
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3FB950"))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F85149"));

            // Тип
            InfoType.Text = ni.NetworkInterfaceType.ToString();

            // Скорость
            try
            {
                var speedBps = ni.Speed;
                InfoSpeed.Text = speedBps switch
                {
                    <= 0             => "Н/Д",
                    < 1_000_000      => $"{speedBps / 1000} Кбит/с",
                    < 1_000_000_000  => $"{speedBps / 1_000_000} Мбит/с",
                    _                => $"{speedBps / 1_000_000_000} Гбит/с"
                };
            }
            catch
            {
                InfoSpeed.Text = "Н/Д";
            }
        }
        catch (NetworkInformationException ex)
        {
            AppendResult($"[ОШИБКА] Не удалось получить данные интерфейса: {ex.Message}");
            ClearInterfaceInfo();
        }
        catch (Exception ex)
        {
            AppendResult($"[ОШИБКА] Ошибка при отображении интерфейса: {ex.Message}");
            ClearInterfaceInfo();
        }
    }

    private void ClearInterfaceInfo()
    {
        InfoName.Text = InfoIp.Text = InfoMask.Text = InfoMac.Text =
        InfoStatus.Text = InfoType.Text = InfoSpeed.Text = InfoIpv6.Text = "—";
    }

    // ─── Анализ URL ──────────────────────────────────────────────────────────

    private async Task AnalyzeUrlAsync(string rawUrl)
    {
        // Валидация входных данных
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            AppendResult("[ОШИБКА] Введите URL для анализа");
            return;
        }

        rawUrl = rawUrl.Trim();

        if (rawUrl.Length > MaxUrlLength)
        {
            AppendResult($"[ОШИБКА] URL слишком длинный (максимум {MaxUrlLength} символов)");
            return;
        }

        // Проверка на управляющие и недопустимые символы
        if (rawUrl.Any(c => c < 0x20 || c == 0x7F))
        {
            AppendResult("[ОШИБКА] URL содержит недопустимые управляющие символы");
            return;
        }

        // Если схема не указана — пробуем добавить https://
        if (!rawUrl.Contains("://"))
        {
            rawUrl = "https://" + rawUrl;
            AppendResult($"[INFO] Схема не указана, используется https://");
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            AppendResult($"[ОШИБКА] Некорректный URL: не удалось распарсить");
            ClearUrlComponents();
            return;
        }

        // Допустимые схемы
        var allowedSchemes = new[] { "http", "https", "ftp", "ftps" };
        if (!allowedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
        {
            AppendResult($"[ОШИБКА] Схема «{uri.Scheme}» не поддерживается. Допустимые: {string.Join(", ", allowedSchemes)}");
            ClearUrlComponents();
            return;
        }

        // Валидация хоста
        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            AppendResult("[ОШИБКА] Хост не указан");
            ClearUrlComponents();
            return;
        }
        if (host.Length > 253)
        {
            AppendResult("[ОШИБКА] Имя хоста превышает 253 символа");
            ClearUrlComponents();
            return;
        }

        // Отображаем компоненты URI
        UriScheme.Text   = uri.Scheme;
        UriHost.Text     = host;
        UriPort.Text     = uri.IsDefaultPort ? $"{uri.Port} (по умолчанию)" : uri.Port.ToString();
        UriPath.Text     = string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/" ? "/" : uri.AbsolutePath;
        UriQuery.Text    = string.IsNullOrEmpty(uri.Query) ? "—" : uri.Query;
        UriFragment.Text = string.IsNullOrEmpty(uri.Fragment) ? "—" : uri.Fragment;

        var sb = new StringBuilder();
        sb.AppendLine($"══ Анализ URL: {TruncateSafe(rawUrl, 80)} ══");
        sb.AppendLine($"  Схема:     {uri.Scheme}");
        sb.AppendLine($"  Хост:      {host}");
        sb.AppendLine($"  Порт:      {uri.Port}{(uri.IsDefaultPort ? " (по умолчанию)" : "")}");
        sb.AppendLine($"  Путь:      {uri.AbsolutePath}");
        sb.AppendLine($"  Параметры: {(string.IsNullOrEmpty(uri.Query) ? "нет" : uri.Query)}");
        sb.AppendLine($"  Фрагмент:  {(string.IsNullOrEmpty(uri.Fragment) ? "нет" : uri.Fragment)}");

        // Тип адреса
        var addressType = DetermineAddressType(host);
        sb.AppendLine($"  Тип адреса: {addressType}");

        AppendResult(sb.ToString());
        SetStatus("Выполняется DNS и Ping...");

        // DNS
        await ResolveDnsAsync(host, sb);

        // Ping
        await PingHostAsync(host, sb);

        // Добавляем в историю
        var historyEntry = new UrlHistoryEntry
        {
            Url       = TruncateSafe(rawUrl, 80),
            CheckedAt = DateTime.Now,
            Status    = "Проверен"
        };
        AddToHistory(historyEntry);
        SetStatus("Анализ завершён");
    }

    private async Task ResolveDnsAsync(string host, StringBuilder sb)
    {
        try
        {
            // Не делаем DNS-запрос для IP-адресов
            if (IPAddress.TryParse(host, out _))
            {
                AppendResult($"  DNS: хост уже является IP-адресом, DNS-запрос не требуется");
                return;
            }

            SetStatus("DNS-запрос...");
            var addresses = await Dns.GetHostAddressesAsync(host)
                .WaitAsync(TimeSpan.FromSeconds(5));

            if (addresses.Length == 0)
            {
                AppendResult($"  DNS: записи не найдены для «{host}»");
                return;
            }

            var dnsResult = new StringBuilder();
            dnsResult.AppendLine($"  DNS: разрешено {addresses.Length} адрес(ов):");
            foreach (var addr in addresses.Take(10)) // ограничиваем вывод
                dnsResult.AppendLine($"    • {addr} ({addr.AddressFamily})");
            if (addresses.Length > 10)
                dnsResult.AppendLine($"    ... и ещё {addresses.Length - 10}");

            AppendResult(dnsResult.ToString());
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostNotFound)
        {
            AppendResult($"  DNS: хост «{host}» не найден");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TryAgain)
        {
            AppendResult($"  DNS: временная ошибка сервера, попробуйте позже");
        }
        catch (SocketException ex)
        {
            AppendResult($"  DNS: ошибка сокета — {ex.Message} (код: {ex.SocketErrorCode})");
        }
        catch (TimeoutException)
        {
            AppendResult($"  DNS: превышено время ожидания (5 сек)");
        }
        catch (ArgumentException ex)
        {
            AppendResult($"  DNS: недопустимое имя хоста — {ex.Message}");
        }
        catch (Exception ex)
        {
            AppendResult($"  DNS: непредвиденная ошибка — {ex.Message}");
        }
    }

    private async Task PingHostAsync(string host, StringBuilder sb)
    {
        // Ping не имеет смысла для loopback — можно, но предупредим
        using var ping = new Ping();
        try
        {
            SetStatus("Ping...");
            var reply = await ping.SendPingAsync(host, PingTimeoutMs);

            var pingResult = reply.Status switch
            {
                IPStatus.Success =>
                    $"  Ping: {reply.RoundtripTime} мс, TTL={reply.Options?.Ttl ?? 0}, адрес={reply.Address}",
                IPStatus.TimedOut =>
                    $"  Ping: таймаут ({PingTimeoutMs} мс)",
                IPStatus.DestinationHostUnreachable =>
                    $"  Ping: хост недоступен",
                IPStatus.DestinationNetworkUnreachable =>
                    $"  Ping: сеть недоступна",
                IPStatus.DestinationUnreachable =>
                    $"  Ping: адресат недоступен",
                IPStatus.TimeExceeded =>
                    $"  Ping: превышено TTL",
                _ =>
                    $"  Ping: {reply.Status}"
            };
            AppendResult(pingResult);
        }
        catch (PingException ex)
        {
            // PingException часто оборачивает SocketException
            var inner = ex.InnerException?.Message ?? ex.Message;
            AppendResult($"  Ping: ошибка — {inner}");
        }
        catch (ArgumentNullException)
        {
            AppendResult($"  Ping: хост не указан");
        }
        catch (ArgumentOutOfRangeException)
        {
            AppendResult($"  Ping: недопустимый таймаут");
        }
        catch (InvalidOperationException ex)
        {
            AppendResult($"  Ping: операция недопустима — {ex.Message}");
        }
        catch (Exception ex)
        {
            AppendResult($"  Ping: непредвиденная ошибка — {ex.Message}");
        }
    }

    // ─── Определение типа адреса ─────────────────────────────────────────────

    private static string DetermineAddressType(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "Неизвестно";

        // Проверяем как IP-адрес
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip))
                return "Loopback (петлевой)";

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                // RFC 1918: приватные диапазоны
                bool isPrivate =
                    bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168);
                // RFC 3927: link-local 169.254.x.x
                bool isLinkLocal = bytes[0] == 169 && bytes[1] == 254;
                // RFC 1122: этот хост 0.x.x.x
                bool isThisHost = bytes[0] == 0;
                // Широковещательный
                bool isBroadcast = bytes[0] == 255;

                if (isPrivate)   return "Локальный (приватный, RFC 1918)";
                if (isLinkLocal) return "Link-local (169.254.x.x)";
                if (isThisHost)  return "Этот хост (0.x.x.x)";
                if (isBroadcast) return "Широковещательный";

                return "Публичный (IPv4)";
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal)   return "Link-local IPv6 (fe80::/10)";
                if (ip.IsIPv6SiteLocal)   return "Site-local IPv6 (fec0::/10)";
                if (ip.IsIPv6Multicast)   return "Multicast IPv6";
                // fc00::/7 — Unique Local Address
                var bytes6 = ip.GetAddressBytes();
                if ((bytes6[0] & 0xFE) == 0xFC) return "Unique Local Address IPv6 (fc00::/7)";
                return "Публичный (IPv6)";
            }

            return "IP-адрес (неизвестный тип)";
        }

        // Это доменное имя
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return "Loopback (localhost)";

        // .local — mDNS / локальная сеть
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return "Локальный (mDNS / .local)";

        // .internal, .corp, .home, .lan — часто используемые частные суффиксы
        var privateSuffixes = new[] { ".internal", ".corp", ".home", ".lan", ".intranet" };
        if (privateSuffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            return "Локальный (внутренний домен)";

        return "Публичный (доменное имя)";
    }

    // ─── История ─────────────────────────────────────────────────────────────

    private void AddToHistory(UrlHistoryEntry entry)
    {
        _history.Add(entry);
        _historyItems.Add(entry.ToString());

        // Ограничиваем размер истории
        while (_history.Count > MaxHistoryCount)
        {
            _history.RemoveAt(0);
            _historyItems.RemoveAt(0);
        }
    }

    // ─── Вспомогательные ─────────────────────────────────────────────────────

    private void AppendResult(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var current = ResultsBox.Text ?? string.Empty;
            // Ограничиваем объём результатов — не более ~50 000 символов
            if (current.Length > 50_000)
                current = current[^30_000..];
            ResultsBox.Text = current + text + Environment.NewLine;
        });
    }

    private void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusBar.Text = message;
        });
    }

    private void ClearUrlComponents()
    {
        UriScheme.Text = UriHost.Text = UriPort.Text =
        UriPath.Text = UriQuery.Text = UriFragment.Text = "—";
    }

    private static string TruncateSafe(string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= maxLen ? s : s[..maxLen] + "…";
    }

    // ─── Обработчики событий ─────────────────────────────────────────────────

    private void RefreshInterfaces_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearInterfaceInfo();
        LoadInterfaces();
    }

    private void FilterBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = FilterBox.Text ?? string.Empty;
        // Ограничение длины фильтра
        if (text.Length > MaxFilterLength)
        {
            FilterBox.Text = text[..MaxFilterLength];
            return;
        }
        ApplyFilter(text);
    }

    private void InterfaceList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = InterfaceList.SelectedIndex;
        if (idx < 0 || idx >= _filteredInterfaces.Count)
        {
            ClearInterfaceInfo();
            return;
        }
        ShowInterfaceInfo(_filteredInterfaces[idx]);
    }

    private void AnalyzeUrl_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var url = UrlInput.Text ?? string.Empty;
        _ = AnalyzeUrlAsync(url);
    }

    private void UrlInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var url = UrlInput.Text ?? string.Empty;
            _ = AnalyzeUrlAsync(url);
        }
    }

    private void ClearResults_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ResultsBox.Text = string.Empty;
        ClearUrlComponents();
        SetStatus("Результаты очищены");
    }

    private void ClearHistory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _history.Clear();
        _historyItems.Clear();
        SetStatus("История очищена");
    }

    private void HistoryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = HistoryList.SelectedIndex;
        if (idx < 0 || idx >= _history.Count) return;

        // Подставляем URL из истории в поле ввода
        UrlInput.Text = _history[idx].Url;
    }
}
