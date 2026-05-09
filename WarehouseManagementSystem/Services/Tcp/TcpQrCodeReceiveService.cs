using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Dapper;
using WarehouseManagementSystem.Db;

namespace WarehouseManagementSystem.Services.Tcp
{
    public class TcpQrCodeReceiveService : BackgroundService
    {
        private const string ErrorMessage = "ERROR";
        private const string FinishedMessage = "Finished";

        private readonly IDatabaseService _db;
        private readonly ILogger<TcpQrCodeReceiveService> _logger;
        private readonly TcpQrCodeServerOptions _options;
        private TcpListener? _listener;

        public TcpQrCodeReceiveService(
            IDatabaseService db,
            IConfiguration configuration,
            ILogger<TcpQrCodeReceiveService> logger)
        {
            _db = db;
            _logger = logger;
            _options = configuration.GetSection("TcpQrCodeServer").Get<TcpQrCodeServerOptions>()
                ?? new TcpQrCodeServerOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("TCP扫码接收服务未启用");
                return;
            }

            var listenAddress = ResolveListenAddress();
            _listener = new TcpListener(listenAddress, _options.Port);

            try
            {
                _listener.Start(_options.Backlog);
                _logger.LogInformation(
                    "TCP扫码接收服务已启动，监听地址 {ListenAddress}:{Port}，Backlog={Backlog}",
                    listenAddress,
                    _options.Port,
                    _options.Backlog);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                    _ = Task.Run(() => HandleClientAsync(client, stoppingToken), CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("TCP扫码接收服务正在停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TCP扫码接收服务异常退出");
            }
            finally
            {
                _listener?.Stop();
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _listener?.Stop();
            return base.StopAsync(cancellationToken);
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            var clientIp = remoteEndPoint?.Address.ToString() ?? string.Empty;
            var clientLabel = remoteEndPoint?.ToString() ?? "Unknown";

            using (client)
            {
                client.ReceiveBufferSize = _options.ReceiveBufferSize;
                client.SendBufferSize = 4096;

                _logger.LogInformation("扫码客户端已连接：{ClientEndPoint}", clientLabel);

                try
                {
                    var stream = client.GetStream();
                    var buffer = new byte[_options.ReceiveBufferSize];

                    while (!stoppingToken.IsCancellationRequested && client.Connected)
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ReceiveTimeoutSeconds));

                        var length = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
                        if (length == 0)
                        {
                            _logger.LogInformation("扫码客户端主动断开：{ClientEndPoint}", clientLabel);
                            break;
                        }

                        var rawMessage = Encoding.UTF8.GetString(buffer, 0, length);
                        var message = NormalizeMessage(rawMessage);

                        if (string.IsNullOrWhiteSpace(message))
                        {
                            _logger.LogWarning("收到空扫码数据：Client={ClientEndPoint}, Length={Length}", clientLabel, length);
                            await SendAsync(stream, ErrorMessage, stoppingToken);
                            continue;
                        }

                        _logger.LogInformation(
                            "收到扫码数据：Client={ClientEndPoint}, ClientIp={ClientIp}, Content={Content}",
                            clientLabel,
                            clientIp,
                            message);

                        var response = await SaveQrCodeAsync(clientIp, message);
                        await SendAsync(stream, response, stoppingToken);

                        _logger.LogInformation(
                            "已回复扫码客户端：Client={ClientEndPoint}, Response={Response}",
                            clientLabel,
                            response);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("服务停止，关闭扫码客户端连接：{ClientEndPoint}", clientLabel);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(
                        "扫码客户端接收超时，关闭连接：{ClientEndPoint}, TimeoutSeconds={TimeoutSeconds}",
                        clientLabel,
                        _options.ReceiveTimeoutSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理扫码客户端连接失败：{ClientEndPoint}", clientLabel);
                }
            }
        }

        private async Task<string> SaveQrCodeAsync(string clientIp, string message)
        {
            try
            {
                using var connection = _db.CreateConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

                //var exists = await connection.ExecuteScalarAsync<int>(@"
                //SELECT COUNT(1)
                //FROM RCS_QrCodes WITH (UPDLOCK, HOLDLOCK)
                //WHERE CarIP = @CarIP
                //AND Excute = 0;",
                //    new { CarIP = clientIp },
                //    transaction);

                //if (exists > 0)
                //{
                //    transaction.Commit();
                //    _logger.LogInformation(
                //        "客户端 {ClientIp} 已存在未执行扫码记录，本次不重复插入。Content={Content}",
                //        clientIp,
                //        message);
                //    return IsErrorMessage(message) ? ErrorMessage : FinishedMessage;
                //}

                var isError = IsErrorMessage(message);
                await connection.ExecuteAsync(@"
                INSERT INTO RCS_QrCodes
                    (QRCode, CreateTime, CarIP, TaskType, Normal, IfSend, Remark, Excute)
                VALUES
                    (@QRCode, @CreateTime, @CarIP, @TaskType, @Normal, @IfSend, @Remark, @Excute);",
                    new
                    {
                        QRCode = isError ? string.Empty : message,
                        CreateTime = DateTime.Now,
                        CarIP = clientIp,
                        TaskType = 0,
                        Normal = isError,
                        IfSend = isError,
                        Remark = "扫码",
                        Excute = false
                    },
                    transaction);

                transaction.Commit();

                _logger.LogInformation(
                    "扫码记录已入库：ClientIp={ClientIp}, IsError={IsError}, QRCode={QRCode}",
                    clientIp,
                    isError,
                    isError ? string.Empty : message);

                return isError ? ErrorMessage : FinishedMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫码数据入库失败：ClientIp={ClientIp}, Content={Content}", clientIp, message);
                return "调用接口异常" + ex.Message;
            }
        }

        private static async Task SendAsync(NetworkStream stream, string message, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private static string NormalizeMessage(string message)
        {
            return message.Trim('\0', '\r', '\n', '\t', ' ');
        }

        private static bool IsErrorMessage(string message)
        {
            return string.Equals(message, ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        private IPAddress ResolveListenAddress()
        {
            if (!string.IsNullOrWhiteSpace(_options.Host)
                && !string.Equals(_options.Host, "auto", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_options.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
            {
                if (IPAddress.TryParse(_options.Host, out var configuredAddress))
                {
                    _logger.LogInformation("TCP扫码接收服务使用配置的监听IP：{ListenAddress}", configuredAddress);
                    return configuredAddress;
                }

                _logger.LogWarning("TCP扫码接收服务配置的Host无效：{Host}，将自动检测本机IP", _options.Host);
            }

            var candidates = GetLocalIPv4Candidates().ToList();
            if (candidates.Count > 0)
            {
                var selected = candidates[0];
                _logger.LogInformation(
                    "TCP扫码接收服务自动检测到本机IP：{SelectedIp}。候选IP：{Candidates}",
                    selected,
                    string.Join(", ", candidates));
                return selected;
            }

            _logger.LogWarning("未检测到可用本机IPv4地址，TCP扫码接收服务将监听所有网卡 0.0.0.0");
            return IPAddress.Any;
        }

        private static IEnumerable<IPAddress> GetLocalIPv4Candidates()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up)
                .Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(x =>
                {
                    var properties = x.GetIPProperties();
                    var hasGateway = properties.GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.Any.Equals(g.Address));

                    return properties.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Where(a => !IPAddress.IsLoopback(a.Address))
                        .Select(a => new { a.Address, HasGateway = hasGateway });
                })
                .OrderByDescending(x => x.HasGateway)
                .ThenBy(x => x.Address.ToString())
                .Select(x => x.Address)
                .Distinct();
        }
    }
}
