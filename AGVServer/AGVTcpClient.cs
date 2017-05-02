using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

using System.ComponentModel;
using System.Windows.Forms;
using AGV.util;
namespace AGV
{

    public class AGVTcpClient
    {
        private TcpClient client = null;
        private Thread recvThread;
        public bool ConnectStatus = false;  //连接状态
        private string stx = ((char)2).ToString();
        private string etx = ((char)3).ToString();
        private bool isConnectThread = false;   //重连线程是否开启
        private byte readTimeOutTimes = 0;  //读取消息超时次数
        public bool isRecvMsgFlag = false;  //是否开启数据接收处理线程
        public delegate void handleRecvMessageCallback(int fID, byte[] buffer, int length);  //消息处理回调函数
        public delegate void handleReconnectCallback(AGVTcpClient tcpClient, bool status);  //重连回调函数
        public object clientLock = new Object();

        private handleRecvMessageCallback hrmCallback = null;
        private handleReconnectCallback hrctCallback = null;

        public ForkLift t_forklift = null;
        public string ip;
        public int port;

        public AGVTcpClient(ForkLift fl)
        {
            this.ip = fl.ip;
            this.port = fl.port;
            t_forklift = fl;
        }

        private TcpClient getTcpClient()
        {
            while (client == null)
            {
                Console.WriteLine("client is null wait 1 second");
                AGVLog.WriteWarn("client is null, wait 1 second", new StackFrame(true));
                Thread.Sleep(1);
            }

            return client;  
        }

        private static void setTcpClient(TcpClient tcpClient)
        {
            tcpClient.ReceiveTimeout = AGVInitialize.TCPCONNECT_REVOUT; //读取超时1s
            tcpClient.SendTimeout = AGVInitialize.TCPCONNECT_SENDOUT;  //发送超时1s
        }

        public void TcpConnect(string ip, int port)
        {
            try
            {
                if (client == null)
                {
                    client = new TcpClient();
                    client.Connect(ip, port);
                    setTcpClient(client);
                    ConnectStatus = true;
                    
                    AGVLog.WriteInfo("connect ip: " + ip + " port: " + port + "succee", new StackFrame(true));
                    Console.WriteLine("connect ip: " + ip + " port: " + port + "succee");
                }
            }
            catch (Exception ex)
            {
                AGVLog.WriteError("Connect ip: " + ip + " port: " + port + " fail" + ex.Message, new StackFrame(true));
                Console.WriteLine("Connect ip: " + ip + " port: " + port + " fail");
      
                Closeclient();
            }
        }
        /// <summary>
        /// 重新与车子建立连接
        /// </summary>
        public void reConnect()
        {
            Console.WriteLine("ConnectStatus:   " + ConnectStatus);
            AGVLog.WriteInfo("ConnectStatus: " + ConnectStatus, new StackFrame(true));
            while (t_forklift.isUsed == 1 && !ConnectStatus)
            {
                isConnectThread = true; //设置重连标志true
                Thread.Sleep(5000);  //5秒钟重连一次
                Console.WriteLine("start to reconnect");
                AGVLog.WriteWarn("start to reconnect", new StackFrame(true));
                TcpConnect(this.ip, this.port);
                if (ConnectStatus == true)
                {
                    AGVLog.WriteInfo("reconnect ip: " + ip + "port :" + "success", new StackFrame(true));
                    Console.WriteLine("reconnect ip: " + ip + "port :" + "success");
                    AGVLog.WriteInfo("restart recv thread", new StackFrame(true));
                    if (hrctCallback != null)
                    {
                       hrctCallback(this, true);
                    }
                }
            }
            isConnectThread = false; //重连成功

        }

        /// <summary>
        /// 消息处理回调函数注册
        /// </summary>
        /// <param name="hrmCallback">消息处理回调</param>
        public void registerRecvMessageCallback(handleRecvMessageCallback hrmCallback)
        {
            this.hrmCallback = hrmCallback;
        }

        /// <summary>
        /// 注册重连回调函数
        /// </summary>
        /// <param name="hrmCallback">重连回调</param>
        public void registerReconnectCallback(handleReconnectCallback hrctCallback)
        {
            this.hrctCallback = hrctCallback;
        }

