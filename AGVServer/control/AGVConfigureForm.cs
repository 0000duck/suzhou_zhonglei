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
    public partial class AGVConfigureForm : Form
    {
        private int disableForkLiftNumber = 1;  //默认选择在1号车
        public AGVConfigureForm()
        {
            InitializeComponent();
        }

        public void disableForklift_CheckedChanged(object sender, EventArgs e)
        {
            RadioButton rb = (RadioButton)sender;
            if (rb.Checked == true)
            {
                Console.WriteLine("rb.name " + rb.Name);
                if (rb.Name.Equals("disableF1"))
                {
                    disableForkLiftNumber = 1;
                } else if (rb.Name.Equals("disableF2"))
                {
                    disableForkLiftNumber = 2;
                }
            }
            
        }

        private void confirm_Click(object sender, EventArgs e)
        {
            DialogResult dr;
            string message = "确认禁用" + disableForkLiftNumber + "号车吗";
            dr = MessageBox.Show(message, "禁用单车确认", MessageBoxButtons.YesNo);

            if (dr == System.Windows.Forms.DialogResult.Yes)
            {
                AGVInitialize.getInitialize().getAGVUtil().disableForklift(disableForkLiftNumber);
                this.Hide();
            }

        }
    }
}
