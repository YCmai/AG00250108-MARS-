using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace WarehouseManagementSystem.Data
{
    /// <summary>
    /// 数据库连接管理类
    /// </summary>
    public class ApplicationDbContext
    {
        private readonly string _connectionString;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="configuration">配置对象</param>
        public ApplicationDbContext(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new ArgumentNullException(nameof(configuration), "Connection string not found");
        }

        /// <summary>
        /// 获取数据库连接
        /// </summary>
        /// <returns>数据库连接</returns>
        public SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }

        /// <summary>
        /// 获取连接字符串
        /// </summary>
        /// <returns>连接字符串</returns>
        public string GetConnectionString()
        {
            return _connectionString;
        }
    }
} 