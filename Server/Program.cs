using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public enum DownloadStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public class DownloadItem
{
    public int Id { get; set; }
    public string Url { get; set; }
    public string SavePath { get; set; }
    public DownloadStatus Status { get; set; }
    public double Progress { get; set; }
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
    public string ErrorMessage { get; set; }
    public int ThreadCount { get; set; }
}

public class DownloadManager
{
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<int, (DownloadItem Item, CancellationTokenSource Cts)> downloads;
    private readonly ConcurrentDictionary<string, List<int>> tagIndex;
    private int nextId = 1;

    public DownloadManager()
    {
        httpClient = new HttpClient();
        downloads = new ConcurrentDictionary<int, (DownloadItem, CancellationTokenSource)>();
        tagIndex = new ConcurrentDictionary<string, List<int>>();
    }

    public async Task AddDownloadAsync(string url, string savePath, int threadCount = 1, IEnumerable<string> tags = null)
    {
        var downloadItem = new DownloadItem
        {
            Id = nextId++,
            Url = url,
            SavePath = savePath,
            ThreadCount = threadCount,
            Status = DownloadStatus.Queued,
            Tags = tags?.ToList() ?? new List<string>()
        };

        foreach (var tag in downloadItem.Tags)
        {
            tagIndex.AddOrUpdate(tag,
                new List<int> { downloadItem.Id },
                (_, list) => { list.Add(downloadItem.Id); return list; });
        }

        var cts = new CancellationTokenSource();
        downloads.TryAdd(downloadItem.Id, (downloadItem, cts));

        await DownloadFileAsync(downloadItem, cts.Token);
    }

    private async Task DownloadFileAsync(DownloadItem downloadItem, CancellationToken cancellationToken)
    {
        downloadItem.Status = DownloadStatus.Downloading;
        downloadItem.StartTime = DateTime.Now;

        try
        {
            using var response = await httpClient.GetAsync(downloadItem.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            downloadItem.TotalBytes = response.Content.Headers.ContentLength ?? 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(downloadItem.SavePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;
            long totalRead = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;
                downloadItem.DownloadedBytes = totalRead;
                downloadItem.Progress = downloadItem.TotalBytes > 0 ? (double)totalRead / downloadItem.TotalBytes * 100 : 0;

                UpdateUI(downloadItem);
            }

            downloadItem.Status = DownloadStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            downloadItem.Status = DownloadStatus.Cancelled;
            if (File.Exists(downloadItem.SavePath))
            {
                File.Delete(downloadItem.SavePath);
            }
        }
        catch (Exception ex)
        {
            downloadItem.Status = DownloadStatus.Failed;
            downloadItem.ErrorMessage = ex.Message;
        }
        finally
        {
            downloadItem.EndTime = DateTime.Now;
            UpdateUI(downloadItem);
            downloads.TryRemove(downloadItem.Id, out _);
        }
    }

    public void PauseDownload(int downloadId)
    {
        if (downloads.TryGetValue(downloadId, out var item))
        {
            item.Cts.Cancel();
            item.Item.Status = DownloadStatus.Paused;
            UpdateUI(item.Item);
        }
    }

    public void ResumeDownload(int downloadId)
    {
        if (downloads.TryGetValue(downloadId, out var item) && item.Item.Status == DownloadStatus.Paused)
        {
            var newCts = new CancellationTokenSource();
            downloads[downloadId] = (item.Item, newCts);
            Task.Run(() => DownloadFileAsync(item.Item, newCts.Token));
        }
    }

    public void CancelDownload(int downloadId)
    {
        if (downloads.TryGetValue(downloadId, out var item))
        {
            item.Cts.Cancel();
            item.Item.Status = DownloadStatus.Cancelled;
            UpdateUI(item.Item);
        }
    }

    public IEnumerable<DownloadItem> SearchByTag(string tag)
    {
        if (tagIndex.TryGetValue(tag, out var ids))
        {
            return ids.Select(id => downloads.TryGetValue(id, out var item) ? item.Item : null)
                      .Where(item => item != null);
        }
        return Enumerable.Empty<DownloadItem>();
    }

    public IEnumerable<DownloadItem> GetAllDownloads()
    {
        return downloads.Values.Select(x => x.Item).OrderBy(x => x.Id);
    }

    private void UpdateUI(DownloadItem item)
    {
        Console.Clear();
        Console.WriteLine("=== Download Manager ===");
        Console.WriteLine("ID | URL | Progress | Status");

        foreach (var download in GetAllDownloads())
        {
            Console.WriteLine($"{download.Id} | {Truncate(download.Url, 30)} | {download.Progress:F1}% | {download.Status}");
        }

        Console.WriteLine("\nCommands: add, pause [id], resume [id], cancel [id], search [tag], list, exit");
    }

    private string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }

    public async Task RunAsync()
    {
        Console.WriteLine("Download Manager started. Type 'help' for commands.");

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input)) continue;

            try
            {
                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var command = parts[0].ToLower();

                switch (command)
                {
                    case "add":
                        Console.Write("URL: ");
                        var url = Console.ReadLine()?.Trim();
                        Console.Write("Save path: ");
                        var path = Console.ReadLine()?.Trim();
                        Console.Write("Threads (1): ");
                        var threads = int.TryParse(Console.ReadLine(), out var t) ? t : 1;
                        Console.Write("Tags (comma separated): ");
                        var tags = Console.ReadLine()?.Split(',').Select(x => x.Trim());

                        await AddDownloadAsync(url, path, threads, tags);
                        break;

                    case "pause":
                        if (parts.Length > 1 && int.TryParse(parts[1], out var pauseId))
                            PauseDownload(pauseId);
                        break;

                    case "resume":
                        if (parts.Length > 1 && int.TryParse(parts[1], out var resumeId))
                            ResumeDownload(resumeId);
                        break;

                    case "cancel":
                        if (parts.Length > 1 && int.TryParse(parts[1], out var cancelId))
                            CancelDownload(cancelId);
                        break;

                    case "search":
                        if (parts.Length > 1)
                        {
                            var results = SearchByTag(parts[1]);
                            Console.WriteLine($"Found {results.Count()} downloads:");
                            foreach (var item in results)
                            {
                                Console.WriteLine($"{item.Id}: {item.Url} ({item.Status})");
                            }
                        }
                        break;

                    case "list":
                        foreach (var item in GetAllDownloads())
                        {
                            Console.WriteLine($"{item.Id}: {item.Url} - {item.Progress:F1}% ({item.Status})");
                        }
                        break;

                    case "exit":
                        return;

                    case "help":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("add - Add new download");
                        Console.WriteLine("pause [id] - Pause download");
                        Console.WriteLine("resume [id] - Resume paused download");
                        Console.WriteLine("cancel [id] - Cancel download");
                        Console.WriteLine("search [tag] - Search downloads by tag");
                        Console.WriteLine("list - List all downloads");
                        Console.WriteLine("exit - Exit application");
                        break;

                    default:
                        Console.WriteLine("Unknown command. Type 'help' for available commands.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}

public static class Program
{
    public static async Task Main()
    {
        var manager = new DownloadManager();
        await manager.RunAsync();
    }
}