using YoloDotNet.Core;
using YoloDotNet.Models.Interfaces;

namespace Snet.Server.models.data
{
    /// <summary>
    /// 硬件数据
    /// </summary>
    public class HardwareData
    {
        public CpuExecutionProvider CpuExecutionProvider { get; set; }

        public CudaExecutionProvider CudaExecutionProvider { get; set; }

        public TensorRtExecutionProvider TensorRtExecutionProvider { get; set; }

        public IExecutionProvider GetHardware()
        {
            if (this.CpuExecutionProvider != null)
            {
                return this.CpuExecutionProvider;
            }

            if (this.CudaExecutionProvider != null)
            {
                return this.CudaExecutionProvider;
            }

            if (this.TensorRtExecutionProvider != null)
            {
                return this.TensorRtExecutionProvider;
            }
            return null;
        }
    }
}
