using System;

using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;

using AGV.util;
namespace AGV
{
    public enum SHEDULE_PAUSE_TYPE_T
    {
        SHEDULE_PAUSE_TYPE_MIN = 0,  //一般叉获任务，优先级最低
        SHEDULE_PAUSE_SYSTEM_WITH_START, //设置系统暂停，暂停所有的车子，清除标志之后，所有的车重新被启动  用于系统暂停功能
        SHEDULE_PAUSE_SYSTEM_WITHOUT_START, // 设置系统暂停，暂停所有的车子，清除标志后，所有的车子不被启动，需要手动启动
        SHEDULE_PAUSE_UP_WITH_START, // 暂停楼上的车子，清除标志后，楼上的车子重新被启动  用于 下货期间 楼上楼下都有货
        SHEDULE_PAUSE_UP_WITHOUT_START,  //暂停楼上的车，清除标志后，楼上的车不被重新启动，需要手动启动    用于检测中断

        SHEDULE_PAUSE_UP_MAX, //区分楼上暂停和楼下暂停指令
        SHEDULE_PAUSE_DOWN_WITH_START, // 暂停楼下的车子，清除标志后，楼下的车子重新被启动  用于上货期间楼上楼下都有货
        SHEDULE_PAUSE_DOWN_WITHOUT_START,  //暂停楼下的车，清除标志后，楼下的车不被重新启动，需要手动启动
        SHEDULE_PAUSE_TYPE_MAX
    };

    public class Schedule
    {
        private List<TaskRecord> taskRecordList = new List<TaskRecord>();  //存储要处理的任务的列表
        private List<ForkLift> forkLiftList = new List<ForkLift>();
        bool scheduleFlag = true;
        string lastMsg = String.Empty;
        public enum eMSG_STAT { MSG_OK, MSG_IMCOMPLETE, MSG_ERR }; //接收消息格式

        private object lockTaskRecord = new object(); //任务记录锁 防止任务记录同时被多个进程修改
        public delegate void handleRecvMessageCallback(int fID, byte[] buffer, int length);  //消息处理回调函数
        public delegate void handleReconnectCallback(AGVTcpClient tcpClient, bool status);  //重连回调函数

        private bool lowpowerShedule = false;  //当前有AGV处于低电池状态
        private int setCtrlTimes = 0;

        private SHEDULE_PAUSE_TYPE_T lastPause = SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN;  //上一个暂停标志，用于对比pause，作比较
        private SHEDULE_PAUSE_TYPE_T pause = SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN;

        static int pauseSetTime_f1 = 0;
        static int pauseSetTime_f2 = 0;
        public TaskRecord addTs = null;
        public TaskRecord removeTs = null;
        public TaskRecord upTaskRecord = null; //上料任务仅允许一个上料任务
        
        private bool downDeliverPeriod = false;  //当前是否处于上货的状态 上货状态的时候 不发送下货任务
        private List<SingleTask> upPickSingleTaskList = new List<SingleTask>();
        private int upPicSingleTaskkUsed = 1; //用于轮流获取楼上取货任务, 默认从14.xml开始卸货

        private List<SingleTask> downPickSingleTaskList = new List<SingleTask>();
        private int downPicSingleTaskkUsed = 0; //用于轮流获取楼上取货任务

        public Schedule()  //一次启动删除一个月前的任务
        {
            //刪除一个月前的任务
            if (!initEnv())
            {
            }

        }

        private bool initEnv()
        {
            bool envOK = false;

            lock (AGVInitialize.getInitialize().getLockTask())
            {
                connectForks();

                foreach(SingleTask st in AGVInitialize.getInitialize().getSingleTaskList())
                {
                    if (st.taskType == TASKTYPE_T.TASK_TYPE_UP_PICK)
                    {
                        upPickSingleTaskList.Add(st);  //总共只有两个楼上取货任务
                    } else if (st.taskType == TASKTYPE_T.TASK_TYPE_DOWN_PICK)
                    {
                        downPickSingleTaskList.Add(st);
                    }
                }
            }

            Thread.Sleep(100);
            while (true)
            {
                foreach (ForkLift fl in forkLiftList)
                {
                    if (fl.isUsed == 1)
                    {
                        if (fl.position.px == 0 || fl.position.py == 0)
                        {
                            Console.WriteLine("Wait for Fork " + fl.id + " to update position");
                            //continue;
                        }
                    }
                }
                break;
            }
            return envOK;
        }

        private void shedulePause()  //用于系统暂停时，检测暂停是否发送成功，如果没有发送成功，则一直向该车发送暂停
        {
            while (pause > SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN && pause < SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_MAX)
            {
                foreach (ForkLift fl in forkLiftList)
                {
		            if (pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_WITH_START || pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_WITHOUT_START) //楼上楼下都有货时，暂停楼上的车，露楼下的车不用检测20160929 破凉
		            {
			            if (fl.forklift_number == 3)
			            {
				            continue;
			            }
		            }
                    if (fl.getPauseStr().Equals("运行"))  //如果该车返回的pauseStat没有被设置成1，则向该车发送暂停
                    {
                        AGVInitialize.getInitialize().getAGVUtil().setForkCtrlWithPrompt(fl, 1);
                    }
               }

                Thread.Sleep(1000);
            }
        }

        public void setLowpowerShedule(bool flag)
        {
            this.lowpowerShedule = flag;
        }

        public void setPause(SHEDULE_PAUSE_TYPE_T spt)
        {
            this.pause = spt;

            AGVLog.WriteInfo(spt.ToString() + "was set ", new StackFrame(true));
            ///同步楼下的客户端
            if (pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START)
            {
                AGVInitialize.getInitialize().getAGVTcpServer().sendDataMessage("systemPause=1");
            } else if (pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN && lastPause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START)
            {
                AGVInitialize.getInitialize().getAGVTcpServer().sendDataMessage("systemPause=0");
            }

            if (this.pause > SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN && this.pause < SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_MAX) 
            {
                AGVLog.WriteInfo("启动暂停检测线程", new StackFrame(true));
                Thread shedulePauseThread;
                shedulePauseThread = new Thread(new ThreadStart(shedulePause));
                shedulePauseThread.IsBackground = true;
                shedulePauseThread.Start();
            }
        }

