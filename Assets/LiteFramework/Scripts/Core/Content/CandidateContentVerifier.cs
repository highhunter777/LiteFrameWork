using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>候选内容校验的结论（§7：每文件摘要**必须复算**，不能只信清单自述）。</summary>
    public readonly struct CandidateVerifyResult
    {
        public readonly bool Passed;
        public readonly DownloadFailureInfo Failure;

        /// <summary>已核对文件数（诊断/进度断言）。</summary>
        public readonly int VerifiedFiles;

        /// <summary>已核对总字节（诊断）。</summary>
        public readonly long VerifiedBytes;

        private CandidateVerifyResult(bool passed, DownloadFailureInfo failure, int verifiedFiles, long verifiedBytes)
        {
            Passed = passed;
            Failure = failure;
            VerifiedFiles = verifiedFiles;
            VerifiedBytes = verifiedBytes;
        }

        internal static CandidateVerifyResult Pass(int files, long bytes)
            => new CandidateVerifyResult(true, default, files, bytes);

        internal static CandidateVerifyResult Fail(DownloadFailureInfo failure, int files, long bytes)
            => new CandidateVerifyResult(false, failure, files, bytes);

        public override string ToString()
            => Passed ? $"候选校验通过（{VerifiedFiles} 文件 / {VerifiedBytes} 字节）" : "候选校验失败：" + Failure;
    }

    /// <summary>
    /// 候选内容校验（《热更与内容发布专项设计》§7"只下载固定 Release 的不可变文件"、
    /// §5"文件完整性对实际原始字节计算完整 SHA-256"）。
    ///
    /// **这是信任链的执行侧**：<see cref="ReleaseManifestValidator"/> 只校验描述的**字段形态**
    /// （路径合法、长度非负、摘要形状），从不读文件字节；本类负责把清单与**落盘内容**对上——
    /// 逐文件复算摘要、核对长度、并检出清单与目录的双向差异。
    ///
    /// 顺序（与 §6"先验证描述再依据可信描述计划下载"呼应）：调用方必须先通过
    /// <see cref="ReleaseManifestValidator.Validate"/> 再调用本类——**未验证的清单不可作为下载依据**。
    ///
    /// 本类纯规则 + IO 端口，零 Unity 依赖，L1 全覆盖。
    /// </summary>
    public static class CandidateContentVerifier
    {
        /// <summary>
        /// 校验候选根下内容与清单一致。
        /// </summary>
        /// <param name="manifest">**已通过 <see cref="ReleaseManifestValidator"/> 的**清单。</param>
        /// <param name="source">候选根文件访问端口。</param>
        /// <param name="declaredPaths">
        /// 候选根下实际存在的文件路径集合（相对路径，正斜杠）。
        /// **为 null 表示调用方不支持目录枚举**——此时跳过"清单外杂散文件"检查，
        /// 但**摘要复算仍然执行**（摘要复算是不可省的核心检查）。
        /// </param>
        /// <param name="ct">取消；已在循环边界检查。</param>
        public static CandidateVerifyResult Verify(
            ReleaseManifest manifest,
            ICandidateFileSource source,
            IReadOnlyCollection<string> declaredPaths = null,
            System.Threading.CancellationToken ct = default)
        {
            if (manifest == null)
                return CandidateVerifyResult.Fail(new DownloadFailureInfo(DownloadFailureKind.ReadError, detail: "清单为空"), 0, 0);
            if (source == null)
                return CandidateVerifyResult.Fail(new DownloadFailureInfo(DownloadFailureKind.ReadError, detail: "缺少候选文件端口"), 0, 0);

            List<ReleaseFileEntry> files = manifest.Files ?? new List<ReleaseFileEntry>();
            int verifiedFiles = 0;
            long verifiedBytes = 0;

            // 复用单个 Hasher：批量校验避免每文件一次 SHA256.Create()（ContentHash.Hasher 的设计意图）
            using (var hasher = new ContentHash.Hasher())
            {
                foreach (ReleaseFileEntry entry in files)
                {
                    if (ct.IsCancellationRequested)
                        return CandidateVerifyResult.Fail(
                            new DownloadFailureInfo(DownloadFailureKind.Canceled), verifiedFiles, verifiedBytes);

                    ICandidateFile file = source.Open(entry.Path);
                    if (file == null)
                        return CandidateVerifyResult.Fail(
                            new DownloadFailureInfo(DownloadFailureKind.FileMissing, entry.Path),
                            verifiedFiles, verifiedBytes);

                    // 长度先于摘要：廉价且能立刻拒掉截断/超长，避免为大文件白算一遍摘要
                    if (file.Length != entry.Length)
                        return CandidateVerifyResult.Fail(
                            new DownloadFailureInfo(DownloadFailureKind.LengthMismatch, entry.Path,
                                expected: entry.Length.ToString(), actual: file.Length.ToString()),
                            verifiedFiles, verifiedBytes);

                    byte[] bytes = file.ReadAll();
                    if (bytes == null)
                        return CandidateVerifyResult.Fail(
                            new DownloadFailureInfo(DownloadFailureKind.ReadError, entry.Path, detail: "读取返回空"),
                            verifiedFiles, verifiedBytes);

                    // 端口自报长度可能与实读不符（假件实现错误/文件被并发改写）——以实读为准再核一次
                    if (bytes.LongLength != entry.Length)
                        return CandidateVerifyResult.Fail(
                            new DownloadFailureInfo(DownloadFailureKind.LengthMismatch, entry.Path,
                                expected: entry.Length.ToString(), actual: bytes.LongLength.ToString()),
                            verifiedFiles, verifiedBytes);

                    // 复算摘要（§5 原始字节，不做任何换行归一化）
                    string actual = hasher.ComputeHex(bytes);
                    if (!ContentHash.HexEquals(actual, entry.Sha256))
                        return CandidateVerifyResult.Fail(
                            new DownloadFailureInfo(DownloadFailureKind.DigestMismatch, entry.Path,
                                expected: entry.Sha256, actual: actual),
                            verifiedFiles, verifiedBytes);

                    verifiedFiles++;
                    verifiedBytes += bytes.LongLength;
                }
            }

            // 清单外文件检查（双向差异的"多"一侧）：防多源拼接/残留污染把未声明内容带进候选根。
            // 大小写按 OrdinalIgnoreCase 比较——Windows 文件系统不区分大小写，
            // 清单声明的 "a.lua" 与落盘的 "A.lua" 在真实平台上无法区分，必须按同一口径判定重复/杂散。
            if (declaredPaths != null)
            {
                var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Count; i++) expected.Add(Normalize(files[i].Path));

                foreach (string actualPath in declaredPaths)
                {
                    if (ct.IsCancellationRequested)
                        return CandidateVerifyResult.Fail(
                            new DownloadFailureInfo(DownloadFailureKind.Canceled), verifiedFiles, verifiedBytes);

                    if (!expected.Contains(Normalize(actualPath)))
                        return CandidateVerifyResult.Fail(
                            new DownloadFailureInfo(DownloadFailureKind.UnexpectedFile, actualPath,
                                detail: "清单未声明的文件（多源拼接/残留污染防护）"),
                            verifiedFiles, verifiedBytes);
                }
            }

            return CandidateVerifyResult.Pass(verifiedFiles, verifiedBytes);
        }

        /// <summary>
        /// 检出清单自身的路径重复（大小写/分隔符规范化后同名）。
        /// <see cref="ReleaseManifestValidator"/> 已做**精确**重复检查；本方法补上平台归一化口径——
        /// 清单里同时声明 "a/b.lua" 与 "A/B.lua" 在 Windows 上是同一个文件，会让一份候选覆盖另一份。
        /// </summary>
        public static bool TryFindDuplicatePath(ReleaseManifest manifest, out string duplicated)
        {
            duplicated = null;
            if (manifest?.Files == null) return false;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReleaseFileEntry f in manifest.Files)
            {
                if (f?.Path == null) continue;
                if (!seen.Add(Normalize(f.Path))) { duplicated = f.Path; return true; }
            }
            return false;
        }

        /// <summary>路径归一化（比较用）：分隔符统一为正斜杠。</summary>
        private static string Normalize(string path)
            => path == null ? string.Empty : path.Replace('\\', '/');
    }
}
