using SqlSugar;
namespace Snet.Yolo.Server.models.data
{
    /// <summary>数据库中的单次任务标注快照。</summary>
    [SugarTable("annotation")]
    public class AnnotationData
    {
        /// <summary>数据库自增主键。</summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }
        /// <summary>所属任务主键。</summary>
        public int taskId { get; set; }
        /// <summary>任务内标注序号。</summary>
        public int annotationIndex { get; set; }
        /// <summary>标记该标注是否已取消。</summary>
        public bool wasCancelled { get; set; }
        /// <summary>最近更新时间。</summary>
        public DateTime updatedAt { get; set; } = DateTime.Now;
        /// <summary>序列化后的标注结果数组。</summary>
        [SugarColumn(IsNullable = true)]
        public string resultJson { get; set; } = "[]";
    }
}
