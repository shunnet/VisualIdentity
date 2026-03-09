using Snet.Utility;
using Snet.Yolo.Server.models.@enum;
using System.ComponentModel;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 识别数据
    /// </summary>
    public class IdentityData
    {
        /// <summary>
        /// 唯一标识符
        /// </summary>
        [Description("唯一标识符")]
        public string? SN { get; set; } = Guid.NewGuid().ToUpperNString();

        /// <summary>
        /// 识别类型
        /// </summary>
        [Description("识别类型")]
        public OnnxType IdentifyType { get; set; } = OnnxType.ObjectDetection;

        /// <summary>
        /// 硬件<br/>
        /// 使用什么硬件来进行运算
        /// </summary>
        [Description("硬件")]
        public IExecutionProvider Hardware { get; set; }
    }
}
