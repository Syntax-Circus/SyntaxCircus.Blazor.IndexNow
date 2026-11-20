using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace SyntaxCircus.Blazor.IndexNow;

/// <summary>
/// Full IndexNow admin editor: settings, ownership file, sitemap preview, submission, history and retry.
/// Downloads use <c>data:</c> URIs on plain anchors, so no JavaScript interop or extra package is needed.
/// Failures from <see cref="IIndexNowAdminClient"/> are replaced by fixed, safe messages.
/// </summary>
public partial class IndexNowEditor : IDisposable
{
    private const int PreviewPageSize = 100;
    private const int HistoryBatchUrlLimit = 100;
    private static readonly TimeSpan InterruptedSubmissionAge = TimeSpan.FromMinutes(11);
    private static readonly JsonSerializerOptions DownloadJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<string> _selectedUrls = new(StringComparer.Ordinal);
    private IndexNowSettingsDto? _settings;
    private IndexNowPreviewDto? _preview;
    private IndexNowKeyFileDto? _keyFile;
    private IReadOnlyList<IndexNowSubmissionSummaryDto>? _history;
    private IndexNowSubmissionDetailDto? _lastSubmission;
    private IndexNowSubmissionDetailDto? _historyDetails;
    private string? _historyDownloadUrl;
    private string? _problem;
    private string? _historyProblem;
    private string? _historyDetailProblem;
    private string? _message;
    private Guid? _openHistoryItem;
    private int _previewPage;
    private bool _busy;
    private bool _disposed;
    private bool _enableIndexNow;
    private string _canonicalUrl = string.Empty;
    private string _sitemapPath = string.Empty;
    private string _keyFilePath = string.Empty;

    [Inject] private IIndexNowAdminClient Client { get; set; } = default!;

    /// <summary>Origin offered as the canonical URL while nothing is saved yet. Saving stays explicit.</summary>
    [Parameter] public string? SuggestedCanonicalUrl { get; set; }

    private bool HasChanges => _settings is { } settings
        && (_enableIndexNow != settings.IsEnabled || _canonicalUrl != settings.CanonicalUrl
            || _sitemapPath != settings.SitemapPath || _keyFilePath != settings.KeyFilePath);

    private bool IsConfigured => _settings is not null && !string.IsNullOrWhiteSpace(_settings.CanonicalUrl);

    private bool CanSave => !_busy && HasChanges;

    private bool CanCheckSetup => !_busy && IsConfigured && !HasChanges;

    private bool CanSubmit => CanCheckSetup && _settings!.IsEnabled;

    private int PreviewPageCount => Math.Max(1, ((_preview?.Urls.Count ?? 0) + PreviewPageSize - 1) / PreviewPageSize);

    private string? OwnershipFileUrl => IsConfigured ? _settings!.CanonicalUrl.TrimEnd('/') + _settings.KeyFilePath : null;

    private string? KeyFileDownloadUrl => _keyFile is { } file
        ? "data:text/plain;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(file.Content))
        : null;

    protected override Task OnInitializedAsync() =>
        RunAsync(async token =>
        {
            await LoadSettingsAsync(token);
            await LoadHistoryAsync(token);
        });

    private Task ReloadSettingsAsync() => RunAsync(LoadSettingsAsync);

    private async Task LoadSettingsAsync(CancellationToken token)
    {
        try
        {
            var result = await Client.GetSettingsAsync(token);
            if (!_disposed)
            {
                ApplySettings(result, message: null);
            }
        }
        catch (Exception ex) when (IsClientFailure(ex, token))
        {
            _problem = "IndexNow settings could not be loaded.";
        }
    }

    private Task SaveSettingsAsync() => !CanSave ? Task.CompletedTask : RunAsync(async token =>
    {
        var update = new IndexNowSettingsDto(_enableIndexNow, _canonicalUrl.Trim(), _sitemapPath.Trim(), _keyFilePath.Trim());
        try
        {
            var result = await Client.SaveSettingsAsync(update, token);
            if (!_disposed)
            {
                ApplySettings(result, "IndexNow settings saved.");
            }
        }
        catch (Exception ex) when (IsClientFailure(ex, token))
        {
            _problem = "IndexNow settings could not be saved. Check the values and try again.";
        }
    });

    private void ApplySettings(IndexNowSettingsDto settings, string? message)
    {
        _settings = settings;
        _enableIndexNow = settings.IsEnabled;
        _canonicalUrl = string.IsNullOrEmpty(settings.CanonicalUrl) && !settings.IsEnabled
            ? SuggestedCanonicalUrl ?? string.Empty
            : settings.CanonicalUrl;
        _sitemapPath = settings.SitemapPath;
        _keyFilePath = settings.KeyFilePath;
        _message = message;
        ClearSetupResults();
    }

    private Task GenerateKeyFileAsync() => !CanCheckSetup ? Task.CompletedTask : RunAsync(async token =>
    {
        try
        {
            var result = await Client.GenerateKeyFileAsync(token);
            if (!_disposed)
            {
                _keyFile = result;
            }
        }
        catch (Exception ex) when (IsClientFailure(ex, token))
        {
            _problem = "The ownership file could not be generated.";
        }
    });

    private Task PreviewAsync() => !CanCheckSetup ? Task.CompletedTask : RunAsync(async token =>
    {
        ClearPreview();
        try
        {
            var result = await Client.PreviewAsync(token);
            if (!_disposed)
            {
                _preview = result;
            }
        }
        catch (Exception ex) when (IsClientFailure(ex, token))
        {
            _problem = "The ownership file and sitemap could not be checked.";
        }
    });

