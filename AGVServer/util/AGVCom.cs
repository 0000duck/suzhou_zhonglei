using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.VisualBasic.Devices;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;
using AGV;

namespace AGV.util
{
    public enum LIFT_IN_COMMAND_T
    {
        LIFT_IN_COMMAND_MIN = 0,  //无任务
        LIFT_IN_COMMAND_UP = 0x1, //楼下取货，送到楼上来
        LIFT_IN_COMMAND_DOWN = 0x2, //楼上取货，送到楼下去
        LIFT_IN_COMMAND_MAX,
    };

    public enum LIFT_OUT_COMMAND_T
    {
        LIFT_OUT_COMMAND_MIN = 0,  //无任务
        LIFT_OUT_COMMAND_UP = 0x1, //楼下货已经到位，提醒楼下3号AGV去取货
        LIFT_OUT_COMMAND_DOWN = 0x2, //楼上货已经到位，提醒楼上AGV去取货
        LIFT_OUT_COMMAND_UP_DOWN = 0x3, //楼上楼下都有货，这个时候需要弹出信号，提示框提示
        LIFT_OUT_COMMAND_louxia_baojing = 0x4,
        LIFT_OUT_COMMAND_loushang_baojing = 0x8,
        LIFT_OUT_COMMAND_MAX,
    };

    /// <summary>
    /// 串口控制升降机
    /// 发送数据格式 一个字，两个字节{0x1, 0x0}, 读取能读到6个字节，前两个返回的是控制字节，后面四个是反馈字节，后面反馈字节中有一个控制指令
    /// </summary>
    public class AGVCom
    {
        Computer my = new Computer();
        SerialPort sp = new SerialPort();
        private bool isStop = false;
        private static byte[] dataCommand = new byte[2];  //升降机控制指令, 0x6上料 0x5下料
        

        public static byte[] UP_COMMAND = { 0x1, 0x0};  //表示上料指令
        public static byte[] DOWN_COMMAND = { 0x2, 0x0};  //表示下料指令

        byte[] common = {0x0, 0x0, 0x0, 0x0}; //每次读数据，需要先写一个数据, 写0表示清除之前的命令
        private LIFT_OUT_COMMAND_T outCommand = 0; //输出命令

        public AGVCom()
        {
            getCHPort();
        }

        private SerialPort getCHPort()
        {
            foreach (String portName in SerialPort.GetPortNames())
            {
                Console.WriteLine("portName = " + portName);
                sp.PortName = portName;  //选取最大的Com端口，跟电脑端要配合
                //if (portName.IndexOf("COM1") < 0)  //除COM1 以外的程序
                {
                    //break;
                }
            }

            return sp;
        }

        private void setSerialPort()
        {
            sp.BaudRate  = 9600;
            sp.DataBits = 7;
            sp.StopBits = StopBits.One;
            sp.Parity = Parity.None;
            sp.ReadTimeout = 1000;
        }

        private void openSerialPort()
        {
            try
            {
                if (!sp.IsOpen)
                {
                    sp.Open();
                }
            } catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw ex;
            }
            
        }

        public bool initSerialPort()
        {
            bool stat = true;
            try
            {
                if (sp.PortName.Equals("COM1"))
                {
                    throw new Exception("没有找到升降机串口，请检查");
                }
                setSerialPort();
                openSerialPort();
            }catch (Exception ex)
            {
                stat = false;  //串口异常
            }

            return stat;
        }

        public void setDataCommand(LIFT_IN_COMMAND_T command)
        {
            if (command == LIFT_IN_COMMAND_T.LIFT_IN_COMMAND_DOWN)
            {
                dataCommand[0] = DOWN_COMMAND[0];
                dataCommand[1] = DOWN_COMMAND[1];
                Console.WriteLine("down command was set");
            }
            else if (command == LIFT_IN_COMMAND_T.LIFT_IN_COMMAND_UP)
            {
                dataCommand[0] = UP_COMMAND[0];
                dataCommand[1] = UP_COMMAND[1];
                Console.WriteLine("down command was up");
            }
        }

        private LIFT_OUT_COMMAND_T readDataCommand(byte[] response)
        {
            LIFT_OUT_COMMAND_T command = LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_MIN;
            int i = 0;
            for(i = 0; i < response.Length; i++)
            {
                //Console.WriteLine(" read response[" + i + "] = " + response[i]);
                if ( (LIFT_OUT_COMMAND_T) response[i] < LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_MAX && (LIFT_OUT_COMMAND_T) response[i] > LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_MIN)
                {
                    command = (LIFT_OUT_COMMAND_T)response[i];
                }
            }
            if (command != LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_MIN)
            {
                //Console.WriteLine(" out vommmmmm = " + command);
            }
            return command;
        }

