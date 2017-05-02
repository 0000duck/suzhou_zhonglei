using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AGV
{
    public partial class TaskButton : Button
    {
        private Object ob = null;
        public SingleTask st = null; //每个一个按钮对应一个任务
        public TaskButton()
        {
            InitializeComponent();
            this.BackColor = Color.White;
        }

        delegate void setButtonTextCallBack(string text);
        public void setButtonText(string text)
        {
            if (this.InvokeRequired)
            {
                setButtonTextCallBack stcb = new setButtonTextCallBack(setButtonText);
                this.Invoke(stcb, new object[] { text });
            }
            else
            {
                this.Text = text;
            }
        }

        public void bindValue(Object ob)
        {
            this.ob = ob;
        }

        public Object getBindValue()
        {
            return ob;
        }

        public void setSingleTask(SingleTask st)
        {
            this.st = st;
        }
    }
}
