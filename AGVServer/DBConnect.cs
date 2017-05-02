using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
//Add MySql Library
using MySql.Data.MySqlClient;

using AGV.util;
namespace AGV
{
    public class DBConnect
    {
        private MySqlConnection connection;
        private string server;
        private string database;
        private string uid;
        private string password;
        private Object lockDB = new Object();
        //Constructor
        public DBConnect()
        {
            Initialize();
        }

        private void Initialize()
        {
            server = "localhost";
            database = "agv";
            uid = "root";
            password = "123456";
            string connectionString;
            connectionString = "server=" + server + ";" + "DATABASE=" + database + ";" + "UID=" + uid + ";" + "PASSWORD=" + password + ";";

            connection = new MySqlConnection(connectionString);
        }

        private bool OpenConnection()
        {
            try
            {
                if (connection.State == System.Data.ConnectionState.Closed)  //如果当前是关闭状态
                    connection.Open();
                return true;
            }
            catch (MySqlException ex)
            {
                //When handling errors, you can your application's response based on the error number.
                //The two most common error numbers when connecting are as follows:
                //0: Cannot connect to server.
                //1045: Invalid user name and/or password.
                switch (ex.Number)
                {
                    case 0:
                        MessageBox.Show("Cannot connect to server.  Contact administrator");
                        break;

                    case 1045:
                        MessageBox.Show("Invalid username/password, please try again");
                        break;
                }
                return false;
            }
        }

        //Close connection
        private bool CloseConnection()
        {
            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                    connection.Close();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
        }

        
        //Delete statement
        public void DeleteWithSql(string sql)
        {
            string query = sql;
            AGVLog.WriteInfo("DeleteWithSql sql = " + sql, new StackFrame(true));
            try
            {
                lock (lockDB)
                {
                    if (this.OpenConnection() == true)
                    {
                        MySqlCommand cmd = new MySqlCommand(query, connection);
                        cmd.ExecuteNonQuery();
                        this.CloseConnection();
                    }
                }
            }catch(Exception)
            {
                Console.WriteLine(" delete sql err : " + sql);
                this.CloseConnection();
            }
            
        }

        public List<ForkLift> SelectForkList()
        {
            string query = "select * from forklift order by number";


            //Create a list to store the result
            List<ForkLift> list = new List<ForkLift>();
            AGVLog.WriteInfo("SelectForkList sql = " + query, new StackFrame(true));
            try
            {
                //Open connection
            lock(lockDB)
            {
                if (this.OpenConnection() == true)
                {
                    //Create Command
                    MySqlCommand cmd = new MySqlCommand(query, connection);
                    //Create a data reader and Execute the command
                    MySqlDataReader dataReader = cmd.ExecuteReader();

                    //Read the data and store them in the list
                    while (dataReader.Read())
                    {
                        ForkLift fl = new ForkLift();
                        fl.id = int.Parse(dataReader["id"] + "");
                        fl.forklift_number = int.Parse(dataReader["number"] + "");
                        fl.ip = dataReader["ip"] + "";
                        fl.port = int.Parse(dataReader["port"] + "");
                        list.Add(fl);
                    }

                    //close Data Reader
                    dataReader.Close();

                    //close Connection
                    this.CloseConnection();

                }
            }
            
            }catch(Exception ex)
            {
                Console.WriteLine(" sql err " + ex.ToString());
                this.CloseConnection();
            }

            return list;
            
        }

