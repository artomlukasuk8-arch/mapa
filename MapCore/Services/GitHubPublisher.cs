using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MapCore.Services;

public sealed class GitHubPublisher
{
    private static readonly HttpClient Client = new();

    public async Task PublishDirectoryAsync(
        string owner,
        string repository,
        string branch,
        string remoteDirectory,
        string commitMessage,
        string token,
        string localDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Contains('/'))
        {
            throw new ArgumentException("Вкажіть власника GitHub-репозиторію.", nameof(owner));
        }

        if (string.IsNullOrWhiteSpace(repository) || repository.Contains('/'))
        {
            throw new ArgumentException("Вкажіть назву GitHub-репозиторію.", nameof(repository));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Потрібен GitHub token із правом запису Contents.", nameof(token));
        }

        var directory = Path.GetFullPath(localDirectory);
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            throw new InvalidOperationException("Немає файлів для публікації.");
        }

        var remotePrefix = NormalizeRemoteDirectory(remoteDirectory);
        var apiBase = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/contents/";

        for (var i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(directory, files[i]).Replace('\\', '/');
            var repositoryPath = string.IsNullOrEmpty(remotePrefix) ? relativePath : $"{remotePrefix}/{relativePath}";
            var encodedPath = string.Join('/', repositoryPath.Split('/').Select(Uri.EscapeDataString));
            var url = apiBase + encodedPath;
            var fileInfo = new FileInfo(files[i]);
            if (fileInfo.Length > 100L * 1024 * 1024)
            {
                throw new InvalidOperationException($"Файл {relativePath} перевищує ліміт GitHub Contents API у 100 МБ.");
            }

            progress?.Report($"Публікація {i + 1}/{files.Length}: {relativePath}");
            var sha = await GetExistingFileShaAsync(url, branch, token, cancellationToken);
            var content = Convert.ToBase64String(await File.ReadAllBytesAsync(files[i], cancellationToken));
            var payload = new Dictionary<string, object?>
            {
                ["message"] = commitMessage,
                ["content"] = content
            };
            if (!string.IsNullOrWhiteSpace(branch))
            {
                payload["branch"] = branch.Trim();
            }

            if (sha is not null)
            {
                payload["sha"] = sha;
            }

            using var request = CreateRequest(HttpMethod.Put, url, token);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Client.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, relativePath, cancellationToken);
        }
    }

    private static async Task<string?> GetExistingFileShaAsync(
        string url,
        string branch,
        string token,
        CancellationToken cancellationToken)
    {
        var requestUrl = string.IsNullOrWhiteSpace(branch)
            ? url
            : $"{url}?ref={Uri.EscapeDataString(branch.Trim())}";
        using var request = CreateRequest(HttpMethod.Get, requestUrl, token);
        using var response = await Client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "перевірка існуючого файла", cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("sha", out var sha) ? sha.GetString() : null;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("MapBuilder/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string fileName, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = responseBody;
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                detail = message.GetString() ?? responseBody;
            }
        }
        catch (JsonException)
        {
        }

        throw new HttpRequestException($"GitHub не прийняв файл «{fileName}» ({(int)response.StatusCode}): {detail}");
    }

    private static string NormalizeRemoteDirectory(string remoteDirectory)
    {
        var segments = remoteDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains('\\')))
        {
            throw new ArgumentException("Шлях у репозиторії містить недозволені сегменти.", nameof(remoteDirectory));
        }

        return string.Join('/', segments);
    }
}