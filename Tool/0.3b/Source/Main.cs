using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows.Forms;
using System.Text.RegularExpressions;

namespace ATX2AT_Configuration_Tool
{
    public partial class Main : Form
    {
        SerCOM ProcessCOM;

        public struct ATX2AT
        {
            public string fw_ver;
            public bool power_status;
            public bool blowtype;
            public bool power_good;
            public int ssaver_timeout;
            public int slowblow_ms;

            public byte cset_index;
            public int Prev_DIPSwitch;
            public int DIPSwitch;

            public double cur_5v_amp;
            public double cur_12v_amp;
            public double cur_5v_volt;
            public double cur_12v_volt;

            public double cur_5v_amp_max;
            public double cur_12v_amp_max;
            public double cur_5v_volt_max;
            public double cur_12v_volt_max;

            public double lim_5v;
            public double lim_12v;
        }

        public double[] eeprom_lim_5v = new double[32];
        public double[] eeprom_lim_12v = new double[32];

        ATX2AT A2A;

        public Main()
        {
            InitializeComponent();

            // Hide Log if not in Debug Mode
            #if !DEBUG
                this.Height = 407;
            #endif

            Version appver = Assembly.GetExecutingAssembly().GetName().Version;
            this.Text += " " + appver.Major.ToString() + '.' + appver.Minor.ToString() + 'b';
        }

        private void Main_Load(object sender, EventArgs e)
        {
            ProcessCOM = new SerCOM();

            // Create Event Handlers
            ProcessCOM.ProcessLog += new SerCOM.NotifyLog(ProcessLog);
            ProcessCOM.ProcessData += new SerCOM.NotifyBytes(ProcessRAWData);

            // Try to connect at startup
            ProcessCOM.ConnectToDevice();

            // Init Prev_DIPSwitch to -1 (because 0 is a valid value)
            A2A.Prev_DIPSwitch--;
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            // We're about to quit. Close Serial.
            ProcessCOM.CloseConnection();
        }

        // Because we need delegate (SerCOM is running on another thread)
        private delegate void DataReceivedDelegate(byte[] BytesRcvd);
        private delegate void LogReceivedDelegate(string log);

        // We got a "Process Log" from SerCOM. Proceed.
        private void ProcessLog(string log)
        {
            if (this.InvokeRequired)
            {
                // Insure the caller is on the correct thread.
                this.Invoke(new LogReceivedDelegate(ProcessLog), new Object[] { log });
            }
            else
            {
                // Write to LogBox with Timestamp
                textBox_Log.AppendText(DateTime.Now.ToLongTimeString() + ": " + log + "\r\n");
            }
        }

