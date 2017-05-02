using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;

using System.Runtime.InteropServices;

using AGV.util;
namespace AGV
{
    /// <summary>
    /// 用于处理系统消息，并弹出提示框显示
    /// </summary>
    ///         
    public enum AGVMESSAGE_TYPE_T
    {
            AGVMESSAGE_MIN = 0,
            AGVMESSAGE_LOWPOWER,
            AGVMEASAGE_LIFT_UPDOWN, //升降机楼上楼下都有货
            AGVMESSAGE_LIFT_COM,  //升降机端口问题

            AGVMESSAGE_LIFT_BUG,  //升降机 卡货

            AGVMESSAGE_SENDTASK_ERR,
            AGVMESSAGE_SENDPAUSE_ERR,

            AGVMESSAGE_NET_ERR, //网络错误

            AGVMESSAGE_AGV_ALARM, //防撞信号
            AGVMESSAGE_MAX,
    };

    

    public class Message
    {
        private AGVMESSAGE_TYPE_T message_type = AGVMESSAGE_TYPE_T.AGVMESSAGE_MIN;
        private string message_str = String.Empty;

        

        public void setMessage(AGVMESSAGE_TYPE_T type, string str)
        {
            this.message_type = type;
            this.message_str = str;
        }
        
        public void clear()
        {
            this.message_str = String.Empty;
            this.message_type = AGVMESSAGE_TYPE_T.AGVMESSAGE_MIN;
        }

        public AGVMESSAGE_TYPE_T getMessageType()
        {
            return this.message_type;
        }

        public string getMessageStr()
        {
            return this.message_str;
        }

        public void setMessageType(AGVMESSAGE_TYPE_T type)
        {
            this.message_type = type;
        }

        public void setMessageStr(string str)
        {
            this.message_str = str;
        }
    }

    public class AGVMessage
    {
        #region 导入库

