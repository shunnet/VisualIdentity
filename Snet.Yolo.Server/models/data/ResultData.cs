using SqlSugar;
namespace Snet.Yolo.Server.models.data
{
    [SugarTable("result")]
    public class ResultData
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }
        public int annotationId { get; set; }
        public int regionIndex { get; set; }
        [SugarColumn(IsNullable = true)]
        public string type { get; set; } = string.Empty;
        [SugarColumn(IsNullable = true)]
        public string fromName { get; set; } = string.Empty;
        [SugarColumn(IsNullable = true)]
        public string toName { get; set; } = string.Empty;
        [SugarColumn(IsNullable = true)]
        public string parentId { get; set; } = string.Empty;
        [SugarColumn(IsNullable = true)]
        public string valueJson { get; set; } = "{}";
        public DateTime createTime { get; set; } = DateTime.Now;
    }
}