    private void MovePreviewPage(int direction)
    {
        if (!_busy)
        {
            _previewPage = Math.Clamp(_previewPage + direction, 0, PreviewPageCount - 1);
        }
    }

    private void SetSelected(string url, bool selected)
    {
        if (!CanSubmit)
        {
            return;
        }

        if (selected)
        {
            _selectedUrls.Add(url);
        }
        else
        {
            _selectedUrls.Remove(url);
        }
    }

    private Task SubmitSelectedAsync() => SubmitAsync(all: false);

    private Task SubmitAllAsync() => SubmitAsync(all: true);

    private Task SubmitAsync(bool all)
    {
        if (!CanSubmit || _preview is not { } preview)
        {
            return Task.CompletedTask;
        }

        var urls = all ? preview.Urls : preview.Urls.Where(_selectedUrls.Contains).ToArray();
        if (urls.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async token =>
        {
            _lastSubmission = null;
            try
            {
                var result = await Client.SubmitAsync(urls, token);
                await FinishSubmissionAsync(result, token);
            }
            catch (Exception ex) when (IsClientFailure(ex, token))
            {
                _problem = "IndexNow could not process the submission.";
            }
        });
    }

    private Task RetryAsync(IndexNowSubmissionSummaryDto item) => !CanRetry(item)
        ? Task.CompletedTask
        : RunAsync(async token =>
        {
            _lastSubmission = null;
            try
            {
                var result = await Client.RetryAsync(item.Id, token);
                await FinishSubmissionAsync(result, token);
            }
            catch (Exception ex) when (IsClientFailure(ex, token))
            {
                _problem = "IndexNow could not process the retry.";
            }
        });

    private async Task FinishSubmissionAsync(IndexNowSubmissionDetailDto result, CancellationToken token)
    {
        if (_disposed)
        {
            return;
        }

        _lastSubmission = result;
        await LoadHistoryAsync(token);
    }

    private Task RefreshHistoryAsync() => RunAsync(LoadHistoryAsync);

    private async Task LoadHistoryAsync(CancellationToken token)
    {
        try
        {
            var result = await Client.GetHistoryAsync(token);
            if (!_disposed)
            {
                _history = result;
                _historyProblem = null;
                ClearHistoryDetails();
            }
        }
        catch (Exception ex) when (IsClientFailure(ex, token))
        {
            _historyProblem = "Submission history could not be loaded.";
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (_busy || _disposed)
        {
            return;
        }

        _busy = true;
        _problem = null;
        _message = null;
        var token = _lifetime.Token;
        try
        {
            await operation(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Closing the panel cancels the active request.
        }
        finally
        {
            _busy = false;
        }
    }

    // Any failure from the host client becomes a safe message, except our own cancellation.
    private static bool IsClientFailure(Exception ex, CancellationToken token) =>
        !(ex is OperationCanceledException && token.IsCancellationRequested);

    private void ClearSetupResults()
    {
        _keyFile = null;
        ClearPreview();
    }

    private void ClearPreview()
    {
        _preview = null;
        _previewPage = 0;
        _selectedUrls.Clear();
    }

    private Task ToggleDetailsAsync(Guid id)
    {
        if (_busy || _disposed)
        {
            return Task.CompletedTask;
        }

        var wasOpen = _openHistoryItem == id;
        ClearHistoryDetails();
        if (wasOpen)
        {
            return Task.CompletedTask;
        }

        _openHistoryItem = id;
        return RunAsync(async token =>
        {
            try
            {
                var result = await Client.GetSubmissionAsync(id, token);
                if (!_disposed)
                {
                    _historyDetails = result;
                    _historyDownloadUrl = "data:application/json;charset=utf-8;base64,"
                        + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(result, DownloadJsonOptions));
                }
            }
            catch (Exception ex) when (IsClientFailure(ex, token))
            {
                _historyDetailProblem = "Submission details could not be loaded.";
            }
        });
    }

    private void ClearHistoryDetails()
    {
        _openHistoryItem = null;
        _historyDetails = null;
        _historyDownloadUrl = null;
        _historyDetailProblem = null;
    }

    private bool CanRetry(IndexNowSubmissionSummaryDto submission) => CanSubmit && CanRetryStatus(submission);

    private static bool CanRetryStatus(IndexNowSubmissionSummaryDto submission) =>
        submission.Status is IndexNowSubmissionStatus.Failed or IndexNowSubmissionStatus.Partial or IndexNowSubmissionStatus.Cancelled
        || IsInterrupted(submission.Status, submission.CreatedAt);

    private static bool IsInterrupted(IndexNowSubmissionStatus status, DateTimeOffset createdAt) =>
        status == IndexNowSubmissionStatus.InProgress && DateTimeOffset.UtcNow - createdAt > InterruptedSubmissionAge;

    private static string StatusLabel(IndexNowSubmissionStatus status, DateTimeOffset createdAt) => status switch
    {
        IndexNowSubmissionStatus.Received => "Received by IndexNow",
        IndexNowSubmissionStatus.ValidationPending => "Ownership validation pending",
        IndexNowSubmissionStatus.Failed => "Submission failed",
        IndexNowSubmissionStatus.Partial => "Some batches failed",
        IndexNowSubmissionStatus.Cancelled => "Submission cancelled",
        IndexNowSubmissionStatus.InProgress when IsInterrupted(status, createdAt) => "Interrupted or unconfirmed",
        IndexNowSubmissionStatus.InProgress => "Submission in progress",
        _ => "Submission status unavailable"
    };

    private static string ActorName(IndexNowSubmissionSummaryDto submission) =>
        string.IsNullOrWhiteSpace(submission.ActorName) ? "system" : submission.ActorName;

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
