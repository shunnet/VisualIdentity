using Snet.Server.models.@enum;
using SqlSugar;

namespace Snet.Server.models.data
{
    /// <summary>
    /// 模型
    /// </summary>
    public class OnnxData
    {
        /// <summary>
        /// 下标<br/>
        /// 无需手动设置(自增)
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int index { get; set; }

        /// <summary>
        /// 名称
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string describe { get; set; }

        /// <summary>
        /// 文件大小<br/>
        /// 无需手动设置
        /// </summary>
        public string size { get; set; }

        /// <summary>
        /// 路径<br/>
        /// 无需手动设置
        /// </summary>
        public string path { get; set; }

        /// <summary>
        /// 模型类型
        /// </summary>
        public OnnxType? onnxType { get; set; }

        /// <summary>
        /// 创建时间<br/>
        /// 无需手动设置
        /// </summary>
        public DateTime createTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间<br/>
        /// 无需手动设置
        /// </summary>
        public DateTime updateTime { get; set; } = DateTime.Now;
    }
}
