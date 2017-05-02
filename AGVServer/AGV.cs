using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;

using AGV.util;
using Microsoft.VisualBasic.Devices;
namespace AGV
{
     class AGVMain
    {
        protected static void testTask()
        {

        }

        [DllImport("user32.dll")]
        public static extern bool MessageBeep(uint uType);
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            AGVLog agvLog = new AGVLog();
            //List<SingleTask> sList = new List<SingleTask>();
            agvLog.initAGVLog(); //初始化log

            Application.EnableVisualStyles(); 
            Application.SetCompatibleTextRenderingDefault(false);
            AGVInitialize.getInitialize().getLoginFrm().ShowDialog();

            //agvTask.startConnectForkLift(); //开始与车子建立连接
            //agvTask.startPLCRecv(); //开始采集PLC数据
            //agvTask.startToHandleTaskList(); //开始处理任务发送
            AGVInitialize.getInitialize().agvInit();
            AGVInitialize.getInitialize().getAGVTcpServer().StartAccept();
            
            AGVInitialize.getInitialize().getSchedule().startShedule();


            AGVInitialize.getInitialize().getAGVMessage().setUsbGreenLed();  //default green led
            AGVInitialize.getInitialize().getAGVMessage().StartHandleMessage();
            int i = 0;
            //while(i < 1)
            {
                i++;
                //AGVInitialize.getInitialize().getAGVCom().setDataCommand(LIFT_IN_COMMAND_T.LIFT_IN_COMMAND_UP);
            }
            AGVInitialize.getInitialize().getMainFrm().ShowDialog();
            //Application.Run(AGVInitialize.getInitialize().getMainFrm());

            //AGVTcpClient tcpClient = new AGVTcpClient();

            //Thread.Sleep(1000);

            
            //tcpClient.SendMessage("cmd=set task by name;name=abing;");

           //connectDB();
            //AGVInitialize init = AGVInitialize.getInitialize();
            //init.getAllForkLifts(false);

            //

            //while (!isExit) { Thread.Sleep(5); };

        }
    }
}
