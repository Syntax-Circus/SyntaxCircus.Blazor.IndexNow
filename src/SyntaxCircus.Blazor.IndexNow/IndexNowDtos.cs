using System.Text.Json.Serialization;

namespace SyntaxCircus.Blazor.IndexNow;

/// <summary>Lifecycle state of an IndexNow submission. Serialized as a string.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IndexNowSubmissionStatus
{
    InProgress,
    Received,
    ValidationPending,
    Partial,
    Failed,
    Cancelled
}

/// <summary>Saved IndexNow settings for the site.</summary>
public sealed record IndexNowSettingsDto(bool IsEnabled, string CanonicalUrl, string SitemapPath, string KeyFilePath);

/// <summary>URLs found in the sitemap by a preview.</summary>
public sealed record IndexNowPreviewDto(IReadOnlyList<string> Urls);

/// <summary>Outcome of one HTTP batch sent to IndexNow.</summary>
public sealed record IndexNowBatchDto(IReadOnlyList<string> Urls, int? StatusCode, string? Error);

/// <summary>One row of the submission history.</summary>
public sealed record IndexNowSubmissionSummaryDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    IndexNowSubmissionStatus Status,
    string Trigger,
    int UrlCount,
    string? Error,
    string? ActorName);

/// <summary>A submission with its URLs and per-batch results.</summary>
public sealed record IndexNowSubmissionDetailDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    IndexNowSubmissionStatus Status,
    string Trigger,
    int UrlCount,
    string? Error,
    string? ActorName,
    IReadOnlyList<string> Urls,
    IReadOnlyList<IndexNowBatchDto> Batches);

/// <summary>Request body for submitting explicit URLs.</summary>
public sealed record IndexNowSubmitRequest(IReadOnlyList<string> Urls);

/// <summary>A generated ownership key file.</summary>
public sealed record IndexNowKeyFileDto(string FileName, string Content);
