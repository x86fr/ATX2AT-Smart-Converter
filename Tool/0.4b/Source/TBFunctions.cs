using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace ATX2AT_Configuration_Tool
{
    public class TBFunctions
    {

        public byte ComputeChecksum(byte[] data)
        {
            byte chksum = 0x00;

            for (int i = 0; i < data.Length - 1; i++) { chksum ^= data[i]; }

            return chksum;
        }

        public List<int> SearchBytePattern(byte[] needle, byte[] haystack)
        {
            List<int> positions = new List<int>();
            int patternLength = needle.Length;
            int totalLength = haystack.Length;
            byte firstMatchByte = needle[0];
            for (int i = 0; i < totalLength; i++)
            {
                if (firstMatchByte == haystack[i] && totalLength - i >= patternLength)
                {
                    byte[] match = new byte[patternLength];
                    Array.Copy(haystack, i, match, 0, patternLength);
                    if (match.SequenceEqual<byte>(needle))
                    {
                        positions.Add(i + patternLength); // Report position AFTER the delimiter (remove patternLength to include delimiter)
                        i += patternLength - 1;
                    }
                }
            }
            return positions;
        }

        public int SearchBytePatternSingle(byte[] needle, byte[] haystack, int offset = 0)
        {
            int c = haystack.Length - needle.Length + 1;
            int j;

            for (int i = offset; i < c; i++)
            {
                if (haystack[i] != needle[0]) continue;
                for (j = needle.Length - 1; j >= 1 && haystack[i + j] == needle[j]; j--) ;
                if (j == 0) return i;
            }
            return -1;
        }


        public string Byte2String(byte[] bytes)
        {
            char[] c = new char[bytes.Length * 2];
            int b;
            for (int i = 0; i < bytes.Length; i++)
            {
                b = bytes[i] >> 4;
                c[i * 2] = (char)(55 + b + (((b - 10) >> 31) & -7));
                b = bytes[i] & 0xF;
                c[i * 2 + 1] = (char)(55 + b + (((b - 10) >> 31) & -7));
            }
            return new string(c);
        }

        public string Byte2String(byte bytes)
        {
            char[] c = new char[2];
            int b;

            b = bytes >> 4;
            c[0] = (char)(55 + b + (((b - 10) >> 31) & -7));
            b = bytes & 0xF;
            c[1] = (char)(55 + b + (((b - 10) >> 31) & -7));

            return new string(c);
        }

    }



    public class MyLovelyProgressBar : ProgressBar
    {
        public MyLovelyProgressBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        private string foregroundText;
        public string ForegroundText
        {
            get { return foregroundText; }
            set
            {
                if (foregroundText != value)
                {
                    Invalidate();
                    foregroundText = value;
                }
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams result = base.CreateParams;
                result.ExStyle |= 0x02000000; // WS_EX_COMPOSITED 
                return result;
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            switch (m.Msg)
            {
                case 15: //WmPaint
                    using (var graphics = Graphics.FromHwnd(Handle))
                        PaintForeGroundText(graphics);
                    break;
            }
        }

        private void PaintForeGroundText(Graphics graphics)
        {
            if (!string.IsNullOrEmpty(ForegroundText))
            {
                var size = graphics.MeasureString(ForegroundText, this.Font);
                var point = new PointF(Width / 2.0F - size.Width / 2.0F, Height / 2.0F - size.Height / 2.0F);
                graphics.DrawString(ForegroundText, this.Font, new SolidBrush(Color.Black), point);
            }
        }
    }
}
