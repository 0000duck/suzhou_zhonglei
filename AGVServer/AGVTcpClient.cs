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
        public bool ConnectStatus = false;  //����״̬
        private string stx = ((char)2).ToString();
        private string etx = ((char)3).ToString();
        private bool isConnectThread = false;   //�����߳��Ƿ���
        private byte readTimeOutTimes = 0;  //��ȡ��Ϣ��ʱ����
        public bool isRecvMsgFlag = false;  //�Ƿ������ݽ��մ����߳�
        public delegate void handleRecvMessageCallback(int fID, byte[] buffer, int length);  //��Ϣ����ص�����
        public delegate void handleReconnectCallback(AGVTcpClient tcpClient, bool status);  //�����ص�����
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
            tcpClient.ReceiveTimeout = AGVInitialize.TCPCONNECT_REVOUT; //��ȡ��ʱ1s
            tcpClient.SendTimeout = AGVInitialize.TCPCONNECT_SENDOUT;  //���ͳ�ʱ1s
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
        /// �����복�ӽ�������
        /// </summary>
        public void reConnect()
        {
            Console.WriteLine("ConnectStatus:   " + ConnectStatus);
            AGVLog.WriteInfo("ConnectStatus: " + ConnectStatus, new StackFrame(true));
            while (t_forklift.isUsed == 1 && !ConnectStatus)
            {
                isConnectThread = true; //����������־true
                Thread.Sleep(5000);  //5��������һ��
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
            isConnectThread = false; //�����ɹ�

        }

        /// <summary>
        /// ��Ϣ����ص�����ע��
        /// </summary>
        /// <param name="hrmCallback">��Ϣ����ص�</param>
        public void registerRecvMessageCallback(handleRecvMessageCallback hrmCallback)
        {
            this.hrmCallback = hrmCallback;
        }

        /// <summary>
        /// ע�������ص�����
        /// </summary>
        /// <param name="hrmCallback">�����ص�</param>
        public void registerReconnectCallback(handleReconnectCallback hrctCallback)
        {
            this.hrctCallback = hrctCallback;
        }

        public void startRecvMsg(ForkLift fl)
        {
            recvThread = new Thread(new ParameterizedThreadStart(receive));
            recvThread.IsBackground = true;
            this.isRecvMsgFlag = true;  //���ý��ձ�־
            recvThread.Start(fl);
        }

        /// <summary>
        /// �������ݽ��մ����߳�
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
            if (ConnectStatus == false) //�������״̬
                return;

            while (isRecvMsgFlag)
            {
                try
                {
                    vClient = getTcpClient();
                    msock = client.Client;
                    Array.Clear(buffer, 0, buffer.Length);
                    int bytes = msock.Receive(buffer);
                    readTimeOutTimes = 0; //��ȡ��ʱ��������
                    if (hrmCallback != null)  //������Ϣ
                    {
                        hrmCallback(forklift.id, buffer, bytes);
                        Thread.Sleep(10);
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.ErrorCode == 10060 && readTimeOutTimes < 10) //��ʱ��������10�Σ��ر�socket��������
                    {
                        AGVLog.WriteWarn("read msg timeout", new StackFrame(true));
                        Console.WriteLine("read msg timeout");
                        readTimeOutTimes++;
                        continue;
                    }
                    AGVLog.WriteError("��ȡ��Ϣ����" + ex.ErrorCode, new StackFrame(true));
                    Console.WriteLine("recv msg client close" + ex.ErrorCode);
                    Closeclient();
                }
            }
        }

        public void SendMessage(string sendMessage)
        {
            Socket msock;
            Console.WriteLine("SendMessage ConnectStatus " + ConnectStatus);
            if (client == null || ConnectStatus == false) //�������״̬
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
                AGVLog.WriteError("������Ϣ����" + se.Message, new StackFrame(true));
                Console.WriteLine("send message error" + se.Message);
                Closeclient();
            }
        }

        public void Sendbuffer(byte[] buffer)
        {
            if (client == null || ConnectStatus == false) //�������״̬
                return;

            Socket msock;
            try
            {
                msock = client.Client;
                msock.Send(buffer);
            }
            catch (Exception ex)
            {
                AGVLog.WriteError("������Ϣ����" + ex.Message, new StackFrame(true));
                Console.WriteLine("send message error");
                Closeclient();
            }
        }

        /// <summary>
        /// �����������߳�
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
                    AGVLog.WriteInfo("�ر�socket", new StackFrame(true));
                    client.Client.Close();
                    client.Close();
                    client = null;
                    if (isConnectThread == false && t_forklift.isUsed == 1)  //connect�Ͽ������복������
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
                AGVLog.WriteError("�ر�socket����" + ex.Message, new StackFrame(true));
                Console.WriteLine("close socket fail");
            }
            Console.WriteLine("client is null now");
            AGVLog.WriteWarn("client is null now", new StackFrame(true));
        }
    }
}