        public void startRecvMsg(ForkLift fl)
        {
            recvThread = new Thread(new ParameterizedThreadStart(receive));
            recvThread.IsBackground = true;
            this.isRecvMsgFlag = true;  //设置接收标志
            recvThread.Start(fl);
        }

        /// <summary>
        /// 结束数据接收处理线程
        /// </summary>
        public void setRecvFlag(bool status)
        {
            this.isRecvMsgFlag = status;
        }

        public void receive(object fl)
        {
            ForkLift forklift = (ForkLift)fl;
            byte[] buffer = new byte[512];
            Socket msock;
            TcpClient vClient = null;
            Console.WriteLine("receive ConnectStatus: " + ConnectStatus);
            if (ConnectStatus == false) //检查连接状态
                return;

            while (isRecvMsgFlag)
            {
                try
                {
                    vClient = getTcpClient();
                    msock = client.Client;
                    Array.Clear(buffer, 0, buffer.Length);
                    int bytes = msock.Receive(buffer);
                    readTimeOutTimes = 0; //读取超时次数清零
                    if (hrmCallback != null)  //处理消息
                    {
                        hrmCallback(forklift.id, buffer, bytes);
                        Thread.Sleep(10);
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.ErrorCode == 10060 && readTimeOutTimes < 10) //超时次数超过10次，关闭socket进行重连
                    {
                        AGVLog.WriteWarn("read msg timeout", new StackFrame(true));
                        Console.WriteLine("read msg timeout");
                        readTimeOutTimes++;
                        continue;
                    }
                    AGVLog.WriteError("读取消息错误" + ex.ErrorCode, new StackFrame(true));
                    Console.WriteLine("recv msg client close" + ex.ErrorCode);
                    Closeclient();
                }
            }
        }

        public void SendMessage(string sendMessage)
        {
            Socket msock;
            Console.WriteLine("SendMessage ConnectStatus " + ConnectStatus);
            if (client == null || ConnectStatus == false) //检查连接状态
            {
                Exception ex = new Exception("connect err");
                throw (ex);
            }

            msock = client.Client;
            try
            {
                byte[] data = new byte[128];
                data = Encoding.ASCII.GetBytes(sendMessage);
                msock.Send(data);

            }
            catch (Exception se)
            {
                AGVLog.WriteError("发送消息错误" + se.Message, new StackFrame(true));
                Console.WriteLine("send message error" + se.Message);
                Closeclient();
            }
        }

        public void Sendbuffer(byte[] buffer)
        {
            if (client == null || ConnectStatus == false) //检查连接状态
                return;

            Socket msock;
            try
            {
                msock = client.Client;
                msock.Send(buffer);
            }
            catch (Exception ex)
            {
                AGVLog.WriteError("发送消息错误" + ex.Message, new StackFrame(true));
                Console.WriteLine("send message error");
                Closeclient();
            }
        }

        /// <summary>
        /// 开启重连的线程
        /// </summary>
        private void startReconnectThread()
        {
            Thread thread;
            thread = new Thread(new ThreadStart(reConnect));
            thread.IsBackground = true;
            thread.Start();
        }

        public void Closeclient()
        {
            try
            {
                ConnectStatus = false;
                if (client != null)
                {
                    AGVLog.WriteInfo("关闭socket", new StackFrame(true));
                    client.Client.Close();
                    client.Close();
                    client = null;
                    if (isConnectThread == false && t_forklift.isUsed == 1)  //connect断开后开启与车子重连
                    {
                        Console.WriteLine("Close to start reconnect" + isConnectThread);
                        startReconnectThread();
                    }

                    if (t_forklift.isUsed == 1)
                    {
                        if (hrctCallback != null)
                            hrctCallback(this, false);
                    }
                    
                }
            }
            catch (Exception ex)
            {
                AGVLog.WriteError("关闭socket错误" + ex.Message, new StackFrame(true));
                Console.WriteLine("close socket fail");
            }
            Console.WriteLine("client is null now");
            AGVLog.WriteWarn("client is null now", new StackFrame(true));
        }
    }
}
