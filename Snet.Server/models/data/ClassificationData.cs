using Snet.Server.@interface;

namespace Snet.Server.models.data
{
    /// <summary>
    /// 分类
    /// </summary>
    public class ClassificationData : IData
    {
        public ClassificationData() { }
        public ClassificationData(byte[] file, int classes = 1)
        {
            this.File = file;
            this.Classes = classes;
        }
        /// <summary>
        /// 传进来的图片
        /// </summary>
        public byte[] File { get; set; }
        /// <summary>
        /// 分类
        /// </summary>
        public int Classes { get; set; } = 1;
    }
}
