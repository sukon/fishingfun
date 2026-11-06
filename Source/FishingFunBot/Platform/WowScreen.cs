using System.Drawing;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FishingFun
{
    public static class WowScreen
    {


        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, ref Rect rectangle);

        public struct Rect
        {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Right { get; set; }
            public int Bottom { get; set; }
        }

        private static Rectangle wowWindowdBounds = new Rectangle();

        public static Color GetColorAt(Point pos, Bitmap bmp)
        {
            return bmp.GetPixel(pos.X, pos.Y);
        }

        public static Bitmap GetBitmap()
        {
            var wowProcess = WowProcess.Get();

            if (wowProcess == null)
            {
                return new Bitmap(0, 0);
            }
            var handle = wowProcess.MainWindowHandle;

            var rect = new Rect();
            GetWindowRect(handle, ref rect);
            var bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            wowWindowdBounds = bounds;
            var result = new Bitmap(bounds.Width /2 , (bounds.Height / 2));

            using (var graphics = Graphics.FromImage(result))
            {
                graphics.CopyFromScreen(new Point(bounds.Left + bounds.Width /4, bounds.Top + bounds.Height /4), Point.Empty, bounds.Size);
            }

            return result;


            //var bmpScreen = new Bitmap(Screen.PrimaryScreen.Bounds.Width / 2, (Screen.PrimaryScreen.Bounds.Height / 2)-100);
            //var graphics = Graphics.FromImage(bmpScreen);
            //graphics.CopyFromScreen(Screen.PrimaryScreen.Bounds.Width / 4, Screen.PrimaryScreen.Bounds.Height / 4, 0, 0, bmpScreen.Size);
            //graphics.Dispose();
            //return bmpScreen;
        }

        public static Point GetScreenPositionFromBitmapPostion(Point pos)
        {
            return new Point(pos.X += wowWindowdBounds.Left + wowWindowdBounds.Width / 4, pos.Y += wowWindowdBounds.Top + wowWindowdBounds.Height / 4);
        }
    }
}