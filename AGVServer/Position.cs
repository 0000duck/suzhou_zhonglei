using System;
namespace AGV
{
    public class Position  //描述位置
    {
        public int px = 0;  //位置横坐标
        public int py = 0;  //位置纵坐标
        public int area = 1;   //默认在区域1  位置区域   1代表区域1：  x1>x>x2 && y1<y<y3   (正常情况车子不会出现在x1<x<x2 && y2<y<y3的位置，所以这段位置不单独考虑) 2代表区域2：x<x2 || y<y1l
        public int startPx = 0;
        public int startPy = 0;

        public Position()
        {
        }

        public Position(int px, int py, int area)
        {
            this.px = px;
            this.py = py;
        }


        public void setPx(int px)
        {
            this.px = px;
        }

        public void setPy(int py)
        {
            this.py = py;
        }

        public void setArea(int area)
        {
            this.area = area;
        }


        public int calcArea(int px, int py)
        {
            int area = 0;

            if (px > AGVInitialize.BORDER_X_2 && px < AGVInitialize.BORDER_X_1 && py < AGVInitialize.BORDER_Y_1 && py < AGVInitialize.BORDER_Y_3)
                area = 1;
            else if (px < AGVInitialize.BORDER_X_2 && py < AGVInitialize.BORDER_Y_1)
                area = 2;
            return area;
        }

        public void setStartPosition(int px, int py)
        {
            this.startPx = px;
            this.startPy = py;
        }

        public void updateStartPosition()  //用当前的位置，更新起始位置的坐标
        {
            this.startPx = this.px;
            this.startPy = this.py;
        }

    }
}
