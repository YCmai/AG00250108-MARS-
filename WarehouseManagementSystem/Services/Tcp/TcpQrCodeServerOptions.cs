namespace WarehouseManagementSystem.Services.Tcp
{
    public class TcpQrCodeServerOptions
    {
        public bool Enabled { get; set; } = true;
        public string? Host { get; set; }
        public int Port { get; set; } = 9000;
        public int Backlog { get; set; } = 50;
        public int ReceiveBufferSize { get; set; } = 1024 * 1024;
        public int ReceiveTimeoutSeconds { get; set; } = 300;
    }
}