        public List<User> SelectUserList()
        {
            string query = "select * from user";

            //Create a list to store the result
            List<User> list = new List<User>();

            try
            {
        
            lock(lockDB)
            {
                if (this.OpenConnection() == true)
                {
                    //Create Command
                    MySqlCommand cmd = new MySqlCommand(query, connection);
                    //Create a data reader and Execute the command
                    MySqlDataReader dataReader = cmd.ExecuteReader();

                    //Read the data and store them in the list
                    while (dataReader.Read())
                    {
                        User user = new User();
                        user.id = int.Parse(dataReader["id"] + "");
                        user.userType = (USER_TYPE_T)int.Parse(dataReader["userType"] + "");
                        user.userPasswd = dataReader["userPasswd"] + "";
                        user.userName = dataReader["userName"] + "";

                        list.Add(user);
                    }

                    //close Data Reader
                    dataReader.Close();

                    //close Connection
                    this.CloseConnection();

                }
            }
            }catch(Exception ex)
            {
                Console.WriteLine("ex " + ex.ToString());
                this.CloseConnection();
            }

            return list;

        }

        public SingleTask SelectSingleTaskByName(string taskName)  //一个taskName只对应一条记录
        {
            string query = "select * from singletask";

            SingleTask st = new SingleTask();

            //Open connection
            try
            {
                lock (lockDB)
                {
                    if (this.OpenConnection() == true)
                    {
                        //Create Command
                        MySqlCommand cmd = new MySqlCommand(query, connection);
                        //Create a data reader and Execute the command
                        MySqlDataReader dataReader = cmd.ExecuteReader();

                        //Read the data and store them in the list
                        while (dataReader.Read())
                        {
                            st.taskID = int.Parse(dataReader["id"] + "");
                            st.taskName = dataReader["taskName"] + "";
                            st.taskUsed = bool.Parse(dataReader["taskUsed"] + "");
                        }

                        //close Data Reader
                        dataReader.Close();

                        //close Connection
                        this.CloseConnection();

                    }
                }
            }catch(Exception ex)
            {
                Console.WriteLine("ex " + ex.ToString());
                this.CloseConnection();
            }

            return st;
            
        }

