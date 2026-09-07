using Snet.Core.extend;
using Snet.DB;
using Snet.Model.data;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
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
        public UserOperate() : this(PublicHandler.DefaultSN) { }

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
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly SemaphoreSlim _addLock = new(1, 1);
        private const string DefaultAdministratorUsername = "snet";
        private const string DefaultAdministratorPassword = "123456";

        /// <summary>
        /// 初始化（建库、建表与创建种子管理员）。
        /// </summary>
        private async Task<OperateResult> InitAsync(CancellationToken token = default)
        {
            if (_initResult is not null) { return _initResult; }
            await _initLock.WaitAsync(token);
            try
            {
                if (_initResult is not null) { return _initResult; }
                if (!Directory.Exists(DbPath)) { Directory.CreateDirectory(DbPath); }
                var _st = await operate.GetStatusAsync(token);
                if (!_st.Status)
                {
                    var opened = await operate.OnAsync(token);
                    if (!opened.Status) { return opened; }
                }
                var exist = await operate.ExistAsync<UserData>(token);
                if (!exist.Status)
                {
                    var created = await operate.CreateAsync<UserData>(token);
                    if (!created.Status) { return created; }
                }
                var all = await operate.QueryAsync<UserData>(token: token);
                all.GetDetails(out List<UserData>? users);
                if (users is not { Count: > 0 })
                {
                    var bootstrapPassword = ResolveBootstrapPassword();
                    var inserted = await operate.InsertAsync(new UserData { username = DefaultAdministratorUsername, password = Hash(bootstrapPassword), role = "Admin" }, token);
                    if (!inserted.Status) { return inserted; }
                }
                else if (users.FirstOrDefault(user => user.username == DefaultAdministratorUsername) is { } administrator)
                {
                    var configuredPassword = Environment.GetEnvironmentVariable("SNET_BOOTSTRAP_ADMIN_PASSWORD");
                    if (!string.IsNullOrWhiteSpace(configuredPassword) && !Verify(administrator.password, configuredPassword, out _))
                    {
                        Console.Error.WriteLine("[SECURITY] Existing administrator password synchronized from SNET_BOOTSTRAP_ADMIN_PASSWORD.");
                        administrator.password = Hash(configuredPassword);
                        administrator.updateTime = DateTime.Now;
                        var updated = await operate.UpdateAsync(administrator,
                            row => new { row.password, row.updateTime },
                            row => row.index == administrator.index, token);
                        if (!updated.Status) { return updated; }
                    }
                }
                else if (users.Count == 1 && users[0].username == "admin")
                {
                    var legacyAdministrator = users[0];
                    legacyAdministrator.username = DefaultAdministratorUsername;
                    legacyAdministrator.password = Hash(ResolveBootstrapPassword());
                    legacyAdministrator.role = "Admin";
                    legacyAdministrator.active = 1;
                    legacyAdministrator.updateTime = DateTime.Now;
                    var updated = await operate.UpdateAsync(legacyAdministrator,
                        row => new { row.username, row.password, row.role, row.active, row.updateTime },
                        row => row.index == legacyAdministrator.index, token);
                    if (!updated.Status) { return updated; }
                }
                _initResult = OperateResult.CreateSuccessResult("ok");
                return _initResult;
            }
            catch (Exception ex) { return OperateResult.CreateFailureResult(ex.Message); }
            finally { _initLock.Release(); }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> AddAsync(string username, string password, string role, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) { return OperateResult.CreateFailureResult("用户名或密码不能为空。"); }
            if (role is not ("Admin" or "User")) { role = "User"; }
            await _addLock.WaitAsync(token);
            try
            {
                var exists = await operate.QueryAsync<UserData>(u => u.username == username, token);
                if (exists.GetDetails(out List<UserData>? dup) && dup is { Count: > 0 }) { return OperateResult.CreateFailureResult("用户名已存在。"); }
                var user = new UserData { username = username, password = Hash(password), role = role };
                return await operate.InsertAsync(user, token);
            }
            finally { _addLock.Release(); }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> UpdateAsync(int index, string? password, string? role, bool? active, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            var q = await operate.QueryAsync<UserData>(u => u.index == index, token);
            if (!q.GetDetails(out List<UserData>? list) || list is not { Count: > 0 }) { return OperateResult.CreateFailureResult("用户不存在。"); }
            var user = list[0];
            var newPassword = string.IsNullOrWhiteSpace(password) ? user.password : Hash(password);
            var newRole = role ?? user.role;
            var newActive = active.HasValue ? (active.Value ? 1 : 0) : user.active;
            user.password = newPassword;
            user.role = newRole;
            user.active = newActive;
            user.updateTime = DateTime.Now;
            return await operate.UpdateAsync(user, u => new { u.password, u.role, u.active, u.updateTime }, c => c.index == index, token);
        }

        /// <inheritdoc/>
        public async Task<OperateResult> DeleteAsync(int index, CancellationToken token = default)
        {
            var init = await InitAsync(token); if (!init.Status) { return init; }
            return await operate.DeleteAsync<UserData>(u => u.index == index, token);
        }

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
            var q = await operate.QueryAsync<UserData>(predicate, token);
            q.GetDetails(out List<UserData>? qlist);
            return q;
        }

        /// <inheritdoc/>
        public async Task<OperateResult> VerifyAsync(string username, string password, CancellationToken token = default)
        {
            var init = await InitAsync(token);
            if (!init.Status) { return init; }
            var result = await operate.QueryAsync<UserData>(u => u.username == username && u.active == 1, token);
            if (!result.GetDetails(out List<UserData>? users) || users is not { Count: > 0 }) { return OperateResult.CreateFailureResult("用户名或密码错误。"); }
            var user = users[0];
            var okHash = Verify(user.password, password, out var needsUpgrade);
            if (!okHash) { return OperateResult.CreateFailureResult("用户名或密码错误。"); }
            if (needsUpgrade)
            {
                user.password = Hash(password);
                user.updateTime = DateTime.Now;
                await operate.UpdateAsync(user, row => new { row.password, row.updateTime }, row => row.index == user.index, token);
            }
            return OperateResult.CreateSuccessResult("登录成功", new { user.index, user.username, user.role });
        }

        /// <summary>
        /// PBKDF2 哈希
        /// </summary>
        private static string Hash(string plain)
        {
            const int iterations = 210_000;
            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(plain, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            return $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        private static string ResolveBootstrapPassword()
        {
            var password = Environment.GetEnvironmentVariable("SNET_BOOTSTRAP_ADMIN_PASSWORD");
            return string.IsNullOrWhiteSpace(password) ? DefaultAdministratorPassword : password;
        }

        private static bool Verify(string stored, string plain, out bool needsUpgrade)
        {
            needsUpgrade = false;
            try
            {
                int iterations;
                string saltText;
                string hashText;
                if (stored.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal))
                {
                    var parts = stored.Split('$');
                    if (parts.Length != 4 || !int.TryParse(parts[1], out iterations) || iterations is < 10_000 or > 2_000_000) { return false; }
                    saltText = parts[2];
                    hashText = parts[3];
                    needsUpgrade = iterations < 210_000;
                }
                else
                {
                    var parts = stored.Split(':');
                    if (parts.Length != 2) { return false; }
                    iterations = 10_000;
                    saltText = parts[0];
                    hashText = parts[1];
                    needsUpgrade = true;
                }
                var salt = Convert.FromBase64String(saltText);
                var expected = Convert.FromBase64String(hashText);
                if (salt.Length is < 8 or > 64 || expected.Length is < 16 or > 64) { return false; }
                var actual = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(plain, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, expected.Length);
                return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException) { return false; }
            catch (ArgumentException) { return false; }
        }

        /// <inheritdoc/>
        public override void Dispose() { base.Dispose(); }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync() => await base.DisposeAsync();
    }
}
