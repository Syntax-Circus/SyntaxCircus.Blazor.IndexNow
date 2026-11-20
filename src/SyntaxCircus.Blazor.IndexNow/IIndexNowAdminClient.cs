namespace SyntaxCircus.Blazor.IndexNow;

/// <summary>
/// Host-implemented client the IndexNow components use to talk to the application's admin API.
/// Implementations should throw on failure; the components show a safe message and never the exception text.
/// </summary>
public interface IIndexNowAdminClient
{
    Task<IndexNowSettingsDto> GetSettingsAsync(CancellationToken ct = default);

    Task<IndexNowSettingsDto> SaveSettingsAsync(IndexNowSettingsDto settings, CancellationToken ct = default);

    Task<IndexNowPreviewDto> PreviewAsync(CancellationToken ct = default);

    Task<IndexNowSubmissionDetailDto> SubmitAsync(IReadOnlyList<string> urls, CancellationToken ct = default);

    Task<IReadOnlyList<IndexNowSubmissionSummaryDto>> GetHistoryAsync(CancellationToken ct = default);

    Task<IndexNowSubmissionDetailDto> GetSubmissionAsync(Guid id, CancellationToken ct = default);

    Task<IndexNowSubmissionDetailDto> RetryAsync(Guid id, CancellationToken ct = default);

    Task<IndexNowKeyFileDto> GenerateKeyFileAsync(CancellationToken ct = default);
}
