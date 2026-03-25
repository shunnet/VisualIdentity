namespace Snet.Yolo.Server.handler
{
    /// <summary>
    /// 视觉识别公共工具类，提供默认配置常量、文件大小格式化等功能。
    /// </summary>
    public static class PublicHandler
    {
        /// <summary>
        /// 默认SN
        /// </summary>
        public static readonly string DefaultSN = "Snet.Yolo";

        /// <summary>
        /// 默认路径
        /// </summary>
        public static readonly string DefaultPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        /// <summary>
        /// 默认数据库名称
        /// </summary>
        public static readonly string DefaultDBName = "manage.db";

        /// <summary>
        /// 自动转换文件大小单位，保留两位小数
        /// </summary>
        /// <param name="bytes">字节</param>
        /// <returns>大小</returns>
        public static string GetFileSize(this long bytes)
        {
            const long GB = 1024L * 1024L * 1024L;
            const long MB = 1024L * 1024L;
            const long KB = 1024L;

            if (bytes >= GB)
                return (bytes / (double)GB).ToString("F2") + " GB";
            else if (bytes >= MB)
                return (bytes / (double)MB).ToString("F2") + " MB";
            else if (bytes >= KB)
                return (bytes / (double)KB).ToString("F2") + " KB";
            else
                return bytes + " B";
        }


    }
}
