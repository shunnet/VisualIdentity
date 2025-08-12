using Snet.Server.models.@enum;
using Snet.Unility;
using System.ComponentModel;
using YoloDotNet.Core;
using YoloDotNet.Models.Interfaces;

namespace Snet.Server.models.data
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
        /// 模型路径
        /// </summary>
        [Description("模型路径")]
        public string OnnxPath { get; set; }

        /// <summary>
        /// 识别类型
        /// </summary>
        [Description("识别类型")]
        public OnnxType IdentifyType { get; set; } = OnnxType.ObjectDetection;

        /// <summary>
        /// 硬件<br/>
        /// 使用什么硬件来进行运算<br/>
        /// CPU：new CpuExecutionProvider();<br/>
        /// CUDA：new CudaExecutionProvider(0, false);<br/>
        /// NVIDIA：new TensorRtExecutionProvider();<br/>
        /// </summary>
        [Description("硬件")]
        public IExecutionProvider Hardware { get; set; } = new CpuExecutionProvider();
    }
}
