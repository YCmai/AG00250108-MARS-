using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Service.Properties;
using System.Xml.Linq;
using Client100.Service;
using System.Runtime.InteropServices.WindowsRuntime;
using Client100.Entity;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using System.Configuration;

namespace Service
{
    public partial class Form1 : Form
    {
        public static string WebServerIP = ConfigurationSettings.AppSettings["WebServerIP"];//服务器IP
        public static string WebServerPort = ConfigurationSettings.AppSettings["WebServerPort"];//服务器端口
        public static LoggerHelper log = new LoggerHelper();
        public static DbService db = new DbService();
        private NotifyIcon notifyIcon;
        Thread threadWatch = null; //负责监听客户端的线程
        Socket socketWatch = null;  //负责监听客户端的套接字     

        public Form1()
        {
            InitializeComponent();
            //关闭对文本框的非法线程操作检查
            System.Windows.Forms.TextBox.CheckForIllegalCrossThreadCalls = false;
            System.Windows.Forms.Control.CheckForIllegalCrossThreadCalls = false;  //允许跨线程更改UI
            InitNotifyIcon();
        }


        private void InitNotifyIcon()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = this.Icon; // 使用窗体图标
            notifyIcon.Text = "我的扫码应用"; // 鼠标悬停显示的文本
            notifyIcon.Visible = true;

            // 双击托盘图标时显示窗体
            notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
            };

