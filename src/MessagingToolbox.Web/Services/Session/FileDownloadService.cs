using Microsoft.JSInterop;

namespace MessagingToolbox.Web.Services.Session;

/// <summary>
/// Hides the JS interop details of exporting messages to a file download (FR-5.4); the module
/// is imported lazily on first use.
/// </summary>
public sealed class FileDownloadService
{
    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _filesModule;

    public FileDownloadService(IJSRuntime jsRuntime) => _jsRuntime = jsRuntime;

    public async Task DownloadFileAsync(string fileName, Stream contentStream, CancellationToken cancellationToken = default)
    {
        _filesModule ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/files.js");

        using DotNetStreamReference streamReference = new(contentStream);
        await _filesModule.InvokeVoidAsync("downloadFileFromStream", cancellationToken, fileName, streamReference);
    }
}
