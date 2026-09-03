using Snet.Core.extend;
using Snet.DB;
using Snet.Model.data;
using Snet.Utility;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;

namespace Snet.Yolo.Server
{
    /// <summary>
    /// 用户管理操作类，基于 SQLite 实现用户的增删改查与登录校验。
    /// </summary>
    public class UserOperate : CoreUnify<UserOperate, string>, IUser, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 无参构造
        /// </summary>
        public UserOperate() : base() { }

        /// <summary>
        /// 有参构造
        /// </summary>
        public UserOperate(string data) : base(data) { }

        /// <inheritdoc/>
        protected override string CN => "用户数据库";

        /// <inheritdoc/>
        protected override string CD => "用户管理与登录";

        /// <summary>
        /// 数据库路径
        /// </summary>
        private readonly string DbPath = Path.Combine(PublicHandler.DefaultPath, "db");

        /// <summary>
        /// 数据库操作对象
        /// </summary>
        private DBOperate operate => DBOperate.Instance(new DBData.Basics
        {
            SN = PublicHandler.DefaultSN,
            ConnectStr = $"Data Source={Path.Combine(DbPath, PublicHandler.DefaultDBName)}",
            DBType = DBData.DBType.SQLite,
            HandlerType = DBData.DBHandlerType.Default
        });

        /// <summary>
        /// 初始化状态
        /// </summary>
        private OperateResult? _initResult;

        /// <summary>
        /// 初始化(建库建表&种子管理员)
        /// </summary>
        private async Task<OperateResult> InitAsync(CancellationToken token = default)
        {
            if (_initResult is not null) { return _initResult; }
            try
            {
                if (!Directory.Exists(DbPath)) { Directory.CreateDirectory(DbPath); }
                OperateResult result = await operate.OnAsync(token);
                if (!(await operate.ExistAsync<UserData>(token)).Status) { await operate.CreateAsync<UserData>(token); }
                var all = await operate.QueryAsync<UserData>(static _ => true, token);
                if (all.GetDetails(out List<UserData>? users) && users is { Count: 0 })
                {
                    await operate.InsertAsync(new UserData { username = "admin", password = Hash("123456"), role = "Admin" }, token);
                    Console.WriteLine("[USER] seeded admin at " + Path.Combine(DbPath, PublicHandler.DefaultDBName));
                }
                _initResult = result;
                return result;
            }
            catch (Exception ex) { return OperateResult.CreateFailureResult(ex.Message); }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> AddAsync(string username, string password, string role, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) { return OperateResult.CreateFailureResult("用户名或密码不能为空。"); }
            if (role is not ("Admin" or "User")) { role = "User"; }
            var exists = await operate.QueryAsync<UserData>(u => u.username == username, token);
            if (exists.GetDetails(out List<UserData>? dup) && dup is { Count: > 0 }) { return OperateResult.CreateFailureResult("用户名已存在。"); }
            var user = new UserData { username = username, password = Hash(password), role = role };
            return await operate.InsertAsync(user, token);
        }

        /// <inheritdoc/>
        public async Task<OperateResult> UpdateAsync(int index, string? password, string? role, bool? active, CancellationToken token = default)
        {
            var q = await operate.QueryAsync<UserData>(u => u.index == index, token);
            if (!q.GetDetails(out List<UserData>? list) || list is not { Count: > 0 }) { return OperateResult.CreateFailureResult("用户不存在。"); }
            var user = list[0];
            var newPassword = string.IsNullOrWhiteSpace(password) ? user.password : Hash(password);
            var newRole = role ?? user.role;
            var newActive = active ?? user.active;
            return await operate.UpdateAsync(user, u => new { password = newPassword, role = newRole, active = newActive, updateTime = DateTime.Now }, c => c.index == index, token);
        }

        /// <inheritdoc/>
        public async Task<OperateResult> DeleteAsync(int index, CancellationToken token = default)
            => await operate.DeleteAsync<UserData>(u => u.index == index, token);

        /// <inheritdoc/>
        public async Task<OperateResult> QueryAsync(int index, CancellationToken token = default)
            => await QueryAsync(u => u.index == index, token);

        /// <inheritdoc/>
        public async Task<OperateResult> QueryAsync(CancellationToken token = default)
            => await QueryAsync(static _ => true, token);

        private async Task<OperateResult> QueryAsync(System.Linq.Expressions.Expression<Func<UserData, bool>> predicate, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) { return init; }
            return await operate.QueryAsync<UserData>(predicate, token);
        }

        /// <inheritdoc/>
        public async Task<OperateResult> VerifyAsync(string username, string password, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) { return init; }
            Console.WriteLine("[USER] Verify " + username);
            var result = await operate.QueryAsync<UserData>(u => u.username == username && u.active, token);
            if (!result.GetDetails(out List<UserData>? users) || users is not { Count: > 0 }) { return OperateResult.CreateFailureResult("用户名或密码错误。"); }
            var user = users[0];
            if (!Verify(user.password, password)) { return OperateResult.CreateFailureResult("用户名或密码错误。"); }
            return OperateResult.CreateSuccessResult("登录成功", new { user.index, user.username, user.role });
        }

        /// <summary>
        /// PBKDF2 哈希
        /// </summary>
        private static string Hash(string plain)
        {
            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(plain, salt, 10000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
        }

        private static bool Verify(string stored, string plain)
        {
            var parts = stored.Split(':');
            if (parts.Length != 2) { return false; }
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(plain, salt, 10000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            return hash.SequenceEqual(expected);
        }

        /// <inheritdoc/>
        public override void Dispose() { base.Dispose(); }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync() => await base.DisposeAsync();
    }
}
