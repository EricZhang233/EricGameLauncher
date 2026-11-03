using System.Net.Http;

namespace EricGameLauncher;

internal static class UpdateDownload
{
    internal static async Task DownloadAsync(HttpClient client, string url, Stream destination, Action<double>? progress,
        TimeSpan totalTimeout, TimeSpan idleTimeout, CancellationToken cancellationToken = default)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(totalTimeout);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
        idle.CancelAfter(idleTimeout);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, idle.Token).WaitAsync(idle.Token);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadAsStreamAsync(idle.Token);
        byte[] buffer = new byte[81920];
        long received = 0;
        while (true)
        {
            idle.CancelAfter(idleTimeout);
            int count = await body.ReadAsync(buffer.AsMemory(), idle.Token).AsTask().WaitAsync(idle.Token);
            if (count == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, count), idle.Token);
            received += count;
            if (response.Content.Headers.ContentLength is long length && length > 0) progress?.Invoke(received * 100d / length);
        }
        if (response.Content.Headers.ContentLength is long expected && received != expected) throw new EndOfStreamException();
    }
}
