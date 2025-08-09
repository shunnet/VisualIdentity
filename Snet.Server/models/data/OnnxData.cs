using Snet.Server.models.@enum;

namespace Snet.Server.models.data
{
    /// <summary>
    /// 模型
    /// </summary>
    public class OnnxData
    {
        /// <summary>
        /// 下标
        /// </summary>
        public int index { get; set; }

        /// <summary>
        /// 路径
        /// </summary>
        public string path { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string describe { get; set; }

        /// <summary>
        /// 模型类型
        /// </summary>
        public OnnxType onnxType { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime createTime { get; set; }
    }
}
