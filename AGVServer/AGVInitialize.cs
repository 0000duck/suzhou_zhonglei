using System;
using System.Collections.Generic;
using System.Xml;

using System.Windows.Forms;
using System.Diagnostics;

using System.Runtime.InteropServices;

using AGVPLC;
using AGV.util;
namespace AGV
{

    /// <summary>
    /// 需要车子需要调度的几个临界点
    /// </summary>
    public enum SHEDULE_TYPE_T
    {
        SHEDULE_TYPE_MIN,

        SHEDULE_TYPE_1TO2,  //从区域1到区域2

        SHEDULE_TYPE_2TO3,  //从区域2到区域3

        SHEDULE_TYPE_3TO2,  //从区域3到区域2

        SHEDULE_TYPE_2TO1,  //从区域2到区域1
    };

    /// <summary>
    /// 初始化单例plc,task等，定义线程执行时间间隔
    /// </summary>
    public class AGVInitialize
    {
        #region 导入设备打开接口 JG_OpenUSBAlarmLamp
        /// <summary>
        /// 功能：打开设备
        /// 说明：如果当前设备在自检过程中(上电时发生)，该命令无效，返回值为0
        /// </summary>
        /// <param name="ulDVID">IN,保留参数，代入0</param>
        /// <returns>1打开成功，0打开失败</returns>
        [DllImport("jg_usbAlarmLamp.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern Int32 JG_OpenUSBAlarmLamp(Int32 ulDVID);
        #endregion

        public static AGVInitialize initialize = null;
        public static string AGVCONFIG_PATH = "config/AGVConfig.xml";
        private MainFrm mainFrm = null;
        private LoginFrm loginFrm = null;
        private AGVConfigureForm fcForm = null;
        private AGVManualCtrlForm amcForm = null;


        private DBConnect dbConnect = null;
        private List<ForkLift> forkLiftList = new List<ForkLift>();  //存储所有车子的信息
        private List<SingleTask> singleTaskList = null;  //缓存所有将发送或正在处理的任务

        public static byte AGVTASK_MAX = 14; //最多14个任务
        private object lockTask = new object();  //任务线程锁
        private object lockShedule = new object();  //调度线程锁
        private object lockForkLift = new object();  //PLC线程锁  读写操作不同同时进行
        private object lockData = new object();  //数据锁 不能同时操作数据
        public static int readPLCInterval = 2000;   //2秒钟读一次 时间太长会导致连续拍两次按钮检测不到
        public static int handleTaskInterval = 1000; //任务处理时间间隔
        private User currentUser = null;
        public Schedule schedule = null;
        public AGVTcpServer agvTcpServer = null;
        private AGVCom agvCom = null;  //串口用于读取升降机信息
        private AGVUtil agvUtil = null;
        private AGVMessage agvMessage = null;

        public static bool useUsbAlarm = true;  //默认使用usb报警灯，如果打开usb报警灯失败，则切换到电脑声音报警

        public const int TCPCONNECT_REVOUT = 2500;
        public const int TCPCONNECT_SENDOUT = 1000;
        public const int AGVALARM_TIME = 30;  //检测到防撞信号超过12次，报警

        public const int BORDER_X_DEVIARION = 100;
        public const int BORDER_X_1 = 40200;
        public const int BORDER_X_2 = 28500; //区域1 和 区域2 的分界点
        public const int BORDER_X_3 = 1200;  //区域2 和区域3 的分界点

        public const int BORDER_X_2_DEVIATION_PLUS = 1500;  //区域3的正误差，如果车子由3进入2，正常应该停在BORDER_X_2处，超出1200+1500就会报警
        public const int BORDER_X_3_DEVIATION_PLUS = 2000;  //区域3的正误差，如果车子由2进入1，正常应该停在BORDER_X_3处，超出BORDER_X_3+BORDER_X_3_DEVIATION_PLUS就会报警
        public const int BORDER_Y_1 = -100;
        public const int BORDER_Y_2 = 12740;
        public const int BORDER_Y_3 = 1000;


        public enum ENV_ERR_TYPE
        {
            ENV_ERR_OK = 0,
            ENV_LIFT_COM_ERR = 1,  //升降机串口错误
            ENV_CACHE_TASKRECORD_WARNING = 2,  //缓存的任务记录
            ENV_CACHE_UPTASKRECORD_WARNING = 3, //缓存的上货任务

            ENV_ERR_MAX
        };

        public String env_err_type_text(ENV_ERR_TYPE err)
        {
            string err_text = "";
            switch(err)
            {
                case ENV_ERR_TYPE.ENV_ERR_OK:
                    err_text = "OK";
                    break;
                
                case ENV_ERR_TYPE.ENV_LIFT_COM_ERR:
                    err_text = "请检查升降机串口";
                    break;
                
                case ENV_ERR_TYPE.ENV_CACHE_TASKRECORD_WARNING:
                    err_text = "检测到有未完成的任务，是否保存";
                    break;

                case ENV_ERR_TYPE.ENV_CACHE_UPTASKRECORD_WARNING:
                    err_text = "检测到有未完成的上货，是否保存";
                    break;

                default :
                    Console.WriteLine("没有找到对应的错误");
                    break;
            }

            return err_text;
        }

        public static AGVInitialize getInitialize()  //初始化单例
        {
            if (initialize == null)
                initialize = new AGVInitialize();

            return initialize;
        }

        public AGVMessage getAGVMessage()
        {
            if (agvMessage == null)
            {
                agvMessage = new AGVMessage();
            }

            return agvMessage;
        }

        public MainFrm getMainFrm()
        {
            if (mainFrm == null)
                mainFrm = new MainFrm();

            return mainFrm;
        }

        public LoginFrm getLoginFrm()
        {
            if (loginFrm == null)
            {
                loginFrm = new LoginFrm();
            }

            return loginFrm;
        }

        public AGVConfigureForm getAGVConfigureForm()
        {
            if (fcForm == null)
            {
                fcForm = new AGVConfigureForm();
            }

            return fcForm;
        }

        public AGVManualCtrlForm getAGVManualCtrlForm()
        {
            if (amcForm == null)
            {
                amcForm = new AGVManualCtrlForm();
                amcForm.initAGVMCForm();
            }

            return amcForm;
        }
        public void setCurrentUser(User user)
        {
            this.currentUser = user;
        }

        public User getCurrentUser()
        {
            return currentUser;
        }

        /// <summary>
        /// 获取任务线程锁
        /// </summary>
        /// <returns></returns>
        public object getLockTask()
        {
            return lockTask;
        }

        /// <summary>
        /// 获取调度锁
        /// </summary>
        /// <returns></returns>
        public object getLockShedule()
        {
            return lockShedule;
        }

        public object getLockForkLift()
        {
            return lockForkLift;
        }


        public AGVTcpServer getAGVTcpServer()
        {
            if (agvTcpServer == null)
                agvTcpServer = new AGVTcpServer();

            return agvTcpServer;
        }


         public List<SingleTask> getSingleTaskList()  //获取供选择任务列表
         {
             lock (lockData)
             {
                 if (singleTaskList == null)
                 {
                     singleTaskList = AGVInitialize.getInitialize().getDBConnect().SelectSingleTaskList();
                 }
             }
             return singleTaskList;
         }

        public List<SingleTask> getDownPickSingleTask()
         {
             List<SingleTask> sList = AGVInitialize.getInitialize().getDBConnect().SelectSingleTaskList();
             List<SingleTask> downList = new List<SingleTask>();
             foreach(SingleTask st in sList)
             {
                 if (st.taskType == TASKTYPE_T.TASK_TYPE_DOWN_PICK)
                 {
                     downList.Add(st);
                 }
             }

             return downList;
         }

        public ForkLift getForkLiftByID(int forkLiftID)
        {
            ForkLift forkLift = null;
            foreach (ForkLift fl in forkLiftList)
            {
                
                if (fl.id == forkLiftID)
                    forkLift = fl;
            }

            return forkLift;
        }

        public SingleTask getSingleTaskByID(int singleTaskID)
        {
            SingleTask singleTask = null;
            foreach (SingleTask st in singleTaskList)
            {
                if (st.taskID == singleTaskID)
                    singleTask = st;
            }

            return singleTask;
        }

        public SingleTask getSingleTaskByTaskName(string taskName)
        {
            SingleTask singleTask = null;
            foreach (SingleTask st in singleTaskList)
            {
                if (st.taskName.StartsWith(taskName))
                    singleTask = st;
            }

            return singleTask;
        }

        public DBConnect getDBConnect()   //获取数据库连接
        {
            if (dbConnect == null)
                dbConnect = new DBConnect();
            return dbConnect;
        }

        public Schedule getSchedule()
        {
            if (schedule == null)
                schedule = new Schedule();

            return schedule;
        }

        public AGVCom getAGVCom()
        {
            if (agvCom == null)
            {
                agvCom = new AGVCom();
            }
            return agvCom;
        }
        

        public AGVUtil getAGVUtil()
        {
            if (agvUtil == null)
            {
                agvUtil = new AGVUtil();
            }

            return agvUtil;
        }

        /// <summary>
        /// 可能有缓存任务，刚启动时，车子的状态需要设置
        /// </summary>
        private void setForkliftStateFirst()
        {
            foreach (ForkLift fl in forkLiftList)
            {
                List<TaskRecord> taskRecordList = AGVInitialize.getInitialize().getAGVUtil().updateTaskRecordList();
                foreach (TaskRecord tr in taskRecordList)
                {
                    if (tr.taskRecordStat == TASKSTAT_T.TASK_SEND || tr.taskRecordStat == TASKSTAT_T.TASK_SEND_SUCCESS)
                    {
                        if (tr.forkLift != null && tr.forkLift.forklift_number == fl.forklift_number)
                        {
                            fl.taskStep = ForkLift.TASK_STEP.TASK_EXCUTE;
                        }
                    }
                }
            }
        }

        public AGVInitialize()
        {

        }

        /// <summary>
        /// 开机判断有没有缓存的上货任务，如果有，设置当前阶段为上货
        /// </summary>
        /// <param name="trList"></param>
        /// <returns></returns>
        private bool checkCurrentPeriod(List<TaskRecord> trList)
        {
            foreach (TaskRecord tr in trList)
            {
                if (tr.singleTask.taskType == TASKTYPE_T.TASK_TYPE_UP_PICK || tr.singleTask.taskType == TASKTYPE_T.TASK_TYPE_DOWN_DILIVERY)  //上料任务
                {
                    return true;
                }
            }

            return false;
        }

        private ENV_ERR_TYPE checkCacheTaskRecoreds()
        {
            ENV_ERR_TYPE err = ENV_ERR_TYPE.ENV_ERR_OK;
            List<TaskRecord> taskRecordList = new List<TaskRecord>();
            taskRecordList = getAGVUtil().updateTaskRecordList();
            if (checkCurrentPeriod(taskRecordList))
            {
                err = ENV_ERR_TYPE.ENV_CACHE_UPTASKRECORD_WARNING;
                return err;
            }

            if (taskRecordList.Count > 0)
            {
                err = ENV_ERR_TYPE.ENV_CACHE_TASKRECORD_WARNING;
            }

            return err;
        }

        private ENV_ERR_TYPE checkRunning()
        {
            ENV_ERR_TYPE err = ENV_ERR_TYPE.ENV_ERR_OK;
            bool stat = true;
            ///检查升降串口，必要条件，升降机串口不能用，主程序不能运行
            agvCom = getAGVCom();
            stat = agvCom.initSerialPort();
            if (stat == false)
            {
                err = ENV_ERR_TYPE.ENV_LIFT_COM_ERR;
                return err;
            }

            err = checkCacheTaskRecoreds();

            ///检查3号车是否可用，备选条件，如果不可用，是否存在其它上货方式
            
            ///检测USB报警灯，如果USB报警灯有问题，切换到电脑声音报警
            try
            {
                if (JG_OpenUSBAlarmLamp(0) == 0)
                {
                    MessageBox.Show("打开报警灯失败，切换到电脑声音报警");
                    useUsbAlarm = false;
                }
            }catch (Exception ex)
            {
                MessageBox.Show("打开报警灯失败，切换到电脑声音报警");
                useUsbAlarm = false;
            }
            return err;
        }

        private void handleCheckRunning(ENV_ERR_TYPE err)
        {
            if (err == ENV_ERR_TYPE.ENV_LIFT_COM_ERR)
            {
                DialogResult dr;
                dr = MessageBox.Show(env_err_type_text(err), "错误提示", MessageBoxButtons.OK);

                if (dr == DialogResult.OK)
                {
                    Console.WriteLine(" exit ");
                    System.Environment.Exit(0);
                }
            }
            else if (err == ENV_ERR_TYPE.ENV_CACHE_TASKRECORD_WARNING)
            {
                DialogResult dr;
                dr = MessageBox.Show(env_err_type_text(err), "检测到缓存任务", MessageBoxButtons.YesNo);

                if (dr == DialogResult.Yes)
                {
                    Console.WriteLine(" do nothing ");
                }
                else if (dr == DialogResult.No)
                {
                    getAGVUtil().deleteCacheTaskRecord();
                }
            }
            else if (err == ENV_ERR_TYPE.ENV_CACHE_UPTASKRECORD_WARNING)
            {
                DialogResult dr;
                dr = MessageBox.Show(env_err_type_text(err), "缓存任务", MessageBoxButtons.YesNo);

                if (dr == DialogResult.Yes)
                {
                    AGVInitialize.getInitialize().getSchedule().setDownDeliverPeriod(true);  //设置当前处于上货阶段
                }
                else if (dr == DialogResult.No)
                {
                    getAGVUtil().deleteCacheTaskRecord();
                }
            }
        }
        public void agvInit()
        {
            AGVLog.WriteInfo("程序启动", new StackFrame(true));
            forkLiftList = getAGVUtil().getForkLiftList();
            singleTaskList = getSingleTaskList();
            setForkliftStateFirst();

            ENV_ERR_TYPE err = checkRunning();
            handleCheckRunning(err);
            
            agvCom.startReadSerialPortThread();
        }


    }
}