        public bool getSystemPause()
        {
            if (pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START || pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITHOUT_START)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 检测是否有待发送任务
        /// </summary>
        /// <returns></returns>
        public bool checkReadySendTaskRecord()
        {
            foreach (TaskRecord tr in taskRecordList)
            {
                if (tr.taskRecordStat == TASKSTAT_T.TASK_READY_SEND)  //上料任务
                {
                    Console.WriteLine(" there is task ready send");

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 根据顺序获取楼上取货任务
        /// </summary>
        /// <returns></returns>
        private SingleTask getUpPickSingleTaskOnTurn()
        {
            int tmp = upPicSingleTaskkUsed % 2;
            int count = upPickSingleTaskList.Count;
            SingleTask st = null;
            if (tmp < count)
            {
                st = upPickSingleTaskList[tmp];
            }

            Console.WriteLine(" get up st = " + st.taskID + " taskName = " + st.taskName);
            return st; //轮流获取single task的值
        }

        /// <summary>
        /// 根据顺序获取楼上取货任务
        /// </summary>
        /// <returns></returns>
        private SingleTask getDownPickSingleTaskOnTurn()
        {
            downPickSingleTaskList = AGVInitialize.getInitialize().getDownPickSingleTask();
            int tmp = downPicSingleTaskkUsed % 2;
            int count = downPickSingleTaskList.Count;
            SingleTask st = null;

            if (tmp < count)
            {
                st = downPickSingleTaskList[tmp];
            }

            return st; //轮流获取single task的值
        }

        public void updateDownPickSingleTask()
        {
            downPickSingleTaskList = AGVInitialize.getInitialize().getDownPickSingleTask();
        }

        private TaskRecord checkUpTaskRecord()
        {
            foreach (TaskRecord tr in taskRecordList)
            {
                if (tr.singleTask.taskType == TASKTYPE_T.TASK_TYPE_DOWN_DILIVERY)  //上料任务
                {
                    Console.WriteLine(" check up record = " + tr.taskRecordID + " tr stat = " + tr.taskRecordStat);

                    return tr;
                }
            }

            return null;
        }


        private bool checkDownDeliverPeriodOver(int upStep)
        {
            foreach(ForkLift fl in forkLiftList)
            {
                if (fl.isUsed == 1 && fl.taskStep != ForkLift.TASK_STEP.TASK_IDLE) //如果有车在运行
                {
                    return false;
                }
            }

            if (AGVInitialize.getInitialize().getAGVCom().getOutCommand() != 0) //升降机上有任务, 表示上料没有结束
            {
                return false;
            }

            if (upStep > 0)
            {
                Console.WriteLine("当前处于上货 " + upStep + " 阶段");
                AGVLog.WriteError("当前处于上货 " + upStep + " 阶段", new StackFrame(true));
                return false;
            }

            return true;
        }

        /// <summary>
        /// 检测楼上往楼下送货任务有没有结束
        /// </summary>
        /// <param name="upStep"></param>
        /// <returns></returns>
        private bool checkUpDeliverPeriodOver(int step)
        {
            foreach (ForkLift fl in forkLiftList)
            {
                if (fl.isUsed == 1 && fl.taskStep != ForkLift.TASK_STEP.TASK_IDLE) //如果有车在运行
                {
                    Console.WriteLine("fl " + fl.id + "is run");
                    return false;
                }
            }

            if (AGVInitialize.getInitialize().getAGVCom().getOutCommand() != 0) //升降机上有任务, 表示上料没有结束
            {
                Console.WriteLine("升降机上有货11");
                return false;
            }

            if (step > 0)
            {
                Console.WriteLine("当前处于下货 " + step + " 阶段");
                AGVLog.WriteError("当前处于下货 " + step + " 阶段", new StackFrame(true));
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="needStart">需要启动的车</param>
        /// <param name="work"></param>
        /// <returns></returns>
        public bool check_start_state(ForkLift needStart, ForkLift work)
        {
            if (work.position.area == 1 && work.isUsed == 1) //只有下货阶段可以提前启动
            {
                if (!downDeliverPeriod && work.position.px - needStart.position.px > 500)
                {
                    return true;
                }

                ///上货的时候直接不给启动
                Console.WriteLine("叉车 " + work.id + "在区域1，不能启动" + " 距离 " + (work.position.px - needStart.position.px));
                AGVLog.WriteInfo("叉车 " + work.id + "在区域1，不能启动" + " 距离 " + (work.position.px - needStart.position.px), new StackFrame(true));
                return false;
            }

            return true;

        }
        /// <summary>
        /// 设置将要添加的任务列表，只能在sheduleTask方法中队taskRecordlist进行操作
        /// </summary>
        /// <param name="tr"></param>
        public void setAddTaskRecord(TaskRecord tr)
        {
            addTs = tr;
        }

        private void flReconnectCallback(AGVTcpClient agvTcpClient, bool status)
        {
            agvTcpClient.setRecvFlag(status);
            if (status)  //如果接收成功
            {
                agvTcpClient.startRecvMsg(agvTcpClient.t_forklift);  //重新启动接收程序线程
            }
            else
            {
                AGVLog.WriteError(agvTcpClient.t_forklift.forklift_number + "号车 连接错误", new StackFrame(true));
                Message message = new Message();
                message.setMessageType(AGVMESSAGE_TYPE_T.AGVMESSAGE_NET_ERR);
                message.setMessageStr(agvTcpClient.t_forklift.forklift_number + "号车 连接错误");

                AGVInitialize.getInitialize().getAGVMessage().setMessage(message);
            }

        }

        private List<ForkLift> connectForks()
        {
            forkLiftList = AGVInitialize.getInitialize().getAGVUtil().getForkLiftList();

            foreach (ForkLift fl in forkLiftList)
            {
                try
                {
                    fl.tcpClient = new AGVTcpClient(fl);

                    fl.tcpClient.registerRecvMessageCallback(handleForkLiftMsg);
                    fl.tcpClient.registerReconnectCallback(flReconnectCallback);
                    fl.tcpClient.TcpConnect(fl.ip, fl.port);
                    fl.tcpClient.startRecvMsg(fl);   //开始接收数据

                }
                catch (Exception ex)
                {
                    fl.tcpClient.isRecvMsgFlag = true;  //设置重启线程标志
                    //fl.isUsed = 0;
                    AGVLog.WriteError("connect ip: " + fl.ip + "port: " + fl.port + "fail", new StackFrame(true));
                }
            }

            return forkLiftList;
        }

        /// <summary>
        /// 根据编号，查找对应的单车
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        private ForkLift getForkLiftByNunber(int number)
        {
            foreach (ForkLift fl in forkLiftList)
            {
                if (fl.forklift_number == number)
                    return fl;
            }

            return null;
        }

        /// <summary>
        /// 获取优先选择的单车
        /// </summary>
        /// <param name="fl_1"></param>
        /// <param name="fl_2"></param>
        /// <returns></returns>
        public ForkLift getHighLevel_ForkLiftf(ForkLift fl_1, ForkLift fl_2)
        {
            if (fl_1.position.startPx < fl_2.position.startPx)
                return fl_1;
            else
                return fl_2;
        }

        public ForkLift getSheduleForkLift()  //如果两辆AGV都空闲，必须选择前面一辆AGV，否则后面一辆AGV一直不能走
        {
            ForkLift fl_1 = null;
            ForkLift fl_2 = null;  //另一辆车
            ForkLift forklift = null;
            int freeForkCount = 0; //空闲车辆总数
            foreach (ForkLift fl in forkLiftList)
            {
                    if (fl.forklift_number == 3)  //只考虑楼上的车子
                        continue;

                    if (fl_1 == null)
                    {
                        fl_1 = fl;
                    }
                    else
                    {
                        fl_2 = fl;
                    }

                    if (fl.isUsed == 1 && fl.taskStep == ForkLift.TASK_STEP.TASK_IDLE && fl.finishStatus == 1)  //如果有车子同时满足要求，选择使用优先级较高的车
                    {
                        fl.position.updateStartPosition();  //车子执行完任务回到起始位置 这时候的起始位置才是有效的
                        freeForkCount++;
                        forklift = fl;                   //可用的叉车
                    }
           }
            

            if (freeForkCount == 1)
            {
                
                if (forklift.id == fl_1.id)
                {
                    if (!check_start_state(forklift, fl_2))
                    {
                        return null;
                    }
                }
                else if (forklift.id == fl_2.id)
                {
                    if (!check_start_state(forklift, fl_1))
                    {
                        return null;
                    }
                }
                 
            }
            else if (freeForkCount == 2)
            {
                forklift = getHighLevel_ForkLiftf(fl_1, fl_2);  //有车可选的时候，使用优先级高的车
            }

            return forklift;
        }

        /// <summary>
        /// 开启调度任务，检测数据库待执行的任务，将任务有效率
        /// </summary>
        public void startShedule()
        {
            Thread startSheduleThread;
            startSheduleThread = new Thread(new ThreadStart(sheduleTask));
            startSheduleThread.IsBackground = true;
            startSheduleThread.Start();

            Thread ctrlThread;
            ctrlThread = new Thread(new ThreadStart(scheduleInstruction));
            ctrlThread.IsBackground = true;
            ctrlThread.Start();
        }

        /// <summary>
        /// 添加任务记录
        /// </summary>
        /// <param name="taskRecordStat"></param>
        /// <param name="st"></param>
        /// <param name="taskPalletType"></param>
        public void addTaskRecord(TASKSTAT_T stat, SingleTask st)
        {
            /*TaskRecord tr = new TaskRecord();
            tr.taskRecordStat = taskRecordStat;
            tr.singleTask = st;
            tr.taskRecordName = st.taskName;*/
            AGVInitialize.getInitialize().getDBConnect().InsertTaskRecord(stat, st);
        }

        /// <summary>
        /// 添加任务记录，包括两部1、更新taskRecordList 2、更新数据库
        /// </summary>
        /// <param name="taskRecordStat"></param>
        /// <param name="st"></param>
        /// <param name="taskPalletType"></param>
        public void addTaskRecord(TaskRecord tr)
        {
            AGVInitialize.getInitialize().getDBConnect().InsertTaskRecord(tr);
        }

        /// <summary>
        /// 根据相关条件移除任务 包括更新任务列表 删除数据库
        /// </summary>
        /// <param name="taskRecordStat"></param>
        /// <param name="st"></param>
        /// <param name="taskPalletType"></param>
        public void removeTaskRecord(SingleTask st, TASKSTAT_T taskRecordStat)
        {
            AGVInitialize.getInitialize().getDBConnect().RemoveTaskRecord(st, taskRecordStat);
        }

        public void topTaskRecord(TaskRecord tr)
        {
            int count = 0;
            count = AGVInitialize.getInitialize().getDBConnect().selectMaxBySql("select max(taskLevel) from taskrecord");  //查询所有被置过顶的任务
            tr.taskLevel = count + 1;

            AGVInitialize.getInitialize().getDBConnect().UpdateTaskRecord(tr);
        }

        /// <summary>
        /// 用于当前处于上货或下货阶段
        /// </summary>
        /// <param name="ddp"></param>
        public void setDownDeliverPeriod(bool ddp)
        {
            downDeliverPeriod = ddp;
        }

        /// <summary>
        /// 获取当前是处于上货还是下货阶段
        /// </summary>
        /// <returns> true 表示当前处于上货阶段 false 表示当前处于下货阶段</returns>
        public bool getDownDeliverPeriod()
        {
            return this.downDeliverPeriod;
        }

        private void sheduleTask()
        {
            List<TaskRecord> tempTaskRecordList = new List<TaskRecord>();
            ForkLift fl = null;
            SingleTask st = null;
            int upRecordStep = 0;  //没有上货， 该值大于0的时候，表示上货还没有结束，可能存在升降机正在运送，车子当前没有任务
            int downRecordStep = 0;  //没有下货，该值大于0的时候， 表示下货没有结束
            int result = -1;
            while (scheduleFlag)
            {
                Thread.Sleep(2000);
                if (getSystemPause())  //系统暂停后，调度程序不执行
                {
                    Console.WriteLine("system pause");
                    continue;
                }
                //Console.WriteLine(" shedule Task");
                lock (AGVInitialize.getInitialize().getLockTask())
                {
                    taskRecordList = AGVInitialize.getInitialize().getAGVUtil().updateTaskRecordList();
                    upTaskRecord = checkUpTaskRecord();
                }
                    //if (upTaskRecord != null)
                      //  Console.WriteLine(" upTaskRecord name = " + upTaskRecord.taskRecordName);

                    if (downDeliverPeriod) //当前处于上货阶段，有的话控制升降机上升
                    {
                        //读取升降机上料信号
                        if (AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_UP)  //检测到楼下有货，发送指令到升降机运货到楼上
                        {
                            AGVInitialize.getInitialize().getAGVCom().setDataCommand(LIFT_IN_COMMAND_T.LIFT_IN_COMMAND_UP);
                            while (AGVInitialize.getInitialize().getAGVCom().getOutCommand() != LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_DOWN)  //等待升降机送货到楼上
                            {
                                Console.WriteLine("wait lifter up goods");
                                Thread.Sleep(100);
                            }

                            if (upRecordStep > 0) //升降机将货运到楼上,保证upRecordStep不小于0
                            {
                                upRecordStep--;
                                AGVLog.WriteError("上货期间 升降机将货运送到楼上: step = " + upRecordStep , new StackFrame(true));
                            }
                        }

                        if (AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_DOWN
                            || AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_UP_DOWN) //上货期间 楼上楼下都有货， 楼上的车子需要继续运行
                        {
                            lock(AGVInitialize.getInitialize().getLockForkLift())
                            {
                                //运动到楼上后，发送指令到楼上AGV，把货取走
                                fl = getSheduleForkLift();
                                if (fl != null)
                                {
                                    st = getUpPickSingleTaskOnTurn();
                                    TaskRecord tr_tmp = new TaskRecord();
                                    tr_tmp.singleTask = st;
                                    tr_tmp.taskRecordName = st.taskName;
                                    AGVInitialize.getInitialize().getSchedule().addTaskRecord(tr_tmp);
                                    AGVInitialize.getInitialize().getAGVUtil().sendTask(tr_tmp, fl); //发送任务
                                    upPicSingleTaskkUsed++; //用于下次切换卸货点
                                } else
                                {
                                    Console.WriteLine(" 楼上没有可用的车去卸货");
                                    AGVLog.WriteError(" 楼上没有可用的车去卸货", new StackFrame(true));
                                }
                            }
                            if (fl != null)
                            {
                                
                                while (AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_DOWN
                                    || AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_UP_DOWN)  //等待楼上货物被取走,如果车的状态回到idle,说明任务发送失败
                                {
                                    if (fl.taskStep == ForkLift.TASK_STEP.TASK_IDLE)
                                    {
                                        break;
                                    }
                                    Console.WriteLine("wait lifter goods to be pick");
                                    Thread.Sleep(500);
                                }

                                if (upRecordStep > 0) //升降机将货运到楼上,保证upRecordStep不小于0
                                {
                                    upRecordStep--;
                                    AGVLog.WriteError("上货期间 楼上货物被取走: step = " + upRecordStep, new StackFrame(true));
                                }
                            }
                        }
                            

                        if (checkDownDeliverPeriodOver(upRecordStep))  //检测上料任务有没有结束 条件1：没有上料任务缓存  条件2：所有车子空闲 条件3：升降机上没有货物
                        {
                            downDeliverPeriod = false;
                        }

                        downRecordStep = 0; //下料信号置0
                        Console.WriteLine("上料阶段");
                    } 
                    
                    if (upTaskRecord != null)
                    {
                        if (!downDeliverPeriod && !checkUpDeliverPeriodOver(downRecordStep)) //检查下货任务有没有结束，升降机从楼上到楼下流程走完，没有正在执行的下货任务
                            {
                                Console.WriteLine("当前下货任务没有执行完成，执行完后再开始执行上货任务");
                            
                            } else if (upTaskRecord.taskRecordStat == TASKSTAT_T.TASK_READY_SEND)
                            {
                                fl = getForkLiftByNunber(3);  //获取楼下三号车

                                if (fl.taskStep != ForkLift.TASK_STEP.TASK_IDLE)
                                {
                                    Console.WriteLine("上料任务正在执行，等待上料任务执行完成");
                                    continue;
                                }

                                if (AGVInitialize.getInitialize().getAGVCom().getOutCommand() != LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_MIN) //只要升降机上有货或有异常都不发送上货任务，否则容易造成楼上楼下都要货
                                {
                                    Console.WriteLine(" 升降机楼下有货，不发送上货任务");   
                                } else
                                {
                                    lock(AGVInitialize.getInitialize().getLockForkLift())  //锁住车的状态
                                    {
                                        result = AGVInitialize.getInitialize().getAGVUtil().sendTask(upTaskRecord, fl); //发送任务
                                        downDeliverPeriod = true; //上货任务发送后，才进入上料阶段
                                        
                                        if (result == 0) //发送成功 才正式进入上货阶段
                                        {
                                            if (upRecordStep <= 2) //避免上货 step被加得太多，不能进入下货阶段
                                            {
                                               upRecordStep += 2;
                                               AGVLog.WriteError("上货期间 发送任务: step = " + upRecordStep, new StackFrame(true));
                                            }
                                        }
                                    }
                                }
                            }
                    }

                    //检测升降机2楼有货物，发送指令将升降机送货到楼下
                    //检测升降机1楼有货物，调度1楼AGV送货
                    //读取升降机上料信号
                    if (!downDeliverPeriod)
                    {
                        Console.WriteLine(" 下料阶段");
                        upRecordStep = 0; //上料信号置0
                        if (AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_DOWN)  //检测到楼上有货，发送指令到升降机运货到楼下
                        {
                            int times_tmp = 0;
                            AGVInitialize.getInitialize().getAGVCom().setDataCommand(LIFT_IN_COMMAND_T.LIFT_IN_COMMAND_DOWN);
                            while (AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_DOWN && times_tmp < 60)  //等待升降机送货到楼下
                            {
                                Console.WriteLine("wait lifter down goods"); //光电感应大概10S可以结束
                                times_tmp++;
                                Thread.Sleep(1000);
                            }

                            if (times_tmp < 60)
                            {
                                if (downRecordStep > 0) //升降机将货运到楼上,保证upRecordStep不小于0
                                {
                                    downRecordStep--;
                                    AGVLog.WriteError("下货期间 楼上货物送到楼下: step = " + downRecordStep, new StackFrame(true));
                                }
                            }

                            times_tmp = 0;
                        }

                        if (AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_UP ||
                                AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_UP_DOWN) //检测到楼下有货，通知AGV来取货
                        {
                            TaskRecord tr_tmp = new TaskRecord();
                            fl = getForkLiftByNunber(3);
                            st = getDownPickSingleTaskOnTurn();
                            tr_tmp.singleTask = st;
                            tr_tmp.taskRecordName = st.taskName;
                            if (fl != null && fl.taskStep == ForkLift.TASK_STEP.TASK_IDLE)
                            {
                                AGVInitialize.getInitialize().getSchedule().addTaskRecord(tr_tmp); //发货后，才确认添加该记录
                                result = AGVInitialize.getInitialize().getAGVUtil().sendTask(tr_tmp, fl);
                                if (result == 0) //任务发送成功
                                {
                                    downPicSingleTaskkUsed++; //用于下次切换卸货点
                                    if (downRecordStep > 0) //升降机将货运到楼上,保证upRecordStep不小于0
                                    {
                                        downRecordStep--;
                                        AGVLog.WriteError("下货期间 楼下货物被取走: step = " + downRecordStep, new StackFrame(true));
                                    }
                                }
                                else
                                {
                                    AGVInitialize.getInitialize().getSchedule().removeTaskRecord(tr_tmp.singleTask, tr_tmp.taskRecordStat);  //如果任务没发送成功，删除该条记录
                                }
                            }

                        }

                        lock (AGVInitialize.getInitialize().getLockTask())
                        {
                            foreach (TaskRecord tr in taskRecordList)
                            {
                                lock(AGVInitialize.getInitialize().getLockForkLift())
                                {
                                    if(tr.taskRecordStat == TASKSTAT_T.TASK_READY_SEND)
                                    {
                                        fl = null;
                                        if (tr.singleTask.taskType == TASKTYPE_T.TASK_TYPE_DOWN_PICK)
                                            fl = getForkLiftByNunber(3);
                                        else if (tr.singleTask.taskType == TASKTYPE_T.TASK_TYPE_UP_DILIVERY)
                                            fl = getSheduleForkLift();  //有任务执行的时候，才考虑检查车子状态
                                        //if (fl.taskStep == ForkLift.TASK_STEP.TASK_IDLE && fl.finishStatus == 1)  //检查车子的状态，向空闲的车子发送任务,如果发送失败，后面会检测发送状态，
                                        //并将该任务状态改成待发重新发送
                                        if (fl != null && fl.taskStep == ForkLift.TASK_STEP.TASK_IDLE)
                                        {
                                            result = AGVInitialize.getInitialize().getAGVUtil().sendTask(tr, fl); //发送任务
                                            if (result == -1) //任务没有发送成功会中断本次循环，防止发送任务到后面的车
                                            {
                                                break;
                                            }

                                            if (tr.singleTask.taskType == TASKTYPE_T.TASK_TYPE_UP_DILIVERY && downRecordStep < 4) //发送的是楼上送货，并且送货发送次数小于2次
                                            {
                                                downRecordStep += 2;
                                                AGVLog.WriteError("下货期间 发送任务: step = " + downRecordStep, new StackFrame(true));
                                            }
                                            
                                        }
                                    }
                                }
                            }
                        }
                    }
            }
        }

        private void sheduleLift()
        {
            Message message = new Message();
            if (AGVInitialize.getInitialize().getAGVCom().getOutCommand() == LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_UP_DOWN)  //楼上楼下都有货
            {
                AGVLog.WriteWarn("升降机楼上楼下都有货，楼上的车被暂停", new StackFrame(true));
                message.setMessageType(AGVMESSAGE_TYPE_T.AGVMEASAGE_LIFT_UPDOWN);
                message.setMessageStr("升降机楼上楼下都有货，楼上的车被暂停");

                AGVInitialize.getInitialize().getAGVMessage().setMessage(message);  //发送消息
            } else if (AGVInitialize.getInitialize().getAGVCom().getOutCommand() > LIFT_OUT_COMMAND_T.LIFT_OUT_COMMAND_UP_DOWN)  //升降机 卡货
            {
                AGVLog.WriteWarn("升降机卡货，系统被暂停", new StackFrame(true));
                message.setMessageType(AGVMESSAGE_TYPE_T.AGVMESSAGE_LIFT_BUG);
                message.setMessageStr("升降机卡货，系统被暂停");

                AGVInitialize.getInitialize().getAGVMessage().setMessage(message);  //发送消息
            }
        }


        private void checkPausePosition(ForkLift fl)
        {
            if (fl.shedulePause == 1)
            {

                if (fl.position.area == 3 && fl.position.px > AGVInitialize.BORDER_X_3 + AGVInitialize.BORDER_X_3_DEVIATION_PLUS)  //从区域3进入区域2的时候， 如果没有暂停成功或暂停慢了，重新启动车子，并报警
                {
                    Console.WriteLine(fl.forklift_number + "号车 pause position = " + fl.position.px);

                    fl.position.area = 2;
                    fl.shedulePause = 0;

                    Message message = new Message();
                    message.setMessageType(AGVMESSAGE_TYPE_T.AGVMESSAGE_SENDPAUSE_ERR);
                    message.setMessageStr("第二分界线 检测中断错误");

                    AGVLog.WriteError("第二分界线 检测中断错误", new StackFrame(true));
                    AGVInitialize.getInitialize().getAGVMessage().setMessage(message);
                } else if (fl.position.area == 2 && fl.position.px > AGVInitialize.BORDER_X_2 + AGVInitialize.BORDER_X_2_DEVIATION_PLUS)  //从区域2进入区域1的时候， 如果没有暂停成功或暂停慢了，重新启动车子，并报警
                {
                    Console.WriteLine(fl.forklift_number + "号车 pause position = " + fl.position.px);

                    fl.position.area = 1;
                    fl.shedulePause = 0;

                    Message message = new Message();
                    message.setMessageType(AGVMESSAGE_TYPE_T.AGVMESSAGE_SENDPAUSE_ERR);
                    message.setMessageStr("第一分界线 检测中断错误");

                    AGVLog.WriteError("第一分界线 检测中断错误", new StackFrame(true));
                    AGVInitialize.getInitialize().getAGVMessage().setMessage(message);
                } else if (fl.position.area == 2 && fl.position.px < AGVInitialize.BORDER_X_3 - AGVInitialize.BORDER_X_3_DEVIATION_PLUS) //从区域2进入区域3的时候，暂停没成功或暂停慢了， 报警，需要手动启动
                {
                    Console.WriteLine(fl.forklift_number + "号车 pause position = " + fl.position.px);

                    fl.position.area = 3;
                    fl.shedulePause = 0;

                    Message message = new Message();
                    message.setMessageType(AGVMESSAGE_TYPE_T.AGVMESSAGE_SENDPAUSE_ERR);
                    message.setMessageStr("第二分界线 检测中断错误");

                    AGVLog.WriteError("第二分界线 检测中断错误", new StackFrame(true));
                    AGVInitialize.getInitialize().getAGVMessage().setMessage(message);
                } else if (fl.position.area == 1 && fl.position.px < AGVInitialize.BORDER_X_2 - AGVInitialize.BORDER_X_2_DEVIATION_PLUS) //从区域2进入区域3的时候，暂停没成功或暂停慢了， 报警，需要手动启动
                {
                    Console.WriteLine(fl.forklift_number + "号车 pause position = " + fl.position.px);

                    fl.position.area = 2;
                    fl.shedulePause = 0;

                    Message message = new Message();
                    message.setMessageType(AGVMESSAGE_TYPE_T.AGVMESSAGE_SENDPAUSE_ERR);
                    message.setMessageStr("第二分界线 检测中断错误");

                    AGVLog.WriteError("第一分界线 检测中断错误", new StackFrame(true));
                    AGVInitialize.getInitialize().getAGVMessage().setMessage(message);
                }
            }
        }

        private SHEDULE_TYPE_T getForkSheduleType(ForkLift fl)
        {
            if (fl.position.area == 1 && fl.position.px < AGVInitialize.BORDER_X_2 - AGVInitialize.BORDER_X_DEVIARION)
            {
                Console.WriteLine(" check " + fl.forklift_number + "号车 从区域1进入2");
                return SHEDULE_TYPE_T.SHEDULE_TYPE_1TO2;
            }
            else if (fl.position.area == 2 && fl.position.px < AGVInitialize.BORDER_X_3 - AGVInitialize.BORDER_X_DEVIARION)
            {
                Console.WriteLine(" check " + fl.forklift_number + "号车 从区域2进入3");
                return SHEDULE_TYPE_T.SHEDULE_TYPE_2TO3;
            }
            else if (fl.position.area == 3 && fl.position.px > AGVInitialize.BORDER_X_3 + AGVInitialize.BORDER_X_DEVIARION)
            {
                Console.WriteLine(" check " + fl.forklift_number + "号车 从区域3进入2");
                return SHEDULE_TYPE_T.SHEDULE_TYPE_3TO2;
            } else if (fl.position.area == 2 && fl.position.px > AGVInitialize.BORDER_X_2 + AGVInitialize.BORDER_X_DEVIARION)
            {
                Console.WriteLine(" check " + fl.forklift_number + "号车 从区域2进入1");
                return SHEDULE_TYPE_T.SHEDULE_TYPE_2TO1;
            }

            return SHEDULE_TYPE_T.SHEDULE_TYPE_MIN;
        }

        private void _sheduleRunning()
        {
            ForkLift fl_1 = null;
            ForkLift fl_2 = null;
            SHEDULE_TYPE_T shedule_type = SHEDULE_TYPE_T.SHEDULE_TYPE_MIN;
            foreach (ForkLift fl in forkLiftList)
            {
                if (fl.forklift_number != 3 && fl.isUsed == 1 && fl.taskStep != ForkLift.TASK_STEP.TASK_IDLE)  //调度没有在使用的车 车子任务没有完成，只有当两辆车同时使用时，才调度
                {
                    if (fl_1 != null)
                        fl_2 = fl;
                    else
                        fl_1 = fl;
                }
            }

            if (fl_1 != null && fl_2 != null)  //两车同时运行时才需要调度
            {
                shedule_type = getForkSheduleType(fl_1);

                if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_1TO2)
                {
                    if (fl_2.shedulePause == 0 && fl_2.position.area == 2)  //检测到另一辆车在区域2运行，需要暂停刚进入区域2的车
                    {
                        if (fl_1.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 1);
                            fl_1.shedulePause = 1;
                        }
                    }
                    else //否则该车正常进入区域2，考虑到之前可能被暂停，没有车在区域2后，该车将被启动
                    {
                        fl_1.position.area = 2; 
                        if (fl_1.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 0);
                            fl_1.shedulePause = 0;
                        }
                    }
                } else if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_2TO3)
                {
                    if (fl_2.shedulePause == 0 && fl_2.position.area == 3)
                    {
                        if (fl_1.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 1);
                            fl_1.shedulePause = 1;
                        }
                    } else
                    {
                        fl_1.position.area = 3;
                        if (fl_1.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 0);
                            fl_1.shedulePause = 0;
                        }
                    }
                }
                else if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_3TO2)
                {
                    if (fl_2.shedulePause == 0 && fl_2.position.area == 2)
                    {
                        if (fl_1.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 1);
                            fl_1.shedulePause = 1;
                        }
                    }
                    else
                    {
                        fl_1.position.area = 2;
                        if (fl_1.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 0);
                            fl_1.shedulePause = 0;
                        }
                    }
                }
                else if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_2TO1)
                {
                    if (fl_2.shedulePause == 0 && fl_2.position.area == 1)
                    {
                        if (fl_1.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 1);
                            fl_1.shedulePause = 1;
                        }
                    }
                    else
                    {
                        fl_1.position.area = 1;
                        if (fl_1.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 0);
                            fl_1.shedulePause = 0;
                        }
                    }
                }

                shedule_type = getForkSheduleType(fl_2);

                if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_1TO2)
                {
                    if (fl_1.shedulePause == 0 && fl_1.position.area == 2)  //检测到另一辆车在区域2运行，需要暂停刚进入区域2的车
                    {
                        if (fl_2.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 1);
                            fl_2.shedulePause = 1;
                        }
                    }
                    else //否则该车正常进入区域2，考虑到之前可能被暂停，没有车在区域2后，该车将被启动
                    {
                        fl_2.position.area = 2;
                        if (fl_2.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 0);
                            fl_2.shedulePause = 0;
                        }
                    }
                }
                else if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_2TO3)
                {
                    if (fl_1.shedulePause == 0 && fl_1.position.area == 3)
                    {
                        if (fl_2.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 1);
                            fl_2.shedulePause = 1;
                        }
                    }
                    else
                    {
                        fl_2.position.area = 3;
                        if (fl_2.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 0);
                            fl_2.shedulePause = 0;
                        }
                    }
                }
                else if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_3TO2)
                {
                    if (fl_1.shedulePause == 0 && fl_1.position.area == 2)
                    {
                        if (fl_2.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 1);
                            fl_2.shedulePause = 1;
                        }
                    }
                    else
                    {
                        fl_2.position.area = 2;
                        if (fl_2.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 0);
                            fl_2.shedulePause = 0;
                        }
                    }
                }
                else if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_2TO1)
                {
                    if (fl_1.shedulePause == 0 && fl_1.position.area == 1)
                    {
                        if (fl_2.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 1);
                            fl_2.shedulePause = 1;
                        }
                    }
                    else
                    {
                        fl_2.position.area = 1;
                        if (fl_2.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 0);
                            fl_2.shedulePause = 0;
                        }
                    }
                }

                /*if (fl_1.position.area == 1 && fl_1.position.px < AGVInitialize.BORDER_X_2) //刚刚进入第二区域，需要检测另一辆车的位置
                {
                    Console.WriteLine("check forklift " + fl_1.id + "enter area 2");
                    if (fl_2.shedulePause == 0 && fl_2.position.area == 2)
                    {
                        //向1车发送停止
                        if (fl_1.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 1);
                            fl_1.shedulePause = 1;
                        }
                    }
                    else
                    {
                        fl_1.position.area = 2;
                        if (fl_1.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 0);
                            fl_1.shedulePause = 0;
                        }
                    }

                }
                else if (fl_1.position.area == 2 && fl_1.position.px > AGVInitialize.BORDER_X_2)    //车子刚进入第一区域，需要检测另一车子的位置
                {
                    Console.WriteLine("check forklift " + fl_2.id + "enter area 1");
                    if (fl_2.shedulePause == 0 && fl_2.position.area == 1)
                    {
                        //向一车发停止
                        if (fl_1.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 1);
                            fl_1.shedulePause = 1;
                        }
                    }
                    else
                    {
                        fl_1.position.area = 1;
                        if (fl_1.shedulePause == 1)
                        {
                            //向1车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 0);
                            fl_1.shedulePause = 0;
                        }

                    }
                }


                if (fl_2.position.area == 1 && fl_2.position.px < AGVInitialize.BORDER_X_2) //需要检测另一辆车的位置
                {
                    Console.WriteLine("check forklift " + fl_1.id + "enter area 2");
                    if (fl_1.shedulePause == 0 && fl_1.position.area == 2)
                    {
                        //向2车发送停止
                        if (fl_2.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 1);
                            fl_2.shedulePause = 1;
                        }
                    }
                    else
                    {
                        fl_2.position.area = 2;
                        if (fl_2.shedulePause == 1)
                        {
                            //向2车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 0);
                            fl_2.shedulePause = 0;
                        }
                    }

                }
                else if (fl_2.position.area == 2 && fl_2.position.px > AGVInitialize.BORDER_X_2)
                {
                    Console.WriteLine("check forklift " + fl_1.id + "enter area 1");
                    if (fl_1.shedulePause == 0 && fl_1.position.area == 1)
                    {
                        //向2车发停止
                        if (fl_2.shedulePause == 0)
                        {
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 1);
                            fl_2.shedulePause = 1;
                        }
                    }
                    else
                    {
                        fl_2.position.area = 1;
                        if (fl_2.shedulePause == 1)
                        {
                            //向2车发送启动
                            AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_2, 0);
                            fl_2.shedulePause = 0;
                        }

                    }
                }*/

                checkPausePosition(fl_1);
                checkPausePosition(fl_2);             
            }
            else if (fl_1 != null && fl_2 == null)
            {
                shedule_type = getForkSheduleType(fl_1);
                if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_1TO2)
                {
                    fl_1.position.area = 2;
                } else if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_2TO3)
                {
                    fl_1.position.area = 3;
                } else if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_3TO2)
                {
                    fl_1.position.area = 2;
                } else if (shedule_type == SHEDULE_TYPE_T.SHEDULE_TYPE_2TO1)
                {
                    fl_1.position.area = 1;
                }

                if (fl_1.shedulePause == 1) //如果车子被暂停，启动该车
                {
                    AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 0);
                    fl_1.shedulePause = 0;
                }
                ///Console.WriteLine("only one forklift was used forklift " + fl_1.id + " position = " + fl_1.position.area + "x = " + fl_1.position.px + " y = " + fl_1.position.py);
                /*if (fl_1.position.area == 1 && fl_1.position.px < AGVInitialize.BORDER_X_2) //刚刚进入第二区域，需要检测另一辆车的位置
                {
                    Console.WriteLine("update forklift " + fl_1.id + " enter area 2");
                    fl_1.position.area = 2;
                    if (fl_1.shedulePause == 1)
                    {
                        AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 0);
                    }
                }
                else if (fl_1.position.area == 2 && fl_1.position.px > AGVInitialize.BORDER_X_2)    //车子刚进入第一区域，需要检测另一车子的位置
                {
                    if (!checkReadySendTaskRecord() && fl_1.shedulePause == 1)  //另一辆车任务执行完成，检测是否有缓存任务，如果有缓存任务，优先发送缓存任务，提高车子效率
                    {
                        AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl_1, 0);
                        fl_1.shedulePause = 0;
                        fl_1.position.area = 1;
                        Console.WriteLine("update forklift " + fl_1.id + " enter area 1");
                    }
                    else if (fl_1.shedulePause == 0)
                    {
                        fl_1.position.area = 1;
                        Console.WriteLine("update forklift " + fl_1.id + " enter area 1");
                    }
                }*/

                
            }
        }

        public void scheduleInstruction()
        {
            while (scheduleFlag)
            {
                Thread.Sleep(500);
                sheduleLift();

                if (getSystemPause())
                {
                    if (lastPause != SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START && lastPause != SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITHOUT_START) //避免多次设置
                    {
                        foreach (ForkLift fl in forkLiftList)
                        {
                            if (fl.shedulePause == 0)
                            {
                                AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl, 1);  //向不是暂停的车发送暂停指令
                            }
                        }
                    }

                    lastPause = pause;
                    continue; //系统暂停后不需要调度
                } else if (pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_WITH_START || pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_WITHOUT_START) //暂停楼上的车，有时候卸货不及时
                {
                    if (lastPause != SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_WITH_START && lastPause != SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_WITHOUT_START) //避免多次设置
                    {

                        foreach (ForkLift fl in forkLiftList)
                        {
                            if (fl.forklift_number != 3 && fl.shedulePause == 0)  //只调度楼上的车
                            {
                                AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl, 1);
                            }
                        }
                    }

                    lastPause = pause;
                    continue;  //楼上的车子被暂停后，不需要调度
                } else if (pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_DOWN_WITH_START || pause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_DOWN_WITHOUT_START)
                {
                    if (lastPause != SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_DOWN_WITH_START && lastPause != SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_DOWN_WITHOUT_START) //避免多次设置
                    {
                        ForkLift fl = getForkLiftByNunber(3);
                        AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl, 1);
                    }

                    lastPause = pause;
                }
                else 
                {
                    if (lastPause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START)
                    {
                        foreach (ForkLift fl in forkLiftList)
                        {
                            if (fl.shedulePause == 0)
                            {
                                AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl, 0);  //之前不是暂停的车，发送启动指令
                            }
                        }
                    }

                    if (lastPause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_WITH_START)
                    {
                        foreach (ForkLift fl in forkLiftList)
                        {
                            if (fl.forklift_number != 3 && fl.shedulePause == 0)
                            {
                                AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl, 0);  //之前不是暂停的车，发送启动指令
                            }
                        }
                    }

                    if (lastPause == SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_DOWN_WITH_START)
                    {
                        ForkLift fl = getForkLiftByNunber(3);
                        AGVInitialize.getInitialize().getAGVUtil().setForkCtrl(fl, 0);
                    }

                    lastPause = pause;
                }

                lock(AGVInitialize.getInitialize().getLockForkLift())  //加锁，避免车的状态不一致
                {
                    _sheduleRunning();
                }

            }
        }

        /// <summary>
        /// 检查任务状态
        /// </summary>
        /// <param name="fl">车子实例</param>
        /// <param name="taskName"></param>
        /// <returns></returns>
        private bool checkTaskSendStat(ForkLift fl, string taskName)
        {
            bool stat = true;
            //Console.WriteLine(" fl id " + fl.id + " finishStatus = " + fl.finishStatus);
            if (fl.finishStatus == 1) //车子状态空闲有两种可能1：车子任务执行完成 2：任务在执行中 报文没有及时反馈
            {
                foreach (TaskRecord tr in taskRecordList)
                {
                    //if (tr.forkLift != null && tr.taskRecordName.Equals(taskName))
                    if(tr.forkLift != null && tr.forkLift.id == fl.id)  //可能存在任务没发送成功，反馈的taskName与现在taskrecord的名称不一样
                    {
                            if (tr.taskRecordStat == TASKSTAT_T.TASK_SEND)
                            {
                                if (fl.waitTimes > 15) //等待次数超过15次，后面重新发送该任务
                                {
                                    Console.WriteLine("send task: " + taskName + "to " + fl.forklift_number + "fail");
                                    fl.waitTimes = 0;
                                    tr.forkLift = null;
                                    fl.taskStep = ForkLift.TASK_STEP.TASK_IDLE;  //车子状态改为空闲
                                    fl.currentTask = "";
                                    AGVInitialize.getInitialize().getDBConnect().updateForkLift(fl);  //赋值空字符串
                                    if (tr.singleTask.taskType == TASKTYPE_T.TASK_TYPE_UP_PICK)
                                    {
                                        
                                        AGVInitialize.getInitialize().getDBConnect().RemoveTaskRecord(tr);
                                        AGVLog.WriteWarn("forklift number: " + fl.forklift_number + " taskName: " + taskName + " 发送失败 移除任务", new StackFrame(true));
                                    }
                                    else
                                    {
                                        tr.taskRecordStat = TASKSTAT_T.TASK_READY_SEND; //改变任务的状态，后面重新发送
                                        tr.singleTask.taskStat = TASKSTAT_T.TASK_READY_SEND;
                                        AGVInitialize.getInitialize().getDBConnect().UpdateTaskRecord(tr);
                                        AGVLog.WriteWarn("forklift number: " + fl.forklift_number + " taskName: " + taskName + " 发送失败 更新任务状态，等待重新发送", new StackFrame(true));
                                    }                                  
                                    AGVInitialize.getInitialize().getMainFrm().updateFrm(); //设置更新界面
                                }
                                else
                                {
                                    fl.waitTimes++;
                                    Console.WriteLine("fl: " + fl.forklift_number + "taskName: " + taskName + "waittimes: " + fl.waitTimes);
                                    AGVLog.WriteWarn("forklift number: " + fl.forklift_number + " taskName: " + taskName + " waittimes: " + fl.waitTimes, new StackFrame(true));
                                }
                                break;
                            }
                            else if (tr.taskRecordStat == TASKSTAT_T.TASK_SEND_SUCCESS) //确保没有重复进入，否则会插入多条备份记录
                            {
                                Console.WriteLine("task: " + taskName + "in " + fl.forklift_number + "finished");
                                AGVLog.WriteInfo("taskName: " + taskName + "in " + fl.forklift_number + " finished", new StackFrame(true));
                                AGVInitialize.getInitialize().getDBConnect().RemoveTaskRecord(tr);  //移除record是3的记录
                                AGVInitialize.getInitialize().getDBConnect().InsertTaskRecordBak(tr);
                                tr.singleTask.taskStat = TASKSTAT_T.TASK_END;
                                tr.taskRecordStat = TASKSTAT_T.TASK_END;
                                AGVInitialize.getInitialize().getMainFrm().updateFrm(); //设置更新界面 //设置更新界面
                                fl.taskStep = ForkLift.TASK_STEP.TASK_IDLE;
                                fl.currentTask = "";
                                AGVInitialize.getInitialize().getDBConnect().updateForkLift(fl);
                                break;
                            }
                            else if (tr.taskRecordStat == TASKSTAT_T.TASK_END)
                            {
                                //break;  继续任务状态没及时删除,继续循环
                            }

                            break; //每次只匹配一条记录，可能存在两条记录，对应singleTask一样，一条正在运行，一条待发送，适应一键添加功能
                        }

                }
            }
            else if (fl.finishStatus == 0)
            {
                //bool storeTask = true; //是否需要缓存该任务
                foreach (TaskRecord tr in taskRecordList)
                {
                    //Console.WriteLine(" tr stat = " + tr.taskRecordStat + " taskName = " + tr.taskRecordName);
                        if (tr.forkLift != null && tr.forkLift.id == fl.id) //任务列表中匹配到非待发送的该任务则不缓存  
                        {
                            if (tr.taskRecordStat == TASKSTAT_T.TASK_SEND)
                            {
                                tr.singleTask.taskStat = TASKSTAT_T.TASK_SEND_SUCCESS;
                                tr.taskRecordStat = TASKSTAT_T.TASK_SEND_SUCCESS;
                                AGVInitialize.getInitialize().getDBConnect().UpdateTaskRecord(tr);
                                stat = true;
                            }
                            else if (tr.taskRecordStat == TASKSTAT_T.TASK_SEND_SUCCESS && tr.forkLift.id == fl.id)
                            {
                            }

                            fl.waitTimes = 0; //发送任务，等待确认是否发送成功
                            fl.taskStep = ForkLift.TASK_STEP.TASK_EXCUTE;
                            fl.currentTask = tr.singleTask.taskText;
                            break; //每次只匹配一条记录，可能存在两条记录，对应singleTask一样，一条正在运行，一条待发送，适应一键添加功能
                        }
                }
/*
                if (storeTask)  //系统启动后，车子可能正在执行任务起来，将正在执行的任务缓存
                {
                    TaskRecord tr = new TaskRecord();
                    tr.taskRecordName = taskName;
                    tr.forkLift = fl;
                    tr.taskRecordStat = TASKSTAT_T.TASK_SEND_SUCCESS; //状态已经发送成功
                    tr.setSingleTaskByTaskName(AGVUtil.parseTaskRecordName(taskName));
                    fl.taskStep = ForkLift.TASK_STEP.TASK_EXCUTE;
                    addTs = tr;
                    //AGVInitialize.getInitialize().getMainFrm().updateCurrentTask(st.taskName); //更新界面上的当前任务
                    Console.WriteLine("store task: " + tr.taskRecordName + "taskNumber:" + taskRecordList.Count);
                    AGVLog.WriteInfo("store task: " + tr.taskRecordName + "at boot task count: " + taskRecordList.Count, new StackFrame(true));
                }
*/
            }
            else
            {
                Console.WriteLine("fork status err");
                AGVLog.WriteError("fork lift staus: " + fl.finishStatus + "err", new StackFrame(true));
            }
            return stat;
        }

        private bool checkForkLiftPosition(ForkLift fl, int px, int py)
        {
            bool stat = false;
            
            if (fl.position.px == 0 && fl.position.py == 0)
            {
                fl.position.setStartPosition(px, py);
            }

            fl.position.px = px;
            fl.position.py = py;

            return stat;
        }

        private void checkForkliftPauseStat(ForkLift fl, int pause_stat)
        {
            if (fl.shedulePause != pause_stat)  //车的暂停状态与返回值不一样，说明没有发送成功
            {
                setCtrlTimes++;
                if (setCtrlTimes > 10)
                {
                    //setForkCtrl(fl, fl.isPaused);  //重新发送车子暂停状态 
                    //报警，需要人工处理
                    setCtrlTimes = 0;
                    Message message = new Message();
                    message.setMessageType(AGVMESSAGE_TYPE_T.AGVMESSAGE_SENDPAUSE_ERR);
                    message.setMessageStr("检测中断错误");

                    //AGVInitialize.getInitialize().getAGVMessage().setMessage(message);
                }
            }
        }
        /// <summary>
        /// 解析车子反馈报文
        /// </summary>
        /// <param name="buffer">第一个字节为0 最后一个字节为0xff，如果不满足此格式，丢弃该报文</param>
        /// <returns>解析得到的字符串</returns>
        private string parseForkLiftMsg(byte[] buffer, int length)
        {
            if (buffer.Length == 0)
            {
                AGVLog.WriteError("forklift buffer length is 0", new StackFrame(true));
                Console.WriteLine("forklift buffer length is 0");
                return null;
            }

           // if (buffer[0] != 0 || buffer[length - 1] != 0xff)  //第一个byte为0 最后一个为0xff
            //{
             //   AGVLog.WriteError("forklift buffer is not match buffer[0] = " + Convert.ToInt16(buffer[0]) + "buffer[length - 1] = " + Convert.ToInt16(buffer[length - 1]), new StackFrame(true));
               // return null;
            //}

            return Encoding.ASCII.GetString(buffer, 1, buffer.Length - 2);
        }

        /// <summary>
        /// 解析车子反馈报文
        /// </summary>
        /// <param name="buffer">第一个字节为0 最后一个字节为0xff，如果不满足此格式，丢弃该报文</param>
        /// <returns>解析得到的字符串</returns>
        private bool checkForkLiftMsg(int battery_soc, int pause_stat, int finished_stat, int gAlarm)
        {
            if (battery_soc < 0 || pause_stat < 0 || finished_stat < 0 || gAlarm < 0)
                return false;

            return true;
        }

        /*处理车子反馈报文 msg格式cmd=position;battery=%d;error=%d;x=%d;y=%d;a=%f;z=%d;
        speed=%d;task=%s;veer_angle=%f;
        task_step=%d;task_isfinished=%d;task_error=%d;walk_path_id=%d */
        /// <summary>
        /// 
        /// </summary>
        /// <param name="id">表示车子</param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public void handleForkLiftMsg(int id, byte[] buffer, int length)
        {
            int pos = -1;
            int pos_e = -1;
            int pos_t = -1;
            string taskName = "";
            int x = 0; //车子横坐标
            int y = 0; //车子纵坐标
            int pause_stat = -1;  //默认是错误状态
            int battery_soc = -1;
            int finish_stat = -1;
            int gAlarm = 1;  //AGV防撞信号 默认1 表示没有报警

            string msg = parseForkLiftMsg(buffer, length);
            if (string.IsNullOrEmpty(msg))
            {
                AGVLog.WriteError("msg is null", new StackFrame(true));
                return ;
            }

            if (msg.Equals(lastMsg))
            {
                //Console.WriteLine(" same msg, do not handle");
                return;
            }
            //Console.WriteLine("msg = " + msg);
            //解析taskName
            try
            {
                //if (id == 2)
                   // AGVLog.WriteError(msg, new StackFrame(true));

                pos_t = msg.IndexOf("task=");

                if (pos_t != -1)
                {
                    pos_e = msg.Substring(pos_t, msg.Length - pos_t).IndexOf(";");
                    if (pos_e != -1)
                    {
                        taskName = msg.Substring(pos_t + 5, pos_e - 5);
                        //AGVLog.WriteInfo("forklift taskName = " + taskName, new StackFrame(true));
                        //Console.WriteLine("forklift taskName = " + taskName);
                    }
                }

                if (string.IsNullOrEmpty(taskName))
                {
                    //AGVLog.WriteError("forklift taskName is null", new StackFrame(true));
                    //Console.WriteLine("msg format err: taskName is null");
                    //return ;  //主要判断车的finished状态
                }

                //解析坐标位置 x,y
                pos_t = msg.IndexOf(";x=");
                if (pos_t != -1)
                {
                    pos_e = msg.Substring(pos_t + 1, msg.Length - pos_t - 1).IndexOf(";");
                    if (pos_e != -1)
                    {
                        // Console.WriteLine("x = " + msg.Substring(pos_t + 3, pos_e - 2) + " id = " + id);
                        x = int.Parse(msg.Substring(pos_t + 3, pos_e - 2));
                    }
                }

                pos_t = msg.IndexOf(";y=");
                if (pos_t != -1)
                {
                    pos_e = msg.Substring(pos_t + 1, msg.Length - pos_t - 1).IndexOf(";");
                    if (pos_e != -1)
                    {
                        // Console.WriteLine("y = " + msg.Substring(pos_t + 3, pos_e - 2));
                        y = int.Parse(msg.Substring(pos_t + 3, pos_e - 2));
                    }
                }

                pos_t = msg.IndexOf("pause_stat=");
                if (pos_t != -1)
                {
                    pos_e = msg.Substring(pos_t, msg.Length - pos_t).IndexOf(";");
                    pause_stat = int.Parse(msg.Substring(pos_t + 11, pos_e - 11));
                }

                pos_t = msg.IndexOf("gAlarm=");
                if (pos_t != -1)
                {
                    pos_e = msg.Substring(pos_t, msg.Length - pos_t).IndexOf(";");
                    gAlarm = int.Parse(msg.Substring(pos_t + 7, pos_e - 7));
                }

                pos_t = msg.IndexOf("battery=");
                if (pos_t != -1) //获取电池数据
                {
                    pos_e = msg.Substring(pos_t, msg.Length - pos_t).IndexOf(";");
                    battery_soc = int.Parse(msg.Substring(pos_t + 8, pos_e - 8));
                    //Console.WriteLine("battery = " + battery_soc);
                }

                pos = msg.IndexOf("task_isfinished=");
                finish_stat = Convert.ToInt16(msg[pos + 16]) - 48;  //转的对象是单个字符 0会转成48
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.WriteLine("接收 数据异常");
                return;
            }

            if (!checkForkLiftMsg(battery_soc, pause_stat, finish_stat, gAlarm))
            {
                //Console.WriteLine("接收数据异常");
                return;
            }

            lock (AGVInitialize.getInitialize().getLockTask())
            {
                if (pos != -1) //成功匹配到状态
                {
                    foreach (ForkLift fl in forkLiftList)
                    {
                        if (fl.id == id)
                        {
                            if (x != 0 && y != 0)
                            {
                                fl.setPosition(x, y);
                                //Console.WriteLine("fl id = " + fl.id + " position = " + fl.position.px + " area = " + fl.position.area);
                            }

                            if (id == 1 && pauseSetTime_f1 == 0)
                            {
                                fl.shedulePause = pause_stat;  //只在启动的时候设置一次
                                pauseSetTime_f1 = 1;
                                fl.calcPositionArea();
                            }
                            else if (id == 2 && pauseSetTime_f2 == 0)
                            {
                                fl.shedulePause = pause_stat;  //只在启动的时候设置一次
                                pauseSetTime_f2 = 1;
                                fl.calcPositionArea();
                            }

                            fl.pauseStat = pause_stat;
                            if (pause_stat >= 0)
                                checkForkliftPauseStat(fl, pause_stat);

                            fl.finishStatus = finish_stat;
                            fl.updateBatterySoc(battery_soc);
                            fl.updateAlarm(gAlarm);

                            AGVLog.WriteInfo("forklift id " + id + "taskName = " + taskName + "forklift stat = " + fl.finishStatus, new StackFrame(true));
                            //Console.WriteLine(" forklift id = " + id + " forklift finishStatus = " + fl.finishStatus + " taskName = " + taskName);
                            //lock (AGVInitialize.getInitialize().getLockTask())
                            {
                                bool stat = checkTaskSendStat(fl, taskName);

                                if (stat == false)
                                {
                                    Console.WriteLine("任务列表中不能匹配正确状态的任务");
                                    AGVLog.WriteError("任务列表中不能匹配正确状态的任务", new StackFrame(true));
                                }
                            }                       
                        }
                    }
                }
            }

            //lastMsg = msg;
            return ;
        }
    }
}
