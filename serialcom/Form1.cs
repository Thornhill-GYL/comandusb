using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Ivi.Visa;
using System.Diagnostics;
using NationalInstruments.Visa;
using VISAInstrument.Extension;
using VISAInstrument.Ulitity;
using VISAInstrument.Port;

namespace serialcom
{



    public partial class Form1 : Form
    {
        class readparater//这是读取串口接受数据的类
        {
            public int temp_value;

            public string modeget = "";
            public int first_count;
            public int count = 0;
            public int mode_set = 0;
            public byte[] numberget;
            public readparater(int start = 4)
            {
                temp_value = 0;

                first_count = start;
                count = 0;
                mode_set = 0;
                modeget = "";
                numberget = new byte[4];
            }
            public void resetnum(int start)
            {
                count = 0;
                first_count = start;

            }

        }


        Queue<string> myq = new Queue<string>();
        Queue<string> myresult = new Queue<string>();
        const int Mode_control = 0;
        const int Mode_set = -1;
        const int Mode_read = 1;
        int static_dynamic = 0;
        int send_confirm = 0;
        int receive_over_sta = 0;
        int receive_over_dyn = 0;
        int over = 0;
        int deal_over = 0;
        PortOperatorBase portOperatorBase;//usb串口读取
       // string content;//发送scpi命令
        //serial通用变量
        private SerialPort comm = new SerialPort();
        private StringBuilder builder = new StringBuilder();//避免在事件处理方法中反复的创建，定义到外面。  
        private bool Listening = false;//是否没有执行完invoke相关操作
        private bool Closing_new = false;//是否正在关闭串口，执行Application.DoEvents，并阻止再次invoke
        private long received_count = 0;//接收计数  
        private long send_count = 0;//发送计数 

        //serial接收数据相关
        byte[] print_data = new byte[257];
        int[] tmp = new int[50];
        public Form1()
        {
            InitializeComponent();

        }
        //USB读取
        class PortUltility
        {
            private static string ToStringFromPortType(PortType portType)
            {
                switch (portType)
                {
                    case PortType.USB: return "USB";
                    case PortType.GPIB: return "GPIB";
                    case PortType.LAN: return "TCPIP";
                    case PortType.None: return "";
                    case PortType.RS232:
                    default: return "ASRL";
                }
            }

            public static string[] FindAddresses(PortType portType)
            {
                IEnumerable<string> result = new List<string>();
                try
                {
                    result = GlobalResourceManager.Find($"{ToStringFromPortType(portType)}?*INSTR");
                }
                catch (Exception ex)
                {
                    if (!(ex is NativeVisaException))
                    {
                        if (ex.InnerException != null) throw ex.InnerException;
                        else throw ex;
                    }
                }

                return result.ToArray().Where(n => !n.Contains("//")).ToArray();
            }


            public static string[] FindAddresses()
            {
                return FindAddresses(PortType.None);
            }
        }
        private void InvokeToForm(Action action)
        {
            try
            {
                this.Invoke(action);
            }
            catch { }
        }
        private void Form1_Load(object sender, EventArgs e)//这是刷新串口接受页面的函数
        {
            
            string[] content1 = PortUltility.FindAddresses(PortType.USB);
            InvokeToForm(() => cbousb.ShowAndDisplay(content1));//usb读取信息

            this.DoubleBuffered = true;//双缓冲
                                       // this.WindowState = FormWindowState.Maximized;//最大化
            baud.SelectedItem = baud.Items[1];//波特率初始化
            Databox.SelectedItem = Databox.Items[0];
            stopbox.SelectedItem = stopbox.Items[0];
            verifybox.SelectedItem = verifybox.Items[0];
            //显示串口
            string[] ports = SerialPort.GetPortNames();
            Array.Sort(ports);
            comport.Items.AddRange(ports);
            comport.SelectedIndex = comport.Items.Count > 0 ? 0 : -1;
            //初始化SerialPort对象  
            comm.NewLine = "/r/n";
            comm.RtsEnable = false;//reset功能
            comport.Enabled = true;
            baud.Enabled = true;

            comm.DataReceived += comm_DataReceived;//添加事件注册(这里的comm_DataReceived是接受函数)

        }
        //将这些设为全局变量
        int i_flag = 0;//用于静态的标志位
        int j_flag = 0;//用于动态的标志位
        int hex_Flag = 0;
        int look = 0;
        int set_value = 0;//用于区分收到的信息的种类

