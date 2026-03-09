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
        public string BasePath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "details");

        /// <summary>
        /// 格式
        /// </summary>
        public string NameFormat { get; set; }

        /// <summary>
        /// 原图命名格式
        /// </summary>
        public string OriginalImageNamingFormat { get; set; }

        /// <summary>
        /// 结果图命名格式
        /// </summary>
        public string ResultImageNamingFormat { get; set; }

        /// <summary>
        /// 详情命名格式
        /// </summary>
        public string DetailsNamingFormat { get; set; }

        /// <summary>
        /// 识别的数据保留天数，默认30天
        /// </summary>
        public int RetentionDays { get; set; } = 30;
    }
}
