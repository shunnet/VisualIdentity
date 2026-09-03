using Snet.DB.sugar;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 工程数据
    /// </summary>
    [SugarTable("project")]
    public class ProjectData
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }

        /// <summary>
        /// 工程唯一标识
        /// </summary>
        public string projectId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 工程名
        /// </summary>
        public string name { get; set; } = string.Empty;

        /// <summary>
        /// 描述
        /// </summary>
        public string? describe { get; set; }

        /// <summary>
        /// 标签叠加层透明度
        /// </summary>
        public double overlayOpacity { get; set; } = 0.25;

        /// <summary>
        /// 标注配置 XML
        /// </summary>
        public string labelConfigXml { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime createTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime updateTime { get; set; } = DateTime.Now;
    }
}