        int[] info_oprg = new int[10];
        int[] info_chrg_17 = new int[10];
        int[] info_chrg_18 = new int[10];
        string[] op_dec = new string[7];
        string[] ch_dec = new string[10];
        readparater elec_static = new readparater();//实例化一个类来实现读取
        readparater ele_dynamic_A = new readparater();
        readparater time_dynamic_A = new readparater(8);
        readparater ele_dynamic_B = new readparater(10);
        readparater time_dynamic_B = new readparater(14);
        readparater info_V = new readparater();
        readparater info_A = new readparater(8);
        readparater info_W = new readparater(12);
        void comm_DataReceived(object sender, SerialDataReceivedEventArgs e)//接受串口信息函数
        {


            if (Closing_new == true)
                return;
            try
            {
                Listening = true;////设置标记，说明我已经开始处理数据，   一会儿要使用系统UI的。
                int n = comm.BytesToRead;//先记录下来，避免某种原因，人为的原因，操作几次之间时间长，缓存不一致   
                byte[] Received_bytes = new byte[n];//声明一个临时数组存储当前来的串口数据
                byte[] Received_bytes_dynamic = new byte[n];//声明一个临时数组存储当前来的串口数据

                //if (static_dynamic == 1 && send_confirm == 1 && (j_flag >= 26 || j_flag == 0))
                    comm.Read(Received_bytes, 0, n);//读取缓冲数据 
                //else if (static_dynamic == 0 && send_confirm == -1)
                //    comm.Read(Received_bytes_dynamic, 0, n);//读取缓冲数据 

                received_count += n;//增加接收计数  
                builder.Clear();//清除字符串构造器的内容  
                string hexstring = "";
                string moderead = "";
                byte[] kankan = new byte[4];//
                string dynamic_mode = "";
                string set_word = "";
                int test_mode = 0;
                int[] info_oprg1 = new int[10];
                int check_num = 0;
                this.Invoke((EventHandler)(delegate//因为要访问ui资源，所以需要使用invoke方式同步ui。  
                {
                    //string str = a[0].ToString();
                    //builder.Append(str);

                    if (deccheck.Checked)//串口接受
                    {
                        //if (static_dynamic == 1 && send_confirm == 1 )
                        //{
                            foreach (byte b in Received_bytes)
                            {
                                if (i_flag >= 26)//由于是一直在接受，所以一帧的长度为界限清空
                                {
                                // i_flag = 0;
                                over = 1;
                                if (static_dynamic == 1 && send_confirm == 1)
                                    receive_over_sta = 1;
                                else if (static_dynamic == 0 && send_confirm == -1)
                                    receive_over_dyn = 1;
                                }
                                i_flag++;
                                if (i_flag == 3)
                                {
                                    set_word = b.ToString("X2");
                                    set_value = Convert.ToInt32(set_word, 16);
                                }

                                if (set_value == 18)
                                {
                                    recevie_verify(b, i_flag);
                                }
                                //静态电类值读取
                                if (set_value == 35 || set_value == 37 || set_value == 39 || set_value == 43 || set_value == 45 || set_value == 47 || set_value == 49 || set_value == 79)
                                {

                                    check_num = i_flag;
                                    if (i_flag == elec_static.first_count)
                                    {
                                        elec_static.first_count++;
                                        if (elec_static.count < 4)
                                        {
                                            elec_static.numberget[elec_static.count] = b;

                                            elec_static.count++;
                                        }
                                    }
                                    //在一帧结束时，跳出信息
                                    if (i_flag == 25)
                                    {

                                        elec_static.count = 0;
                                        elec_static.first_count = 4;
                                        kankan = elec_static.numberget;
                                        elec_static.temp_value = BitConverter.ToInt32(elec_static.numberget, 0);
                                        switch (set_value)
                                        {
                                            case 35:
                                                MessageBox.Show("最大输入电压:" + elec_static.temp_value.ToString() + "(1mV)");
                                                break;
                                            case 37:
                                                MessageBox.Show("最大输入电流:" + elec_static.temp_value.ToString() + "(0.1mA)");
                                                break;
                                            case 39:
                                                MessageBox.Show("最大输入功率:" + elec_static.temp_value.ToString() + "(1mW)");
                                                break;
                                            case 43:
                                                MessageBox.Show("定电流值:" + elec_static.temp_value.ToString() + "(0.1mA)");
                                                break;
                                            case 45:
                                                MessageBox.Show("定电压值:" + elec_static.temp_value.ToString() + "(1mV)");
                                                break;
                                            case 47:
                                                MessageBox.Show("定功率值:" + elec_static.temp_value.ToString() + "(1mW)");
                                                break;
                                            case 49:
                                                MessageBox.Show("定电阻值:" + elec_static.temp_value.ToString() + "(1mR)");
                                                break;
                                            case 79:
                                                MessageBox.Show("在定电压模式下的最小电压值:" + elec_static.temp_value.ToString() + "(1mV)");
                                                break;

                                        }

                                        //hexstring = "最大输入电压:" + temp_value.ToString() + "mV";
                                    }

                                }
                                //模式读取
                                if (set_value == 41 || set_value == 87)
                                {
                                    if (i_flag == 4)
                                    {
                                        moderead = b.ToString("X2");
                                        test_mode = Convert.ToInt32(moderead, 16);
                                        switch (set_value)
                                        {
                                            case 41:
                                                switch (test_mode)
                                                {
                                                    case 0:
                                                        MessageBox.Show("负载模式为：CC");
                                                        break;
                                                    case 1:
                                                        MessageBox.Show("负载模式为：CV");
                                                        break;
                                                    case 2:
                                                        MessageBox.Show("负载模式为：CW");
                                                        break;
                                                    case 3:
                                                        MessageBox.Show("负载模式为：CR");
                                                        break;

                                                }
                                                break;
                                            case 87:
                                                switch (test_mode)
                                                {
                                                    case 0:
                                                        MessageBox.Show("远程测量模式关闭");
                                                        break;
                                                    case 1:
                                                        MessageBox.Show("远程测量模式打开");
                                                        break;
                                                }

                                                break;
                                        }
                                    }


                                }
                                //动态参数读取
                                if (set_value == 51 || set_value == 53 || set_value == 55 || set_value == 57)
                                {
                                    if (i_flag == ele_dynamic_A.first_count)
                                    {
                                        if (ele_dynamic_A.first_count < 8)
                                            ele_dynamic_A.first_count++;

                                        if (ele_dynamic_A.count < 4)
                                        {
                                            ele_dynamic_A.numberget[ele_dynamic_A.count] = b;
                                            ele_dynamic_A.count++;

                                        }
                                    }
                                    if (i_flag == time_dynamic_A.first_count)
                                    {
                                        if (time_dynamic_A.first_count < 10)
                                            time_dynamic_A.first_count++;
                                        if (time_dynamic_A.count < 2)
                                        {
                                            time_dynamic_A.numberget[2] = 0;
                                            time_dynamic_A.numberget[3] = 0;
                                            time_dynamic_A.numberget[time_dynamic_A.count] = b;
                                            time_dynamic_A.count++;

                                        }
                                    }
                                    if (i_flag == ele_dynamic_B.first_count)
                                    {
                                        if (ele_dynamic_B.first_count < 14)
                                            ele_dynamic_B.first_count++;
                                        if (ele_dynamic_B.count < 4)
                                        {
                                            ele_dynamic_B.numberget[ele_dynamic_B.count] = b;
                                            ele_dynamic_B.count++;

                                        }
                                    }
                                    if (i_flag == time_dynamic_B.first_count)
                                    {
                                        if (time_dynamic_B.first_count < 16)
                                            time_dynamic_B.first_count++;
                                        if (time_dynamic_B.count < 2)
                                        {
                                            time_dynamic_B.numberget[2] = 0;
                                            time_dynamic_B.numberget[3] = 0;
                                            time_dynamic_B.numberget[time_dynamic_B.count] = b;
                                            time_dynamic_B.count++;

                                        }
                                    }
                                    if (i_flag == 16)//模式读取
                                    {
                                        ele_dynamic_B.modeget = b.ToString("X2");
                                        ele_dynamic_B.mode_set = Convert.ToInt32(ele_dynamic_B.modeget, 16);
                                    }

                                    if (i_flag == 25)
                                    {
                                        ele_dynamic_A.resetnum(4);
                                        time_dynamic_A.resetnum(8);
                                        ele_dynamic_B.resetnum(10);
                                        time_dynamic_B.resetnum(14);
                                        ele_dynamic_A.temp_value = BitConverter.ToInt32(ele_dynamic_A.numberget, 0);
                                        time_dynamic_A.temp_value = BitConverter.ToInt32(time_dynamic_A.numberget, 0);
                                        ele_dynamic_B.temp_value = BitConverter.ToInt32(ele_dynamic_B.numberget, 0);
                                        time_dynamic_B.temp_value = BitConverter.ToInt32(time_dynamic_B.numberget, 0);
                                        if (ele_dynamic_B.mode_set == 0)
                                        {
                                            dynamic_mode = "CONTINUES";
                                        }
                                        else if (ele_dynamic_B.mode_set == 1)
                                        {
                                            dynamic_mode = "PULSE";
                                        }
                                        else if (ele_dynamic_B.mode_set == 2)
                                        {
                                            dynamic_mode = "TOGGLED";
                                        }
                                        switch (set_value)
                                        {
                                            case 51:
                                                MessageBox.Show("电流A设定值：" + ele_dynamic_A.temp_value.ToString() + "(0.1mA)\n" +
                                                                "电流A时间值：" + time_dynamic_A.temp_value.ToString() + "(0.1MS)\n" +
                                                                 "电流B设定值：" + ele_dynamic_B.temp_value.ToString() + "(0.1mA)\n" +
                                                                 "电流B时间值：" + time_dynamic_B.temp_value.ToString() + "(0.1MS)\n" +
                                                                 "操作模式：" + dynamic_mode);
                                                break;
                                            case 53:
                                                MessageBox.Show("电压A设定值：" + ele_dynamic_A.temp_value.ToString() + "(1mV)\n" +
                                                               "电压A时间值：" + time_dynamic_A.temp_value.ToString() + "(0.1MS)\n" +
                                                                "电压B设定值：" + ele_dynamic_B.temp_value.ToString() + "(1mV)\n" +
                                                                "电压B时间值：" + time_dynamic_B.temp_value.ToString() + "(0.1MS)\n" +
                                                                "操作模式：" + dynamic_mode);
                                                break;
                                            case 55:
                                                MessageBox.Show("功率A设定值：" + ele_dynamic_A.temp_value.ToString() + "(1mW)\n" +
                                                               "功率A时间值：" + time_dynamic_A.temp_value.ToString() + "(0.1MS)\n" +
                                                                "功率B设定值：" + ele_dynamic_B.temp_value.ToString() + "(1mW)\n" +
                                                                "功率B时间值：" + time_dynamic_B.temp_value.ToString() + "(0.1MS)\n" +
                                                                "操作模式：" + dynamic_mode);
                                                break;
                                            case 57:
                                                MessageBox.Show("电阻A设定值：" + ele_dynamic_A.temp_value.ToString() + "(1mR)\n" +
                                                               "电阻A时间值：" + time_dynamic_A.temp_value.ToString() + "(0.1MS)\n" +
                                                                "电阻B设定值：" + ele_dynamic_B.temp_value.ToString() + "(1mR)\n" +
                                                                "电阻B时间值：" + time_dynamic_B.temp_value.ToString() + "(0.1MS)\n" +
                                                                "操作模式：" + dynamic_mode);
                                                break;

                                        }




                                    }
                                }
                                //负载相关输入信息
                                if (set_value == 95)
                                {
                                    if (i_flag == info_V.first_count)
                                    {
                                        if (info_V.first_count < 8)
                                            info_V.first_count++;
                                        if (info_V.count < 4)
                                        {
                                            info_V.numberget[info_V.count] = b;
                                            info_V.count++;
                                        }
                                    }
                                    if (i_flag == info_A.first_count)
                                    {
                                        if (info_A.first_count < 12)
                                            info_A.first_count++;

                                        if (info_A.count < 4)
                                        {
                                            info_A.numberget[info_A.count] = b;
                                            info_A.count++;

                                        }
                                    }


                                    if (i_flag == info_W.first_count)
                                    {
                                        if (info_W.first_count < 16)
                                            info_W.first_count++;
                                        if (info_W.count < 4)
                                        {
                                            info_W.numberget[info_W.count] = b;
                                            info_W.count++;
                                        }
                                    }
                                    if (i_flag == 16)
                                    {
                                        int i = 0;
                                        int j = 0;

                                        for (i = 0, j = 1; i < 8; i++)
                                        {
                                            if (j <= 128)
                                            {
                                                info_oprg[i] = (b & j) == j ? 1 : 0;
                                            }
                                            j *= 2;

                                        }
                                        info_oprg1 = info_oprg;
                                    }
                                    if (i_flag == 17)
                                    {
                                        int i = 0;
                                        int j = 0;

                                        for (i = 0, j = 1; i < 8; i++)
                                        {
                                            if (j <= 128)
                                            {
                                                info_chrg_17[i] = (b & j) == j ? 1 : 0;
                                            }
                                            j *= 2;

                                        }
                                    }
                                    if (i_flag == 18)
                                    {
                                        int i = 0;
                                        int j = 0;

                                        for (i = 0, j = 1; i < 8; i++)
                                        {
                                            if (j <= 128)
                                            {
                                                info_chrg_18[i] = (b & j) == j ? 1 : 0;
                                            }
                                            j *= 2;

                                        }
                                    }
                                    if (i_flag == 25)
                                    {
                                        info_V.resetnum(4);
                                        info_A.resetnum(8);
                                        info_W.resetnum(12);
                                        info_V.temp_value = BitConverter.ToInt32(info_V.numberget, 0);
                                        info_A.temp_value = BitConverter.ToInt32(info_A.numberget, 0);
                                        info_W.temp_value = BitConverter.ToInt32(info_W.numberget, 0);
                                        op_dec = info_opstr(info_oprg);
                                        ch_dec = info_chstr(info_chrg_17, info_chrg_18);
                                        if (static_dynamic == 1&&send_confirm==1&& over == 1)
                                        {
                                            deal_over = 1;
                                            MessageBox.Show("输入电压：" + info_V.temp_value.ToString() + "(1mV)\n" +
                                                            "输入电流：" + info_A.temp_value.ToString() + "(0.1mA)\n" +
                                                            "输入功率：" + info_W.temp_value.ToString() + "(1mW)\n" +
                                                            "操作状态寄存器：" + op_dec[0] + "   " + "查询状态寄存器：" + ch_dec[0] + "\n" +
                                                            "操作状态寄存器：" + op_dec[1] + "   " + "查询状态寄存器：" + ch_dec[1] + "\n" +
                                                            "操作状态寄存器：" + op_dec[2] + "    " + "查询状态寄存器：" + ch_dec[2] + "\n" +
                                                            "操作状态寄存器：" + op_dec[3] + "   " + "查询状态寄存器：" + ch_dec[3] + "\n" +
                                                            "操作状态寄存器：" + op_dec[4] + "   " + "查询状态寄存器：" + ch_dec[4] + "\n" +
                                                            "操作状态寄存器：" + op_dec[5] + "   " + "查询状态寄存器：" + ch_dec[5] + "\n" +
                                                            "操作状态寄存器：" + op_dec[6] + "   " + "查询状态寄存器：" + ch_dec[6] + "\n" +
                                                              "                                    " + "查询状态寄存器：" + ch_dec[7] + "\n" +
                                                                "                                    " + "查询状态寄存器：" + ch_dec[8] + "\n" +
                                                                 "                                   " + "查询状态寄存器：" + ch_dec[9]);
                                        }
                                        else if (static_dynamic == 0/*&&send_confirm==-1*//*&&receive_over_sta==1*/)
                                        {
                                             deal_over = 1;
                                        over = 1;
                                            v_info.Text = info_V.temp_value.ToString() + "(1mV)";
                                            I_info.Text = info_A.temp_value.ToString() + "(0.1mA)";
                                            W_info.Text = info_W.temp_value.ToString() + "(1mW)";
                                            cal_info.Text = info_oprg[0].ToString();
                                            wtg_info.Text = info_oprg[1].ToString();
                                            rem_info.Text = info_oprg[2].ToString();
                                            out_info.Text = info_oprg[3].ToString();
                                            local_info.Text = info_oprg[4].ToString();
                                            sense_info.Text = info_oprg[5].ToString();
                                            lot_info.Text = info_oprg[6].ToString();

                                            //查询状态寄存器
                                            rv_info.Text = info_chrg_17[0].ToString();
                                            ov_info.Text = info_chrg_17[1].ToString();
                                            oc_info.Text = info_chrg_17[2].ToString();
                                            op_info.Text = info_chrg_17[3].ToString();
                                            oh_info.Text = info_chrg_17[4].ToString();
                                            sv_info.Text = info_chrg_17[5].ToString();
                                            cc_info.Text = info_chrg_17[6].ToString();
                                            cv_info.Text = info_chrg_17[7].ToString();
                                            cp_info.Text = info_chrg_18[0].ToString();
                                            cr_info.Text = info_chrg_18[1].ToString();
                                        }


                                    }



                                }

                                hexstring += b.ToString("X2");







                            }
                        
                    }
                    else if (hexshow.Checked)//判断是否是显示为16进制  
                    {
                        foreach (byte b in Received_bytes) //依次的拼接出16进制字符串  
                        {
                            if (hex_Flag >= 26)
                            {
                                hex_Flag = 0;
                            }
                            hex_Flag++;
                            builder.Append(b.ToString("X2") + " ");
                        }

                    }
                    else
                    {
                        if (asciishow.Checked == true)
                        {
                            string str = Encoding.ASCII.GetString(Received_bytes);//蓝牙AT时'\r'要剔除
                            builder.Append(str.Replace("\r", ""));//直接按ASCII规则转换成字符串
                        }
                        else
                            builder.Append(Encoding.GetEncoding("GB2312").GetString(Received_bytes));//已经可以支持中文
                    }
                        if (static_dynamic == 1)
                            this.receivedbox.AppendText(builder.ToString());//追加的形式添加到文本框末端，并滚动到最后。  
                        else if (static_dynamic == 0)
                            this.infobox.AppendText(builder.ToString());
                        label4.Text = "已接收:" + received_count.ToString();//修改接收计数  
                    if (static_dynamic == 1 && i_flag >= 26/*&&receive_over_sta==1*/)
                        static_dynamic = 0;

                    // static_dynamic = 0;

                }));


              
            }

            finally
            {
                Listening = false;//我用完了，ui可以关闭串口了。
            }

        }
        public string[] info_opstr(int[] num)//串口接收到实时信息
        {
            string[] numtoinfo = new string[7];
            numtoinfo[0] = "CAL=" + num[0].ToString();//因为是小端模式的，所以需要反序。
            numtoinfo[1] = "WTG=" + num[1].ToString();
            numtoinfo[2] = "REM=" + num[2].ToString();
            numtoinfo[3] = "OUT=" + num[3].ToString();
            numtoinfo[4] = "LOCAL=" + num[4].ToString();
            numtoinfo[5] = "SENSE=" + num[5].ToString();
            numtoinfo[6] = "LOT=" + num[6].ToString();
            return numtoinfo;
        }
        public string[] info_chstr(int[] num1, int[] num2)
        {
            string[] chestr = new string[10];
            chestr[0] = "RV=" + num1[0].ToString();
            chestr[1] = "OV=" + num1[1].ToString();
            chestr[2] = "OC=" + num1[2].ToString();
            chestr[3] = "OP=" + num1[3].ToString();
            chestr[4] = "OH=" + num1[4].ToString();
            chestr[5] = "SV=" + num1[5].ToString();
            chestr[6] = "CC=" + num1[6].ToString();
            chestr[7] = "CV=" + num1[7].ToString();
            chestr[8] = "CP=" + num2[0].ToString();
            chestr[9] = "CR=" + num2[1].ToString();
            return chestr;
        }
        public void recevie_verify(byte byte_verify, int start = 0)//该方法是为了检验发回的校验码是否成功
        {

            string temp = "";
            int check_value = 0;
            if (start == 4)
            {
                temp = byte_verify.ToString("X2");
                check_value = Convert.ToInt32(temp, 16);
                if (check_value == 144)
                {

                    MessageBox.Show("校验和错误");

                }
                else if (check_value == 160)
                {

                    MessageBox.Show("参数错误或参数溢出");
                }
                else if (check_value == 176)
                {

                    MessageBox.Show("命令不能被执行");
                }
                else if (check_value == 192)
                {

                    MessageBox.Show("命令无效");
                }
                else if (check_value == 128)
                {
                    MessageBox.Show("成功");

                }

            }


        }
        private void comopen_Click(object sender, EventArgs e)//打开串口或者关闭串口的操作
        {
         
            if (comslc.Checked)
            {
               
                if (comm.IsOpen)
                {
                    Closing_new = true;
                    while (Listening)
                        Application.DoEvents();
                    comm.Close();
                }
                else
                {
                    if (comport.Text == "")
                    {
                        MessageBox.Show("出错！没有串口！");
                        return;
                    }
                    //打开串口
                    Closing_new = false;
                    comm.PortName = comport.Text;
                    comm.BaudRate = int.Parse(baud.Text);
                    try
                    {
                        comm.Open();
                    }
                    catch (Exception ex)
                    {
                        comm = new SerialPort();
                        MessageBox.Show(ex.Message);
                    }
                }
                comopen.Text = comm.IsOpen ? "关闭" : "打开";//设置按钮状态
                comport.Enabled = !comm.IsOpen;
                baud.Enabled = !comm.IsOpen;
            }
            else if(usbslc.Checked)
            {
                if(comopen.Text=="打开")
                {
                    comopen.Text = "关闭";
                    if (cbousb.SelectedIndex == -1) return;
                    try
                    {
                        portOperatorBase = new USBPortOperator(cbousb.SelectedItem.ToString());
                    }
                    catch
                    {
                        MessageBox.Show("出错！");
                    }
                   
                }
                else
                {
                    comopen.Text = "打开";
                    try
                    {
                        portOperatorBase.Close();
                    }
                    catch
                    {
                        MessageBox.Show("关闭失败");
                    }
                   
                }
            }
        }