        // We got a RAW Data trame. Proceed.
        private void ProcessRAWData(byte[] BytesRcvd)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new DataReceivedDelegate(ProcessRAWData), new Object[] { BytesRcvd });
            }
            else
            {
                ProcessCommand(BytesRcvd);
                // Display RAW Bytes (for debugging)
                // textBox_Log.AppendText(" >>> " + BitConverter.ToString(BytesRcvd).Replace("-", " ") + "\r\n");
            }
        }

        private void ProcessCommand(byte[] BytesCMD)
        {

            // The Command type we need to process is on byte 2
            switch (BytesCMD[2])
            {
                default:
                    textBox_Log.AppendText("Unknown Command: " + BitConverter.ToString(BytesCMD).Replace("-", " ") + "\r\n");
                    break;
                case 0xA0:
                    // General Status
                    GetGeneralInfos(BytesCMD);
                    UpdateUI();
                    break;
                case 0xA1:
                    // Full EEPROM Dump
                    GetEEPROMInfo(BytesCMD);
                    ProcessCOM.IsInitialized = true;
                    break;
                case 0xA2:
                    // Power Toggle
                    textBox_status.Text = "Device Power Cycle Toggled";
                    break;
                case 0xB0:
                    // All Settings Reset
                    textBox_status.Text = "Settings reset to Factory Default";
                    ProcessCOM.IsInitialized = false;
                    break;
                case 0xB1:
                    // Save Byte to EEPROM
                    textBox_status.Text = "Setting successfully saved to EEPROM";
                    textBox_Log.AppendText(DateTime.Now.ToLongTimeString() + ": 0x" + BitConverter.ToString(BytesCMD).Replace("-", " 0x") + "\r\n");
                    break;
                case 0xB2:
                    // Save 2-Byte to EEPROM
                    textBox_status.Text = "New Limits successfully saved to EEPROM";
                    textBox_Log.AppendText(DateTime.Now.ToLongTimeString() + ": 0x" + BitConverter.ToString(BytesCMD).Replace("-", " 0x") + "\r\n");
                    ProcessCOM.IsInitialized = false;
                    break;
            }
        }

        private void GetGeneralInfos(byte[] BytesCMD)
        {
            float adc;

            A2A.power_status = (((BytesCMD[4] >> 6) & 1) == 1) ? false : true;  // PE6
            A2A.power_good = (((BytesCMD[4] >> 3) & 1) == 1) ? true : false;    // PC7
            A2A.blowtype = (((BytesCMD[4] >> 4) & 1) == 1) ? true : false;      // PB4

            // BytesCMD[3] is the PINF register
            // Bit Order needed : 0b00-SW6(PB4)-SW5(PF7)-SW4(PF6)-SW3(PF5)-SW2(PF4)-SW1(PF1)
            A2A.DIPSwitch  = ((BytesCMD[3] & 0x02) >> 1); // PF1
            A2A.DIPSwitch |= ((BytesCMD[3] & 0x10) >> 3); // PF4
            A2A.DIPSwitch |= ((BytesCMD[3] & 0x20) >> 3); // PF5
            A2A.DIPSwitch |= ((BytesCMD[3] & 0x40) >> 3); // PF6
            A2A.DIPSwitch |= ((BytesCMD[3] & 0x80) >> 3); // PF7
            A2A.DIPSwitch |=  A2A.blowtype ? 0x20 : 0x00; // PB4
            A2A.DIPSwitch ^= 0x3F; // Flip bit (ON = 0 / OFF = 1 because pullups)

            // RAW EEPROM Order is MSB. SO we need to reverse bit order (flipping not needed)
            // Bit Order needed : 0b000-SW5(PF7)-SW4(PF6)-SW3(PF5)-SW2(PF4)-SW1(PF1)
            A2A.cset_index = (byte)((BytesCMD[3] & 0x02) << 3 | (BytesCMD[3] & 0x10) >> 1 | (BytesCMD[3] & 0x20) >> 3 | (BytesCMD[3] & 0x40) >> 5 | (BytesCMD[3] & 0x80) >> 7);

            if (A2A.cset_index + 1 <= eeprom_lim_5v.Length && A2A.cset_index + 1 <= eeprom_lim_12v.Length)
            {
                A2A.lim_5v = eeprom_lim_5v[A2A.cset_index] / 20.0;
                A2A.lim_12v = eeprom_lim_12v[A2A.cset_index] / 20.0;
            }

            // Compute from RAW ADC Values
            adc = (BytesCMD[5] << 8) | BytesCMD[6];
            A2A.cur_5v_amp = adc * 3.0 / 1024.0 / 50.0 / 0.005; // Raw ADC value * vref (3.00V) / 10 bit (1024) / Shunt Amp (x50) / Shunt Value (5 mOhm)
            if (A2A.cur_5v_amp > A2A.cur_5v_amp_max) { A2A.cur_5v_amp_max = A2A.cur_5v_amp; }

            adc = (BytesCMD[7] << 8) | BytesCMD[8];
            A2A.cur_12v_amp = adc * 3.0 / 1024.0 / 50.0 / 0.01;
            if (A2A.cur_12v_amp > A2A.cur_12v_amp_max) { A2A.cur_12v_amp_max = A2A.cur_12v_amp; }

            adc = (BytesCMD[9] << 8) | BytesCMD[10];
            A2A.cur_5v_volt = adc / 1024 * 3 / 10 * 25;
            if (A2A.cur_5v_volt > A2A.cur_5v_volt_max) { A2A.cur_5v_volt_max = A2A.cur_5v_volt; }

            adc = (BytesCMD[11] << 8) | BytesCMD[12];
            A2A.cur_12v_volt = adc / 1024 * 3 / 10 * 57;
            if (A2A.cur_12v_volt > A2A.cur_12v_volt_max) { A2A.cur_12v_volt_max = A2A.cur_12v_volt; }
        }

        private void GetEEPROMInfo(byte[] BytesCMD)
        {
            int tval;

            // Set FW Ver
            A2A.fw_ver = BytesCMD[4].ToString() + "." + BytesCMD[5].ToString(); 
            lb_fw.Text = A2A.fw_ver;

            // Set Screen Saver Timeout
            A2A.ssaver_timeout = BytesCMD[6];

            foreach (var item in cBox_ssaver.Items)
            {
                tval = Int32.Parse(Regex.Match(item.ToString(), @"\d+").Value);
                if(item.ToString().Contains("hour")) { tval *= 60;  }

                if(A2A.ssaver_timeout == tval) { cBox_ssaver.SelectedIndex = cBox_ssaver.Items.IndexOf(item); break; }
            }

            // Set SlowBlow Delay
            A2A.slowblow_ms = BytesCMD[7] * 10;

            foreach (var item in cBox_SbTimer.Items)
            {
                tval = Int32.Parse(Regex.Match(item.ToString(), @"\d+").Value);

                if (A2A.slowblow_ms == tval) { cBox_SbTimer.SelectedIndex = cBox_SbTimer.Items.IndexOf(item); break; }
            }

            // Just Default Value
            cBox_preset.SelectedIndex = 0;

            // Save all limits saved in EEPROM (32 slots) to later match with DIP Switch Settings
            Array.Copy(BytesCMD, 0x13, eeprom_lim_5v, 0, 32);
            Array.Copy(BytesCMD, 0x33, eeprom_lim_12v, 0, 32);

            #if DEBUG
            textBox_Log.AppendText(DateTime.Now.ToLongTimeString() + ": SSAVER TIMER = " + A2A.ssaver_timeout.ToString() + "\r\n");
            textBox_Log.AppendText(DateTime.Now.ToLongTimeString() + ": SLOWBLOW DELAY = " + A2A.slowblow_ms.ToString() + "\r\n");
            #endif
        }

        private void UpdateUI()
        {

            lb_atxpg.Text = A2A.power_good ? "Yes" : "No";
            lb_power.Text = A2A.power_status ? "ON" : "OFF";
            lb_blow.Text = A2A.blowtype ? "Slow" : "Fast";

            lb_5v_amp.Text = A2A.cur_5v_amp.ToString("F2") + " A";
            lb_12v_amp.Text = A2A.cur_12v_amp.ToString("F2") + " A";

            lb_5v_volt.Text = A2A.cur_5v_volt.ToString("F2") + " V";
            lb_12v_volt.Text = A2A.cur_12v_volt.ToString("F2") + " V";

            lb_5v_amp_max.Text = A2A.cur_5v_amp_max.ToString("F2") + " A";
            lb_12v_amp_max.Text = A2A.cur_12v_amp_max.ToString("F2") + " A";

            lb_5v_volt_max.Text = A2A.cur_5v_volt_max.ToString("F2") + " V";
            lb_12v_volt_max.Text = A2A.cur_12v_volt_max.ToString("F2") + " V";

            Btn_OnOff.Text = A2A.power_status ? "OFF" : "ON";

            if (A2A.Prev_DIPSwitch != A2A.DIPSwitch)
            {
                lim5v.Text = A2A.lim_5v.ToString("F2");
                lim12v.Text = A2A.lim_12v.ToString("F2");

                SetDIPSwitch(A2A.DIPSwitch);

                A2A.Prev_DIPSwitch = A2A.DIPSwitch;
            }

        }
    
        // 
        // Buttons Actions 
        //

        private void Btn_OnOff_Click(object sender, EventArgs e)
        {
            byte[] cmd = new byte[] { 0x56, 0x05, 0xA2, 0x00, 0x00 };

            cmd[3] = A2A.power_status ? (byte)0x00 : (byte)0x01;

            ProcessCOM.SendCommand(cmd);
        }

        private void Log_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            this.Height = (Log_checkBox.Checked) ? 566 : 407;
        }


        private void Btn_ResetMax_Click(object sender, EventArgs e)
        {
            A2A.cur_5v_volt_max = 0;
            A2A.cur_12v_volt_max = 0;
            A2A.cur_5v_amp_max = 0;
            A2A.cur_12v_amp_max = 0;

            UpdateUI();
        }

        private void Btn_Save_Ssaver_Click(object sender, EventArgs e)
        {
            int tval;

            byte[] cmd = new byte[] { 0x56, 0x06, 0xB1, 0x03, 0x00, 0x00 };

            if (cBox_ssaver.SelectedIndex != cBox_ssaver.Items.Count - 1)
            {
                tval = Int32.Parse(Regex.Match(cBox_ssaver.Text, @"\d+").Value);
                if (cBox_ssaver.Text.Contains("hour")) { tval *= 60; }
                cmd[4] = (byte)tval;
            }
            else
            {
                cmd[4] = 0xFF; // Screensaver is disabled if byte is 0xFF
            }

            ProcessCOM.SendCommand(cmd);
        }

        private void Btn_Save_SBDelay_Click(object sender, EventArgs e)
        {

            byte[] cmd = new byte[] { 0x56, 0x06, 0xB1, 0x04, 0x00, 0x00 };

            int tval = Int32.Parse(Regex.Match(cBox_SbTimer.Text, @"\d+").Value) / 10;

            cmd[4] = (byte)tval;

            ProcessCOM.SendCommand(cmd);
        }

        private void Btn_Fw_Reset_Click(object sender, EventArgs e)
        {
            byte[] cmd = new byte[] { 0x56, 0x04, 0xB0, 0x00 };

            ProcessCOM.SendCommand(cmd);
        }

        private void Btn_Save_Limits_Click(object sender, EventArgs e)
        {
            double newlim5v, newlim12v;

            byte[] cmd = new byte[] { 0x56, 0x07, 0xB2, 0x00, 0x00, 0x00, 0x00 };

            cmd[3] = (byte)(A2A.cset_index + 0x10);

            try
            {
                newlim5v = Convert.ToDouble(lim5v.Text.Replace(',','.'));
                newlim12v = Convert.ToDouble(lim12v.Text.Replace(',', '.'));
            }
            catch(Exception ex)
            {
                textBox_status.Text = "[ERROR] Invalid Numbers for Limits Field";
                textBox_Log.AppendText(DateTime.Now.ToLongTimeString() + ": [ERROR] " + ex.Message + "\r\n");
                A2A.Prev_DIPSwitch = -1;
                return;
            }

            // Check if new limits are within specs
            if(newlim5v < 0.05 || newlim12v < 0.05 || newlim5v > 8 || newlim12v > 4.75)
            {
                textBox_status.Text = "[ERROR] New Limits are out-of-range";
                textBox_Log.AppendText(DateTime.Now.ToLongTimeString() + ": " + textBox_status.Text + "\r\n");
                A2A.Prev_DIPSwitch = -1;
                return;
            }

            cmd[4] = (byte)(newlim5v * 20.0);
            cmd[5] = (byte)(newlim12v * 20.0);

            ProcessCOM.SendCommand(cmd);

            #if DEBUG
            textBox_Log.AppendText(DateTime.Now.ToLongTimeString() + ": [SAVE_LIM CMD] 0x" + BitConverter.ToString(cmd).Replace("-", " 0x") + "\r\n");
            #endif
        }

        //
        // Main Timer for auto-update
        //

        private void Timer1_Tick(object sender, EventArgs e)
        {
            byte[] cmd;

            if (!ProcessCOM.IsInitialized)
            {
                // First Loop, we need all EEPROM Settings (Command 0xA1)
                cmd = new byte[] { 0x56, 0x04, 0xA1, 0x00 };
            } else
            {
                // Subsequent Loop, we only need updated data (Command 0xA0)
                cmd = new byte[] { 0x56, 0x04, 0xA0, 0x00 };
            }

            ProcessCOM.SendCommand(cmd);
        }

        //
        // Various Functions
        //

        private void SetDIPSwitch(int DIPSet)
        {
            int xorg = dipswitch.Location.X;
            int yorg = dipswitch.Location.Y;

            toggle1.Location = new Point(xorg + 11, yorg + (((DIPSet & 1) == 1) ? 22 : 38));
            toggle2.Location = new Point(xorg + 28, yorg + ((((DIPSet >> 1) & 1) == 1) ? 22 : 38));
            toggle3.Location = new Point(xorg + 46, yorg + ((((DIPSet >> 2) & 1) == 1) ? 22 : 38));
            toggle4.Location = new Point(xorg + 64, yorg + ((((DIPSet >> 3) & 1) == 1) ? 22 : 38));
            toggle5.Location = new Point(xorg + 82, yorg + ((((DIPSet >> 4) & 1) == 1) ? 22 : 38));
            toggle6.Location = new Point(xorg + 101, yorg + ((((DIPSet >> 5) & 1) == 1) ? 22 : 38));
        }

    }
}
