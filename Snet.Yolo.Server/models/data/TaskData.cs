using SqlSugar;
namespace Snet.Yolo.Server.models.data
{
    /// <summary>数据库中的项目任务快照。</summary>
    [SugarTable("task")]
    public class TaskData
    {
        /// <summary>数据库自增主键。</summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }
        /// <summary>所属项目数据库主键。</summary>
        public int projectId { get; set; }
        /// <summary>项目内任务序号。</summary>
        public int taskIndex { get; set; }
        /// <summary>序列化后的任务内容。</summary>
        [SugarColumn(IsNullable = true)]
        public string dataJson { get; set; } = "{}";
        /// <summary>创建时间。</summary>
        public DateTime createTime { get; set; } = DateTime.Now;
    }
}
