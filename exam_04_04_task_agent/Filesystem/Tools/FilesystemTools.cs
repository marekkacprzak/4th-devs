using Filesystem.Services;

namespace Filesystem.Tools;

public class FilesystemTools
{
    private readonly CentralaApiClient _centrala;
    private readonly HttpClient _http;

    public FilesystemTools(CentralaApiClient centrala, HttpClient http)
    {
        _centrala = centrala;
        _http = http;
    }

    public Task<string> Help() =>
        _centrala.VerifyAsync(new { action = "help" });

    public Task<string> Reset() =>
        _centrala.VerifyAsync(new { action = "reset" });

    public Task<string> Done() =>
        _centrala.VerifyAsync(new { action = "done" });

    public Task<string> ListFiles(string path = "/") =>
        _centrala.VerifyAsync(new { action = "listFiles", path });

    public Task<string> CreateDir(string path) =>
        _centrala.VerifyAsync(new { action = "createDirectory", path });

    public Task<string> CreateFile(string path, string content) =>
        _centrala.VerifyAsync(new { action = "createFile", path, content });

    public Task<string> BatchExecute(object[] operations) =>
        _centrala.VerifyBatchAsync(operations);

    public Task<byte[]> DownloadNotesZip(string url) =>
        _http.GetByteArrayAsync(url);
}
