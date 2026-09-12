using SqlSugar;
namespace Snet.Yolo.Server.models.data
{
    /// <summary>数据库中的单条标注区域结果。</summary>
    [SugarTable("result")]
    public class ResultData
    {
        /// <summary>数据库自增主键。</summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }
        /// <summary>所属标注主键。</summary>
        public int annotationId { get; set; }
        /// <summary>标注内区域序号。</summary>
        public int regionIndex { get; set; }
        /// <summary>区域结果类型。</summary>
        [SugarColumn(IsNullable = true)]
        public string type { get; set; } = string.Empty;
        /// <summary>来源控件名称。</summary>
        [SugarColumn(IsNullable = true)]
        public string fromName { get; set; } = string.Empty;
        /// <summary>目标对象控件名称。</summary>
        [SugarColumn(IsNullable = true)]
        public string toName { get; set; } = string.Empty;
        /// <summary>父区域标识。</summary>
        [SugarColumn(IsNullable = true)]
        public string parentId { get; set; } = string.Empty;
        /// <summary>序列化后的区域值。</summary>
        [SugarColumn(IsNullable = true)]
        public string valueJson { get; set; } = "{}";
        /// <summary>创建时间。</summary>
        public DateTime createTime { get; set; } = DateTime.Now;
    }
}
