using SqlSugar;
namespace Snet.Yolo.Server.models.data
{
    [SugarTable("annotation")]
    public class AnnotationData
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }
        public int taskId { get; set; }
        public int annotationIndex { get; set; }
        public bool wasCancelled { get; set; }
        public DateTime updatedAt { get; set; } = DateTime.Now;
        [SugarColumn(IsNullable = true)]
        public string resultJson { get; set; } = "[]";
    }
}
