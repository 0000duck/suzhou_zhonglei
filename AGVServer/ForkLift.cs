using System;
using System.Collections.Generic;
using System.Diagnostics;

using AGV.power;
using AGV.util;

namespace AGV
{
    public class ForkLift  //描述一个车子
    {
        public int id;  //ID
        public int forklift_number;  //车子编号 唯一
        public string ip;  //车子IP
        public int port; //车子监听数据端口号
        private BatteryInfo bi = new BatteryInfo(); //车子电池信息
        public string currentTask = null; //当前任务 默认没有任务
        public int currentStep = 0;  //当前进去到哪一步，每一个任务都分成几个step，需要保证前面几个step是一样的

        /// <summary>
        /// idle 空闲 sended  任务刚发送 excute 任务刚执行 end  任务执行结束
        /// </summary>
        public enum TASK_STEP {TASK_IDLE, TASK_SENDED, TASK_EXCUTE, TASK_END}; //车子任务执行状态，解决车子接收任务与下一刻反馈报文的不同步
        public TASK_STEP taskStep = TASK_STEP.TASK_IDLE;
        public int waitTimes = 0;
        public static int WAIT_FEEDBACK_TIME = 3; //等待报文3次反馈确认
        public int finishStatus = 1; //任务是否结束 默认没有任务

        public AGVTcpClient tcpClient;  //与服务器建立的TCP连接

        public Position position = null;
        public int shedulePause = 0; //是否暂停, 不包括系统暂停和暂停楼上所有车辆的指令，比如该车辆是运行状态，发送系统暂停或暂停楼上车辆后，该值仍然为0
        public int pauseStat = 0;
        public  int gAlarm = 0;  //AGV防撞信号
        

        public int isUsed = 1;  //车子默认使用两辆
        public int useLevel = 0;   //车子使用优先级，根据车子位置，判断车子使用优先级，车子位于AGV1位置，使用优先级较高

        int waittimes = 0;
        public ForkLift()
        {
            this.position = new Position();
        }

        public ForkLift(int id, int forklift_number, string ip, int port, string currentTask = null, int finishStatus = 1)
        {
            this.id = id;
            this.forklift_number = forklift_number;
            this.ip = ip;
            this.port = port;
            this.currentTask = currentTask;
            this.finishStatus = finishStatus;
        }

        public void setPosition(int x, int y)
        {
            if (position == null)
                this.position = new Position();
            this.position.px = x;
            this.position.py = x;
        }

        public void calcPositionArea()
        {
            if (this.position.px > AGVInitialize.BORDER_X_2)
                this.position.area = 1;
            else if (this.position.px > AGVInitialize.BORDER_X_3)
                this.position.area = 2;
            else
                this.position.area = 3;
        }

        //更新电量百分比
        public void updateBatterySoc(int soc)
        {
            this.bi.setBatterySoc(soc);
        }

        public int getBatterySoc()
        {
            return bi.getBatterySoc();
        }

        public bool getBatteryLowpowerStat()
        {
            return bi.isBatteryLowpower();
        }

        public string getPauseStr()
        {
            return pauseStat == 1 ? "暂停" : "运行";
        }

        public void updateAlarm(int alarm)
        {
            Console.WriteLine(this.forklift_number +"号车 alarm = " + alarm + "gAlarm =" + gAlarm);
            if (alarm == 0)
            {
                gAlarm++;
            }
            else if (alarm == 1)
            {
                gAlarm = 0;
            }
            Console.WriteLine(this.forklift_number + "号车 alarm = " + alarm + "gAlarm =" + gAlarm);

            if (gAlarm > AGVInitialize.AGVALARM_TIME)  //防撞信号 检测超过12次，弹出报警提示
            {
                AGVLog.WriteError(this.forklift_number + "触发防撞，暂停所有AGV", new StackFrame(true));
                Message message = new Message();
                message.setMessageType(AGVMESSAGE_TYPE_T.AGVMESSAGE_AGV_ALARM);
                message.setMessageStr(this.forklift_number + "触发防撞，暂停所有AGV");

                gAlarm = 0;
                AGVInitialize.getInitialize().getAGVMessage().setMessage(message);
            }
        }

    }
}
