using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace LiteFramework
{
    /// <summary>
    /// 本地持久化门面(静态,豁免名单第三位:Log / ReferencePool / FileSys)。装配:GameEntry.Awake 调 Init。
    /// 契约(写给未来读代码的人):
    /// ① relPath 禁 .. 段、盘符、绝对开头——**release 也校验**(字符级零分配):越界路径可能来自配置/
    ///    外部输入,属输入校验,不适用"信任 Debug"剥离模型;
    /// ② 原子写:tmp 写完再替换,进程被杀目标仍是旧档;残留 .tmp = 上次半截写,下次写自然覆盖;
    ///    同一文件并发写 = 违例(存档走主线程流程;异步逃生口按单飞使用);
    /// ③ JSON 二分口径:不存在 → default 静默(**首启是正常状态**);损坏 → default + Log.Error(事故可见);
    ///    需要区分两者(版本迁移)用 TryReadJson——detail = "not_found" / "parse: ...",处置权在调用方;
    /// ④ 同步是主路径(偏好/存档小文件,主线程等微秒级 IO),异步是逃生口(大文件/归档/不能卡帧)——
    ///    元数据操作(Exists/Delete/GetFiles)无异步;
    /// ⑤ 异步签名用 BCL Task:Core 零依赖纪律(csproj 同步摩擦 + Unity 类型不进纯 C# 程序集),
    ///    业务侧 .AsUniTask() 转签名(§7.7)。
    /// </summary>
    public static class FileSys
    {
        private static IPathProvider s_path;
        private static IJsonSerializer s_json;

        /// <summary>装配唯一入口,一切消费之前(消费者全部晚于 Launch 解析;Editor domain reload 静态复位后由 Awake 重调)。</summary>
        public static void Init(IPathProvider provider, IJsonSerializer serializer)
        {
            s_path = provider ?? throw new ArgumentNullException(nameof(provider));
            s_json = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        // ---- 路径 ----

        /// <summary>热路径两段组合(零分配):PathOf("save", "player.json")。</summary>
        public static string PathOf(string first, string second)
        {
            EnsureInit();
            ValidateRelPath(first);
            ValidateRelPath(second);
            return Path.Combine(Path.Combine(s_path.RootPath, first), second);
        }

        /// <summary>低频多段组合(每次分配一个数组——仅初始化/低频路径使用)。</summary>
        public static string PathOf(params string[] parts)
        {
            EnsureInit();
            string p = s_path.RootPath;
            for (int i = 0; i < parts.Length; i++)
            {
                ValidateRelPath(parts[i]);
                p = Path.Combine(p, parts[i]);
            }
            return p;
        }

        // ---- 文本 ----

        public static void WriteAllText(string relPath, string content)
        {
            EnsureInit();
            string path = Combine(relPath);
            EnsureDirFor(path);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            CommitAtomic(path, tmp);
        }

        /// <summary>不存在 → null(存在性查询是合法语义,调用方据此走首启分支)。</summary>
        public static string ReadAllText(string relPath)
        {
            EnsureInit();
            string path = Combine(relPath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        // ---- 字节(候选内容用) ----

        /// <summary>
        /// 原子写字节。**候选文件含二进制**(资源/Lua 字节码/任意清单项),文本通道不能用。
        /// 与 <see cref="WriteAllText"/> 同款:临时文件 + 元数据级原子提交(§8"不能把普通覆盖写文件称为原子事务")。
        /// </summary>
        public static void WriteAllBytes(string relPath, byte[] bytes)
        {
            EnsureInit();
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            string path = Combine(relPath);
            EnsureDirFor(path);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            CommitAtomic(path, tmp);
        }

        /// <summary>不存在 → null(与 <see cref="ReadAllText"/> 同语义)。</summary>
        public static byte[] ReadAllBytes(string relPath)
        {
            EnsureInit();
            string path = Combine(relPath);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        /// <summary>文件字节长度;不存在 → -1(区别于 0 字节的空文件)。</summary>
        public static long GetFileLength(string relPath)
        {
            EnsureInit();
            string path = Combine(relPath);
            return File.Exists(path) ? new FileInfo(path).Length : -1L;
        }

        /// <summary>
        /// 向文件末尾追加字节(临时文件续传用);不存在则创建。目录自动建立。
        ///
        /// **非原子**——与 <see cref="WriteAllBytes"/> 的区别是有意为之:续传临时文件允许中途留存,
        /// 由调用方在完成时整体校验摘要后原子提交(<c>WriteAllBytes</c> 走临时+Replace)。
        /// 半截临时文件本身不是可用候选,校验会拒它。
        /// </summary>
        public static void AppendAllBytes(string relPath, byte[] bytes)
        {
            EnsureInit();
            if (bytes == null || bytes.Length == 0) return;
            string path = Combine(relPath);
            EnsureDirFor(path);
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
                stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// 递归枚举目录下**所有文件**(相对 RootPath、正斜杠)。目录不存在 → 空数组。
        ///
        /// 与 <see cref="GetFiles"/> 的区别:后者非递归、且刻意不带通配符 pattern
        /// (跨平台大小写行为不一致)。本方法供候选根全量清点用——候选校验需要"清单 vs 落盘"的
        /// 双向差异,必须看到全部层级。
        /// </summary>
        public static string[] GetFilesRecursive(string relDir)
        {
            EnsureInit();
            string dir = Combine(relDir);
            if (!Directory.Exists(dir)) return Array.Empty<string>();

            string[] full = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            var result = new string[full.Length];
            int prefixLen = s_path.RootPath.Length + 1;
            for (int i = 0; i < full.Length; i++)
                result[i] = full[i].Substring(prefixLen).Replace('\\', '/');
            return result;
        }

        /// <summary>
        /// 递归删除目录(候选清理用)。目录不存在 = 幂等成功。
        /// **越界保护**:<see cref="Combine"/> 已拒绝绝对路径/盘符/`..` 段,故不可能删到 RootPath 之外。
        /// </summary>
        public static void DeleteDirectory(string relDir)
        {
            EnsureInit();
            if (string.IsNullOrEmpty(relDir)) throw new ArgumentException("禁删除根目录", nameof(relDir));
            string dir = Combine(relDir);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        /// <summary>目录是否存在(判定候选根是否已建立)。</summary>
        public static bool DirectoryExists(string relDir)
        {
            EnsureInit();
            return !string.IsNullOrEmpty(relDir) && Directory.Exists(Combine(relDir));
        }

        // ---- JSON ----

        public static void WriteJson<T>(string relPath, T value)
        {
            EnsureInit();
            WriteAllText(relPath, s_json.Serialize(value));
        }

        /// <summary>便捷层:不存在 → default 静默(首启);损坏 → default + Log.Error。需区分用 TryReadJson。</summary>
        public static T ReadJson<T>(string relPath)
        {
            EnsureInit();
            bool ok = TryReadJsonCore<T>(relPath, out var v, out var detail);
            if (!ok && detail != "not_found")
                Log.Error($"JSON 损坏 {relPath}({detail})——按 default 继续;版本迁移请用 TryReadJson", "FileSys");
            return v;
        }

        /// <summary>
        /// 显式层,不写日志(处置权在调用方——版本迁移/坏档恢复)。
        /// false 时 detail = "not_found"(正常)或 "parse: ..."(事故)。catch Exception 而非 JsonException:
        /// 接口不承诺实现抛什么。
        /// </summary>
        public static bool TryReadJson<T>(string relPath, out T value, out string detail)
        {
            EnsureInit();
            return TryReadJsonCore(relPath, out value, out detail);
        }

        private static bool TryReadJsonCore<T>(string relPath, out T value, out string detail)
        {
            value = default;
            detail = null;
            string json = ReadAllText(relPath);
            if (json == null) { detail = "not_found"; return false; }
            try { value = s_json.Deserialize<T>(json); return true; }
            catch (Exception ex) { detail = "parse: " + ex.Message; return false; }
        }

        // ---- 元数据 ----

        public static bool Exists(string relPath)
        {
            EnsureInit();
            return File.Exists(Combine(relPath));
        }

        /// <summary>不存在 = no-op(清理路径幂等,与事件注销同款宽容)。仅限文件。</summary>
        public static void Delete(string relPath)
        {
            EnsureInit();
            string path = Combine(relPath);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// 返回**相对 RootPath** 的路径(统一正斜杠,可回喂给本类任何 API);目录不存在 → 空数组。
        /// **无通配符 pattern 参数**:Directory.GetFiles 的模式匹配跨平台大小写行为不一致(Windows 忽略,Linux/Android 敏感)——
        /// 按扩展名过滤在调用方代码做(Path.GetExtension + OrdinalIgnoreCase),确定性优先。
        /// </summary>
        public static string[] GetFiles(string relDir)
        {
            EnsureInit();
            string dir = Combine(relDir);
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            string[] full = Directory.GetFiles(dir);
            var result = new string[full.Length];
            int prefixLen = s_path.RootPath.Length + 1;
            for (int i = 0; i < full.Length; i++)
                result[i] = full[i].Substring(prefixLen).Replace('\\', '/');
            return result;
        }

        // ---- 异步(逃生口):原子性保留——tmp 异步写完,Replace 元数据级同步提交(微秒) ----

        /// <summary>同一文件的 in-flight 登记表:并发异步写同一文件 = .tmp 冲突 = 静默损坏,Debug 与 release 都告警(成本可忽略,可见性值得)。
        /// 无锁防护——纪律仍是调用方对同一文件 await 串行,这里只负责让违例可见。</summary>
        private static readonly HashSet<string> s_inFlight = new HashSet<string>(StringComparer.Ordinal);

        public static async Task WriteAllTextAsync(string relPath, string content, CancellationToken ct = default)
        {
            EnsureInit();
            string path = Combine(relPath);
            ct.ThrowIfCancellationRequested();
            EnsureDirFor(path);
            string tmp = path + ".tmp";
            lock (s_inFlight)
            {
                if (!s_inFlight.Add(path))
                    Log.Error($"同一文件并发异步写 \"{relPath}\"——await 串行同一文件,否则 .tmp 冲突静默损坏", "FileSys");
            }
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                using (var sw = new StreamWriter(fs))
                {
                    await sw.WriteAsync(content);   // 单文档单次写不可中断——ct 在步骤边界生效
                    await sw.FlushAsync();
                }
                CommitAtomic(path, tmp);
            }
            finally { lock (s_inFlight) s_inFlight.Remove(path); }
        }

        /// <summary>不存在 → null(与同步口径一致)。</summary>
        public static async Task<string> ReadAllTextAsync(string relPath, CancellationToken ct = default)
        {
            EnsureInit();
            string path = Combine(relPath);
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            using (var sr = new StreamReader(fs))
            {
                string result = await sr.ReadToEndAsync();
                ct.ThrowIfCancellationRequested();
                return result;
            }
        }

        /// <summary>序列化下线程池(Task.Run)——大文档不卡帧;因此 IJsonSerializer 实现必须线程安全。</summary>
        public static async Task WriteJsonAsync<T>(string relPath, T value, CancellationToken ct = default)
        {
            EnsureInit();
            ValidateRelPath(relPath);                                        // 早失败,不白序列化
            string json = await Task.Run(() => s_json.Serialize(value), ct);
            await WriteAllTextAsync(relPath, json, ct);
        }

        /// <summary>口径与同步 ReadJson 一致:不存在 → default 静默;损坏 → default + Log.Error。
        /// 取消不是损坏——OperationCanceledException 先放行,不进损坏分支。</summary>
        public static async Task<T> ReadJsonAsync<T>(string relPath, CancellationToken ct = default)
        {
            EnsureInit();
            string json = await ReadAllTextAsync(relPath, ct);
            if (json == null) return default;
            try { return await Task.Run(() => s_json.Deserialize<T>(json), ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Error($"JSON 损坏 {relPath}({ex.Message})——按 default 继续", "FileSys");
                return default;
            }
        }

        // ---- 内部 ----

        private static void EnsureInit()
        {
            if (s_path == null) throw new InvalidOperationException("FileSys 未 Init——装配顺序违例");
        }

        private static string Combine(string relPath)
        {
            ValidateRelPath(relPath);
            return Path.Combine(s_path.RootPath, relPath);
        }

        /// <summary>字符级零分配扫描:禁 .. 段(只认段边界,"a..b.json" 合法)、盘符、绝对开头。release 保留。</summary>
        private static void ValidateRelPath(string p)
        {
            if (string.IsNullOrEmpty(p)) throw new ArgumentException("路径段为空");
            if (p[0] == '/' || p[0] == '\\') throw new ArgumentException($"禁绝对路径:{p}");
            if (p.Length > 1 && p[1] == ':') throw new ArgumentException($"禁盘符:{p}");
            for (int i = 0; i < p.Length - 1; i++)
            {
                if (p[i] == '.' && p[i + 1] == '.'
                    && (i == 0 || p[i - 1] == '/' || p[i - 1] == '\\')
                    && (i + 2 == p.Length || p[i + 2] == '/' || p[i + 2] == '\\'))
                    throw new ArgumentException($"禁 .. 越界:{p}");
            }
        }

        private static void EnsureDirFor(string path)   // File.WriteAllText 不建目录——首笔存档必炸,补上
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static void CommitAtomic(string path, string tmp)
        {
            if (File.Exists(path)) File.Replace(tmp, path, null);   // Replace 目标不存在时抛——必须分支
            else File.Move(tmp, path);
        }
    }
}