        /**
         *  只查询使用的任务
         *  主要查询缓冲任务和已完成的任务
         **/
        public List<SingleTask> SelectSingleTaskList()
        {
            string query = "select * from singleTask where taskUsed = 1 order by id";

            //Create a list to store the result
            List<SingleTask> list = new List<SingleTask>();

            try
            {
                lock (lockDB)
                {
                    //Open connection
                    if (this.OpenConnection() == true)
                    {
                        //Create Command
                        MySqlCommand cmd = new MySqlCommand(query, connection);
                        //Create a data reader and Execute the command
                        MySqlDataReader dataReader = cmd.ExecuteReader();

                        //Read the data and store them in the list
                        while (dataReader.Read())
                        {
                            SingleTask st = new SingleTask();
                            st.taskID = int.Parse(dataReader["id"] + "");
                            st.taskName = dataReader["taskName"] + "";
                            st.taskText = dataReader["taskText"] + "";
                            st.taskUsed = Convert.ToBoolean(int.Parse(dataReader["taskUsed"] + ""));
                            st.taskType = (TASKTYPE_T)int.Parse(dataReader["taskType"] + "");
                            list.Add(st);
                        }

                        //close Data Reader
                        dataReader.Close();

                        //close Connection
                        this.CloseConnection();


                    }
                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }

            //return list to be displayed
            return list;
        }

        /// <summary>
        /// 插入任务记录
        /// </summary>
        /// <param name="taskRecordStat"></param>
        /// <param name="st"></param>
        public void InsertTaskRecord(TASKSTAT_T taskRecordStat, SingleTask st)
        {
            string sql = "INSERT INTO `agv`.`taskrecord` (`taskRecordStat`, `singleTask`) VALUES ( " + (int) taskRecordStat + ", " + st.taskID + ");";
            AGVLog.WriteInfo("InsertTaskRecord sql = " + sql, new StackFrame(true));
            try
            {
                lock (lockDB)
                {
                    if (this.OpenConnection() == true)
                    {
                        Console.WriteLine("sql = " + sql);
                        MySqlCommand cmd = new MySqlCommand(sql, connection);
                        cmd.ExecuteNonQuery();
                        this.CloseConnection();
                    }
                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }
                 

        }

        /// <summary>
        /// 插入任务记录
        /// </summary>
        /// <param name="taskRecordStat"></param>
        /// <param name="st"></param>
        public void InsertTaskRecord(TaskRecord tr)
        {
            string sql = "INSERT INTO `agv`.`taskrecord` (`taskRecordStat`, `singleTask`) VALUES ( " + (int)tr.taskRecordStat + ", " + tr.singleTask.taskID + ");";
            AGVLog.WriteInfo("InsertTaskRecord sql = " + sql, new StackFrame(true));

            try
            {
                lock (lockDB)
                {
                    if (this.OpenConnection() == true)
                    {
                        Console.WriteLine("sql = " + sql);
                        MySqlCommand cmd = new MySqlCommand(sql, connection);
                        cmd.ExecuteNonQuery();
                        this.CloseConnection();
                    }
                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }
        }

        /// <summary>
        /// 插入任务记录
        /// </summary>
        /// <param name="taskRecordStat"></param>
        /// <param name="st"></param>
        public void InsertTaskRecordBak(TaskRecord tr)
        {
            float now = DateTime.Now.Hour * 60 * 60 + DateTime.Now.Minute * 60 + DateTime.Now.Second;  //当前时间的秒数
            float excute_min = (now - (tr.updateTime.Hour * 60 * 60 + tr.updateTime.Minute * 60 + tr.updateTime.Second)) / 60;
            if (excute_min > 20 || excute_min < 3)
            {
                excute_min = 6;  //过滤异常数据
            }
            string sql = "INSERT INTO `agv`.`taskrecord_bak` (`taskRecordStat`, `forklift`, `singleTask`, `taskRecordExcuteMinute`) VALUES ( " + (int)tr.taskRecordStat + ", " + (int)tr.forkLift.id + ", " + tr.singleTask.taskID + ", " + excute_min + ");";
            AGVLog.WriteInfo("InsertTaskRecordBak sql = " + sql, new StackFrame(true));
            
            
            try
            {
                lock (lockDB)
                {
                    if (this.OpenConnection() == true)
                    {
                        Console.WriteLine("sql = " + sql);
                        MySqlCommand cmd = new MySqlCommand(sql, connection);
                        cmd.ExecuteNonQuery();

                        this.CloseConnection();
                    }
                }
            } catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }

        }
        /// <summary>
        /// 移除任务记录
        /// </summary>
        /// <param name="taskRecordStat">任务状态</param>
        /// <param name="st">对应的任务ID</param>
        /// <param name="taskPalletType">托盘类型 默认托盘类型为0 表示忽略托盘类型这个条件</param>
        public void RemoveTaskRecord(SingleTask st,  TASKSTAT_T taskRecordStat)
        {
            string sql = "delete from taskrecord where taskRecordStat = " + (int) taskRecordStat + " and singleTask = " + st.taskID;
            AGVLog.WriteInfo("RemoveTaskRecord sql = " + sql, new StackFrame(true));
            try
            {
                lock (lockDB)
                {
                    Console.WriteLine("removeTaskRecor sql = " + sql);
                    if (this.OpenConnection() == true)
                    {
                        Console.WriteLine("sql = " + sql);
                        MySqlCommand cmd = new MySqlCommand(sql, connection);
                        cmd.ExecuteNonQuery();

                        this.CloseConnection();
                    }
                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }
        }

        /// <summary>
        /// 移除任务记录
        /// </summary>
        /// <param name="taskRecordStat">任务状态</param>
        /// <param name="st">对应的任务ID</param>
        /// <param name="taskPalletType">托盘类型 默认托盘类型为0 表示忽略托盘类型这个条件</param>
        public void RemoveTaskRecord(TaskRecord tr)
        {
            string sql = "delete from taskrecord where taskRecordStat = " + (int)tr.taskRecordStat + " and forklift = " + tr.forkLift.id + " and singleTask = " + tr.singleTask.taskID;
            AGVLog.WriteInfo("RemoveTaskRecord sql = " + sql, new StackFrame(true));
            try
            {
                lock (lockDB)
                {
                    if (this.OpenConnection() == true)
                    {
                        Console.WriteLine("sql = " + sql);
                        MySqlCommand cmd = new MySqlCommand(sql, connection);
                        cmd.ExecuteNonQuery();

                        this.CloseConnection();
                    }
                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }
        }

        /// <summary>
        /// 更新任务记录
        /// </summary>
        /// <param name="taskRecordStat">任务状态</param>
        /// <param name="st">对应的任务ID</param>
        public void UpdateTaskRecord(TaskRecord tr)
        {

            string sql;
            if (tr.forkLift == null)
            {
                sql = "update taskrecord set taskRecordStat = " + (int)tr.taskRecordStat + ", forklift = NULL , taskLevel = " + tr.taskLevel + " where taskRecordStat != 4 and singleTask = " + tr.singleTask.taskID;
            }
            else 
            {
                sql = "update taskrecord set taskRecordStat = " + (int)tr.taskRecordStat + ", forklift = " + tr.forkLift.id + " , taskLevel = " + tr.taskLevel + " where taskRecordStat != 4 and singleTask = " + tr.singleTask.taskID; ;
            }

            AGVLog.WriteInfo("UpdateTaskRecord sql = " + sql, new StackFrame(true));
            try
            {
                lock (lockDB)
                {
                    Console.WriteLine("UpdateTaskRecord sql = " + sql);
                    if (this.OpenConnection() == true)
                    {
                        MySqlCommand cmd = new MySqlCommand(sql, connection);
                        cmd.ExecuteNonQuery();

                        this.CloseConnection();
                    }

                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }
        }

        /// <summary>
        /// 更新车子状态
        /// </summary>
        /// <param name="fl"></param>
        public void updateForkLift(ForkLift fl)
        {
            string sql = "update forklift set currentTask = \"" + fl.currentTask + "\", taskStep = " + (int)fl.taskStep + " where id = " + fl.id;

            AGVLog.WriteInfo("updateForkLift sql = " + sql, new StackFrame(true));
            try
            {
                lock (lockDB)
                {
                    if (this.OpenConnection() == true)
                    {
                        Console.WriteLine("sql = " + sql);
                        MySqlCommand cmd = new MySqlCommand(sql, connection);
                        cmd.ExecuteNonQuery();

                        this.CloseConnection();
                    }

                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }
        }

        /*
        /// <summary>
        /// 插入任务记录
        /// </summary>
        /// <param name="taskRecordStat"></param>
        /// <param name="st"></param>
        public List<TaskRecord> SelectTaskRecordBySingleTaskAndStat(int taskRecordStat, SingleTask st)
        {
            string sql = "select * from taskrecord where taskRecordStat = " + taskRecordStat + " and singleTask = " + st.taskID;
            List<TaskRecord> taskRecordList = new List<TaskRecord>();

            if (this.OpenConnection() == true)
            {
                Console.WriteLine("sql = " + sql);
                MySqlCommand cmd = new MySqlCommand(sql, connection);
                MySqlDataReader dataReader = cmd.ExecuteReader();
                while (dataReader.Read())
                {
                    TaskRecord taskRecord = new TaskRecord();
                    taskRecord.taskRecordID = int.Parse(dataReader["taskRecordID"] + "");
                    taskRecord.taskRecordStat = (TASKSTAT_T) int.Parse(dataReader["taskRecordStat"] + "");
                    try
                    {
                        taskRecord.forkLift = AGVInitialize.getInitialize().getForkLiftByID(int.Parse(dataReader["forklift"] + ""));
                    }
                    catch (FormatException fx)
                    {
                        Console.WriteLine("message = " + fx.Message);
                    }
                    Console.WriteLine("----------------------------");
                    //taskRecord.singleTask = AGVInitialize.getInitialize().getSingleTaskByID(int.Parse(dataReader["singleTask"] + ""));
                    taskRecordList.Add(taskRecord);
                }

                dataReader.Close();
            }

            this.CloseConnection();
            return taskRecordList;
        }
         * */

        /// <summary>
        /// 插入任务记录
        /// </summary>
        /// <param name="taskRecordStat"></param>
        /// <param name="st"></param>
        public List<TaskRecord> SelectTaskRecordBySql(string sql)
        {
            List<TaskRecord> taskRecordList = new List<TaskRecord>();
            try
            {
                lock (lockDB)
                {
                    if (this.OpenConnection() == true)
                    {
                        //Console.WriteLine("sql = " + sql);
                        MySqlCommand cmd = new MySqlCommand(sql, connection);
                        MySqlDataReader dataReader = cmd.ExecuteReader();
                        while (dataReader.Read())
                        {
                            TaskRecord taskRecord = new TaskRecord();
                            taskRecord.taskRecordID = int.Parse(dataReader["taskRecordID"] + "");

                            taskRecord.taskRecordStat = (TASKSTAT_T)int.Parse(dataReader["taskRecordStat"] + "");
                            taskRecord.taskLevel = int.Parse(dataReader["taskLevel"] + "");
                            try
                            {
                                if (!String.IsNullOrEmpty(dataReader["forklift"].ToString()))
                                {
                                    taskRecord.forkLift = AGVInitialize.getInitialize().getForkLiftByID(int.Parse(dataReader["forklift"] + ""));
                                }
                            }
                            catch (FormatException fx)
                            {
                                Console.WriteLine("message = " + fx.Message);
                            }
                            taskRecord.singleTask = AGVInitialize.getInitialize().getSingleTaskByID(int.Parse(dataReader["singleTask"] + ""));
                            taskRecord.taskRecordName = taskRecord.singleTask.taskName;
                            taskRecord.updateTime = (DateTime)(dataReader["taskRecordUpdateTime"]);

                            taskRecordList.Add(taskRecord);
                        }

                        dataReader.Close();
                        this.CloseConnection();
                    }

                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }
            return taskRecordList;
        }

        /*
        /// <summary>
        /// 插入任务记录
        /// </summary>
        /// <param name="taskRecordStat"></param>
        /// <param name="st"></param>
        public List<TaskRecord> SelectTaskRecordBySingleTaskNameAndStat(string singleTaskName, int taskRecordStat)
        {
            string sql = "select * from taskrecord, singletask where taskRecordStat = " + taskRecordStat + " and singleTask = singletask.id and singletask.taskName = '" + singleTaskName + "'";
            List<TaskRecord> taskRecordList = new List<TaskRecord>();

            if (this.OpenConnection() == true)
            {
                Console.WriteLine("sql = " + sql);
                MySqlCommand cmd = new MySqlCommand(sql, connection);
                MySqlDataReader dataReader = cmd.ExecuteReader();
                while (dataReader.Read())
                {
                    TaskRecord taskRecord = new TaskRecord();
                    taskRecord.taskRecordID = int.Parse(dataReader["taskRecordID"] + "");
                    taskRecord.taskRecordStat = (TASKSTAT_T)int.Parse(dataReader["taskRecordStat"] + "");

                    if (dataReader["forklift"] != null)
                    {
                        try
                        {
                            taskRecord.forkLift = AGVInitialize.getInitialize().getForkLiftByID(int.Parse(dataReader["forklift"] + ""));
                        }catch(FormatException fx)
                        {
                            Console.WriteLine("message = " + fx.Message);
                        }
                    }
                    taskRecord.taskRecordName = taskRecord.singleTask.taskName;
                    taskRecordList.Add(taskRecord);
                }

                dataReader.Close();

            }

            Console.WriteLine("SelectTaskRecordBySingleTaskNameAndStat close");
            this.CloseConnection();
            return taskRecordList;
        }
        */
        //Select statement
        public List<string>[] SelectWithSql(string sql)
        {
            string query = sql;

            //Create a list to store the result
            List<string>[] list = new List<string>[3];
            list[0] = new List<string>();
            list[1] = new List<string>();
            list[2] = new List<string>();

            try
            {
                lock (lockDB)
                {
                    //Open connection
                    if (this.OpenConnection() == true)
                    {
                        //Create Command
                        MySqlCommand cmd = new MySqlCommand(query, connection);
                        //Create a data reader and Execute the command
                        MySqlDataReader dataReader = cmd.ExecuteReader();

                        //Read the data and store them in the list
                        while (dataReader.Read())
                        {
                            list[0].Add(dataReader["id"] + "");
                        }

                        //close Data Reader
                        dataReader.Close();

                        //close Connection
                        this.CloseConnection();

                    }
                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }

            return list;
        }

        //Count statement
        public int Count(string conditionSql)
        {
            string query = "SELECT Count(*) from " + conditionSql;
            int Count = -1;

            try
            {
                lock (lockDB)
                {
                    //Open Connection
                    if (this.OpenConnection() == true)
                    {
                        //Create Mysql Command
                        MySqlCommand cmd = new MySqlCommand(query, connection);

                        //ExecuteScalar will return one value
                        Count = int.Parse(cmd.ExecuteScalar() + "");

                        //close Connection
                        this.CloseConnection();
                    }
                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }

            return Count;
        }

        //Count statement
        public int selectMaxBySql(string sql)
        {
            Console.WriteLine(" max sql " + sql);
            int Count = -1;

            try
            {
                //Open Connection
            lock(lockDB)
            {
                if (this.OpenConnection() == true)
                {
                    //Create Mysql Command
                    MySqlCommand cmd = new MySqlCommand(sql, connection);

                    //ExecuteScalar will return one value
                    Count = int.Parse(cmd.ExecuteScalar() + "");

                    //close Connection
                    this.CloseConnection();


                }
                
            }
            } catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                this.CloseConnection();
            }

            return Count;
        }

        //Backup
        public void Backup()
        {
            try
            {
                DateTime Time = DateTime.Now;
                int year = Time.Year;
                int month = Time.Month;
                int day = Time.Day;
                int hour = Time.Hour;
                int minute = Time.Minute;
                int second = Time.Second;
                int millisecond = Time.Millisecond;

                //Save file to C:\ with the current date as a filename
                string path;
                path = "C:\\" + year + "-" + month + "-" + day + "-" + hour + "-" + minute + "-" + second + "-" + millisecond + ".sql";
                StreamWriter file = new StreamWriter(path);

                
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "mysqldump";
                psi.RedirectStandardInput = false;
                psi.RedirectStandardOutput = true;
                psi.Arguments = string.Format(@"-u{0} -p{1} -h{2} {3}", uid, password, server, database);
                psi.UseShellExecute = false;

                Process process = Process.Start(psi);

                string output;
                output = process.StandardOutput.ReadToEnd();
                file.WriteLine(output);
                process.WaitForExit();
                file.Close();
                process.Close();
            }
            catch (IOException ex)
            {
                MessageBox.Show("Error , unable to backup!");
            }
        }

        //Restore
        public void Restore()
        {
            try
            {
                //Read file from C:\
                string path;
                path = "C:\\MySqlBackup.sql";
                StreamReader file = new StreamReader(path);
                string input = file.ReadToEnd();
                file.Close();


                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "mysql";
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = false;
                psi.Arguments = string.Format(@"-u{0} -p{1} -h{2} {3}", uid, password, server, database);
                psi.UseShellExecute = false;

                
                Process process = Process.Start(psi);
                process.StandardInput.WriteLine(input);
                process.StandardInput.Close();
                process.WaitForExit();
                process.Close();
            }
            catch (IOException ex)
            {
                MessageBox.Show("Error , unable to Restore!");
            }
        }
    }
}
