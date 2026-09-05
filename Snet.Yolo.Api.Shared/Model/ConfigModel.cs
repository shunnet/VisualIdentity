namespace Snet.Yolo.Api.Model
{
    /// <summary>
    /// 配置数据
    /// </summary>
    public class ConfigModel
    {
        /// <summary>
        /// 基础路径
        /// </summary>
        public string BasePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "wwwroot", "details");

        /// <summary>
        /// 格式
        /// </summary>
        public string NameFormat { get; set; } = "yyyyMMddHHmmssffffff";

        /// <summary>
        /// 原图命名格式
        /// </summary>
        public string OriginalImageNamingFormat { get; set; } = "{0}-Original.jpeg";

        /// <summary>
        /// 结果图命名格式
        /// </summary>
        public string ResultImageNamingFormat { get; set; } = "{0}-Result.jpeg";

        /// <summary>
        /// 详情命名格式
        /// </summary>
        public string DetailsNamingFormat { get; set; } = "{0}-Details.ini";

        /// <summary>
        /// 识别的数据保留天数，默认30天
        /// </summary>
        public int RetentionDays { get; set; } = 30;

        /// <summary>
        /// 单张推理图片允许的最大字节数，默认 100 MB。
        /// </summary>
        public long MaxImageBytes { get; set; } = 100L * 1024 * 1024;

        /// <summary>
        /// 单个模型文件允许的最大字节数，默认 1 GB。
        /// </summary>
        public long MaxModelBytes { get; set; } = 1024L * 1024 * 1024;
    }
}