        /// <summary>
        /// 获取输出命令
        /// </summary>
        /// <returns></returns>
        public LIFT_OUT_COMMAND_T getOutCommand()
        {
            return outCommand;
        }

        private void handleLiftComException()
        {
            Message message = new Message();
            message.setMessageType(AGVMESSAGE_TYPE_T.AGVMESSAGE_LIFT_COM);
            message.setMessageStr("升降机PLC端口异常，请检查，当前处于系统暂停");
            AGVInitialize.getInitialize().getAGVMessage().setMessage(message);  //发送消息


            isStop = true; //结束串口读取线程
        }
        /// <summary>
        /// 一个命令尝试发送三次，如果三次都没有收到响应，则不继续发送
        /// </summary>
        /// <param name="command"></param>
        private void sendCommand(byte[] command)
        {
            int count = 0;
            int j = 0;
            int times = 0;
            if (command[0] == 0 && command[1] == 0)  //结束符0x32, 否则不是有效命令
            {
                Console.WriteLine("command err");
                return;
            }
            sp.Write(command, 0, 2);   //总共发送1个字节 
            
            Thread.Sleep(100);
            Console.WriteLine("write byte " + command[0] +  "    " + command[1]);
            while ((count = sp.BytesToRead) != 6 && times < 30 ) //固定升降机返回字节，否则表示升降机出现错误
            {
                times++;
                //Console.WriteLine("wait to read bytes"); //等待反馈
                Thread.Sleep(100);
            }
            times = 0;
            if (count != 6)
            {
                Console.WriteLine(" count = " + count);
            }
            //while(sendTimes > 0)
                int i = 0; //读取三次，看是否写成功，如果写成功能读到响应
                count = sp.BytesToRead;

                byte[] response = new byte[count];
                    try
                    {

                        sp.Read(response, 0, count);
                    }
                    catch(System.TimeoutException te)
                    {
                        Thread.Sleep(10);
                    }

                    for (j = 0; j < count; j++ )
                    {
                        Console.WriteLine("write check response[" + j + "]" + " = " + response[j]);
                        if (j > 0 && response[j] == command[0])
                        {
                            goto end;
                        }
                    }
                        
                    Thread.Sleep(100);

            Console.WriteLine("send fail");
            return;
        end:
            Console.WriteLine("send success");
        }

        private void handleDataSerialPort()
        {
            int count;
            byte[] response = null;
            while(!isStop)
            {
                byte[] readBuffer = new byte[1];
                try
                {
                    sp.Write(common, 0, 4);

                    while((count = sp.BytesToRead) != 8)
                    {
            //            Console.WriteLine("wait to read bytes count = " + count); //等待反馈
                        Thread.Sleep(10);
                    }


                    response = new byte[count];
                    sp.Read(response, 0, count);

                    outCommand = readDataCommand(response);

                    /*if (outCommand == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_loushang_baojing || outCommand == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_louxia_baojing)
                    {
                        if (dataCommand[0] == 0)  //当前没有命令的时候才去发送复位信号，否则会覆盖之前的信号
                        {
                            dataCommand[0] = 0x4;
                        }

                        Console.WriteLine("will to reset");
                    }*/

                    if (outCommand > LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_UP_DOWN) //如果读取到升降机异常， 不再向升降机发送命令
                    {
                        AGVLog.WriteInfo("升降机异常 " + outCommand, new StackFrame(true));
                        continue;
                    }

                    if (dataCommand[0] > 0)
                    {
                        sendCommand(dataCommand);
                        dataCommand[0] = 0;
                        dataCommand[1] = 0;   //清空数据
                    }
                 } catch (Exception ex)
                 {
                    Console.WriteLine(ex.ToString());
                    handleLiftComException();
                 }

                Thread.Sleep(200);
            }  
        }

        public void startReadSerialPortThread()
        {
            Thread dataThread = new Thread(new ThreadStart(handleDataSerialPort));

            isStop = false;
            dataThread.Start();
        }

        public void reStart()
        {
            getCHPort();
            initSerialPort();  //init 可能存在错误，如果存在错误，read的时候会返回错误
            startReadSerialPortThread();
        }

    }
}
