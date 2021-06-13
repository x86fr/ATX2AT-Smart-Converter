using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;


namespace ATX2AT_Configuration_Tool
{
    public partial class FW_Upd : Form
    {
        private SerCOM ATX2ATCOM;

        public FW_Upd()
        {
            InitializeComponent();
        }

        private void FW_Upd_Load(object sender, EventArgs e)
        {
            ATX2ATCOM = new SerCOM();

            // Create Event Handlers
            ATX2ATCOM.ProcessLog += new SerCOM.NotifyLog(ProcessLog);
            //ATX2ATCOM.ProcessData += new SerCOM.NotifyBytes(ProcessRAWData);
        }

        private delegate void LogReceivedDelegate(string log);

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
                textBox1.AppendText(DateTime.Now.ToLongTimeString() + ": " + log + "\r\n");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            textBox1.AppendText("Searching for ATX2AT SC...\r\n");
            // Find UCA
            if (ATX2ATCOM.FindDevice())
            {
                textBox1.AppendText(" - FOUND: " + ATX2ATCOM.description + "\r\n");
            }
            else
            {
                textBox1.AppendText("ERROR: Failed to find ATX2AT SC\r\n");
                return;
            }

            // Put UCA on bootloader mode
            textBox1.AppendText("Reseting ATX2AT SC in bootloader mode...\r\n");

            serialPort1.PortName = ATX2ATCOM.COMPort;
            serialPort1.BaudRate = 1200;
            serialPort1.Open();
            Thread.Sleep(200);
            serialPort1.Close();
            // Find New bootloader COM mode
            textBox1.AppendText(" - Done !\r\n");

            serialPort1.BaudRate = 57600;

            textBox1.AppendText("Trying to find bootloader access...\r\n");
            Thread.Sleep(2500);


            // Find UCA
            if (ATX2ATCOM.FindDevice(true))
            {
                textBox1.AppendText(" - FOUND: " + ATX2ATCOM.description + "\r\n");
            }
            else
            {
                textBox1.AppendText("ERROR: Failed to access bootloader\r\n");
                Thread.Sleep(1750);
                textBox1.AppendText("Trying again...\r\n");
                if (ATX2ATCOM.FindDevice(true))
                {
                    textBox1.AppendText(" - FOUND: " + ATX2ATCOM.description + "\r\n");
                }
                else
                {

                    textBox1.AppendText("ERROR: Failed to access bootloader (again) :(\r\n");
                    return;
                }
            }

            textBox1.AppendText("Flashing firmware...\r\n");

            textBox1.AppendText("=======================================\r\n");

            // Upload firmware
            Process p = new Process();

            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
            p.StartInfo.FileName = "tools/avrdude.exe";
            p.StartInfo.Arguments = @"-Ctools\avrdude.conf -patmega32u4 -q -cavr109 -P" + ATX2ATCOM.COMPort + @" -b57600 -D -Uflash:w:tools\ucafw121.hex:i";

            textBox1.AppendText("Work DIR: " + p.StartInfo.WorkingDirectory + "\r\n");

            p.Start();

            while (!p.StandardError.EndOfStream || !p.StandardOutput.EndOfStream)
            {
                string line = p.StandardOutput.ReadLine();
                string line2 = p.StandardError.ReadLine();
                if (line != null) { textBox1.AppendText(line + "\r\n"); }
                if (line2 != null) { textBox1.AppendText(line2 + "\r\n"); }
            }

            textBox1.AppendText("=======================================\r\n");

            if (p.ExitCode == 0)
            {
                textBox1.AppendText("ATX2AT Firmware Successfully Updated!");
            }
            else
            {
                textBox1.AppendText("ATX2AT Firmware Update Failed. Error Code : " + p.ExitCode.ToString() + "\r\n");
            }
        }


    }
}
