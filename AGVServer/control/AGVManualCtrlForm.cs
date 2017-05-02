using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AGV
{
    public partial class AGVManualCtrlForm : Form
    {
        private List<ForkLift> forkLiftList = AGVInitialize.getInitialize().getAGVUtil().getForkLiftList();
        Hashtable pausePanelHash = new Hashtable();

        public AGVManualCtrlForm()
        {
            InitializeComponent();
        }

        public void initAGVMCForm()
        {
            int tmp = 0;
            foreach (ForkLift fl in forkLiftList)
            {
                if (fl.forklift_number == 3)
                {
                    //continue;
                }
                PauseCtrlPanel pcp = new PauseCtrlPanel();
                pcp.initPanel(fl);
                pcp.Location = new Point(40, 30 + tmp * 60);
                pcp.Size = new Size(200, 50);

                this.Controls.Add(pcp);

                pausePanelHash.Add(fl.forklift_number, pcp);
                tmp++;      
            }           
        }

        public void updateAGVMCForm()
        {
            foreach (DictionaryEntry de in pausePanelHash)
            {
                PauseCtrlPanel pcp = (PauseCtrlPanel)de.Value;
                pcp.updatePanel();
            }
        }
    }
}
