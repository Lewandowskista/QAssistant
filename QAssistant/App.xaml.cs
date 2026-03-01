// Copyright (C) 2026 Lewandowskista
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QAssistant.Services;
using System;
using System.Runtime.InteropServices;

namespace QAssistant
{
    public partial class App : Application
    {
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        public static IntPtr MainWindowHandle { get; private set; }
        public static MainWindow? MainWindowInstance { get; private set; }
        public static bool MinimizeToTray { get; set; } = false;
        public static AutomationApiService AutomationApi { get; } = new();

        // Win32 tray constants
        private const int WM_APP = 0x8000;
        private const int WM_TRAYICON = WM_APP + 1;
        private const int NIM_ADD = 0x00000000;
        private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_LBUTTONDBLCLK = 0x0203;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        private NOTIFYICONDATA _nid;
        private WndProcDelegate? _wndProcDelegate;
        private IntPtr _oldWndProc;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const int GWLP_WNDPROC = -4;

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hInstance, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string pszExeFileName, int nIconIndex);

        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const int ID_SHOW = 1001;
        private const int ID_HIDE = 1002;
        private const int ID_EXIT = 1003;

        public App()
        {
            this.InitializeComponent();
            // Load tray setting immediately so it's ready before window activates
            var trayEnabled = CredentialService.LoadCredential("MinimizeToTray");
            MinimizeToTray = trayEnabled == "true";

            // Start automation API if enabled
            var apiEnabled = CredentialService.LoadCredential("AutomationApiEnabled");
            if (apiEnabled == "true")
            {
                AutomationApiService.GetOrCreateApiKey();
                var portStr = CredentialService.LoadCredential("AutomationApiPort");
                int port = int.TryParse(portStr, out var p) && p is >= 1024 and <= 65535 ? p : 5248;
                AutomationApi.Start(port);
            }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            SetCurrentProcessExplicitAppUserModelID("QAssistant.App");

            m_window = new MainWindow();
            MainWindowInstance = (MainWindow)m_window;
            m_window.Activate();
            MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(m_window);
            MainWindowInstance.Activated += OnWindowFirstActivated;
        }

        private void OnWindowFirstActivated(object sender, WindowActivatedEventArgs e)
        {
            MainWindowInstance!.Activated -= OnWindowFirstActivated;
            SetupTrayIcon();
        }

        private void SetupTrayIcon()
        {
            try
            {
                // Subclass window to receive tray messages
                _wndProcDelegate = TrayWndProc;
                _oldWndProc = SetWindowLongPtr(MainWindowHandle, GWLP_WNDPROC, _wndProcDelegate);

                // Load default app icon
                IntPtr hInstance = GetModuleHandle(null);
                IntPtr hIcon = LoadIcon(hInstance, new IntPtr(32512));
                if (hIcon == IntPtr.Zero)
                    hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512));

                _nid = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = MainWindowHandle,
                    uID = 1,
                    uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                    uCallbackMessage = WM_TRAYICON,
                    hIcon = hIcon,
                    szTip = "QAssistant"
                };

                Shell_NotifyIcon(NIM_ADD, ref _nid);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tray setup error: {ex.Message}");
            }
        }

        private IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAYICON)
            {
                int mouseMsg = lParam.ToInt32() & 0xFFFF;

                if (mouseMsg == WM_LBUTTONDBLCLK)
                {
                    ShowMainWindow();
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    ShowTrayContextMenu();
                }
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        private void ShowTrayContextMenu()
        {
            GetCursorPos(out var pt);
            SetForegroundWindow(MainWindowHandle);

            var hMenu = CreatePopupMenu();
            AppendMenu(hMenu, MF_STRING, new IntPtr(ID_SHOW), "Show");
            AppendMenu(hMenu, MF_STRING, new IntPtr(ID_HIDE), "Hide");
            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);
            AppendMenu(hMenu, MF_STRING, new IntPtr(ID_EXIT), "Exit");

            int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                pt.X, pt.Y, 0, MainWindowHandle, IntPtr.Zero);

            DestroyMenu(hMenu);

            if (cmd == ID_SHOW) ShowMainWindow();
            else if (cmd == ID_HIDE) HideMainWindow();
            else if (cmd == ID_EXIT)
            {
                ExitApp();
            }
        }

        public static void ShowMainWindow()
        {
            MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                MainWindowInstance?.Show();
                MainWindowInstance?.Activate();
            });
        }

        public static void HideMainWindow()
        {
            MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                MainWindowInstance?.Hide();
            });
        }

        public void RemoveTrayIcon()
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
        }

        public static void ExitApp()
        {
            AutomationApi.Dispose();

            if (MainWindowInstance != null)
                ((App)Application.Current).RemoveTrayIcon();

            NotificationService.Instance.Unregister();

            Application.Current.Exit();
        }

        private Window? m_window;
    }
}