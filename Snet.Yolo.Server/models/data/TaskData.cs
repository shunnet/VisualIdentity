using SqlSugar;
namespace Snet.Yolo.Server.models.data
{
    [SugarTable("task")]
    public class TaskData
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }
        public int projectId { get; set; }
        public int taskIndex { get; set; }
        [SugarColumn(IsNullable = true)]
        public string dataJson { get; set; } = "{}";
        public DateTime createTime { get; set; } = DateTime.Now;
    }
}
