using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Q9CS_CrossPlatform
{
    public class LinuxKeyboardHook : IKeyboardHook
    {
        private const int O_RDONLY = 0;
        private const int EVIOCGRAB = unchecked((int)0x40044590);
        private const int EVIOCGBIT = unchecked((int)0x80FF4520); // Fixed: proper EVIOCGBIT macro
        private const int EVIOCGNAME = unchecked((int)0x81004506); // Get device name
        private const int EV_KEY = 0x01;
        private const int KEY_EVENT_SIZE = 24;
        private const int EV_MAX = 0x1f;
        private const int KEY_MAX = 0x2ff;
        
        private bool _isInstalled = false;
        private bool _disposed = false;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _eventLoopTask;
        private int _deviceFd = -1;
        private string _devicePath = "/dev/input/event0";

        // Key mapping from Linux key codes to Windows VK codes
        private static readonly Dictionary<int, int> _keyCodeMapping = new Dictionary<int, int>
        {
            // Numpad keys (Linux keycode -> Windows VK code)
            { 82, 0x60 },  // KP_0 -> VK_NUMPAD0
            { 79, 0x61 },  // KP_1 -> VK_NUMPAD1
            { 80, 0x62 },  // KP_2 -> VK_NUMPAD2
            { 81, 0x63 },  // KP_3 -> VK_NUMPAD3
            { 75, 0x64 },  // KP_4 -> VK_NUMPAD4
            { 76, 0x65 },  // KP_5 -> VK_NUMPAD5
            { 77, 0x66 },  // KP_6 -> VK_NUMPAD6
            { 71, 0x67 },  // KP_7 -> VK_NUMPAD7
            { 72, 0x68 },  // KP_8 -> VK_NUMPAD8
            { 73, 0x69 },  // KP_9 -> VK_NUMPAD9
            { 83, 0x6E },  // KP_Decimal -> VK_DECIMAL
            { 98, 0x6F },  // KP_Divide -> VK_DIVIDE
            { 55, 0x6A },  // KP_Multiply -> VK_MULTIPLY
            { 74, 0x6D },  // KP_Subtract -> VK_SUBTRACT
            { 78, 0x6B },  // KP_Add -> VK_ADD

            // Function keys
            { 68, 0x79 },  // F10 -> VK_F10

            // Letter keys for alternative input
            { 16, 0x51 },  // q -> Q
            { 17, 0x57 },  // w -> W
            { 18, 0x45 },  // e -> E
            { 19, 0x52 },  // r -> R
            { 31, 0x53 },  // s -> S
            { 32, 0x44 },  // d -> D
            { 33, 0x46 },  // f -> F
            { 34, 0x47 },  // g -> G
            { 44, 0x5A },  // z -> Z
            { 45, 0x58 },  // x -> X
            { 46, 0x43 },  // c -> C
            { 47, 0x56 },  // v -> V
            { 48, 0x42 },  // b -> B
            { 30, 0x41 },  // a -> A
            { 20, 0x54 },  // t -> T
        };

        public event KeyboardHookHandler KeyDown;

        public LinuxKeyboardHook()
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Install()
        {
            if (_isInstalled || _disposed)
                return;

            try
            {
                // Find the appropriate input device
                _devicePath = FindKeyboardDevice();
                if (string.IsNullOrEmpty(_devicePath))
                {
                    throw new InvalidOperationException("No keyboard device found");
                }

                Log.Information($"Using keyboard device: {_devicePath}");

                // Open the device
                _deviceFd = OpenDevice(_devicePath);
                if (_deviceFd == -1)
                {
                    int errno = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"Failed to open device: {_devicePath}, errno: {errno}");
                }

                // Try to grab the device for exclusive access (optional)
                if (GrabDevice(_deviceFd) != 0)
                {
                    Log.Warning("Failed to grab device - continuing without exclusive access");
                }

                // Start event loop
                _eventLoopTask = Task.Run(EventLoop, _cancellationTokenSource.Token);
                _isInstalled = true;

                Log.Information("Linux keyboard hook installed successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to install Linux keyboard hook");
                Uninstall();
                throw;
            }
        }

        public void Uninstall()
        {
            if (!_isInstalled)
                return;

            try
            {
                // Cancel event loop
                _cancellationTokenSource?.Cancel();
                
                // Wait for event loop to finish
                if (_eventLoopTask != null)
                {
                    try
                    {
                        _eventLoopTask.Wait(2000); // Wait up to 2 seconds
                    }
                    catch (AggregateException)
                    {
                        // Task was cancelled, which is expected
                    }
                }

                // Release and close device
                if (_deviceFd != -1)
                {
                    ReleaseDevice(_deviceFd);
                    CloseDevice(_deviceFd);
                    _deviceFd = -1;
                }

                _isInstalled = false;
                Log.Information("Linux keyboard hook uninstalled");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error uninstalling Linux keyboard hook");
            }
        }

        private void EventLoop()
        {
            try
            {
                Log.Debug("Event loop started");
                
                byte[] buffer = new byte[KEY_EVENT_SIZE];
                
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        // Use select or poll to check if data is available (non-blocking approach)
                        if (!IsDataAvailable(_deviceFd, 100)) // 100ms timeout
                            continue;

                        // Read event from device
                        int bytesRead = ReadDevice(_deviceFd, buffer, buffer.Length);
                        if (bytesRead == KEY_EVENT_SIZE)
                        {
                            ProcessEvent(buffer);
                        }
                        else if (bytesRead == -1)
                        {
                            int errno = Marshal.GetLastWin32Error();
                            if (errno == 4) // EINTR - interrupted system call
                                continue;
                            
                            Log.Error($"Read error: errno {errno}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error reading from device");
                        break;
                    }
                }
                
                Log.Debug("Event loop ended");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in Linux keyboard hook event loop");
            }
        }

        private void ProcessEvent(byte[] buffer)
        {
            try
            {
                // Parse input_event structure (different on 32/64 bit systems)
                // For simplicity, assuming 64-bit system
                ulong timeSec = BitConverter.ToUInt64(buffer, 0);
                ulong timeUsec = BitConverter.ToUInt64(buffer, 8);
                ushort eventType = BitConverter.ToUInt16(buffer, 16);
                ushort keyCode = BitConverter.ToUInt16(buffer, 18);
                int value = BitConverter.ToInt32(buffer, 20);

                if (eventType == EV_KEY)
                {
                    bool isKeyUp = value == 0;
                    bool isKeyDown = value == 1;
                    bool isKeyRepeat = value == 2;
                    
                    // Only process key down and key up events
                    if (!isKeyDown && !isKeyUp)
                        return;

                    Log.Debug($"Key event: code={keyCode}, value={value}, isKeyUp={isKeyUp}, isKeyDown={isKeyDown}");
                    
                    // Map Linux key code to Windows VK code
                    if (_keyCodeMapping.TryGetValue(keyCode, out int vkCode))
                    {
                        Log.Debug($"Mapped Linux keycode {keyCode} to VK code 0x{vkCode:X2}");
                        
                        try
                        {
                            bool shouldBlock = KeyDown?.Invoke(vkCode, isKeyUp) ?? false;
                            
                            if (shouldBlock)
                            {
                                Log.Debug($"Blocked key: {vkCode} (Linux: {keyCode})");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Error in KeyDown event handler");
                        }
                    }
                    else
                    {
                        Log.Debug($"Unmapped Linux keycode: {keyCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing Linux keyboard event");
            }
        }

        private string FindKeyboardDevice()
        {
            try
            {
                if (!Directory.Exists("/dev/input"))
                {
                    Log.Error("/dev/input directory not found");
                    return null;
                }

                // Look for keyboard devices in /dev/input/
                string[] deviceFiles = Directory.GetFiles("/dev/input", "event*");
                
                foreach (string deviceFile in deviceFiles)
                {
                    if (IsKeyboardDevice(deviceFile))
                    {
                        Log.Information($"Found keyboard device: {deviceFile}");
                        return deviceFile;
                    }
                }
                
                Log.Warning("No keyboard device found, trying default: /dev/input/event0");
                if (File.Exists("/dev/input/event0"))
                    return "/dev/input/event0";
                    
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error finding keyboard device");
                return null;
            }
        }

        private bool IsKeyboardDevice(string devicePath)
        {
            int fd = -1;
            try
            {
                // Check if we can open the device
                fd = OpenDevice(devicePath);
                if (fd == -1) 
                {
                    Log.Debug($"Cannot open device: {devicePath}");
                    return false;
                }

                // Get device name
                byte[] nameBuffer = new byte[256];
                if (ioctl(fd, EVIOCGNAME, nameBuffer) >= 0)
                {
                    string deviceName = Encoding.UTF8.GetString(nameBuffer).TrimEnd('\0');
                    Log.Debug($"Device {devicePath}: {deviceName}");
                    
                    // Check if it's likely a keyboard based on name
                    string lowerName = deviceName.ToLower();
                    if (lowerName.Contains("keyboard") || lowerName.Contains("kbd"))
                    {
                        return true;
                    }
                }

                // Check device capabilities - see if it supports key events
                byte[] evBits = new byte[32]; // EV_MAX / 8 + 1
                if (ioctl(fd, EVIOCGBIT | (0 << 8), evBits) >= 0)
                {
                    // Check if EV_KEY bit is set
                    if ((evBits[EV_KEY / 8] & (1 << (EV_KEY % 8))) != 0)
                    {
                        Log.Debug($"Device {devicePath} supports key events");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, $"Error checking device {devicePath}");
                return false;
            }
            finally
            {
                if (fd != -1)
                    CloseDevice(fd);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Uninstall();
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }

        #region Linux P/Invoke declarations

        [DllImport("libc.so.6", SetLastError = true)]
        private static extern int open(string pathname, int flags);

        [DllImport("libc.so.6", SetLastError = true)]
        private static extern int close(int fd);

        [DllImport("libc.so.6", SetLastError = true)]
        private static extern int read(int fd, byte[] buf, int count);

        [DllImport("libc.so.6", SetLastError = true)]
        private static extern int ioctl(int fd, int request, int arg);

        [DllImport("libc.so.6", SetLastError = true)]
        private static extern int ioctl(int fd, int request, byte[] arg);

        [DllImport("libc.so.6", SetLastError = true)]
        private static extern int select(int nfds, IntPtr readfds, IntPtr writefds, IntPtr exceptfds, ref TimeVal timeout);

        [StructLayout(LayoutKind.Sequential)]
        private struct TimeVal
        {
            public int tv_sec;
            public int tv_usec;
        }

        private static int OpenDevice(string path)
        {
            return open(path, O_RDONLY);
        }

        private static void CloseDevice(int fd)
        {
            close(fd);
        }

        private static int ReadDevice(int fd, byte[] buffer, int count)
        {
            return read(fd, buffer, count);
        }

        private static int GrabDevice(int fd)
        {
            return ioctl(fd, EVIOCGRAB, 1);
        }

        private static int ReleaseDevice(int fd)
        {
            return ioctl(fd, EVIOCGRAB, 0);
        }

        private static bool IsDataAvailable(int fd, int timeoutMs)
        {
            try
            {
                // Simple approach: just return true and let read() handle blocking
                // In a production system, you'd want to use select() or poll()
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}