        private void Clearall_Click(object sender, EventArgs e)
        {
            received_count = 0;
            receivedbox.Text = "已清空!!";
            label4.Text = "已接收：0";
        }

        private void sendbt_Click(object sender, EventArgs e)//串口发送操作
        {
            //定义一个变量，记录发送几个字节
            if (!comm.IsOpen)
                return;
            int n = 0;
            static_dynamic = 1;

            if (hexsend.Checked)
            {
                string strText;
                string boxtext;
                boxtext = sendbox.Text;
                strText = boxtext.Replace(" ", " ");
                byte[] btext = new byte[strText.Length / 2];
                for (int i = 0; i < strText.Length / 2; i++)
                {
                    btext[i] = Convert.ToByte(Convert.ToInt32(strText.Substring(i * 2, 2), 16));
                }
                comm.Write(btext, 0, strText.Length / 2);
                n = strText.Length / 2;
            }
            else
            {
                if (spacesend.Checked)//包含换行符
                {
                    comm.Write(sendbox.Text + System.Environment.NewLine);
                    if (sendbox.Text.Length > 0)
                        n = sendbox.Text.Length + 2;
                    else
                        n = sendbox.Text.Length;
                }
                else//不包含换行符
                {
                    string a = sendbox.Text;
                    string s = sendbox.Text;
                    n = sendbox.Text.Length;
                    if (n >= 1)
                    {
                        if (a[n - 1] == '\n')
                            s = a.Substring(0, n - 1) + "\r\n";
                        n = n + 1;
                        comm.Write(s);
                    }

                }


            }
            send_count += n;
            sendlabel.Text = "已发送:" + send_count.ToString();
            if (n == 0)
            {
                MessageBox.Show("无发送信息！");
            }
        }

        private void sendclear_Click(object sender, EventArgs e)
        {
            send_count = 0;
            sendlabel.Text = "已发送:0";
            sendbox.Text = "";
        }

