# SyntaxCircus.Blazor.IndexNow

Bootstrap-based Blazor admin components for managing [IndexNow](https://www.indexnow.org/) submissions: settings, ownership key file, sitemap preview with paged selection, manual submit, submission history with per-batch details, and retry.

The package contains only the UI and the client contract. Your application implements `IIndexNowAdminClient` against its own admin API (typically backed by `SyntaxCircus.AspNetCore.IndexNow`).

## Install

```bash
dotnet add package SyntaxCircus.Blazor.IndexNow
```

## Usage

1. Implement and register the client:

```csharp
using SyntaxCircus.Blazor.IndexNow;

builder.Services.AddScoped<IIndexNowAdminClient, MyIndexNowAdminClient>();
```

`IIndexNowAdminClient` has `GetSettingsAsync`, `SaveSettingsAsync`, `PreviewAsync`, `SubmitAsync`, `GetHistoryAsync`, `GetSubmissionAsync`, `RetryAsync` and `GenerateKeyFileAsync`. Throw on failure (for example `HttpRequestException` from `EnsureSuccessStatusCode`). The components catch the exception and show a fixed, safe message; the exception text is never rendered.

2. Add `@using SyntaxCircus.Blazor.IndexNow` to `_Imports.razor` and drop the panel into an admin page:

```razor
<IndexNowPanel SuggestedCanonicalUrl="https://www.example.com" />
```

`IndexNowPanel` is a collapsible card with a Manage/Hide button (the `Title` parameter changes the heading, default "Search indexing"). To lay out the page yourself, render `<IndexNowEditor SuggestedCanonicalUrl="..." />` directly. The editor needs an interactive render mode (Blazor Server or WebAssembly).

`SuggestedCanonicalUrl` is only offered in the form while no settings are saved and IndexNow is disabled; saving is always explicit.

## DTOs and JSON

The DTOs are sealed records in the `SyntaxCircus.Blazor.IndexNow` namespace and work with the `System.Text.Json` web defaults (camelCase). `IndexNowSubmissionStatus` (`InProgress`, `Received`, `ValidationPending`, `Partial`, `Failed`, `Cancelled`) carries a `JsonStringEnumConverter` attribute so it travels as a string. Use the same records on the server side to keep the wire contract in one place.

## Downloads

The generated ownership file and the submission JSON are offered as `data:` URI links with the `download` attribute. The package therefore needs no JavaScript interop and has no dependency on `SyntaxCircus.Blazor.Components`.

## Styling

Bootstrap 5 classes only (`card`, `btn`, `alert`, `form-control`, `list-group`, and so on). No CSS or JS assets ship with the package.

## Behavior notes

- Submit and retry are available only when settings are saved, enabled and unmodified.
- Retry is offered for `Failed`, `Partial`, `Cancelled`, and `InProgress` submissions older than 11 minutes (shown as "Interrupted or unconfirmed").
- The preview is paged by 100 URLs; selections persist across pages.

## License

MIT
