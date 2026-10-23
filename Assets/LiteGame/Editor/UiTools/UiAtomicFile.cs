using System;
using System.IO;
using System.Text;

namespace LiteGame.Editor.UiTools
{
    /// <summary>
    /// UI 工具生成物的整文件写入（清单/契约共用）：先写同目录临时件，再替换目标。
    /// Unity 导入可能短暂持有目标句柄——`File.Replace` 失败时重试并退化为共享读覆盖；
    /// 任一路径失败都抛原异常，不静默。
    /// </summary>
    public static class UiAtomicFile
    {
        public static void WriteAllText(string fullPath, string contents, Encoding encoding)
        {
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temp = fullPath + ".tmp";
            try
            {
                File.WriteAllText(temp, contents, encoding);
                ReplaceWithRetry(temp, fullPath);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private static void ReplaceWithRetry(string temp, string target)
        {
            if (!File.Exists(target))
            {
                File.Move(temp, target);
                return;
            }

            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    File.Replace(temp, target, null);
                    return;
                }
                catch (IOException exception)
                {
                    last = exception;
                    System.Threading.Thread.Sleep(40);
                }
            }

            try
            {
                using (var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (var source = File.OpenRead(temp))
                {
                    source.CopyTo(stream);
                }
            }
            catch
            {
                throw last ?? new IOException("无法替换文件：" + target);
            }
        }
    }
}