        private void label5_Click(object sender, EventArgs e)
        {

        }
        //串口的远程命令
        private void com_remote()
        {
            if (!comm.IsOpen)
            {
                MessageBox.Show("串口未打开，请检查");
                return;
            }

            if (remote.Text == "远程操作")
            {
                static_dynamic = 1;
                send_confirm = 1;
                remote.Text = "面板操作";
                string test;
                test = Dectohex(sendbox.Text, 0, 0, 1);
                byte[] btext = new byte[test.Length / 2];
                for (int i = 0; i < test.Length / 2; i++)
                {
                    btext[i] = Convert.ToByte(Convert.ToInt32(test.Substring(i * 2, 2), 16));
                }
                comm.Write(btext, 0, test.Length / 2);

                info_msg.Start();
               // timersend.Interval = 1000;
                timersend.Start();
                timersend.Interval = 10;
            }
            else
            {
                static_dynamic = 1;
                remote.Text = "远程操作";

                string link_local;
                link_local = Dectohex("", 0, 0, 0);
                byte[] btext = new byte[link_local.Length / 2];
                for (int i = 0; i < link_local.Length / 2; i++)
                {
                    btext[i] = Convert.ToByte(Convert.ToInt32(link_local.Substring(i * 2, 2), 16));
                }
                comm.Write(btext, 0, link_local.Length / 2);
                info_msg.Stop();
                timersend.Stop();
                sendbox.Clear();
            }
        }
        //usb的远程操作
        private void usb_remote()
        {
            string content="";
            if (comopen.Text == "打开")
            {
                MessageBox.Show("端口未打开");
                return;
            }
            else if (comopen.Text == "关闭")
            {
                if (remote.Text == "远程操作")
                    {
                       remote.Text = "面板操作";
                       content = "system:remote";
                       worng_flag = 0;//用于检查远程操作是否打开
                    timersend.Start();
                    timersend.Interval = 10;
                    info_msg.Start();

                }
                    else if(remote.Text=="面板操作")
                    {
                        remote.Text = "远程操作";
                        content = "system:local";
                        timersend.Stop();
                        info_msg.Stop();
                }
                myq.Enqueue(content);
                usb_send();

            }



        }
        private void remote_Click(object sender, EventArgs e)//在两种模式下的远程原则
        {
            if (comslc.Checked)
            {
                com_remote();
            }
            else if(usbslc.Checked)
            {
                usb_remote();
            }
            
        }
        public string Dectohex(string decstring = "", int kind = 0, int modeset = 0, int getvlave = 0)
        {
            string sendstring;
            int[] dec = new int[25];
            string[] promitive = new string[25];
            string[] inputvalue = new string[4];
            int[] decvalue = new int[4];
            string middle;
            dec[0] = 170;//同步头
            dec[1] = 0;//地址位
            dec[2] = 32 + kind;
            dec[3] = 0;//初始值输入
            dec[4] = 0;//系统保留
            int correct = 0;//校验初始值

            //对前三个字节做的十六进制转换
            for (int i = 0; i < 3; i++)
            {
                promitive[i] = dec[i].ToString("X2");

            }
            switch (modeset)
            {
                case Mode_set://这是设置数据的状态

                    dec[3] = Int32.Parse(decstring);//输入的指定值
                                                    //对设置的电流电压值进行转换十六进制
                    middle = dec[3].ToString("X8");
                    promitive[3] = "";
                    int j = 0;
                    for (int i = 6; i >= 0; i -= 2)
                    {

                        promitive[3] += middle.Substring(i, 2);
                        if (j < 4)
                        {
                            inputvalue[j] = middle.Substring(i, 2);
                            decvalue[j] = Convert.ToInt32(inputvalue[j], 16);
                        }
                        j++;
                    }
                    dec[3] = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        dec[3] += decvalue[i];
                    }
                    //设置系统保留的十六进制
                    promitive[4] = dec[4].ToString("X36");
                    break;
                case Mode_read:
                    dec[3] = 0;
                    promitive[3] = dec[3].ToString("X8");
                    promitive[4] = dec[4].ToString("X36");
                    break;
                case Mode_control:
                    dec[3] = getvlave;
                    promitive[3] = dec[3].ToString("X2");
                    promitive[4] = dec[4].ToString("X42");
                    break;


            }


            //计算校验和
            string verify = "";
            for (int i = 0; i < 5; i++)
            {
                correct += dec[i];
            }
            verify = correct.ToString("X8");
            promitive[5] = verify.Substring(6, 2);

            sendstring = "";
            for (int i = 0; i < 6; i++)
            {
                sendstring += promitive[i];
            }

