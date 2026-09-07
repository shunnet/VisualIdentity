using SqlSugar;

namespace Snet.Yolo.Server.models.data
{
    /// <summary>
    /// 用户数据
    /// </summary>
    [SugarTable("UserData")]
    public class UserData
    {
        /// <summary>
        /// 下标<br/>
        /// 无需手动设置(自增)
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int index { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string username { get; set; } = string.Empty;

        /// <summary>
        /// 密码(哈希)
        /// </summary>
        public string password { get; set; } = string.Empty;

        /// <summary>
        /// 角色(Admin/User)
        /// </summary>
        public string role { get; set; } = "User";

        /// <summary>
        /// 是否启用(1=是, 0=否)
        /// </summary>
        public int active { get; set; } = 1;

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
