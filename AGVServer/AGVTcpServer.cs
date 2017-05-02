using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Diagnostics;

using AGV.util;
namespace AGV
{

    // State object for reading client data asynchronously     
    public class StateObject
    {
        // Client socket.     
        public Socket workSocket = null;
        // Size of receive buffer.     
        public const int BufferSize = 1024;
        // Receive buffer.     
        public byte[] buffer = new byte[BufferSize];
        // Received data string.     
        public StringBuilder sb = new StringBuilder();
    }

    public class ClientThread
    {
        public Socket client = null;
        int i;
        public ClientThread(Socket k)
        {
            client = k;
        }


        private void handleRecordTask(string taskName, string cmd)
        {
            SingleTask st = AGVInitialize.getInitialize().getSingleTaskByTaskName(taskName);
            if (cmd.Equals("add"))
            {
                st.taskStat = TASKSTAT_T.TASK_SEND;
                AGVInitialize.getInitialize().getSchedule().addTaskRecord(TASKSTAT_T.TASK_READY_SEND, st);
            }
            else if (cmd.Equals("remove"))
            {
                AGVInitialize.getInitialize().getSchedule().removeTaskRecord(st, TASKSTAT_T.TASK_READY_SEND);
            }

        }

        private void handleMessage(String content)
        {
            Console.WriteLine("Content : " + content);
            int pos_c = -1;
            string cmd = null;
            pos_c = content.IndexOf("cmd=");
            if (pos_c != -1)
            {
                cmd = content.Substring(pos_c + 4);
                Console.WriteLine("cmd = " + cmd);

                if (cmd.StartsWith("add_recordTask"))  //添加任务
                {
                    pos_c = cmd.IndexOf("param=");
                    if (pos_c != -1)
                    {
                        string taskName = cmd.Substring(pos_c + 6);
                        handleRecordTask(taskName, "add");
                        Console.WriteLine("taskName = " + taskName);
                    }
                }
                else if (cmd.StartsWith("setSystemPause"))  //移除任务
                {
                    pos_c = cmd.IndexOf("param=");
                    if (pos_c != -1)
                    {
                        string tmp = cmd.Substring(pos_c + 6);
                        Console.WriteLine(" pauseStat = " + tmp);
                        if (tmp.Equals("0"))
                        {
                            AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN);
                        } else if (tmp.Equals("1"))
                        {
                            AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START);
                        }
                    }
                }else if (cmd.StartsWith("updateDownTask"))
                {
                    //AGVInitialize.getInitialize().getSchedule().updateDownPickSingleTask();
                }
            }
        }

        public void ClientService()
        {
            string data = null;
            byte[] bytes = new byte[4096];
            Console.WriteLine("new user");
            try
            {
                Console.WriteLine("read to receive");
                while((i = client.Receive(bytes)) != 0)
                {
                    if (i < 0)
                    {
                        break;
                    }

                    Console.WriteLine(i);
                    data = Encoding.ASCII.GetString(bytes, 0, i);
                    if (data.IndexOf("<AGV>") > -1)
                    {
                        handleMessage(data);
                    }
                }
                Thread.Sleep(10);
                
            } catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            if (client != null)
            {
                client.Close();
                client = null;
            } 
            Console.WriteLine("user duankai");
        }

        public void ServerService()
        {
            try
            {
                while (true)
                {

                    Console.WriteLine("read to send");
                    AGVTcpServer.Send(client);
                    Thread.Sleep(10000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            if (client != null)
            {
                client.Close();
                client = null;
            }
            Console.WriteLine("user duankai");
        }
    }

    public class AGVTcpServer
    {
        // Thread signal.     
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        //private static IPAddress ipAddress = IPAddress.Parse("10.90.35.69");
        private IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 11000);
        Socket listener = null;
        private static ForkLift forklift = AGVInitialize.getInitialize().getForkLiftByID(3);  //指定的三号车
        ClientThread currentClientThread = null;
        private void Listening()
        {
            // Data buffer for incoming data.     
            byte[] bytes = new Byte[1024];
            // Establish the local endpoint for the socket.     
            // The DNS name of the computer     
            // running the listener is "host.contoso.com".     
            // Create a TCP/IP socket.     
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // Bind the socket to the local     
            //endpoint and listen for incoming connections.     
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);
                while (true)
                {
                    // Set the event to nonsignaled state.     
                    allDone.Reset();
                    // Start an asynchronous socket to listen for connections.     
                    Thread tcpThread = new Thread(new ThreadStart(TcpListen));
                    tcpThread.Start();
                    // Wait until a connection is made before continuing.     
                    allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();
        }

        public void StartAccept()
        {
            Thread startSheduleThread;
            startSheduleThread = new Thread(new ThreadStart(Listening));
            startSheduleThread.IsBackground = true;
            startSheduleThread.Start();
        }

        public void TcpListen()
        {
            while(true)
            {
                try
                {
                    Console.WriteLine("wait connection");
                    Socket client = listener.Accept();
                    Console.WriteLine("accept");
                    currentClientThread = new ClientThread(client);
                    Thread serviceThread = new Thread(new ThreadStart(currentClientThread.ClientService));
                    Thread t = new Thread(new ThreadStart(currentClientThread.ServerService));
                    serviceThread.Start();
                    t.Start();
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        public void sendDataMessage(string dataMessage)
        {
            try
            {
                if (currentClientThread != null)
                {
                    if (currentClientThread.client != null)
                    {
                        byte[] byteData = Encoding.ASCII.GetBytes(dataMessage);
                        currentClientThread.client.Send(byteData);
                    }
                }
            }catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            
            
        }

        public static void Send(Socket handler)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("battery_soc=");
            sb.Append(forklift.getBatterySoc() + ";");

            sb.Append("agvMessage=");
            sb.Append((int) AGVInitialize.getInitialize().getAGVMessage().getMessage().getMessageType());

            Console.WriteLine(" send data = " + sb.ToString());
            AGVLog.WriteError(" send data = " + sb.ToString(), new StackFrame(true));
            byte[] byteData = Encoding.ASCII.GetBytes(sb.ToString());
            handler.Send(byteData);
        }
    }
}
