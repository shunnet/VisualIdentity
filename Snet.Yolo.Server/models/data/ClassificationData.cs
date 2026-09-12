using Snet.Yolo.Server.@interface;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 分类
    /// </summary>
    public class ClassificationData : IData
    {
        /// <summary>创建使用默认参数的分类输入。</summary>
        public ClassificationData() { }
        /// <summary>创建指定图片与返回类别数的分类输入。</summary>
        /// <param name="file">待分类图片字节。</param>
        /// <param name="classes">需要返回的类别数量。</param>
        public ClassificationData(byte[] file, int classes = 1)
        {
            this.File = file;
            this.Classes = classes;
        }
        /// <summary>
        /// 传进来的图片
        /// </summary>
        public byte[] File { get; set; } = Array.Empty<byte>();
        /// <summary>
        /// 分类
        /// </summary>
        public int Classes { get; set; } = 1;
    }
}