            return sendstring;
        }
        public string dynamictran(int kind = 0, string V_A = "", string T_A = "", string V_B = "", string T_B = "", int move = 0)
        {
            string dynamic_hex = "";
            string temp = "";
            string verify = "";
            int[] dynamic_dec = new int[10];
            string[] dynamic_pre = new string[10];
            string[] inputvalue = new string[4];
            int[] decvalue = new int[4];
            dynamic_dec[0] = 170;//同步头
            dynamic_dec[1] = 0;//地址位
            dynamic_dec[2] = 32 + kind;
            dynamic_dec[8] = 0;//系统保留
            int correct = 0;//校验码和
            //将前三个字节转换为十六进制字符串
            for (int i = 0; i < 3; i++)
            {
                dynamic_pre[i] = dynamic_dec[i].ToString("X2");
            }


            //输入的A电类的值
            dynamic_dec[3] = Int32.Parse(V_A);
            //对设置A电类值进行转换十六进制
            temp = dynamic_dec[3].ToString("X8");
            dynamic_pre[3] = "";
            int j_va = 0;
            for (int i = 6; i >= 0; i -= 2)
            {

                dynamic_pre[3] += temp.Substring(i, 2);
                if (j_va < 4)
                {
                    inputvalue[j_va] = temp.Substring(i, 2);
                    decvalue[j_va] = Convert.ToInt32(inputvalue[j_va], 16);
                }
                j_va++;
            }
            dynamic_dec[3] = 0;
            for (int i = 0; i < 4; i++)
            {
                dynamic_dec[3] += decvalue[i];
            }


            //输入的A时间值
            dynamic_dec[4] = Int32.Parse(T_A);
            //对设置A时间值进行转换十六进制
            temp = dynamic_dec[4].ToString("X4");
            dynamic_pre[4] = "";
            int j_ta = 0;
            for (int i = 2; i >= 0; i -= 2)
            {

                dynamic_pre[4] += temp.Substring(i, 2);
                if (j_ta < 2)
                {
                    inputvalue[j_ta] = temp.Substring(i, 2);
                    decvalue[j_ta] = Convert.ToInt32(inputvalue[j_ta], 16);
                }
                j_ta++;
            }
            dynamic_dec[4] = 0;
            for (int i = 0; i < 2; i++)
            {
                dynamic_dec[4] += decvalue[i];
            }


            //输入的B电类的值
            dynamic_dec[5] = Int32.Parse(V_B);
            //对设置B电类值进行转换十六进制
            temp = dynamic_dec[5].ToString("X8");
            dynamic_pre[5] = "";
            int j_vb = 0;
            for (int i = 6; i >= 0; i -= 2)
            {

                dynamic_pre[5] += temp.Substring(i, 2);
                if (j_vb < 4)
                {
                    inputvalue[j_vb] = temp.Substring(i, 2);
                    decvalue[j_vb] = Convert.ToInt32(inputvalue[j_vb], 16);
                }
                j_vb++;
            }
            dynamic_dec[5] = 0;
            for (int i = 0; i < 4; i++)
            {
                dynamic_dec[5] += decvalue[i];
            }

            //输入的B时间值
            dynamic_dec[6] = Int32.Parse(T_B);
            //对设置B时间值进行转换十六进制
            temp = dynamic_dec[6].ToString("X4");
            dynamic_pre[6] = "";
            int j_tb = 0;
            for (int i = 2; i >= 0; i -= 2)
            {

                dynamic_pre[6] += temp.Substring(i, 2);
                if (j_tb < 2)
                {
                    inputvalue[j_tb] = temp.Substring(i, 2);
                    decvalue[j_tb] = Convert.ToInt32(inputvalue[j_tb], 16);
                }
                j_tb++;
            }
            dynamic_dec[6] = 0;
            for (int i = 0; i < 2; i++)
            {
                dynamic_dec[6] += decvalue[i];
            }
            //动态模式设置
            dynamic_dec[7] = move;
            dynamic_pre[7] = dynamic_dec[7].ToString("X2");
            //系统保留
            dynamic_pre[8] = dynamic_dec[8].ToString("X18");
            //校验码
            for (int i = 0; i <= 8; i++)
            {
                correct += dynamic_dec[i];
            }
            verify = correct.ToString("X8");
            dynamic_pre[9] = verify.Substring(6, 2);
            for (int i = 0; i <= 9; i++)
            {
                dynamic_hex += dynamic_pre[i];
            }

            return dynamic_hex;
        }
        //串口的发送操作
        private void serialcom_send()
        {
            if (!comm.IsOpen)
            {
                MessageBox.Show("串口未打开，请检查");
                return;
            }
            if (remote.Text == "远程操作")
            {
                MessageBox.Show("请检查是否允许远程操作");
                return;
            }
            static_dynamic = 1;
            string hexstring = "";
            //模式设置
            if (mode_selec.SelectedItem == mode_selec.Items[0])//控制负载输入状态
            {
                if (elec_switch.SelectedItem == elec_switch.Items[0])
                {
                    hexstring = Dectohex("", 1, Mode_control, 0);//OFF
                }
                else if (elec_switch.SelectedItem == elec_switch.Items[1])
                {
                    hexstring = Dectohex("", 1, Mode_control, 1);//ON
                }

            }
            else if (mode_selec.SelectedItem == mode_selec.Items[1])//负载模式的设置
            {
                if (selec_elec.SelectedItem == selec_elec.Items[0])
                {
                    hexstring = Dectohex("", 8, Mode_control, 0);//CC
                }
                else if (selec_elec.SelectedItem == selec_elec.Items[1])
                {
                    hexstring = Dectohex("", 8, Mode_control, 1);//CV
                }
                else if (selec_elec.SelectedItem == selec_elec.Items[2])
                {
                    hexstring = Dectohex("", 8, Mode_control, 2);//CW
                }
                else if (selec_elec.SelectedItem == selec_elec.Items[3])
                {
                    hexstring = Dectohex("", 8, Mode_control, 3);//CR
                }
            }
            else if (mode_selec.SelectedItem == mode_selec.Items[2])//local是否使用
            {
                if (local_permit.SelectedItem == local_permit.Items[0])
                {
                    hexstring = Dectohex("", 53, Mode_control, 0);//禁止
                }
                else if (local_permit.SelectedItem == local_permit.Items[1])
                {
                    hexstring = Dectohex("", 53, Mode_control, 1);//允许
                }
            }
            else if (mode_selec.SelectedItem == mode_selec.Items[3])//远程测量是否打开
            {
                if (elec_switch.SelectedItem == elec_switch.Items[0])
                {
                    hexstring = Dectohex("", 54, Mode_control, 0);//禁止
                }
                else if (elec_switch.SelectedItem == elec_switch.Items[1])
                {
                    hexstring = Dectohex("", 54, Mode_control, 1);//允许
                }
            }


            //数值读取
            if (read_selec.SelectedItem == read_selec.Items[0])
            {
                hexstring = Dectohex("", 3, Mode_read);//读取最大输入电压
            }
            else if (read_selec.SelectedItem == read_selec.Items[1])
            {
                hexstring = Dectohex("", 5, Mode_read);//读取最大输入电流
            }
            else if (read_selec.SelectedItem == read_selec.Items[2])
            {
                hexstring = Dectohex("", 7, Mode_read);//读取最大输入功率值
            }
            else if (read_selec.SelectedItem == read_selec.Items[3])
            {
                hexstring = Dectohex("", 11, Mode_read);//读取负载定电流值
            }
            else if (read_selec.SelectedItem == read_selec.Items[4])
            {
                hexstring = Dectohex("", 13, Mode_read);//读取负载的定电压值
            }
            else if (read_selec.SelectedItem == read_selec.Items[5])
            {
                hexstring = Dectohex("", 15, Mode_read);//读取负载的定功率值
            }
            else if (read_selec.SelectedItem == read_selec.Items[6])
            {
                hexstring = Dectohex("", 17, Mode_read);//读取负载的定电阻值
            }
            else if (read_selec.SelectedItem == read_selec.Items[7])
            {
                hexstring = Dectohex("", 9, Mode_read);//读取负载模式
            }
            else if (read_selec.SelectedItem == read_selec.Items[8])
            {
                hexstring = Dectohex("", 47, Mode_read);//读取负载定电压模式下的最小电压值
            }
            else if (read_selec.SelectedItem == read_selec.Items[9])
            {
                hexstring = Dectohex("", 55, Mode_read);//读取远程测量模式
            }
            else if (read_selec.SelectedItem == read_selec.Items[10])
            {
                hexstring = Dectohex("", 63, Mode_read);//读取负载相关状态
            }
            else if (read_selec.SelectedItem == read_selec.Items[11])
            {
                hexstring = Dectohex("", 19, Mode_read);//读取负载动态电流参数值
            }
            else if (read_selec.SelectedItem == read_selec.Items[12])
            {
                hexstring = Dectohex("", 21, Mode_read);//读取负载动态电压参数值
            }
            else if (read_selec.SelectedItem == read_selec.Items[13])
            {
                hexstring = Dectohex("", 23, Mode_read);//读取负载动态功率参数值
            }
            else if (read_selec.SelectedItem == read_selec.Items[14])
            {
                hexstring = Dectohex("", 25, Mode_read);//读取负载动态电阻参数值
            }


            //数值输入
            if (controlbox.SelectedItem == controlbox.Items[0])
            {
                hexstring = Dectohex(staticnum.Text, 2, Mode_set);//设置最大输入电压值
            }
            else if (controlbox.SelectedItem == controlbox.Items[1])
            {
                hexstring = Dectohex(staticnum.Text, 4, Mode_set);//设置最大输入电流值
            }
            else if (controlbox.SelectedItem == controlbox.Items[2])
            {
                hexstring = Dectohex(staticnum.Text, 6, Mode_set);//设置最大输入功率
            }
            else if (controlbox.SelectedItem == controlbox.Items[3])
            {
                hexstring = Dectohex(staticnum.Text, 10, Mode_set);//设置定电流值
            }
            else if (controlbox.SelectedItem == controlbox.Items[4])
            {
                hexstring = Dectohex(staticnum.Text, 12, Mode_set);//设置定电压值
            }
            else if (controlbox.SelectedItem == controlbox.Items[5])
            {
                hexstring = Dectohex(staticnum.Text, 14, Mode_set);//设置定功率值
            }
            else if (controlbox.SelectedItem == controlbox.Items[6])
            {
                hexstring = Dectohex(staticnum.Text, 16, Mode_set);//设置定电阻值
            }
            else if (controlbox.SelectedItem == controlbox.Items[7])
            {
                hexstring = Dectohex(staticnum.Text, 46, Mode_set);//设置定电压模式下的最小电压值
            }

            //动态数值输入
            if (dynamic_selec.SelectedItem == dynamic_selec.Items[0])
            {

                try
                {

                    if (dynamic_check.SelectedItem == dynamic_check.Items[0])
                        hexstring = dynamictran(18, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 0);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[1])
                        hexstring = dynamictran(18, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 1);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[2])
                        hexstring = dynamictran(18, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 2);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[3])
                        hexstring = dynamictran(18, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 3);
                }
                catch (Exception)
                {
                    MessageBox.Show("请选择操作模式");
                }




            }
            else if (dynamic_selec.SelectedItem == dynamic_selec.Items[1])
            {
                try
                {
                    if (dynamic_check.SelectedItem == dynamic_check.Items[0])
                        hexstring = dynamictran(20, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 0);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[1])
                        hexstring = dynamictran(20, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 1);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[2])
                        hexstring = dynamictran(20, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 2);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[2])
                        hexstring = dynamictran(20, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 3);
                }
                catch (Exception)
                {
                    MessageBox.Show("请选择操作模式");
                }
            }
            else if (dynamic_selec.SelectedItem == dynamic_selec.Items[2])
            {
                try
                {
                    if (dynamic_check.SelectedItem == dynamic_check.Items[0])
                        hexstring = dynamictran(22, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 0);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[1])
                        hexstring = dynamictran(22, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 1);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[2])
                        hexstring = dynamictran(22, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 2);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[3])
                        hexstring = dynamictran(22, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 3);
                }
                catch (Exception)
                {
                    MessageBox.Show("请选择操作模式");
                }
            }
            else if (dynamic_selec.SelectedItem == dynamic_selec.Items[3])
            {
                try
                {
                    if (dynamic_check.SelectedItem == dynamic_check.Items[0])
                        hexstring = dynamictran(24, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 0);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[1])
                        hexstring = dynamictran(24, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 1);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[2])
                        hexstring = dynamictran(24, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 2);
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[3])
                        hexstring = dynamictran(24, V_A.Text, T_A.Text, V_B.Text, T_B.Text, 3);
                }
                catch (Exception)
                {
                    MessageBox.Show("请选择操作模式");
                }
            }
            myq.Enqueue(hexstring);
        }
        //usb的发送命令选择
        int worng_flag = 0;
        private void usb_select_cmd()
        {
            string content="\0";
            string[] dyamic_content = new string[5];
            if(remote.Text=="远程操作")
            {
                MessageBox.Show("请检查是否允许远程操作");
                worng_flag = 1;
                return;
            }
           
            
            //模式设置（测试完成）
            if (mode_selec.SelectedItem==mode_selec.Items[0])//控制负载输入状态
            {
                if(elec_switch.SelectedItem==elec_switch.Items[0])
                {
                    content = "inp 0";//OFF
                }
                else if(elec_switch.SelectedItem==elec_switch.Items[1])
                {
                    content = "inp 1";//ON
                }
            }
            else if (mode_selec.SelectedItem == mode_selec.Items[1])//负载模式的设置
            {
                if (selec_elec.SelectedItem == selec_elec.Items[0])
                {
                    content = "func current";//CC
                }
                else if (selec_elec.SelectedItem == selec_elec.Items[1])
                {
                    content = "func voltage";//CV
                }
                else if (selec_elec.SelectedItem == selec_elec.Items[2])
                {
                    content = "func power";//CW
                }
                else if (selec_elec.SelectedItem == selec_elec.Items[3])
                {
                    content = "func resistance";//CR
                }
            }
            else if (mode_selec.SelectedItem == mode_selec.Items[2])//local是否使用
            {
                if (local_permit.SelectedItem == local_permit.Items[0])
                {
                    content ="syst:rwl";//禁止
                    
                }
                else if (local_permit.SelectedItem == local_permit.Items[1])
                {

                    content = "syst:rem";//允许
                }
            }
            else if (mode_selec.SelectedItem == mode_selec.Items[3])//远程测量是否打开
            {
                if (elec_switch.SelectedItem == elec_switch.Items[0])
                {
                    content = "rem:sens 0";//禁止
                }
                else if (elec_switch.SelectedItem == elec_switch.Items[1])
                {
                    content = "rem:sens 1";//允许
                }
            }

            //数值输入//(最大部分设置部分有问题）
            if (controlbox.SelectedItem == controlbox.Items[0])
            {
                content = Dectohex(staticnum.Text, 2, Mode_set);//设置最大输入电压值
            }
            else if (controlbox.SelectedItem == controlbox.Items[1])
            {
                content = Dectohex(staticnum.Text, 4, Mode_set);//设置最大输入电流值
            }
            else if (controlbox.SelectedItem == controlbox.Items[2])
            {
                content = Dectohex(staticnum.Text, 6, Mode_set);//设置最大输入功率
            }
            else if (controlbox.SelectedItem == controlbox.Items[3])
            {
                content = "current "+ staticnum.Text;//设置定电流值
            }
            else if (controlbox.SelectedItem == controlbox.Items[4])
            {
                content = "volt "+ staticnum.Text;//设置定电压值
            }
            else if (controlbox.SelectedItem == controlbox.Items[5])
            {
                content = "pow "+staticnum.Text;//设置定功率值
            }
            else if (controlbox.SelectedItem == controlbox.Items[6])
            {
                content = "RES " + staticnum.Text;//设置定电阻值
            }
            else if (controlbox.SelectedItem == controlbox.Items[7])//未找到对应的命令
            {
                content = Dectohex(staticnum.Text, 46, Mode_set);//设置定电压模式下的最小电压值
            }
            //动态数值输入
            if (dynamic_selec.SelectedItem == dynamic_selec.Items[0])//电流参数
            {

                try
                {

                    if (dynamic_check.SelectedItem == dynamic_check.Items[0])//continuous
                    {
                        dyamic_content[0] = "CURR:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "CURR:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "CURR:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "CURR:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "CURR:TRAN:MODE CONTinuous";
                        for(int i=0;i<5;i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[1])//PULSE
                    {
                        dyamic_content[0] = "CURR:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "CURR:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "CURR:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "CURR:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "CURR:TRAN:MODE PULSe";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[2])//toggle
                    {
                        dyamic_content[0] = "CURR:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "CURR:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "CURR:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "CURR:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "CURR:TRAN:MODE TOGGle";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }

                }
                catch (Exception)
                {
                    MessageBox.Show("请选择操作模式");
                }




            }
            else if (dynamic_selec.SelectedItem == dynamic_selec.Items[1])//电压
            {
                try
                {
                    if (dynamic_check.SelectedItem == dynamic_check.Items[0])//continuous
                    {
                        dyamic_content[0] = "VOLT:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "VOLT:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "VOLT:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "VOLT:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "VOLT:TRAN:MODE CONTinuous";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[1])//pulse
                    {
                        dyamic_content[0] = "VOLT:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "VOLT:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "VOLT:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "VOLT:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "VOLT:TRAN:MODE PULse";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[2])//toggle
                    {
                        dyamic_content[0] = "VOLT:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "VOLT:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "VOLT:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "VOLT:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "VOLT:TRAN:MODE TOGGle";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                  
                }
                catch (Exception)
                {
                    MessageBox.Show("请选择操作模式");
                }
            }
            else if (dynamic_selec.SelectedItem == dynamic_selec.Items[2])//功率
            {
                try
                {
                    if (dynamic_check.SelectedItem == dynamic_check.Items[0])//continuous
                    {
                        dyamic_content[0] = "POW:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "POW:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "POW:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "POW:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "POW:TRAN:MODE CONTinuous";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[1])//pulse
                    {
                        dyamic_content[0] = "POW:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "POW:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "POW:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "POW:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "POW:TRAN:MODE pulse";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[2])//toggle
                    {
                        dyamic_content[0] = "POW:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "POW:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "POW:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "POW:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "POW:TRAN:MODE TOGGle";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                   
                }
                catch (Exception)
                {
                    MessageBox.Show("请选择操作模式");
                }
            }
            else if (dynamic_selec.SelectedItem == dynamic_selec.Items[3])//电阻
            {
                try
                {
                    if (dynamic_check.SelectedItem == dynamic_check.Items[0])//continous
                    {
                        dyamic_content[0] = "RES:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "RES:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "RES:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "RES:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "RES:TRAN:MODE CONTinuous";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[1])//pulse
                    {
                        dyamic_content[0] = "RES:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "RES:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "RES:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "RES:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "RES:TRAN:MODE PLUSe";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                    else if (dynamic_check.SelectedItem == dynamic_check.Items[2])//TOGGle
                    {
                        dyamic_content[0] = "RES:TRAN:ALEV " + V_A.Text;
                        dyamic_content[1] = "RES:TRAN:BLEV " + V_B.Text;
                        dyamic_content[2] = "RES:TRAN:AWID " + T_A.Text;
                        dyamic_content[3] = "RES:TRAN:BWID " + T_B.Text;
                        dyamic_content[4] = "RES:TRAN:MODE TOGGle";
                        for (int i = 0; i < 5; i++)
                        {
                            myq.Enqueue(dyamic_content[i]);
                        }
                    }
                   
                }
                catch (Exception)
                {
                    MessageBox.Show("请选择操作模式");
                }
            }
            if (content != "\0")
            {
                myq.Enqueue(content);
            }
        }
        //usb的设置操作
        private void usb_send()
        {
            string content;
            string[] now_result = new string[3];
            content = myq.Dequeue();
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (content == "\0")
            {
                MessageBox.Show("命令不能为空");
                return;

            }
            try
            {
                if (content == "MEAS:VOLT?")
                {
                    portOperatorBase.WriteLine(content);
                    now_result[0] = portOperatorBase.ReadLine();
                    v_info.Text = now_result[0];
                }
                else if (content == "MEAS:CURR?")
                {
                    portOperatorBase.WriteLine(content);
                    now_result[1] = portOperatorBase.ReadLine();
                    I_info.Text = now_result[1];
                }
                else if (content == "FETC:POW?")
                {
                    portOperatorBase.WriteLine(content);
                    now_result[2] = portOperatorBase.ReadLine();
                    W_info.Text = now_result[2];
                }
                else
                {
                    portOperatorBase.WriteLine(content);
                    
                }
            }
            catch
            {
                content = $"写入命令\"{content}\"失败！";
            }
            if (content != "MEAS:VOLT?" && content != "MEAS:CURR?" && content != "FETC:POW?")
                DisplayToTextBox($"[Time:{stopwatch.ElapsedMilliseconds}ms] Write: {content}");
            //动态参数测试时，先关闭
            //if (worng_flag == 0)
            //{
            //    msg_work(0, stopwatch.ElapsedMilliseconds);

            //}

        }
        //显示发送数据
        private void DisplayToTextBox(string content)
        {
            sendbox.Text += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {content}\r\n";
            sendbox.SelectionStart = sendbox.Text.Length - 1;
            sendbox.ScrollToCaret();
        }
        private void test_Click(object sender, EventArgs e)
        {
            if(comslc.Checked)
            { 
               serialcom_send();
            }
            else if(usbslc.Checked)
            {
                usb_select_cmd();
                //usb_send();
            }
        }

        private void sendbox_TextChanged(object sender, EventArgs e)
        {

        }

        private void mode_selec_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (controlbox.SelectedIndex != -1)
                controlbox.SelectedIndex = -1;
            if (dynamic_selec.SelectedIndex != -1)
                dynamic_selec.SelectedIndex = -1;
            if (read_selec.SelectedIndex != -1)
                read_selec.SelectedIndex = -1;
            staticnum.Clear();
            reset_dynamic();
            dynamic_check.Enabled = false;
            if (mode_selec.SelectedItem == mode_selec.Items[0])//负载输入状态
            {
                elec_switch.Enabled = true;
                selec_elec.Enabled = false;
                local_permit.Enabled = false;
            }
            else if (mode_selec.SelectedItem == mode_selec.Items[1])//负载模式
            {
                elec_switch.Enabled = false;
                selec_elec.Enabled = true;
                local_permit.Enabled = false;
            }
            else if (mode_selec.SelectedItem == mode_selec.Items[2])//local模式
            {
                elec_switch.Enabled = false;
                selec_elec.Enabled = false;
                local_permit.Enabled = true;
            }
            else if (mode_selec.SelectedItem == mode_selec.Items[3])//远程测量状态
            {
                elec_switch.Enabled = true;
                selec_elec.Enabled = false;
                local_permit.Enabled = false;
            }

        }

        private void elec_switch_ItemCheck(object sender, ItemCheckEventArgs e)
        {

            for (int i = 0; i < elec_switch.Items.Count; i++)
            {
                if (i != e.Index)
                {
                    elec_switch.SetItemCheckState(i, System.Windows.Forms.CheckState.Unchecked);
                }
            }
        }

        private void selec_elec_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            for (int i = 0; i < selec_elec.Items.Count; i++)
            {
                if (i != e.Index)
                {
                    selec_elec.SetItemCheckState(i, System.Windows.Forms.CheckState.Unchecked);
                }
            }
        }

        private void local_permit_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            for (int i = 0; i < local_permit.Items.Count; i++)
            {
                if (i != e.Index)
                {
                    local_permit.SetItemCheckState(i, System.Windows.Forms.CheckState.Unchecked);
                }
            }
        }

        private void read_selec_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (controlbox.SelectedIndex != -1)
                controlbox.SelectedIndex = -1;
            if (dynamic_selec.SelectedIndex != -1)
                dynamic_selec.SelectedIndex = -1;
            if (mode_selec.SelectedIndex != -1)
                mode_selec.SelectedIndex = -1;
            staticnum.Clear();
            reset_dynamic();
            elec_switch.Enabled = false;
            selec_elec.Enabled = false;
            local_permit.Enabled = false;
            dynamic_check.Enabled = false;
        }
        private void com_eledynamic_set()//串口动态单位设置
        {
            if(dynamic_selec.SelectedIndex==0)
            {
                A_E_unit.Text = "0.1mA";
                A_T_unit.Text = "0.1mS";
                B_E_unit.Text = "0.1mA";
                B_T_unit.Text = "0.1mS";
            }
            else if(dynamic_selec.SelectedIndex==1)
            {
                A_E_unit.Text = "mV";
                A_T_unit.Text = "0.1mS";
                B_E_unit.Text = "mV";
                B_T_unit.Text = "0.1mS";
                
            }
            else if(dynamic_selec.SelectedIndex==2)
            {
                A_E_unit.Text = "mW";
                A_T_unit.Text = "0.1mS";
                B_E_unit.Text = "mW";
                B_T_unit.Text = "0.1mS";
            }
            else if(dynamic_selec.SelectedIndex==3)
            {
                A_E_unit.Text = "mR";
                A_T_unit.Text = "0.1mS";
                B_E_unit.Text = "mR";
                B_T_unit.Text = "0.1mS";
            }
        }
        private void usb_eledynamic_set()//usb动态单位设置
        {
            if (dynamic_selec.SelectedIndex == 0)
            {
                A_E_unit.Text = "A";
                A_T_unit.Text = "S";
                B_E_unit.Text = "A";
                B_T_unit.Text = "S";
            }
            else if (dynamic_selec.SelectedIndex == 1)
            {
                A_E_unit.Text = "V";
                A_T_unit.Text = "S";
                B_E_unit.Text = "V";
                B_T_unit.Text = "S";

            }
            else if (dynamic_selec.SelectedIndex == 2)
            {
                A_E_unit.Text = "W";
                A_T_unit.Text = "S";
                B_E_unit.Text = "W";
                B_T_unit.Text = "S";
            }
            else if (dynamic_selec.SelectedIndex == 3)
            {
                A_E_unit.Text = "R";
                A_T_unit.Text = "S";
                B_E_unit.Text = "R";
                B_T_unit.Text = "S";
            }
        }
        private void dynamic_selec_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (controlbox.SelectedIndex != -1)
                controlbox.SelectedIndex = -1;
            if (read_selec.SelectedIndex != -1)
                read_selec.SelectedIndex = -1;
            if (mode_selec.SelectedIndex != -1)
                mode_selec.SelectedIndex = -1;

            //两种不同通讯模式下的单位不同
            if (comslc.Checked)
                com_eledynamic_set();
            else if (usbslc.Checked)
                usb_eledynamic_set();


            staticnum.Enabled = false;
            staticnum.Clear();
            V_A.Enabled = true;
            T_A.Enabled = true;
            V_B.Enabled = true;
            T_B.Enabled = true;
            dynamic_check.Enabled = true;
            elec_switch.Enabled = false;
            selec_elec.Enabled = false;
            local_permit.Enabled = false;

        }
        public void reset_dynamic()
        {
            V_A.Clear();
            V_A.Enabled = false;
            T_A.Clear();
            T_A.Enabled = false;
            V_B.Clear();
            V_B.Enabled = false;
            T_B.Clear();
            T_B.Enabled = false;
        }
        private void com_eleccontrol_set()//com口的静态值单位设定
        {
            if (controlbox.SelectedIndex == 0||controlbox.SelectedIndex==4||controlbox.SelectedIndex==7)//电压
            {
                static_unit.Text = "mV";
            }
            else if(controlbox.SelectedIndex==1||controlbox.SelectedIndex==3)//电流
            {
                static_unit.Text = "0.1mA";
            }
            else if(controlbox.SelectedIndex==2||controlbox.SelectedIndex==5)//功率
            {
                static_unit.Text = "mW";
            }
            else if(controlbox.SelectedIndex==6)
            {
                static_unit.Text = "mR";
            }
           
        }
        private void usb_eleccontrol_set()//usb的静态值单位设定
        {
            if (controlbox.SelectedIndex == 0 || controlbox.SelectedIndex == 4 || controlbox.SelectedIndex == 7)//电压
            {
                static_unit.Text = "V";
            }
            else if (controlbox.SelectedIndex == 1 || controlbox.SelectedIndex == 3)//电流
            {
                static_unit.Text = "A";
            }
            else if (controlbox.SelectedIndex == 2 || controlbox.SelectedIndex == 5)//功率
            {
                static_unit.Text = "W";
            }
            else if (controlbox.SelectedIndex == 6)
            {
                static_unit.Text = "R";
            }

        }
        private void controlbox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (read_selec.SelectedIndex != -1)
                read_selec.SelectedIndex = -1;
            if(dynamic_selec.SelectedIndex!=-1)
                dynamic_selec.SelectedIndex = -1;
            if (mode_selec.SelectedIndex != -1)
                mode_selec.SelectedIndex = -1;

            if (comslc.Checked)
                com_eleccontrol_set();
            else if (usbslc.Checked)
                usb_eleccontrol_set();
            reset_dynamic();
            staticnum.Enabled = true;
            elec_switch.Enabled = false;
            selec_elec.Enabled = false;
            local_permit.Enabled = false;
            dynamic_check.Enabled = false;
        }

        private void dynamic_check_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            for (int i = 0; i < dynamic_check.Items.Count; i++)
            {
                if (i != e.Index)
                {
                    dynamic_check.SetItemCheckState(i, System.Windows.Forms.CheckState.Unchecked);
                }
            }
        }
        private void comtick_send()
        {
            string allhex = "";
            if (static_dynamic == 0)
            {
                allhex = Dectohex("", 63, Mode_read);
                myq.Enqueue(allhex);
            }
        }
        private void usbtick_send()
        {
            string[] now_content = new string[3];
            now_content[0] = "MEAS:VOLT?";//读取输入电压
            now_content[1] = "MEAS:CURR?";//读取输入电流
            now_content[2] = "FETC:POW?"; //读取输入功率
            for (int i = 0; i < 3; i++)
            {
               // portOperatorBase.WriteLine(now_content[i]);
                myq.Enqueue(now_content[i]);
                
            }
          
        }
        private void info_msg_Tick(object sender, EventArgs e)
        {
            if (comslc.Checked)
                comtick_send();
            else if (usbslc.Checked)
                usbtick_send();


        }

        public void queue_send(string hexstr)
        {
           

        }

        private void label31_Click(object sender, EventArgs e)
        {

        }

        private void label32_Click(object sender, EventArgs e)
        {

        }

        private void label30_Click(object sender, EventArgs e)
        {

        }

        private void label34_Click(object sender, EventArgs e)
        {

        }

        private void W_info_Click(object sender, EventArgs e)
        {

        }
        private void comsend_time()
        {
            string hex = "";
            if (deal_over == 1)
            {
                receive_over_sta = 0;//这里是发送的时候，将接受的标志reset一下。

            }

            if (myq.Count != 0)
            {

                hex = myq.Dequeue();
                byte[] btext = new byte[hex.Length / 2];

                for (int i = 0; i < hex.Length / 2; i++)
                {
                    btext[i] = Convert.ToByte(Convert.ToInt32(hex.Substring(i * 2, 2), 16));
                }
                if (static_dynamic == 0)
                {
                    i_flag = 0;
                    send_confirm = -1;
                    receive_over_dyn = 0;

                }
                else if (static_dynamic == 1)
                {
                    send_confirm = 1;
                    i_flag = 0;

                }
                comm.Write(btext, 0, hex.Length / 2);
            }
        }
        private void timersend_Tick(object sender, EventArgs e)
        {
            if (comslc.Checked)
            {
                comsend_time();
            }
            else if(usbslc.Checked)
            {
                if(myq.Count!=0)
                {
                    usb_send();
                }
            }
        }

        private void comslc_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void label7_Click(object sender, EventArgs e)
        {

        }
        private void enablecontrol()
        {
            if(usbslc.Checked)
            {
                askbtn.Enabled = true;
                comport.Enabled = false;
                baud.Enabled = false;
                Databox.Enabled = false;
                stopbox.Enabled = false;
                verifybox.Enabled = false;
                sendbt.Enabled = false;
                sendclear.Enabled = false;
                spacesend.Enabled = false;
                hexsend.Enabled = false;
                hexshow.Enabled = false;
                asciishow.Enabled = false;
                deccheck.Enabled = false;
                

            }
            else if(comslc.Checked)
            {
                askbtn.Enabled = false;
                cbousb.Enabled = false;
                comport.Enabled = true;
                baud.Enabled = true;
                Databox.Enabled = true;
                stopbox.Enabled = true;
                verifybox.Enabled = true;
                sendbt.Enabled = true;
                sendclear.Enabled = true;
                spacesend.Enabled = true;
                hexsend.Enabled = true;
                hexshow.Enabled = true;
                asciishow.Enabled = true;
                deccheck.Enabled = true;
                cbousb.Enabled = false;
            }
        }

        private void slcbtn_Click(object sender, EventArgs e)
        {
            enablecontrol();
        }
        private void msg_work(int kind,long time)
        {
            if(kind==0)//kind=0时为设置模式
            {
                if(time<=200)
                {
                    MessageBox.Show("设置成功");
                }
                else
                {
                    MessageBox.Show("设置失败");
                }
            }
            else if(kind==1)//kind=1时是查询模式
            {
                if (time <= 200)
                {
                    MessageBox.Show("读取成功");
                }
                else
                {
                    MessageBox.Show("读取失败");
                }
            }
        }
        private void usbread_msg()
        {
            string content = "\0";
            string[] dynamic_content = new string[5];
            string[] dynamic_result = new string[5];
            string[] now_content = new string[3];
            string[] now_result = new string[3];
            string result = "\0";
            //数值读取
            if (read_selec.SelectedItem == read_selec.Items[0])
            {
                content = "MEASure:VOLTage:MAX?";//读取最大输入电压
                portOperatorBase.WriteLine(content);
                result = portOperatorBase.ReadLine();
                MessageBox.Show("最大电压为：" + result + "V");

            }
            else if (read_selec.SelectedItem == read_selec.Items[1])
            {
                content = "MEASure:CURRent:MAX?";//读取最大输入电流
                portOperatorBase.WriteLine(content);
                result = portOperatorBase.ReadLine();
                MessageBox.Show("最大电流为：" + result + "A");

            }
            else if (read_selec.SelectedItem == read_selec.Items[2])//未找到最大功率
            {
                content = "MEASure:POWER:MAX?"; ;//读取最大输入功率值
                portOperatorBase.WriteLine(content);
                result = portOperatorBase.ReadLine();
                MessageBox.Show("最大功率为：" + result + "W");

            }
            else if (read_selec.SelectedItem == read_selec.Items[3])
            {
                content = "current?";//读取负载定电流值
                portOperatorBase.WriteLine(content);
                result = portOperatorBase.ReadLine();
                MessageBox.Show("定电流为：" + result + "A");
            }
            else if (read_selec.SelectedItem == read_selec.Items[4])
            {
                content = "voltage?";//读取负载的定电压值
                portOperatorBase.WriteLine(content);
                result = portOperatorBase.ReadLine();
                MessageBox.Show("定电压为：" + result + "V");
            }
            else if (read_selec.SelectedItem == read_selec.Items[5])
            {
                content = "power?";//读取负载的定功率值
                portOperatorBase.WriteLine(content);
                result = portOperatorBase.ReadLine();
                MessageBox.Show("定功率为：" + result + "W");
            }
            else if (read_selec.SelectedItem == read_selec.Items[6])
            {
                content = "RESistance?";//读取负载的定电阻值
                portOperatorBase.WriteLine(content);
                result = portOperatorBase.ReadLine();
                MessageBox.Show("定电阻为：" + result + "R");
            }
            else if (read_selec.SelectedItem == read_selec.Items[7])
            {
                content = "func?";//读取负载模式
                portOperatorBase.WriteLine(content);
                result = portOperatorBase.ReadLine();
                MessageBox.Show("负载模式为：" + result);
            }
            else if (read_selec.SelectedItem == read_selec.Items[8])//未找到相关命令
            {
                content = Dectohex("", 47, Mode_read);//读取负载定电压模式下的最小电压值
            }
            else if (read_selec.SelectedItem == read_selec.Items[9])
            {
                content = "REMote:SENSe?";//读取远程测量模式
                portOperatorBase.WriteLine(content);
                result = portOperatorBase.ReadLine();
                if (result == "0")
                    MessageBox.Show("远程测量模式：关闭");
                else if (result == "1")
                    MessageBox.Show("远程测量模式：打开");
            }
            else if (read_selec.SelectedItem == read_selec.Items[10])//读取负载相关状态
            {
                now_content[0] = "MEAS:VOLT?";//读取输入电压
                now_content[1] = "MEAS:CURR?";//读取输入电流
                now_content[2] = "FETC:POW?"; //读取输入功率
                for (int i = 0; i < 3; i++)
                {
                    portOperatorBase.WriteLine(now_content[i]);
                    now_result[i] = portOperatorBase.ReadLine();
                }
                MessageBox.Show("输入电压为：" + now_result[0] + "(V)\n" +
                               "输入电流为：" + now_result[1] + "(A)\n" +
                               "输入功率为：" + now_result[2] + "(W)\n"
                              );

            }
            else if (read_selec.SelectedItem == read_selec.Items[11])
            {
                //读取负载动态电流参数值
                dynamic_content[0] = "CURRent:TRANsient:ALEVel?";
                dynamic_content[1] = "CURRent:TRANsient:AWIDth?";
                dynamic_content[2] = "CURRent:TRANsient:BLEVel?";
                dynamic_content[3] = "CURRent:TRANsient:BWIDth?";
                dynamic_content[4] = "CURRent:TRANsient:MODE?";
                for (int i = 0; i < 5; i++)
                {
                    //myq.Enqueue(dynamic_content[i]);
                    portOperatorBase.WriteLine(dynamic_content[i]);
                    dynamic_result[i] = portOperatorBase.ReadLine();
                }
                MessageBox.Show("电流A设定值：" + dynamic_result[0] + "(A)\n" +
                               "电流A时间值：" + dynamic_result[1] + "(S)\n" +
                               "电流B设定值：" + dynamic_result[2] + "(A)\n" +
                              "电流B时间值：" + dynamic_result[3] + "(S)\n" +
                              "操作模式：" + dynamic_result[4]);
            }
            else if (read_selec.SelectedItem == read_selec.Items[12])
            {
                //读取负载动态电压参数值
                dynamic_content[0] = "VOLTage:TRANsient:ALEVel?";
                dynamic_content[1] = "VOLTage:TRANsient:AWIDth?";
                dynamic_content[2] = "VOLTage:TRANsient:BLEVel?";
                dynamic_content[3] = "VOLTage:TRANsient:BWIDth?";
                dynamic_content[4] = "VOLTage:TRANsient:MODE?";
                for (int i = 0; i < 5; i++)
                {
                    //myq.Enqueue(dynamic_content[i]);
                    portOperatorBase.WriteLine(dynamic_content[i]);
                    dynamic_result[i] = portOperatorBase.ReadLine();
                }
                MessageBox.Show("电压A设定值：" + dynamic_result[0] + "(V)\n" +
                               "电压A时间值：" + dynamic_result[1] + "(S)\n" +
                               "电压B设定值：" + dynamic_result[2] + "(V)\n" +
                              "电压B时间值：" + dynamic_result[3] + "(S)\n" +
                              "操作模式：" + dynamic_result[4]);
            }
            else if (read_selec.SelectedItem == read_selec.Items[13])
            {
                //读取负载动态功率参数值
                dynamic_content[0] = "POWer:TRANsient:ALEVel?";
                dynamic_content[1] = "POWer:TRANsient:AWIDth?";
                dynamic_content[2] = "POWer:TRANsient:BLEVel?";
                dynamic_content[3] = "POWer:TRANsient:BWIDth?";
                dynamic_content[4] = "POWer:TRANsient:MODE?";
                for (int i = 0; i < 5; i++)
                {
                    //myq.Enqueue(dynamic_content[i]);
                    portOperatorBase.WriteLine(dynamic_content[i]);
                    dynamic_result[i] = portOperatorBase.ReadLine();
                }
                MessageBox.Show("功率A设定值：" + dynamic_result[0] + "(W)\n" +
                               "功率A时间值：" + dynamic_result[1] + "(S)\n" +
                               "功率B设定值：" + dynamic_result[2] + "(W)\n" +
                              "功率B时间值：" + dynamic_result[3] + "(S)\n" +
                              "操作模式：" + dynamic_result[4]);

            }
            else if (read_selec.SelectedItem == read_selec.Items[14])
            {
                //读取负载动态电阻参数值
                dynamic_content[0] = "RESistance:TRANsient:ALEVel?";
                dynamic_content[1] = "RESistance:TRANsient:AWIDth?";
                dynamic_content[2] = "RESistance:TRANsient:BLEVel?";
                dynamic_content[3] = "RESistance:TRANsient:BWIDth?";
                dynamic_content[4] = "RESistance:TRANsient:MODE?";
                for (int i = 0; i < 5; i++)
                {
                    //myq.Enqueue(dynamic_content[i]);
                    portOperatorBase.WriteLine(dynamic_content[i]);
                    dynamic_result[i] = portOperatorBase.ReadLine();
                }
                MessageBox.Show("电阻A设定值：" + dynamic_result[0] + "(R)\n" +
                               "电阻A时间值：" + dynamic_result[1] + "(S)\n" +
                               "电阻B设定值：" + dynamic_result[2] + "(R)\n" +
                              "电阻B时间值：" + dynamic_result[3] + "(S)\n" +
                              "操作模式：" + dynamic_result[4]);
            }
            //if (content != "\0")
            //{
            //    myq.Enqueue(content);
            //}

        }
        private void usbread_result()
        {
            string result;
            //数值显示
            result = portOperatorBase.ReadLine();
            myresult.Enqueue(result);
            if (read_selec.SelectedItem == read_selec.Items[0])
            {
               /* result = portOperatorBase.ReadLine();*///读取最大输入电压
                MessageBox.Show("最大电压为：" + result+"V");
            }
            else if (read_selec.SelectedItem == read_selec.Items[1])
            {
                MessageBox.Show("最大电流为：" + result + "A");//读取最大输入电流
            }
            else if (read_selec.SelectedItem == read_selec.Items[2])//最大功率无命令
            {
                MessageBox.Show("最大功率为：" + result + "W");//读取最大输入功率值
            }
            else if (read_selec.SelectedItem == read_selec.Items[3])
            {
                MessageBox.Show("定电流为：" + result + "A");//读取负载定电流值
            }
            else if (read_selec.SelectedItem == read_selec.Items[4])
            {
                MessageBox.Show("定电压为：" + result + "V");//读取负载的定电压值
            }
            else if (read_selec.SelectedItem == read_selec.Items[5])
            {
                MessageBox.Show("定功率为：" + result + "W");//读取负载的定功率值
            }
            else if (read_selec.SelectedItem == read_selec.Items[6])
            {
                MessageBox.Show("定电阻为：" + result + "R");//读取负载的定电阻值
            }
            else if (read_selec.SelectedItem == read_selec.Items[7])
            {
                MessageBox.Show("负载模式为：" + result );//读取负载模式
            }
            else if (read_selec.SelectedItem == read_selec.Items[8])
            {
                result = Dectohex("", 47, Mode_read);//读取负载定电压模式下的最小电压值
            }
            else if (read_selec.SelectedItem == read_selec.Items[9])
            {
                if(result == "0")
                {
                    MessageBox.Show("远程测量模式：关闭");//读取远程测量模式
                }
                else if(result == "1")
                {
                    MessageBox.Show("远程测量模式：打开");//读取远程测量模式
                }
                
            }
            else if (read_selec.SelectedItem == read_selec.Items[10])
            {
                result = Dectohex("", 63, Mode_read);//读取负载相关状态
            }
            else if (read_selec.SelectedItem == read_selec.Items[11])
            {
                MessageBox.Show(result);//读取负载动态电流参数值
            }
            else if (read_selec.SelectedItem == read_selec.Items[12])
            {
                result = Dectohex("", 21, Mode_read);//读取负载动态电压参数值
            }
            else if (read_selec.SelectedItem == read_selec.Items[13])
            {
                result = Dectohex("", 23, Mode_read);//读取负载动态功率参数值
            }
            else if (read_selec.SelectedItem == read_selec.Items[14])
            {
                result = Dectohex("", 25, Mode_read);//读取负载动态电阻参数值
            }
        }
        private void askbtn_Click(object sender, EventArgs e)
        {
            usbread_msg();
            Stopwatch stopwatch = Stopwatch.StartNew();
           // usb_send();
            //string result;
            //result = portOperatorBase.ReadLine();
            //usbread_result();
            //DisplayToTextBox($"[Time:{stopwatch.ElapsedMilliseconds}ms] Read:  {result}");
        }

        private void elec_switch_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
