using System;

namespace NetworkAnalyzer.Models;

public class UrlHistoryEntry
{
    public string Url { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; }
    public string Status { get; set; } = string.Empty;

    public override string ToString() =>
        $"[{CheckedAt:HH:mm:ss}] {Url} — {Status}";
}