        #region 导入设备状态查询接口 JG_GetUSBAlarmLamp
        /// <summary>
        /// 功能：获取设备当前实时状态
        /// 说明：如果调用本命令时，设备未打开，dll内部自动打开设备，然后执行本命令
        /// 说明：如果当前设备在自检过程中(上电时发生)，该命令无效，返回值为0
        /// </summary>
        /// <param name="ulDVID">IN，保留参数，代入0</param>
        /// <param name="bRed">OUT,当前红灯是否亮 1亮，0不亮</param>
        /// <param name="byRedFre">OUT,当前红灯闪烁频率，值范围0-15，具体定义参见产品手册</param>
        /// <param name="bGreen">OUT,当前绿灯是否亮 1亮，0不亮</param>
        /// <param name="byGreenFre">OUT,当前绿灯闪烁频率，值范围0-15，具体定义参见产品手册</param>
        /// <param name="bYellow">OUT,当前黄灯是否亮 1亮，0不亮</param>
        /// <param name="byYellowFre">OUT,当前黄灯闪烁频率，值范围0-15，具体定义参见产品手册</param>
        /// <param name="bBeep">OUT,当前蜂鸣是否响 1响，0不响</param>
        /// <param name="byBeepFre">OUT,当前蜂鸣频率，值范围0-15，具体定义参见产品手册</param>
        /// <returns>1获取成功，0获取失败</returns>
        [DllImport("jg_usbAlarmLamp.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern Int32 JG_GetUSBAlarmLamp(Int32 ulDVID,
                                             ref Int32 bRed, ref Int32 byRedFre,
                                             ref Int32 bGreen, ref Int32 byGreenFre,
                                             ref Int32 bYellow, ref Int32 byYellowFre,
                                             ref Int32 bBeep, ref Int32 byBeepFre);
        #endregion

        #region 导入设备控制接口 JG_SetUSBAlarmLamp
        /// <summary>
        /// 功能：控制设备状态：红黄绿灯和蜂鸣器
        /// 说明：如果调用本命令时，设备未打开，dll内部自动打开设备，然后执行本命令
        /// 说明：如果当前设备在自检过程中(上电时发生)，该命令无效，返回值为0
        /// </summary>
        /// <param name="ulDVID">IN，保留参数，代入0</param>
        /// <param name="bRed">IN,红灯是否亮 1亮，0不亮</param>
        /// <param name="byRedFre">IN,红灯闪烁频率，值范围0-15，具体定义参见产品手册</param>
        /// <param name="bGreen">IN,绿灯是否亮 1亮，0不亮</param>
        /// <param name="byGreenFre">IN,绿灯闪烁频率，值范围0-15，具体定义参见产品手册</param>
        /// <param name="bYellow">IN,黄灯是否亮 1亮，0不亮</param>
        /// <param name="byYellowFre">IN,黄灯闪烁频率，值范围0-15，具体定义参见产品手册</param>
        /// <param name="bBeep">IN,蜂鸣是否响 1响，0不响</param>
        /// <param name="byBeepFre">IN,蜂鸣频率，值范围0-15，具体定义参见产品手册</param>
        /// <returns>1设置成功，0设置失败</returns>
        [DllImport("jg_usbAlarmLamp.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern Int32 JG_SetUSBAlarmLamp(Int32 ulDVID,
                                             Int32 bRed, Int32 byRedFre,
                                             Int32 bGreen, Int32 byGreenFre,
                                             Int32 bYellow, Int32 byYellowFre,
                                             Int32 bBeep, Int32 byBeepFre);
        #endregion

        #region 导入设备关闭接口 JG_CloseUSBAlarmLamp
        /// <summary>
        /// 功能：关闭设备
        /// </summary>
        /// <param name="ulDVID">IN,保留参数，代入0</param>
        [DllImport("jg_usbAlarmLamp.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void JG_CloseUSBAlarmLamp(Int32 ulDVID);
        #endregion

        #region 其它未引入，可使用的接口说明
        //以下接口可以使用，本例程没有引用，如果需要，可以自行加入您的代码

        // Int32 JG_IfExist(Int32 ulDVID);
        //    '功能：检查JDUPAL-1设备是否存在
        //    '参数：ulDVID，IN,保留参数，代入0
        //    '返回值：存在返回1，否则返回0
        //    '说明：可以在不打开设备的情况下，调用本命令
        //    '说明：如果当前设备在自检过程中(上电时发生)，该命令无效，返回值为0

        // void JG_SetDVCommunicationPara(Int32 ulDVID,Int32 iDelay_write,Int32 iDelay_read);
        //    '功能：设置设备通信延时等待时间(可以不设，系统默认为500ms)
        //    '参数：ulDVID，IN,保留参数，代入0
        //    '参数：iDelay_write，IN,写延时
        //    '参数：iDelay_read，IN,读延时
        //    '返回值：无
        //    '说明：如果调用本命令前，必须打开设备
        //    '说明：如果当前设备在自检过程中(上电时发生)，该命令无效

        [DllImport("jg_usbAlarmLamp.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern Int32 JG_Clear(Int32 ulDVID);
        //    '功能：关闭所有灯和蜂鸣器，等同于全0参数调用JG_SetUSBAlarmLamp()
        //    '参数：ulDVID，IN,保留参数，代入0
        //    '返回值：1执行成功，0执行失败
        //    '说明：如果调用本命令时，设备未打开，dll内部自动打开设备，然后执行本命令
        //    '说明：如果当前设备在自检过程中(上电时发生)，该命令无效，返回值为0
        #endregion

        #endregion

        private Message message = new Message();
        private Message message_next = new Message(); //缓存一个message，当前可能正有message在处理，处理完正在处理的message，再处理该message

       System.Media.SoundPlayer net_sp = new System.Media.SoundPlayer();

        System.Media.SoundPlayer lowpower_sp = new System.Media.SoundPlayer();
        private bool keepBeep = false;
        private bool isStop = false; //是否停止线程
        

        public AGVMessage()
        {
            net_sp.SoundLocation = System.AppDomain.CurrentDomain.BaseDirectory + "sound\\beep.wav";
            net_sp.Load();


            lowpower_sp.SoundLocation = System.AppDomain.CurrentDomain.BaseDirectory + "sound\\lowpower.wav";
            lowpower_sp.Load();
        }

        private void setKeepBeep(bool beep)
        {
            keepBeep = beep;
        }

        public void PlayBeep()
        {
            //调用  
            while (keepBeep)
            {
                try
                {
                    if (message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_NET_ERR || message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_SENDPAUSE_ERR || message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_AGV_ALARM)
                    {
                        net_sp.Play();
                    }
                    else if (message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_LOWPOWER || message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMEASAGE_LIFT_UPDOWN || message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_LIFT_BUG)
                    {
                        lowpower_sp.Play();
                    }
                }catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
                

                Thread.Sleep(1000);
            }
        }

        private void startKeepBeepThread()
        {
            keepBeep = true;
            Thread thread = new Thread(new ThreadStart(PlayBeep));
            thread.Start();
        }

        /// <summary>
        /// 设置usb报警灯
        /// </summary>
        /// <param name="message_type"></param>
        private void setUsbBeep(AGVMESSAGE_TYPE_T message_type)
        {
            switch (message_type)
            {
                case AGVMESSAGE_TYPE_T.AGVMESSAGE_LOWPOWER:  //提示性的 报黄灯
                    JG_SetUSBAlarmLamp(0, 0, 0, 0, 0, 1, 3, 1, 3);
                    break;
                default:
                    JG_SetUSBAlarmLamp(0, 1, 3, 0, 0, 0, 0, 1, 3);  //需要车停的都报红灯
                    break;
            }
        }

        /// <summary>
        /// 清除USB报警灯
        /// </summary>
        private void clearUsbBeep()
        {
            try
            {
                JG_Clear(0);
            }
            catch (Exception ex)
            {
                DialogResult dr = MessageBox.Show("关闭报警灯异常", "报警灯错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

        }

        private void startBeep(AGVMESSAGE_TYPE_T message_type)
        {
            if (AGVInitialize.useUsbAlarm)
                {
                    setUsbBeep(message_type);  //使用usb报警灯报警
                } else
                {
                    startKeepBeepThread();  //使用电脑声音报警
                }
        }

        private void clearBeep()
        {
            if (AGVInitialize.useUsbAlarm)
            {
                clearUsbBeep();
            } else
            {
                setKeepBeep(false);
            }
            
        }

        public void setMessage(Message message)
        {
            if (this.message.getMessageType() != message.getMessageType())
            {
                this.message_next = message;  //下一个要处理的message type与当前的message不一样
                AGVLog.WriteInfo("AGVMessage set message " + message.getMessageStr(), new StackFrame(true));
            } else
            {
                AGVLog.WriteInfo("AGVMessage set same message " + message.getMessageStr(), new StackFrame(true));
            }
        }

        public Message getMessage()
        {
            return this.message;
        }

        public void setStop(bool stop)
        {
            this.isStop = stop;
        }

        private void handleMessage()
        {
            while(!isStop)
            {
                Thread.Sleep(1000);
                if (message_next.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_MIN)
                {
                    continue;
                }
                message.setMessageStr(message_next.getMessageStr());
                message.setMessageType(message_next.getMessageType());

                message_next.clear();

                startBeep(message.getMessageType());
                
                if (message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_LOWPOWER)
                {
                    AGVInitialize.getInitialize().getMainFrm().setFrmEnable(false);
                    DialogResult dr = MessageBox.Show(message.getMessageStr(), "低电量警告", MessageBoxButtons.OK,  MessageBoxIcon.Warning);
                    if (dr == DialogResult.OK)
                    {
                        AGVInitialize.getInitialize().getMainFrm().setFrmEnable(true);
                        AGVInitialize.getInitialize().getMainFrm().setWindowState(FormWindowState.Normal);
                        clearBeep();
                    }
                } else if (message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMEASAGE_LIFT_UPDOWN)
                {
                    bool ddp = AGVInitialize.getInitialize().getSchedule().getDownDeliverPeriod();
                    if (ddp)
                    {
                        AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_DOWN_WITH_START);  //如果当前处于上货阶段，楼上楼下都有货，需要暂停楼下的车
                    }else
                    {
                        AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_WITH_START); //如果当前处于下货阶段，楼上楼下都有货，需要暂停楼上的车
                    }
                    
                    DialogResult dr = MessageBox.Show(message.getMessageStr(), "升降机提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (dr == DialogResult.OK)
                    {
                        AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN); //解除楼上或楼下的暂停
                        clearBeep();
                    }
                } else if (message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_LIFT_COM)
                {
                    AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START);
                    AGVInitialize.getInitialize().getMainFrm().setFrmEnable(false);
                    DialogResult dr = MessageBox.Show(message.getMessageStr(), "升降机错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (dr == DialogResult.OK)
                    {
                        AGVInitialize.getInitialize().getAGVCom().reStart(); //重新启动升降机 PLC读取线程
                        AGVInitialize.getInitialize().getMainFrm().setFrmEnable(true);
                        AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN); //解除系统暂停
                        clearBeep();
                    }
                }
                else if (message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_LIFT_BUG)
                {
                    AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START);
                    AGVInitialize.getInitialize().getMainFrm().setFrmEnable(false);
                    DialogResult dr = MessageBox.Show(message.getMessageStr(), "升降机错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (dr == DialogResult.OK)
                    {
                        //AGVInitialize.getInitialize().getAGVCom().reStart(); //重新启动升降机 PLC读取线程
                        AGVInitialize.getInitialize().getMainFrm().setFrmEnable(true);
                        AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN); //解除系统暂停
                        clearBeep();
                    }
                }
                else if (message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_NET_ERR)
                {
                    AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START);
                    DialogResult dr = MessageBox.Show(message.getMessageStr(), "网络异常", MessageBoxButtons.OK, MessageBoxIcon.Warning); //网络中断时，系统暂停
                    if (dr == DialogResult.OK)
                    {
                        AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN);
                        clearBeep();
                    }
                } else if (message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_SENDPAUSE_ERR)
                {
                    AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_UP_WITHOUT_START);
                    DialogResult dr = MessageBox.Show(message.getMessageStr(), "网络异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (dr == DialogResult.OK)
                    {
                        AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN); //清除楼上暂停的标志，但是车子暂停不会解除
                        clearBeep();
                    }
                }
                else if (message.getMessageType() == AGVMESSAGE_TYPE_T.AGVMESSAGE_AGV_ALARM)  //检测到防撞信号，暂停所有AGV
                {
                    AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_SYSTEM_WITH_START);
                    DialogResult dr = MessageBox.Show(message.getMessageStr(), "防撞提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (dr == DialogResult.OK)
                    {
                        AGVInitialize.getInitialize().getSchedule().setPause(SHEDULE_PAUSE_TYPE_T.SHEDULE_PAUSE_TYPE_MIN); //清除楼上暂停的标志，但是车子暂停不会解除
                        clearBeep();
                    }
                }
                message.clear();  //清除消息
            }
        }

        public void StartHandleMessage()
        {
            Thread startSheduleThread;
            startSheduleThread = new Thread(new ThreadStart(handleMessage));
            startSheduleThread.IsBackground = true;
            startSheduleThread.Start();
        }
    }
}