            // 可选：添加右键菜单
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示", null, (s, e) => {
                this.Show();
                this.WindowState = FormWindowState.Normal;
            });
            menu.Items.Add("退出", null, (s, e) => {
                notifyIcon.Dispose();
                Application.Exit();
            });
            notifyIcon.ContextMenuStrip = menu;
        }




        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                var result = MessageBox.Show("是否最小化到系统托盘？", "提示",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    e.Cancel = true; // 取消关闭
                    this.Hide(); // 隐藏窗体
                    return;
                }
                else
                {
                    notifyIcon.Dispose(); // 清理托盘图标
                }
            }
            base.OnFormClosing(e);
        }



        //创建一个负责和客户端通信的套接字 
        Socket socConnection = null;

        /// <summary>
        /// 监听客户端发来的请求
        /// </summary>
        private void WatchConnecting()
        {
            while (true)  //持续不断监听客户端发来的请求
            {
                try
                {
                    //添加判断textbox字符数量，到达上限后自动清空
                    try
                    {
                        if (textBox5.TextLength > 10000)
                        {
                            textBox5.Clear();
                            textBox5.AppendText("自动清空看板" + "\r\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        textBox5.AppendText("自动清空看板文字异常 " + ex.ToString() + "\r\n");
                    }
                    socConnection = socketWatch.Accept();

                    //textBox5.AppendText("客户端连接成功! " + "\r\n");
                    //创建一个通信线程 
                   
                    ParameterizedThreadStart pts = new ParameterizedThreadStart(ServerRecMsgToSendApi);
                    Thread thr = new Thread(pts);
                    thr.IsBackground = true;
                    //启动线程
                    thr.Start(socConnection);
                }
                catch (Exception ex)
                {
                    textBox5.AppendText("人工工位信号监听服务器异常 " + ex.ToString() + "\r\n");
                }
            }
        }


        /// <summary>
        /// 发送信息到客户端的方法
        /// </summary>
        /// <param name="sendMsg">发送的字符串信息</param>
        private void ServerSendMsg(Socket client, string sendMsg)
        {
            try
            {
                //将输入的字符串转换成 机器可以识别的字节数组
                byte[] arrSendMsg = Encoding.UTF8.GetBytes(sendMsg);
                //向客户端发送字节数组信息
                client.Send(arrSendMsg);
                //将发送的字符串信息附加到文本框txtMsg上
                textBox5.AppendText("服务端回复内容:" + sendMsg + "\r\n");
                log.Info("服务端回复客户端"+client+"内容:" + sendMsg);
            }
            catch (Exception ex)
            {
                textBox5.AppendText("客户端已断开连接,无法发送信息！" + "\r\n");
                log.Info("客户端已断开连接,无法发送信息！" + "\r\n");
            }
        }


        /// <summary>
        /// 接收客户端发来的信息 
        /// </summary>
        /// <param name="socketClientPara">客户端套接字对象</param>
        private async void ServerRecMsgToSendApi(object socketClientPara)
        {
            Socket socketServer = socketClientPara as Socket;
            socketServer.ReceiveTimeout = 5 * 60 * 1000;// 3分钟的timeout
            string clientIp = "";
            try
            {
                clientIp = ((IPEndPoint)socketServer.RemoteEndPoint).Address.ToString(); // 获取客户端 IP 地址
                textBox5.AppendText( "IP:" + clientIp + "客户端连接成功! " + DateTime.Now + "\r\n");
            }
            catch
            {
                textBox5.AppendText("客户端连接成功! " + "\r\n");
            }
            while (true)
            {
                //创建一个内存缓冲区 其大小为1024*1024字节  即1M
                byte[] arrServerRecMsg = new byte[1024 * 1024];
                try
                {
                    //将接收到的信息存入到内存缓冲区,并返回其字节数组的长度
                    int length = socketServer.Receive(arrServerRecMsg);
                    //将机器接受到的字节数组转换为人可以读懂的字符串
                    string strSRecMsg = Encoding.UTF8.GetString(arrServerRecMsg, 0, length);
                    var content = strSRecMsg;
                    //将发送的字符串信息附加到文本框txtMsg上  
                    textBox5.AppendText("客户端 "+ clientIp  + "时间:" + GetCurrentTime() + "\r\n" +  "发送二维码数据" + strSRecMsg + "\r\n");
                    log.Info("客户端 " + clientIp + "时间:" + GetCurrentTime() + "\r\n" + "发送原始数据" + strSRecMsg + "\r\n");
                    //调取中控接口
                    try
                    {

                        if (content== "ERROR")
                        {
                            log.Info("客户端 " + clientIp + "扫码异常ERROR");
                            var model = db.GetRCS_QrCodes(new RCS_QrCodes() { CarIP = clientIp, Excute=false });
                            if (model==null)
                            {
                                db.InsertRCS_QrCodes(new RCS_QrCodes() { CarIP = clientIp, CreateTime = DateTime.Now,  QRCode = "", TaskType = TaskType.fullload, Normal=true, IfSend=true,Remark ="扫码",Excute =false  });
                                //扫不到码，异常
                                ServerSendMsg(socketServer, "ERROR"); 
                            }
                            else
                            {
                                //扫不到码，异常
                                ServerSendMsg(socketServer, "ERROR");
                            }
                        }
                        else
                        {
                            log.Info("客户端 " + clientIp + "扫码数据为"+ content);
                            var model = db.GetRCS_QrCodes(new RCS_QrCodes() { CarIP = clientIp, Excute = false });
                            if (model == null)
                            {
                                db.InsertRCS_QrCodes(new RCS_QrCodes() { CarIP = clientIp, CreateTime = DateTime.Now,   QRCode = strSRecMsg, TaskType = TaskType.fullload, Normal= false, IfSend = false,Remark = "扫码", Excute = false });
                                ServerSendMsg(socketServer, "Finished");
                            }
                            else {

                                ServerSendMsg(socketServer, "Finished");

                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        ServerSendMsg(socketServer, "调用接口异常" + ex.Message + "\r\n");
                        textBox5.AppendText( GetCurrentTime() + "\r\n" + clientIp + "发来信号-原始数据" + strSRecMsg + "异常：" + ex.ToString() + "\r\n");
                        log.Info( GetCurrentTime() + "\r\n" + clientIp + "发来信号-原始数据" + strSRecMsg + "异常：" + ex.ToString() + "\r\n");
                    }
                }
                catch (Exception ex)
                {
                    var isConnected = IsClientConnected(socketServer);
                    if (!isConnected)
                    {
                        socketServer.Close();
                        socketServer.Dispose();
                        textBox5.AppendText(DateTime.Now + "==" + clientIp + "客户端已断开连接！" + "\r\n");
                        log.Info(DateTime.Now + "==" + clientIp + "客户端已断开连接！" + "\r\n");
                        //跳出循环
                        break;
                    }
                    else
                    {
                        //处理解析消息错误
                    }
                  
                }
            }
        }

        private bool IsClientConnected(Socket clientSocket)
        {
            try
            {
                // 如果 Socket.Poll 方法返回 true，并且 Socket.Available 属性为 0，表示连接已断开
                return !(clientSocket.Poll(1, SelectMode.SelectRead) && clientSocket.Available == 0);
            }
            catch (SocketException) // 发生异常时，认为连接已断开
            {
                return false;
            }
        }

      
        /// <summary>
        /// 获取当前系统时间的方法
        /// </summary>
        /// <returns>当前时间</returns>
        private DateTime GetCurrentTime()
        {
            DateTime currentTime = new DateTime();
            currentTime = DateTime.Now;
            return currentTime;
        }

        private void btnServerConn_Click_1(object sender, EventArgs e)
        {
            try
            {
              
                //定义一个套接字用于监听客户端发来的信息  包含3个参数(IP4寻址协议,流式连接,TCP协议)
                socketWatch = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                //服务端发送信息 需要1个IP地址和端口号
                IPAddress ipaddress = IPAddress.Parse(this.textBox2.Text.Trim()); //获取文本框输入的IP地址
                                                                                  //将IP地址和端口号绑定到网络节点endpoint上 
                IPEndPoint endpoint = new IPEndPoint(ipaddress, int.Parse(this.textBox1.Text.Trim())); //获取文本框上输入的端口号

                socketWatch.IOControl(IOControlCode.KeepAliveValues, KeepAliveData(), null);
                socketWatch.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);                                                                                    //监听绑定的网络节点
                socketWatch.Bind(endpoint);
                //将套接字的监听队列长度限制为20
                socketWatch.Listen(50);
                //创建一个监听线程 
                threadWatch = new Thread(WatchConnecting);
                //将窗体线程设置为与后台同步
                threadWatch.IsBackground = true;
                //启动线程
                threadWatch.Start();
                //启动线程后 txtMsg文本框显示相应提示
                textBox5.AppendText("开始监听客户端传来的信息!" + "\r\n");
                this.btnServerConn.Enabled = false;
            }
            catch (Exception ex)
            {
                textBox5.AppendText("服务端启动服务失败!" + "\r\n");
                log.Info("服务端启动服务失败!" + "\r\n");
                this.btnServerConn.Enabled = true;
            }
        }
        private static byte[] KeepAliveData()
        {
            uint dummy = 0;
            byte[] inOptionValues = new byte[Marshal.SizeOf(dummy) * 3];
            BitConverter.GetBytes((uint)1).CopyTo(inOptionValues, 0);
            //空闲发送时间
            BitConverter.GetBytes((uint)1000).CopyTo(inOptionValues, Marshal.SizeOf(dummy));
            //发送间隔
             BitConverter.GetBytes((uint)500).CopyTo(inOptionValues, Marshal.SizeOf(dummy) * 2);
            return inOptionValues;
        }

        private void 确定_Click(object sender, EventArgs e)
        {

        }

       

        private void Form1_Load(object sender, EventArgs e)
        {

        }


        public class Resqust
        {
            public string code { get; set; }
        }

        public class PriorityJsonResult
        {
            public string code { get; set; }

            public string message { get; set; }
        }

        private void button3_Click(object sender, EventArgs e)
        {
           
        }

        private void btnGetLocalIP_Click(object sender, EventArgs e)
        {
            //接收IPv4的地址
            IPAddress localIP = GetLocalIPv4Address();
            //赋值给文本框
            this.textBox2.Text = localIP.ToString();
        }

        /// <summary>
        /// 获取本地IPv4地址
        /// </summary>
        /// <returns></returns>
        public IPAddress GetLocalIPv4Address()
        {
            IPAddress localIpv4 = null;
            //获取本机所有的IP地址列表
            IPAddress[] IpList = Dns.GetHostAddresses(Dns.GetHostName());
            //循环遍历所有IP地址
            foreach (IPAddress IP in IpList)
            {
                //判断是否是IPv4地址
                if (IP.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIpv4 = IP;
                }
                else
                {
                    continue;
                }
            }
            return localIpv4;
        }

    }
}
