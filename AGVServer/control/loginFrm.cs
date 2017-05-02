using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AGV
{
    public partial class LoginFrm : Form
    {
        private List<User> userList = AGVInitialize.getInitialize().getAGVUtil().getUsers();

        public LoginFrm()
        {
            InitializeComponent();
        }


        private bool checkUser(string userName, string passwd)
        {
            foreach(User user in userList)
            {
                if (user.userPasswd.Equals(passwd) && user.userName.Equals(userName))
                {
                    AGVInitialize.getInitialize().setCurrentUser(user);
                    return true;
                }
            }

            return false;
        }

        private void loginFrm_Load(object sender, EventArgs e)
        {

        }

        private void loginFrm_Closing(object sender, FormClosingEventArgs e)
        {
            this.Hide();
            if (AGVInitialize.getInitialize().getCurrentUser() == null)
            {
                System.Environment.Exit(0);
            }
        }

        private void resetButton_Click(object sender, EventArgs e)
        {
            Button resetButton = (Button)sender;
            this.passwdText.Clear();
            this.userNameText.Clear();
            Console.WriteLine("reset click button name = " + resetButton.Name);
        }

        private void loginButton_Click(object sender, EventArgs e)
        {
            string userName = this.userNameText.Text;
            string userPasswd = this.passwdText.Text;

            Console.WriteLine(" name = " + userName + " userPasswd = " + userPasswd);
            if (checkUser(userName, userPasswd))
            {
                MessageBox.Show("登录成功");
                this.Dispose();
            }
            else
            {
                MessageBox.Show("登录失败，程序退出");
                this.Dispose();
                System.Environment.Exit(0);
            }
        }
    }
}
