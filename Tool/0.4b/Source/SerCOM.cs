/*
 *  A generic Serial Communication Class I wrote for the Universal Chip Analyzer
 *  and reused for the ATX2AT Smart Converter. Reuse it for your own project!
 *  -------
 *  S.DEMEULEMEESTER 2020
 *  
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ATX2AT_Configuration_Tool
{

    class SerCOM
    {
        // Some constant specific for the ATX2AT Smart Converter
        const byte HeaderByte = 0x56;
        const string ProductName = "ATX2AT Smart Converter";
        const string Product_VID = "2341";
        const string Product_DID = "8037";
        const string Bootloader_VID = "2341";
        const string Bootloader_DID = "0037";
        const int MaxBufferSize = 256;

        // Below is quite generic code.
        public delegate void NotifyBytes(byte[] data);
        public event NotifyBytes ProcessData;

        public delegate void NotifyLog(string log);
        public event NotifyLog ProcessLog;

        public string COMPort = "";
        public bool IsConnected = false;
        public bool IsInitialized = false;
        public string description = ProductName;

        private readonly TBFunctions TBF = new TBFunctions();

        readonly byte[] RAWInputBuffer = new byte[MaxBufferSize];
        int BytesRead = 0;

        readonly SerialPort USBCOM = new SerialPort()
        {
            DtrEnable = true,
            RtsEnable = true
         };

        struct ComPort
        {
            public string name;
            public string vid;
            public string pid;
            public string description;
            public bool IsDetected;
            public bool IsBootloader;
        }

        private static List<ComPort> GetCOMPorts()
        {
            ManagementObjectSearcher UCAPortSearcher = new ManagementObjectSearcher("SELECT * FROM WIN32_SerialPort");

            using (UCAPortSearcher)
            {
                var ports = UCAPortSearcher.Get().Cast<ManagementBaseObject>().ToList();
                return ports.Select(p =>
                {
                    ComPort c = new ComPort();
                    string vidPattern = @"VID_([0-9A-F]{4})";
                    string pidPattern = @"PID_([0-9A-F]{4})";

                    c.name = p.GetPropertyValue("DeviceID").ToString();
                    c.vid = p.GetPropertyValue("PNPDeviceID").ToString();
                    c.description = p.GetPropertyValue("Caption").ToString();

                    Match mVID = Regex.Match(c.vid, vidPattern, RegexOptions.IgnoreCase);
                    Match mPID = Regex.Match(c.vid, pidPattern, RegexOptions.IgnoreCase);

                    if (mVID.Success)
                        c.vid = mVID.Groups[1].Value;
                    if (mPID.Success)
                        c.pid = mPID.Groups[1].Value;

                    if (c.vid == Product_VID && c.pid == Product_DID) { c.IsDetected = true; } else { c.IsDetected = false; }
                    if (c.vid == Bootloader_VID && c.pid == Bootloader_DID) { c.IsBootloader = true; } else { c.IsBootloader = false; }

                    return c;

                }).ToList();
            }
        }

        public bool FindDevice(bool bootloader = false)
        {
            List<ComPort> ports = GetCOMPorts();

            foreach (ComPort testCOM in ports)
            {
                if (!bootloader && testCOM.IsDetected)
                {
                    COMPort = testCOM.name;
                    ProcessLog("Found " + ProductName + " [" + COMPort + "]");
                    return true;
                }
                if (bootloader && testCOM.IsBootloader)
                {
                    COMPort = testCOM.name;
                    ProcessLog("Found " + ProductName + " *BOOTLOADER* [" + COMPort + "]");
                    return true;
                }
            }
            ProcessLog(ProductName + " Not Found!");
            return false;
        }

        public void ConnectToDevice()
        {
            FindDevice();

            if (!USBCOM.IsOpen)
            {
                try
                {
                    USBCOM.DataReceived += new SerialDataReceivedEventHandler(ProcessSerialBytes);
                    USBCOM.PortName = COMPort;
                    USBCOM.Open();
                    ProcessLog("Connection Successful");
                    IsConnected = true;
                } catch (Exception ex)
                {
                  ProcessLog("Failed to Open COM Port: " + ex.ToString());
                }
            }

        }

        public void CloseConnection()
        {
            // Close Connection
            if (USBCOM.IsOpen)
            {
                USBCOM.DiscardInBuffer();
                USBCOM.DiscardOutBuffer();
                USBCOM.Close();
            }
        }

        public void SendCommand(byte[] cmd)
        {
            // Compute Checksum and add it as last byte
            cmd[cmd.Length - 1] = TBF.ComputeChecksum(cmd);

            //Send Command
            USBCOM.Write(cmd, 0, cmd.Length);

        }

        void ProcessSerialBytes(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int BytesToRead = USBCOM.BytesToRead;

                USBCOM.Read(RAWInputBuffer, BytesRead, BytesToRead);

                BytesRead += BytesToRead;

                // We have a full frame with the correct number of bytes. Proceed
                if (RAWInputBuffer[0] == 0x56 && BytesRead == RAWInputBuffer[1])
                {
                    // Crop buffer to the correct lenght
                    byte[] CleanBuffer = new byte[BytesRead];
                    Array.Copy(RAWInputBuffer, 0, CleanBuffer, 0, BytesRead);

                    // Checksum correct ?
                    if (CleanBuffer[BytesRead-1] == TBF.ComputeChecksum(CleanBuffer))
                    {
                        // Yes! Process Data
                        ProcessData(CleanBuffer);
                    }  else
                    {
                        // Nope. Reject
                        ProcessLog("Wrong checksum Received\r\n");
                    }

                    // Clean RAW Input buffer for next command
                    Array.Clear(RAWInputBuffer, 0x00, RAWInputBuffer.Length);
                    BytesRead = 0;
                }
                else if (RAWInputBuffer[0] != HeaderByte)
                {
                    // We don't have a correct header. Guess it's a debug string. Report as is. 
                    ProcessLog("Wrong header: \r\n" + Encoding.Default.GetString(RAWInputBuffer) + "\r\n");
                    Array.Clear(RAWInputBuffer, 0x00, RAWInputBuffer.Length);
                    BytesRead = 0;
                }
                else if (BytesRead > RAWInputBuffer[1])
                {
                    // Buffer overrun
                    ProcessLog("Buffer Overrun!\r\n");
                    Array.Clear(RAWInputBuffer, 0x00, RAWInputBuffer.Length);
                    BytesRead = 0;
                }
            }
            catch (SystemException ex)
            {
                ProcessLog("Data Receive Failed: " + ex.ToString());
            }
        }

    }
}
