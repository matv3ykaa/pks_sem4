using System;

namespace HttpMonitor.Models;

public class RequestLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long ElapsedMs { get; set; }
    public string Body { get; set; } = string.Empty;
    public string Headers { get; set; } = string.Empty;

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] {Method,-4} {Url} → {StatusCode} ({ElapsedMs}ms)";
}

public class StoredMessage
{
    public string Id { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
}