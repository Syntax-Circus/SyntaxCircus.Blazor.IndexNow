using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace SyntaxCircus.Blazor.IndexNow.Tests;

public sealed class IndexNowEditorTests : IDisposable
{
    private static readonly IndexNowSettingsDto Saved = new(true, "https://example.com", "/sitemap.xml", "/key.txt");

    private readonly BunitContext _context = new();
    private readonly IIndexNowAdminClient _client = Substitute.For<IIndexNowAdminClient>();

    public IndexNowEditorTests()
    {
        _context.Services.AddSingleton(_client);
        _client.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(Saved);
        _client.GetHistoryAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<IndexNowSubmissionSummaryDto>());
    }

    public void Dispose() => _context.Dispose();

    private static IndexNowSubmissionDetailDto Detail(Guid id, IndexNowSubmissionStatus status, params string[] urls) =>
        new(id, DateTimeOffset.UtcNow, status, "manual", urls.Length, null, "Jon", urls, [new IndexNowBatchDto(urls, 200, null)]);

    private static IndexNowSubmissionSummaryDto Summary(Guid id, IndexNowSubmissionStatus status, DateTimeOffset? createdAt = null) =>
        new(id, createdAt ?? DateTimeOffset.UtcNow, status, "manual", 2, null, "Jon");

    [Fact]
    public void Render_LoadsSettingsAndEmptyHistory()
    {
        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-site").GetAttribute("value").ShouldBe("https://example.com");
        cut.Find("#indexnow-sitemap").GetAttribute("value").ShouldBe("/sitemap.xml");
        cut.Find("#indexnow-key-path").GetAttribute("value").ShouldBe("/key.txt");
        cut.Markup.ShouldContain("No IndexNow submissions yet.");
        cut.Find("#indexnow-save").HasAttribute("disabled").ShouldBeTrue();
        cut.Markup.ShouldContain("example.com/key.txt");
    }

    [Fact]
    public void Render_WithoutSavedSettings_UsesSuggestedCanonicalUrl()
    {
        _client.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(new IndexNowSettingsDto(false, "", "/sitemap.xml", "/indexnow-key.txt"));

        var cut = _context.Render<IndexNowEditor>(p => p.Add(x => x.SuggestedCanonicalUrl, "https://suggested.test"));

        cut.Find("#indexnow-site").GetAttribute("value").ShouldBe("https://suggested.test");
        cut.Find("#indexnow-save").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#indexnow-check").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Save_SendsTrimmedSettingsAndShowsMessage()
    {
        _client.SaveSettingsAsync(Arg.Any<IndexNowSettingsDto>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IndexNowSettingsDto>());
        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-site").Input("  https://new.example  ");
        cut.Find("#indexnow-save").Click();

        _client.Received(1).SaveSettingsAsync(
            Arg.Is<IndexNowSettingsDto>(s => s.CanonicalUrl == "https://new.example" && s.IsEnabled && s.KeyFilePath == "/key.txt"),
            Arg.Any<CancellationToken>());
        cut.Find("#indexnow-message").TextContent.ShouldContain("IndexNow settings saved.");
    }

    [Fact]
    public void Save_WhenClientThrows_ShowsSafeMessageWithoutExceptionText()
    {
        _client.SaveSettingsAsync(Arg.Any<IndexNowSettingsDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("secret connection string"));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-site").Input("https://other.example");
        cut.Find("#indexnow-save").Click();

        cut.Find("#indexnow-error").TextContent.ShouldContain("could not be saved");
        cut.Markup.ShouldNotContain("secret connection string");
    }

    [Fact]
    public void Load_WhenClientThrows_ShowsSafeMessage()
    {
        _client.GetSettingsAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("boom 500"));

        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-error").TextContent.ShouldContain("settings could not be loaded");
        cut.Markup.ShouldNotContain("boom 500");
        cut.FindAll("#indexnow-site").Count.ShouldBe(0);
    }

    [Fact]
    public void Preview_PagesByHundredAndSubmitsSelectedAcrossPages()
    {
        var urls = Enumerable.Range(1, 150).Select(i => $"https://example.com/p{i}").ToArray();
        _client.PreviewAsync(Arg.Any<CancellationToken>()).Returns(new IndexNowPreviewDto(urls));
        _client.SubmitAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Detail(Guid.NewGuid(), IndexNowSubmissionStatus.Received, "https://example.com/p1", "https://example.com/p101"));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-check").Click();
        cut.FindAll("#indexnow-preview li").Count.ShouldBe(100);
        cut.Markup.ShouldContain("Page 1 of 2");

        cut.FindAll("#indexnow-preview li input")[0].Change(true);
        cut.Find("#indexnow-next-page").Click();
        cut.FindAll("#indexnow-preview li").Count.ShouldBe(50);
        cut.FindAll("#indexnow-preview li input")[0].Change(true);
        cut.Find("#indexnow-submit-selected").TextContent.ShouldContain("(2)");
        cut.Find("#indexnow-submit-selected").Click();

        _client.Received(1).SubmitAsync(
            Arg.Is<IReadOnlyList<string>>(u => u.SequenceEqual(new[] { "https://example.com/p1", "https://example.com/p101" })),
            Arg.Any<CancellationToken>());
        cut.Find("#indexnow-result").TextContent.ShouldContain("Received by IndexNow");
    }

    [Fact]
    public void SubmitAll_SendsEveryPreviewedUrlAndReloadsHistory()
    {
        var urls = new[] { "https://example.com/a", "https://example.com/b" };
        _client.PreviewAsync(Arg.Any<CancellationToken>()).Returns(new IndexNowPreviewDto(urls));
        var id = Guid.NewGuid();
        _client.SubmitAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Detail(id, IndexNowSubmissionStatus.ValidationPending, urls));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-check").Click();
        cut.Find("#indexnow-submit-all").Click();

        _client.Received(1).SubmitAsync(Arg.Is<IReadOnlyList<string>>(u => u.Count == 2), Arg.Any<CancellationToken>());
        _client.Received(2).GetHistoryAsync(Arg.Any<CancellationToken>());
        cut.Find("#indexnow-result").TextContent.ShouldContain("Ownership validation pending");
    }

    [Fact]
    public void Submit_WhenDisabled_IsNotOffered()
    {
        _client.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(Saved with { IsEnabled = false });
        _client.PreviewAsync(Arg.Any<CancellationToken>()).Returns(new IndexNowPreviewDto(["https://example.com/a"]));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-check").Click();

        cut.Find("#indexnow-submit-all").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Submit_WhenClientThrows_ShowsSafeError()
    {
        _client.PreviewAsync(Arg.Any<CancellationToken>()).Returns(new IndexNowPreviewDto(["https://example.com/a"]));
        _client.SubmitAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("stack trace here"));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-check").Click();
        cut.Find("#indexnow-submit-all").Click();

        cut.Find("#indexnow-error").TextContent.ShouldContain("could not process the submission");
        cut.Markup.ShouldNotContain("stack trace here");
    }

    [Fact]
    public void Preview_WhenClientThrows_ShowsSafeError()
    {
        _client.PreviewAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("nope"));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-check").Click();

        cut.Find("#indexnow-error").TextContent.ShouldContain("could not be checked");
    }

    [Fact]
    public void GenerateKeyFile_OffersDownloadWithFileName()
    {
        _client.GenerateKeyFileAsync(Arg.Any<CancellationToken>()).Returns(new IndexNowKeyFileDto("abc12345.txt", "abc12345"));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-generate").Click();

        var link = cut.Find("#indexnow-key-download");
        link.GetAttribute("download").ShouldBe("abc12345.txt");
        link.GetAttribute("href").ShouldStartWith("data:text/plain;charset=utf-8;base64,");
    }

    [Fact]
    public void History_ListsItemsAndShowsBatchDetailsWithJsonDownload()
    {
        var id = Guid.NewGuid();
        _client.GetHistoryAsync(Arg.Any<CancellationToken>()).Returns([Summary(id, IndexNowSubmissionStatus.Received)]);
        _client.GetSubmissionAsync(id, Arg.Any<CancellationToken>())
            .Returns(Detail(id, IndexNowSubmissionStatus.Received, "https://example.com/a"));
        var cut = _context.Render<IndexNowEditor>();

        cut.Markup.ShouldContain("Received by IndexNow");
        cut.Markup.ShouldContain("by Jon");
        cut.Find($"#indexnow-details-{id}").Click();

        cut.Find($"#indexnow-batches-{id}").TextContent.ShouldContain("HTTP 200");
        cut.Find($"#indexnow-batches-{id} a").GetAttribute("download").ShouldBe($"indexnow-submission-{id}.json");
        cut.Find($"#indexnow-batches-{id} a").GetAttribute("href").ShouldStartWith("data:application/json");

        cut.Find($"#indexnow-details-{id}").Click();
        cut.FindAll($"#indexnow-batches-{id}").Count.ShouldBe(0);
    }

    [Fact]
    public void History_DetailFailure_ShowsSafeMessage()
    {
        var id = Guid.NewGuid();
        _client.GetHistoryAsync(Arg.Any<CancellationToken>()).Returns([Summary(id, IndexNowSubmissionStatus.Received)]);
        _client.GetSubmissionAsync(id, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find($"#indexnow-details-{id}").Click();

        cut.Find($"#indexnow-batches-{id}").TextContent.ShouldContain("could not be loaded");
        cut.Markup.ShouldNotContain("db down");
    }

    [Fact]
    public void History_LoadFailure_ShowsWarning()
    {
        _client.GetHistoryAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        var cut = _context.Render<IndexNowEditor>();

        cut.Find("#indexnow-history-error").TextContent.ShouldContain("history could not be loaded");
    }

    [Fact]
    public void Retry_IsOfferedOnlyForFailedPartialCancelledOrInterrupted()
    {
        var failed = Guid.NewGuid();
        var received = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        var stale = Guid.NewGuid();
        _client.GetHistoryAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Summary(failed, IndexNowSubmissionStatus.Failed),
            Summary(received, IndexNowSubmissionStatus.Received),
            Summary(fresh, IndexNowSubmissionStatus.InProgress),
            Summary(stale, IndexNowSubmissionStatus.InProgress, DateTimeOffset.UtcNow.AddMinutes(-30))
        ]);

        var cut = _context.Render<IndexNowEditor>();

        cut.FindAll($"#indexnow-retry-{failed}").Count.ShouldBe(1);
        cut.FindAll($"#indexnow-retry-{received}").Count.ShouldBe(0);
        cut.FindAll($"#indexnow-retry-{fresh}").Count.ShouldBe(0);
        cut.Find($"#indexnow-retry-{stale}").TextContent.ShouldContain("Retry unconfirmed URLs");
        cut.Markup.ShouldContain("Interrupted or unconfirmed");
    }

    [Fact]
    public void Retry_CallsClientAndShowsResult()
    {
        var id = Guid.NewGuid();
        _client.GetHistoryAsync(Arg.Any<CancellationToken>()).Returns([Summary(id, IndexNowSubmissionStatus.Failed)]);
        _client.RetryAsync(id, Arg.Any<CancellationToken>())
            .Returns(Detail(Guid.NewGuid(), IndexNowSubmissionStatus.Received, "https://example.com/a"));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find($"#indexnow-retry-{id}").Click();

        _client.Received(1).RetryAsync(id, Arg.Any<CancellationToken>());
        cut.Find("#indexnow-result").TextContent.ShouldContain("Received by IndexNow");
    }

    [Fact]
    public void Retry_WhenClientThrows_ShowsSafeError()
    {
        var id = Guid.NewGuid();
        _client.GetHistoryAsync(Arg.Any<CancellationToken>()).Returns([Summary(id, IndexNowSubmissionStatus.Failed)]);
        _client.RetryAsync(id, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("internal"));
        var cut = _context.Render<IndexNowEditor>();

        cut.Find($"#indexnow-retry-{id}").Click();

        cut.Find("#indexnow-error").TextContent.ShouldContain("could not process the retry");
        cut.Markup.ShouldNotContain("internal");
    }

    [Fact]
    public void Panel_TogglesEditorAndPassesSuggestedUrl()
    {
        _client.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(new IndexNowSettingsDto(false, "", "/sitemap.xml", "/k.txt"));
        var cut = _context.Render<IndexNowPanel>(p => p.Add(x => x.SuggestedCanonicalUrl, "https://panel.test"));

        cut.FindAll("#indexnow-site").Count.ShouldBe(0);
        cut.Find("#indexnow-toggle").TextContent.Trim().ShouldBe("Manage IndexNow");

        cut.Find("#indexnow-toggle").Click();

        cut.Find("#indexnow-toggle").TextContent.Trim().ShouldBe("Hide IndexNow");
        cut.Find("#indexnow-toggle").GetAttribute("aria-expanded").ShouldBe("true");
        cut.Find("#indexnow-site").GetAttribute("value").ShouldBe("https://panel.test");

        cut.Find("#indexnow-toggle").Click();
        cut.FindAll("#indexnow-site").Count.ShouldBe(0);
    }
}

public sealed class IndexNowDtoJsonTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Status_SerializesAsString_AndRoundTripsCamelCase()
    {
        var dto = new IndexNowSubmissionSummaryDto(Guid.Empty, DateTimeOffset.UnixEpoch, IndexNowSubmissionStatus.ValidationPending, "manual", 1, null, null);
        var json = JsonSerializer.Serialize(dto, Options);

        json.ShouldContain("\"status\":\"ValidationPending\"");
        json.ShouldContain("\"urlCount\":1");
        JsonSerializer.Deserialize<IndexNowSubmissionSummaryDto>(json, Options).ShouldBe(dto);
    }
}
