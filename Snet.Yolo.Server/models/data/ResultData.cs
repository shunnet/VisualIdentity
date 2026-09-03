using Snet.DB.sugar;
namespace Snet.Yolo.Server.models.data
{
    [SugarTable("result")]
    public class ResultData
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }
        public int annotationId { get; set; }
        public int regionIndex { get; set; }
        public string type { get; set; } = string.Empty;
        public string fromName { get; set; } = string.Empty;
        public string toName { get; set; } = string.Empty;
        public string parentId { get; set; } = string.Empty;
        public string valueJson { get; set; } = "{}";
        public DateTime createTime { get; set; } = DateTime.Now;
    }
}
