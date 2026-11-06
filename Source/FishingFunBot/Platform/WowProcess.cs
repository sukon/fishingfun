using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

#nullable enable
namespace FishingFun
{
    public static class WowProcess
    {
        public static ILog logger = LogManager.GetLogger("Fishbot");

        private const UInt32 WM_KEYDOWN = 0x0100;
        private const UInt32 WM_KEYUP = 0x0101;
        private static ConsoleKey lastKey;
        private static Random random = new Random();

        /// <summary>
        /// Time to wait after the right mouse button has been released before the cursor is
        /// moved back to where the user left it. The game samples the mouse position once
        /// per frame, so moving the cursor in the same frame as the button release makes the
        /// game apply that movement as a look (camera rotation) while the right button is
        /// still considered to be down.
        /// </summary>
        private const int CursorRestoreDelayMs = 150;

        public static bool IsWowClassic()
        {
            var wowProcess = Get();
            return wowProcess != null ? wowProcess.ProcessName.ToLower().Contains("classic") : false; ;
        }

        //Get the wow-process, if success returns the process else null
        public static Process? Get(string name = "")
        {
            var names = string.IsNullOrEmpty(name) ? new List<string> { "Wow", "WowClassic", "Wow-64" } : new List<string> { name };

            var processList = Process.GetProcesses();
            foreach (var p in processList)
            {
                if (names.Contains(p.ProcessName))
                {
                    return p;
                }
            }

            logger.Error($"Failed to find the wow process, tried: {string.Join(", ", names)}");

            return null;
        }

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, UInt32 Msg, int wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ScreenToClient(IntPtr hWnd, ref System.Drawing.Point lpPoint);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;

        /// <summary>
        /// Time in milliseconds to wait after the cursor has been moved onto the bobber and
        /// before the right mouse button is pressed.
        /// 
        /// The game rotates the camera when it sees the mouse move while the right mouse
        /// button is down. Moving the cursor generates a mouse move message that the game
        /// reads on one of its own frames, so the cursor has to be moved and the game given
        /// time to read that movement before the button is pressed. If the game reads the
        /// movement while the button is down it applies it as a camera rotation and the
        /// view swings away from the bobber.
        /// 
        /// Raise this value if the camera still moves. See issue #44 "Camera going wild"
        /// and the discussion on issue #13.
        /// </summary>
        public static int LootDelay = 1000;

        /// <summary>
        /// Builds the lParam for a mouse message from a screen position. The mouse
        /// messages need the position in client coordinates, otherwise the click is
        /// reported at the top left corner of the window.
        /// </summary>
        private static IntPtr MakeClickLParam(IntPtr windowHandle, System.Drawing.Point screenPosition)
        {
            var clientPosition = screenPosition;
            ScreenToClient(windowHandle, ref clientPosition);

            return unchecked((IntPtr)((clientPosition.Y << 16) | (clientPosition.X & 0xFFFF)));
        }

        /// <summary>
        /// True when the user is physically holding a mouse button down. Injecting a
        /// click at the same time as real input confuses the game.
        /// </summary>
        private static bool IsMouseButtonHeldDown()
        {
            const short keyDownMask = unchecked((short)0x8000);

            return (GetAsyncKeyState(VK_LBUTTON) & keyDownMask) != 0
                || (GetAsyncKeyState(VK_RBUTTON) & keyDownMask) != 0;
        }

        /// <summary>
        /// Moves the cursor to the given screen position. This must only be done while no
        /// mouse button is down, see <see cref="LootDelay"/>.
        /// </summary>
        private static void MoveCursorTo(System.Drawing.Point position)
        {
            SetCursorPos(position.X, position.Y);
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out uint ProcessId);

        static Process GetActiveProcess()
        {
            IntPtr hwnd = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return Process.GetProcessById((int)pid);
        }

