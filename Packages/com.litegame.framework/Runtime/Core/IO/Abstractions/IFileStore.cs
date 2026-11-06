using System.Threading;
using System.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 本地持久化端口（B0/B1）：路径安全 + 文本/字节/JSON 读写 + 目录与磁盘余量。
    /// </summary>
    public interface IFileStore
    {
        string PathOf(string first, string second);
        string PathOf(params string[] parts);

        bool Exists(string relPath);
        string ReadAllText(string relPath);
        byte[] ReadAllBytes(string relPath);
        void WriteAllText(string relPath, string content);
        void WriteAllBytes(string relPath, byte[] bytes);
        void AppendAllBytes(string relPath, byte[] bytes);
        void Delete(string relPath);
        long GetFileLength(string relPath);

        string[] GetFiles(string relDir);
        string[] GetFilesRecursive(string relDir);
        bool DirectoryExists(string relDir);
        void DeleteDirectory(string relDir);

        void WriteJson<T>(string relPath, T value);
        T ReadJson<T>(string relPath);
        bool TryReadJson<T>(string relPath, out T value, out string detail);

        Task WriteAllTextAsync(string relPath, string content, CancellationToken ct = default);
        Task<string> ReadAllTextAsync(string relPath, CancellationToken ct = default);
        Task WriteJsonAsync<T>(string relPath, T value, CancellationToken ct = default);
        Task<T> ReadJsonAsync<T>(string relPath, CancellationToken ct = default);

        long GetAvailableBytes(string relDir);
        long WriteProbe(string relDir, int blockBytes = 4 * 1024 * 1024, long capBytes = 512L * 1024 * 1024);
    }
}
