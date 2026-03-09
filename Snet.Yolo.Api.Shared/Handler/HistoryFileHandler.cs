using Snet.Core.extend;
using Snet.Log;
using Snet.Yolo.Api.Model;

namespace Snet.Yolo.Api.Handler
{
    /// <summary>
    /// 历史文件处理
    /// </summary>
    public class HistoryFileHandler : CoreUnify<HistoryFileHandler, String>, IDisposable
    {
        /// <summary>
        /// 有参构造函数
        /// </summary>
        /// <param name="basics">基础数据</param>
        public HistoryFileHandler(String basics) : base(basics) { }
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public HistoryFileHandler() : base() { }
        /// <summary>
        /// 配置
        /// </summary>
        private ConfigModel _config;
        /// <summary>
        /// 是否在删除文件夹
        /// </summary>
        private readonly SemaphoreSlim _deleteLock = new(1, 1);

        /// <summary>
        /// 设置配置
        /// </summary>
        /// <param name="config">配置数据</param>
        public void SetConfig(ConfigModel config)
        {
            _config = config;
        }
        /// <summary>
        /// 删除逻辑异步
        /// </summary>
        public async Task DeleteLogicAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // 执行一次清理操作
                    await HistoryLogDeleteAsync(token);

                    // 每 1 小时执行一次
                    await Task.Delay(TimeSpan.FromHours(1), token);
                }
            }
            catch (TaskCanceledException)
            {
                // 任务被取消，正常退出
            }
            catch (OperationCanceledException)
            {
                // 操作被取消，正常退出
            }
            catch (Exception)
            {
                // 其他异常忽略
            }
        }

        /// <summary>
        /// 执行一次历史文件夹删除逻辑
        /// </summary>
        private async Task HistoryLogDeleteAsync(CancellationToken token)
        {
            if (!await _deleteLock.WaitAsync(0, token)) return; // 没拿到锁就退出
            try
            {
                string path = basics;

                if (!Directory.Exists(path)) return;

                foreach (string folder in Directory.GetDirectories(path))
                {
                    string folderName = Path.GetFileName(folder); // 例如 "2025-08-20"

                    // 解析文件夹名为日期
                    if (DateTime.TryParseExact(
                            folderName,
                            "yyyy-MM-dd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime folderDate))
                    {
                        if (_config?.RetentionDays != null)
                        {
                            // 判断是否早于指定保留天数
                            if (folderDate <= DateTime.Today.AddDays(-_config.RetentionDays))
                            {
                                try
                                {
                                    Directory.Delete(folder, true); // 递归删除整个文件夹
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.Error($"删除历史文件：{folder}, 错误：{ex.Message}", exception: ex);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("删除历史文件异常：" + ex.Message, exception: ex);
            }
            finally
            {
                _deleteLock.Release();
            }
        }
    }
}