        private static void KeyDown(ConsoleKey key)
        {
            lastKey = key;
            var wowProcess = Get();
            if (wowProcess != null)
            {
                PostMessage(wowProcess.MainWindowHandle, WM_KEYDOWN, (int)key, IntPtr.Zero);
            }
        }

        private static void KeyUp()
        {
            KeyUp(lastKey);
        }

        public static void PressKey(ConsoleKey key)
        {
            KeyDown(key);
            Thread.Sleep(50 + random.Next(0, 75));
            KeyUp(key);
        }

        public static void KeyUp(ConsoleKey key)
        {
            var wowProcess = Get();
            if (wowProcess != null)
            {
                PostMessage(wowProcess.MainWindowHandle, WM_KEYUP, (int)key, IntPtr.Zero);
            }
        }

        public static void RightClickMouse(ILog logger, System.Drawing.Point position)
        {
            var activeProcess = GetActiveProcess();
            var wowProcess = WowProcess.Get();
            if (wowProcess != null)
            {
                // If the user is holding a mouse button down the game is getting real
                // input from them. Mixing that with an injected click makes the game
                // behave unpredictably, so skip the click in that case.
                if (IsMouseButtonHeldDown())
                {
                    logger.Info("Mouse button is held down by the user, skipping the loot click.");
                    return;
                }

                var oldPosition = System.Windows.Forms.Cursor.Position;

                var lParam = MakeClickLParam(wowProcess.MainWindowHandle, position);

                // Send a button up before anything else. If the game still thinks the
                // right button is down from an earlier click it treats any mouse movement
                // as a look and the camera swings away.
                PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, lParam);

                // Move the cursor onto the bobber and give the game time to read that
                // movement before the button is pressed. The game rotates the camera when
                // it sees the mouse move while the right mouse button is down, which is
                // what makes the view swing wildly when the fish bites.
                MoveCursorTo(position);
                Thread.Sleep(LootDelay);

                bool buttonPressed = PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONDOWN, Keys.VK_RMB, lParam);
                if (!buttonPressed)
                {
                    logger.Error("Failed to send the right mouse button down message.");
                }

                Thread.Sleep(30 + random.Next(0, 47));

                // The button must always be released, otherwise the game keeps the right
                // button held down and the camera keeps moving with the mouse.
                if (!PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, lParam))
                {
                    logger.Error("Failed to send the right mouse button up message.");
                }

                // Give the game time to process the button release before the cursor is
                // moved back, otherwise that movement is also seen as a camera rotation.
                Thread.Sleep(CursorRestoreDelayMs);

                RefocusOnOldScreen(logger, activeProcess, wowProcess, oldPosition);
            }
        }

        public static void RightClickMouse()
        {
            var activeProcess = GetActiveProcess();
            var wowProcess = WowProcess.Get();
            if (wowProcess != null)
            {
                var oldPosition = System.Windows.Forms.Cursor.Position;
                PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONDOWN, Keys.VK_RMB, IntPtr.Zero);
                Thread.Sleep(30 + random.Next(0, 47));
                PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, IntPtr.Zero);
            }
        }

        private static void RefocusOnOldScreen(ILog logger, Process activeProcess, Process wowProcess, System.Drawing.Point oldPosition)
        {
            try
            {
                if (activeProcess.MainWindowTitle != wowProcess.MainWindowTitle)
                {
                    // get focus back on this screen
                    PostMessage(activeProcess.MainWindowHandle, Keys.WM_RBUTTONDOWN, Keys.VK_RMB, IntPtr.Zero);
                    Thread.Sleep(30);
                    PostMessage(activeProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, IntPtr.Zero);

                    KeyDown(ConsoleKey.Escape);
                    Thread.Sleep(30);
                    KeyUp(ConsoleKey.Escape);

                    // Move the cursor back only after the button release has been processed,
                    // otherwise that movement is also seen as a camera rotation.
                    MoveCursorTo(oldPosition);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message);
            }
        }
    }
}