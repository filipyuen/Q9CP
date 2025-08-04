using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Microsoft.Data.Sqlite;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using WindowsInput;
using WindowsInput.Native;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Text.Json;

namespace Q9CS_CrossPlatform
{
    public class WindowSizeConfig
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public WindowState WindowState { get; set; }
    }
    public enum q9command
    {
        cancel,
        prev,
        next,
        homo,
        openclose,
        relate,
        shortcut
    }

    public partial class MainWindow : Window
    {
        private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private const double AspectRatio = 280.0 / 350.0; // 4:5 ratio
        private bool _isProgrammaticResize = false; // Flag to prevent recursive resizing
        private TextBox _inputBox;
        private TextBlock _statusBlock;
        private Button[] _candidateButtons;
        private readonly CodeTable _codeTable;
        private readonly IInputSimulator _inputSimulator;
        private List<string> _currentCandidates = new List<string>();
        private int _currentPage = 0;
        private const int CandidatesPerPage = 9;
        private string _currCode = "";
        private bool _selectMode = false;
        private bool _homo = false;
        private bool _openclose = false;
        private string _lastWord = "";
        private string _statusPrefix = "";
        private bool _scOutput = false; // Simplified Chinese toggle
        private bool _useNumpad = true; // Numpad toggle
        private Dictionary<int, Bitmap> _images = new Dictionary<int, Bitmap>();
        private bool _isHidden = false;
        private Dictionary<char, int> _altKeyMapping = new Dictionary<char, int>();

        // Global hotkey monitoring
        private Timer _globalHotkeyTimer;
        private bool _f10WasPressed = false;

        // Global keyboard hook
        private GlobalKeyboardHook _globalHook;
        private bool _globalInputEnabled = true; // Toggle for global input capture

        // Font detection
        private string _preferredFontFamily = "Microsoft YaHei";

        private void ToggleInputMode_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ToggleNumpadMode();
        }
        private void LoadWindowSize()
        {
            var screen = this.Screens.Primary.WorkingArea;
            this.Position = new PixelPoint(
                screen.Width - (int)this.Width,
                (screen.Height - (int)this.Height) / 2);
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<WindowSizeConfig>(json);
                    if (config != null)
                    {
                        // Ensure minimum constraints
                        this.Width = Math.Max(config.Width, this.MinWidth);
                        this.Height = Math.Max(config.Height, this.MinHeight);
                        var primaryScreen = this.Screens.Primary.WorkingArea;
                        int x = Math.Clamp(config.X, primaryScreen.X, primaryScreen.X + primaryScreen.Width - (int)this.Width);
                        int y = Math.Clamp(config.Y, primaryScreen.Y, primaryScreen.Y + primaryScreen.Height - (int)this.Height);
                        this.Position = new PixelPoint(x, y);
                        this.WindowState = WindowState.Normal; // Always start in Normal state
                        this.Topmost = true;
                    }
                    else
                    {
                        SetDefaultSizeAndPosition();
                    }
                }
                else
                {
                    SetDefaultSizeAndPosition();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load window size from config.json. Using defaults.");
                SetDefaultSizeAndPosition();
            }
        }
        private void SetDefaultSizeAndPosition()
        {
            this.Width = 280;
            this.Height = 350;
            var screen = this.Screens.Primary.WorkingArea;
            this.Position = new PixelPoint(
                screen.Width - (int)this.Width,
                (screen.Height - (int)this.Height) / 2);
            this.WindowState = WindowState.Normal;
            this.Topmost = true; // Ensure window remains topmost
            Log.Information($"Set default window size and position: Width=280, Height=350, X={this.Position.X}, Y={this.Position.Y}, Topmost={this.Topmost}");
        }
        private void SaveWindowSize()
        {
            // Skip saving if maximized or width >= screen width
            double screenWidth = this.Screens.Primary.WorkingArea.Width;
            if (this.WindowState == WindowState.Maximized || this.Width >= screenWidth)
            {               
                return;
            }

            try
            {
                var config = new WindowSizeConfig
                {
                    Width = this.Width,
                    Height = this.Height,
                    X = this.Position.X,
                    Y = this.Position.Y
                };
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save window size to config.json.");
            }
        }
        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (_isProgrammaticResize) return; // Prevent recursive calls

            // Skip if maximized or width >= screen width
            double screenWidth = this.Screens.Primary.WorkingArea.Width;
            if (this.WindowState == WindowState.Maximized || e.NewSize.Width >= screenWidth)
            {
                return;
            }

            // Maintain aspect ratio (width / height = 280 / 350 = 0.8)
            _isProgrammaticResize = true;
            try
            {
                double expectedHeight = e.NewSize.Width / AspectRatio;
                if (Math.Abs(e.NewSize.Height - expectedHeight) > 1)
                {
                    this.Height = Math.Max(expectedHeight, this.MinHeight);
                }
                SaveWindowSize(); // Save size after manual resize
            }
            finally
            {
                _isProgrammaticResize = false;
            }
        }

        private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty && this.WindowState == WindowState.Maximized)
            {
                _isProgrammaticResize = true;
                try
                {                    
                    // First, set to Normal state
                    this.WindowState = WindowState.Normal;
                    
                    // Load the saved size
                    LoadWindowSize();
                    
                    // Use Dispatcher to ensure Topmost is set after the window state change is complete
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        this.Topmost = false; // Reset first
                        this.Topmost = true;  // Then set to true
                    }, Avalonia.Threading.DispatcherPriority.Background);
                }
                finally
                {
                    _isProgrammaticResize = false;
                }
            }
        }
        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowSize(); // Save size before closing
            StopGlobalHotkeyMonitoring();

            // Cleanup global keyboard hook
            if (_globalHook != null)
            {
                _globalHook.Uninstall();
                _globalHook.KeyDown -= OnGlobalKeyDown;
                _globalHook = null;
            }

            // Dispose of images
            foreach (var bitmap in _images.Values)
            {
                bitmap?.Dispose();
            }
            _images.Clear();
        }

        private void InitializeAltKeyMapping()
        {
            // Initialize alternative key mapping for computers without numpad
            _altKeyMapping['X'] = 1;  // num1
            _altKeyMapping['C'] = 2;  // num2
            _altKeyMapping['V'] = 3;  // num3
            _altKeyMapping['S'] = 4;  // num4
            _altKeyMapping['D'] = 5;  // num5
            _altKeyMapping['F'] = 6;  // num6
            _altKeyMapping['W'] = 7;  // num7
            _altKeyMapping['E'] = 8;  // num8
            _altKeyMapping['R'] = 9;  // num9
            _altKeyMapping['Z'] = 0;  // num0
            _altKeyMapping['B'] = 10; // cancel
            _altKeyMapping['G'] = 11; // relate
            _altKeyMapping['A'] = 12; // prev/shortcut
            _altKeyMapping['T'] = 13; // homo
            _altKeyMapping['Q'] = 14; // openclose
        }

        private void DetectPreferredFont()
        {
            string[] fontTargets = ("Noto Sans HK Medium,Noto Sans HK,Noto Sans HK Black,Noto Sans HK Light,Noto Sans HK Thin,Noto Sans TC,Noto Sans TC Black,Noto Sans TC Light,Noto Sans TC Medium,Noto Sans TC Regular,Noto Sans TC Thin,Noto Serif CJK TC Black,Noto Serif CJK TC Medium,Noto Sans CJK JP,Noto Sans CJK TC Black,Noto Sans CJK TC Bold,Noto Sans CJK TC Medium,Noto Sans CJK TC Regular,Noto Sans CJK DemiLight,Microsoft JhengHei").Split(',');

            foreach (string fontName in fontTargets)
            {
                try
                {
                    var fontFamily = new FontFamily(fontName.Trim());
                    // Try to create a typeface to test if font exists
                    var typeface = new Typeface(fontFamily);
                    _preferredFontFamily = fontName.Trim();
                    break;
                }
                catch (Exception ex)
                {
                    continue;
                }
            }

            if (_preferredFontFamily == "Microsoft Sans Serif")
            {
                Log.Warning("No preferred fonts found, using fallback: Microsoft YaHei");
            }
        }

        private void HandleNumpadInput(Key key)
        {
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                int inputInt = (int)key - (int)Key.NumPad0;
                PressKey(inputInt);
            }
            else if (key == Key.Decimal)
            {
                CommandInput(q9command.cancel);
            }
            else if (key == Key.Add) // +
            {
                CommandInput(q9command.relate);
            }
            else if (key == Key.Subtract) // -
            {
                if (_selectMode)
                    CommandInput(q9command.prev);
                else
                    CommandInput(q9command.shortcut);
            }
            else if (key == Key.Multiply) // *
            {
                CommandInput(q9command.homo);
            }
            else if (key == Key.Divide) // /
            {
                CommandInput(q9command.openclose);
            }
        }

        private void HandleAlternativeInput(Key key)
        {
            // Convert key to character and handle alternative input
            char keyChar = GetKeyChar(key);
            if (keyChar != '\0' && _altKeyMapping.ContainsKey(keyChar))
            {
                int mappedValue = _altKeyMapping[keyChar];

                if (mappedValue <= 9) // Numbers 0-9
                {
                    PressKey(mappedValue);
                }
                else if (mappedValue == 10) // Cancel
                {
                    CommandInput(q9command.cancel);
                }
                else if (mappedValue == 11) // Relate
                {
                    CommandInput(q9command.relate);
                }
                else if (mappedValue == 12) // Prev/Shortcut
                {
                    if (_selectMode)
                        CommandInput(q9command.prev);
                    else
                        CommandInput(q9command.shortcut);
                }
                else if (mappedValue == 13) // Homo
                {
                    CommandInput(q9command.homo);
                }
                else if (mappedValue == 14) // OpenClose
                {
                    CommandInput(q9command.openclose);
                }
            }
        }

        private bool OnGlobalKeyDown(int vkCode, bool isKeyUp)
        {
            try
            {
                // Don't process if hidden or global input disabled
                if (_isHidden || !_globalInputEnabled)
                    return false; // Let the key pass through

                // Convert VK code to our key handling
                bool shouldBlock = false;

                if (_useNumpad)
                {
                    // Handle ONLY numpad keys (0x60-0x69) - NOT regular numbers (0x30-0x39)
                    if (vkCode >= 0x60 && vkCode <= 0x69) // NumPad 0-9 ONLY
                    {
                        int num = vkCode - 0x60;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => PressKey(num));
                        shouldBlock = true;
                    }
                    else if (vkCode == 0x6E) // Numpad Decimal point ONLY
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => CommandInput(q9command.cancel));
                        shouldBlock = true;
                    }
                    else if (vkCode == 0x6B) // Numpad Add (+)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => CommandInput(q9command.relate));
                        shouldBlock = true;
                    }
                    else if (vkCode == 0x6D) // Numpad Subtract (-)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (_selectMode)
                                CommandInput(q9command.prev);
                            else
                                CommandInput(q9command.shortcut);
                        });
                        shouldBlock = true;
                    }
                    else if (vkCode == 0x6A) // Numpad Multiply (*)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => CommandInput(q9command.homo));
                        shouldBlock = true;
                    }
                    else if (vkCode == 0x6F) // Numpad Divide (/)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => CommandInput(q9command.openclose));
                        shouldBlock = true;
                    }
                }
                else
                {
                    // Handle alternative letter keys (unchanged)
                    char keyChar = VkCodeToChar(vkCode);
                    if (keyChar != '\0' && _altKeyMapping.ContainsKey(keyChar))
                    {
                        int mappedValue = _altKeyMapping[keyChar];

                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (mappedValue <= 9) // Numbers 0-9
                            {
                                PressKey(mappedValue);
                            }
                            else if (mappedValue == 10) // Cancel
                            {
                                CommandInput(q9command.cancel);
                            }
                            else if (mappedValue == 11) // Relate
                            {
                                CommandInput(q9command.relate);
                            }
                            else if (mappedValue == 12) // Prev/Shortcut
                            {
                                if (_selectMode)
                                    CommandInput(q9command.prev);
                                else
                                    CommandInput(q9command.shortcut);
                            }
                            else if (mappedValue == 13) // Homo
                            {
                                CommandInput(q9command.homo);
                            }
                            else if (mappedValue == 14) // OpenClose
                            {
                                CommandInput(q9command.openclose);
                            }
                        });
                        shouldBlock = true;
                    }
                }

                // Always handle F10 for visibility toggle
                if (vkCode == 0x79) // F10
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => ToggleVisibility());
                    shouldBlock = true;
                }

                return shouldBlock; // Block key if we processed it
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in global key handler");
                return false;
            }
        }

        private char VkCodeToChar(int vkCode)
        {
            switch (vkCode)
            {
                case 0x41: return 'A';
                case 0x42: return 'B';
                case 0x43: return 'C';
                case 0x44: return 'D';
                case 0x45: return 'E';
                case 0x46: return 'F';
                case 0x47: return 'G';
                case 0x51: return 'Q';
                case 0x52: return 'R';
                case 0x53: return 'S';
                case 0x54: return 'T';
                case 0x56: return 'V';
                case 0x57: return 'W';
                case 0x58: return 'X';
                case 0x5A: return 'Z';
                default: return '\0';
            }
        }

        private char GetKeyChar(Key key)
        {
            // Convert Avalonia Key to character
            switch (key)
            {
                case Key.A: return 'A';
                case Key.B: return 'B';
                case Key.C: return 'C';
                case Key.D: return 'D';
                case Key.E: return 'E';
                case Key.F: return 'F';
                case Key.G: return 'G';
                case Key.Q: return 'Q';
                case Key.R: return 'R';
                case Key.S: return 'S';
                case Key.T: return 'T';
                case Key.V: return 'V';
                case Key.W: return 'W';
                case Key.X: return 'X';
                case Key.Z: return 'Z';
                default: return '\0';
            }
        }

        // NEW: Check if F10 is pressed globally (cross-platform approach)
        private bool IsF10Pressed()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows approach using GetAsyncKeyState
                    const int VK_F10 = 0x79;
                    return (GetAsyncKeyState(VK_F10) & 0x8000) != 0;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Linux approach - could use xdotool or similar
                    // For now, return false as implementing this requires more complex setup
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking F10 key state");
            }
            return false;
        }

        // Windows API for global key detection
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private void StartGlobalHotkeyMonitoring()
        {
            // Only enable global hotkey monitoring on Windows for now
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _globalHotkeyTimer = new Timer(CheckGlobalHotkeys, null, 0, 50); // Check every 50ms
            }
        }

        private void StopGlobalHotkeyMonitoring()
        {
            _globalHotkeyTimer?.Dispose();
            _globalHotkeyTimer = null;
        }

        private void CheckGlobalHotkeys(object state)
        {
            try
            {
                bool f10CurrentlyPressed = IsF10Pressed();

                // Detect F10 key press (edge detection)
                if (f10CurrentlyPressed && !_f10WasPressed)
                {
                    // F10 was just pressed
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (_isHidden)
                        {
                            ToggleVisibility();
                        }
                    });
                }

                _f10WasPressed = f10CurrentlyPressed;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in global hotkey monitoring");
            }
        }

        private void ToggleVisibility()
        {
            _isHidden = !_isHidden;

            if (_isHidden)
            {
                this.Hide();
                // Start monitoring for F10 when hidden
                StartGlobalHotkeyMonitoring();
            }
            else
            {
                // Stop global monitoring when visible
                StopGlobalHotkeyMonitoring();
                this.Show();
                this.Topmost = true;
            }
        }

        private void ToggleNumpadMode()
        {
            _useNumpad = !_useNumpad;
            UpdateStatusDisplay();
        }

        private void ToggleSCMode()
        {
            _scOutput = !_scOutput;
            UpdateStatusDisplay();
        }

        private void UpdateStatusDisplay()
        {
            string inputMode = _useNumpad ? "數字鍵盤" : "字母鍵盤";
            string outputMode = _scOutput ? "簡體" : "繁體";
            _statusBlock.Text = $"輸入:{inputMode} 輸出:{outputMode} F10:隱藏";
        }

        public MainWindow()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/app.log")
                .CreateLogger();

            try
            {
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "files/dataset.db");
                if (!File.Exists(dbPath))
                {
                    throw new FileNotFoundException("Database not found", dbPath);
                }
                _codeTable = new CodeTable(dbPath);
                _inputSimulator = new InputSimulator();

                DetectPreferredFont();
                PreloadImages();
                InitializeComponent();

                // Initialize global keyboard hook
                InitializeGlobalKeyboardHook();

                // Set fixed window size and disable maximize
                this.Width = 280;
                this.Height = 350;
                this.CanResize = true;
                this.WindowState = WindowState.Normal;
                LoadWindowSize();
                this.Topmost = true;

                this.SizeChanged += OnSizeChanged;
                this.PropertyChanged += OnPropertyChanged;
                this.Closing += OnClosing;

                

                // Show initial help message
                UpdateStatusDisplay();

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize MainWindow.");
                _statusBlock = new TextBlock { Text = $"初始化失敗: {ex.Message}" };
                Content = _statusBlock;
            }
        }

        private void InitializeGlobalKeyboardHook()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _globalHook = new GlobalKeyboardHook();
                    _globalHook.KeyDown += OnGlobalKeyDown;
                    _globalHook.Install();
                }
                else
                {
                    Log.Warning("Global keyboard hook not available on this platform.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to install global keyboard hook.");
                _globalInputEnabled = false;
            }
        }

        private void PreloadImages()
        {
            try
            {
                string imgDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "files/img");
                if (!Directory.Exists(imgDir))
                {
                    Log.Error("Image directory not found: {0}", imgDir);
                    return;
                }

                // Preload images for indices 1–109 (0_1.png to 10_9.png)
                for (int i = 0; i <= 10; i++)
                {
                    for (int j = 1; j <= 9; j++)
                    {
                        string imgPath = Path.Combine(imgDir, $"{i}_{j}.png");
                        if (File.Exists(imgPath))
                        {
                            _images[i * 10 + j] = new Bitmap(imgPath);
                        }
                    }
                }

                // Copy images for indices 111–119 with opacity placeholder
                for (int j = 1; j <= 9; j++)
                {
                    if (_images.ContainsKey(j))
                    {
                        _images[110 + j] = _images[j]; // Opacity handled in SetButtonText
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to preload images.");
            }
        }

        private void SetDefaultImages()
        {
            // Show default stroke patterns (0_1.png to 0_9.png) in Q9 layout
            // Layout: 7 8 9
            //         4 5 6  
            //         1 2 3
            for (int i = 0; i < 9; i++)
            {
                int imgIndex = i + 1; // Images are 0_1.png to 0_9.png
                if (_images.ContainsKey(imgIndex))
                {
                    var img = new Image
                    {
                        Source = _images[imgIndex],
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };

                    _candidateButtons[i].Content = img;
                }
                else
                {
                    _candidateButtons[i].Content = "";
                }
            }

            _candidateButtons[9].Content = "標點";  // Cell0 - punctuation
            _candidateButtons[10].Content = "取消"; // Cell10 - cancel
        }

        private void InitializeComponent()
        {
            var screen = this.Screens.Primary.WorkingArea;
            this.Position = new PixelPoint(
                screen.Width - (int)this.Width,
                (screen.Height - (int)this.Height) / 2);
            try
            {
                Log.Information("Loading XAML...");
                AvaloniaXamlLoader.Load(this);
                _inputBox = this.FindControl<TextBox>("InputBox");
                _statusBlock = this.FindControl<TextBlock>("StatusBlock");
                _candidateButtons = new[]
                {
                    this.FindControl<Button>("Cell7"), // Maps to index 1
                    this.FindControl<Button>("Cell8"), // Maps to index 2
                    this.FindControl<Button>("Cell9"), // Maps to index 3
                    this.FindControl<Button>("Cell4"), // Maps to index 4
                    this.FindControl<Button>("Cell5"), // Maps to index 5
                    this.FindControl<Button>("Cell6"), // Maps to index 6
                    this.FindControl<Button>("Cell1"), // Maps to index 7
                    this.FindControl<Button>("Cell2"), // Maps to index 8
                    this.FindControl<Button>("Cell3"), // Maps to index 9
                    this.FindControl<Button>("Cell0"), // Next page
                    this.FindControl<Button>("Cell10") // Cancel
                };

                // Initialize alternative key mapping
                InitializeAltKeyMapping();

                for (int i = 0; i < _candidateButtons.Length; i++)
                {
                    int capturedIndex = i < 9 ? (i % 3) + ((2 - i / 3) * 3) + 1 : i == 9 ? 0 : 10;
                    if (i < 9)
                    {
                        int localIndex = capturedIndex;
                        // Special case for Cell9 (index 0 in array, maps to NumPad1)
                        if (i < 9) // Cell9
                        {
                            var j = i + 1;
                            _candidateButtons[i].Click += (s, e) => PressKey(j); // Mimic NumPad1
                        }
                        else
                        {
                            _candidateButtons[i].Click += (s, e) => SelectWord(localIndex);
                        }
                    }
                    else if (i == 9) // Cell0
                        _candidateButtons[i].Click += (s, e) => PressKey(0);
                    else // Cell10
                        _candidateButtons[i].Click += (s, e) => CommandInput(q9command.cancel);

                    // Right-click handler for input mode toggle
                    _candidateButtons[i].PointerPressed += (s, e) =>
                    {
                        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                        {
                            ToggleNumpadMode();
                            e.Handled = true;
                        }
                        // Middle mouse button for SC toggle
                        else if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
                        {
                            ToggleSCMode();
                            e.Handled = true;
                        }
                    };
                }

                // Add mouse handlers to the main window as well
                this.PointerPressed += (s, e) =>
                {
                    if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                    {
                        ToggleNumpadMode();
                        e.Handled = true;
                    }
                    else if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
                    {
                        ToggleSCMode();
                        e.Handled = true;
                    }
                };

                // Updated key handling
                this.KeyDown += (s, e) => HandleKeyInput(e.Key);

                // Handle window events for global hotkey management
                this.Closing += (s, e) =>
                {
                    StopGlobalHotkeyMonitoring();
                };

                Log.Information("XAML loaded successfully.");
                Cancel(true); // Initialize UI
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load XAML.");
                _statusBlock = new TextBlock { Text = $"XAML 載入失敗: {ex.Message}" };
                Content = _statusBlock;
            }
        }

        private void HandleKeyInput(Key key)
        {
            if (_isHidden)
            {
                if (key == Key.F10)
                {
                    ToggleVisibility();
                }
                return;
            }

            // Handle F10 to hide
            if (key == Key.F10)
            {
                ToggleVisibility();
                return;
            }

            if (!_useNumpad)
            {
                // Handle alternative letter keys
                HandleAlternativeInput(key);
                return;
            }

            // ONLY handle numpad keys when in numpad mode - NOT regular number keys
            if (key >= Key.NumPad0 && key <= Key.NumPad9) // ONLY numpad keys
            {
                int inputInt = (int)key - (int)Key.NumPad0;
                PressKey(inputInt);
            }
            else if (key == Key.Decimal) // ONLY numpad decimal
            {
                CommandInput(q9command.cancel);
            }
            else if (key == Key.Add) // Numpad +
            {
                CommandInput(q9command.relate);
            }
            else if (key == Key.Subtract) // Numpad -
            {
                if (_selectMode)
                    CommandInput(q9command.prev);
                else
                    CommandInput(q9command.shortcut);
            }
            else if (key == Key.Multiply) // Numpad *
            {
                CommandInput(q9command.homo);
            }
            else if (key == Key.Divide) // Numpad /
            {
                CommandInput(q9command.openclose);
            }
        }

        // ... (rest of the methods continue in next part due to length)

        private void PressKey(int inputInt)
        {
            string inputStr = inputInt.ToString();
            if (_selectMode)
            {
                if (inputInt == 0)
                {
                    CommandInput(q9command.next);
                }
                else
                {
                    SelectWord(inputInt);
                }
            }
            else
            {
                _currCode += inputStr;
                SetStatusPrefix(_currCode);
                UpdateStatus();
                if (inputInt == 0)
                {
                    ProcessResult(KeyInput(Convert.ToInt32(_currCode)));
                }
                else
                {
                    if (_currCode.Length == 3)
                    {
                        ProcessResult(KeyInput(Convert.ToInt32(_currCode)));
                    }
                    else if (_currCode.Length == 1)
                    {
                        SetButtonImg(inputInt);
                    }
                    else
                    {
                        SetButtonImg(10);
                    }
                }
            }
        }

        private void CommandInput(q9command command)
        {
            if (command == q9command.cancel)
            {
                Cancel(true);
            }
            else if (command == q9command.openclose)
            {
                _homo = false;
                _openclose = true;
                string opencloseStr = string.Join("", KeyInput(1));
                string[] opencloseArr = new string[(int)(opencloseStr.Length / 2.0)];
                for (int i = 0; i < opencloseStr.Length; i += 2)
                {
                    opencloseArr[i / 2] = opencloseStr.Substring(i, 2);
                }
                SetStatusPrefix("「」");
                StartSelectWord(opencloseArr);
            }
            else if (command == q9command.homo)
            {
                if (!_selectMode && !string.IsNullOrEmpty(_currCode))
                {
                    // Process current code to get candidates for homo
                    var candidates = KeyInput(Convert.ToInt32(_currCode));
                    if (candidates != null && candidates.Length > 0)
                    {
                        _homo = true;
                        SetStatusPrefix($"同音[{_currCode}]");
                        StartSelectWord(candidates);
                        return;
                    }
                }
                else if (_selectMode)
                {
                    // In select mode, toggle homo for next selection
                    _homo = !_homo;
                }
                else
                {
                    // Not in select mode and no current code
                    _homo = !_homo;
                }
                RenewStatus();
            }
            else if (command == q9command.shortcut && !_selectMode)
            {
                if (_currCode.Length == 0)
                {
                    SetStatusPrefix("速選");
                    StartSelectWord(KeyInput(1000));
                }
                else if (_currCode.Length == 1)
                {
                    SetStatusPrefix($"速選{Convert.ToInt32(_currCode)}");
                    StartSelectWord(KeyInput(1000 + Convert.ToInt32(_currCode)));
                }
            }
            else if (command == q9command.relate)
            {
                // FIXED: Only allow relate when NOT in select mode AND we have a last word
                if (!_selectMode && _lastWord.Length == 1)
                {
                    _homo = false;
                    SetStatusPrefix($"[{_lastWord}]關聯");
                    StartSelectWord(GetRelate(_lastWord));
                }
                else if (_selectMode)
                {
                    // Ignore relate command in select mode - do nothing
                    Log.Information("Relate command ignored in select mode");
                    return;
                }
                else if (string.IsNullOrEmpty(_lastWord))
                {
                    // No last word available
                    _statusBlock.Text = "請先輸入一個字";
                    return;
                }
            }
            else if (command == q9command.prev && _selectMode)
            {
                AddPage(-1);
            }
            else if (command == q9command.next && _selectMode)
            {
                AddPage(1);
            }
        }


        private void Cancel(bool cleanRelate = true)
        {
            _selectMode = false;
            _homo = false;
            _openclose = false;
            _currCode = "";
            _currentPage = 0;
            _currentCandidates = new List<string>();
            SetStatusPrefix();
            UpdateStatus();
            if (cleanRelate)
            {
                SetDefaultImages(); // Show default stroke pattern images
            }
        }

        private void ProcessResult(string[] words)
        {
            if (words == null || words.Length == 0)
            {
                Cancel();
                return;
            }
            StartSelectWord(words);
        }

        private void StartSelectWord(string[] words)
        {
            if (words == null || words.Length == 0) return;
            _currentCandidates = words.ToList();
            _selectMode = true;
            _currCode = "";
            ShowPage(0);
            _candidateButtons[9].Content = _currentCandidates.Count > 9 ? "下頁" : ""; // Cell0
            _candidateButtons[10].Content = "取消"; // Cell10
        }

        private void ShowPage(int showPage)
        {
            _currentPage = showPage;
            string[] words = new string[11]; // 0-9, extra for Cancel
            for (int i = 1; i <= 9; i++)
            {
                int p = _currentPage * 9 + i - 1;
                words[i] = p < _currentCandidates.Count && _currentCandidates[p] != "*" ? _currentCandidates[p] : "";
            }
            SetButtonText(words);
            UpdateStatus(_currentCandidates.Count > 9 ? $"{_currentPage + 1}/{(_currentCandidates.Count + 8) / 9}頁" : "");
        }

        private void AddPage(int addNum)
        {
            int totalPage = (_currentCandidates.Count + 8) / 9;
            int newPage = _currentPage + addNum;
            if (newPage < 0)
                newPage = totalPage - 1;
            else if (newPage >= totalPage)
                newPage = 0;
            ShowPage(newPage);
        }

        private void SelectWord(int inputInt)
        {
            int key = _currentPage * 9 + inputInt - 1;
            if (key >= _currentCandidates.Count) return;
            
            string typeWord = _currentCandidates[key];
            
            if (_homo)
            {
                _homo = false;
                string[] homoWords = GetHomo(typeWord);
                if (homoWords.Length > 0)
                {
                    SetStatusPrefix($"同音[{typeWord}]");
                    StartSelectWord(homoWords);
                    return;
                }
            }
            else if (_openclose)
            {
                _openclose = false;
                SendOpenClose(typeWord);
                Cancel();
                return;
            }
            
            // Send the selected word
            SendText(typeWord);
            
            // IMPORTANT: Set _lastWord for single characters to enable relate function
            if (typeWord.Length == 1)
            {
                _lastWord = typeWord;
                Log.Information($"Set _lastWord to: '{_lastWord}' - relate function now available");
                
                // Get related words for display
                string[] relates = GetRelate(typeWord);
                if (relates != null && relates.Length > 0)
                {
                    SetZeroWords(relates);
                    Cancel(false);
                    return;
                }
            }
            else
            {
                _lastWord = ""; // Clear last word for multi-character selections
                Log.Information("Cleared _lastWord - relate function disabled");
            }
            
            Cancel();
        }

        private void SetButtonText(string[] words)
        {
            int type = _currCode.Length == 3 && int.TryParse(_currCode, out int code) ? code : 10;

            for (int i = 0; i < 9; i++)
            {
                int wordIndex = i + 1;

                if (i < words.Length - 1 && !string.IsNullOrEmpty(words[wordIndex]))
                {
                    // In selection mode, show only text without images
                    if (_selectMode)
                    {
                        _candidateButtons[i].Content = words[wordIndex];
                    }
                    else
                    {
                        // In input mode, show images with text if available
                        int imgIndex = (type == 10 ? 11 : type) * 10 + wordIndex;
                        if (_images.ContainsKey(imgIndex))
                        {
                            var img = new Image
                            {
                                Source = _images[imgIndex],
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                            };

                            if (imgIndex >= 111 && imgIndex <= 119)
                                img.Opacity = 0.5;

                            var textBlock = new TextBlock
                            {
                                Text = words[wordIndex],
                                FontFamily = _preferredFontFamily,
                                FontSize = 18,
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                TextAlignment = Avalonia.Media.TextAlignment.Center
                            };

                            var stackPanel = new StackPanel
                            {
                                Orientation = Avalonia.Layout.Orientation.Vertical,
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                Spacing = 2
                            };
                            stackPanel.Children.Add(img);
                            stackPanel.Children.Add(textBlock);

                            _candidateButtons[i].Content = stackPanel;
                        }
                        else
                        {
                            _candidateButtons[i].Content = words[wordIndex];
                        }
                    }
                }
                else
                {
                    _candidateButtons[i].Content = "";
                }
            }

            // Update control buttons
            _candidateButtons[9].Content = _currentCandidates.Count > 9 ? "下頁" : type == 0 ? "標點" : type <= 9 ? "姓氏" : "選字";
            _candidateButtons[10].Content = "取消";
        }

        private void SetButtonImg(int type)
        {
            for (int i = 0; i < 9; i++)
            {
                int num = (type == 10 ? 11 : type) * 10 + (i + 1);
                if (_images.ContainsKey(num))
                {
                    var img = new Image
                    {
                        Source = _images[num],
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };

                    if (type == 10)
                        img.Opacity = 0.5;

                    _candidateButtons[i].Content = img;
                }
                else
                {
                    _candidateButtons[i].Content = "";
                }
            }

            _candidateButtons[9].Content = type == 0 ? "標點" : type <= 9 ? "姓氏" : "選字";
            _candidateButtons[10].Content = "取消";
        }

        private void SetZeroWords(string[] words)
        {
            for (int i = 0; i < 9; i++)
            {
                if (i < words.Length && words[i] != "*" && !string.IsNullOrEmpty(words[i]))
                {
                    // Show related words with small image in background and gray text in top-left
                    int imgIndex = i + 1; // Use images 1-9 for positions

                    var grid = new Grid();

                    // Add background image (like original - images[100 + i])
                    if (_images.ContainsKey(100 + imgIndex))
                    {
                        var bgImg = new Image
                        {
                            Source = _images[100 + imgIndex],
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Stretch = Avalonia.Media.Stretch.Fill
                        };
                        grid.Children.Add(bgImg);
                    }

                    // Add the related character text in top-left with responsive font size
                    var textBlock = new TextBlock
                    {
                        Text = words[i],
                        FontFamily = _preferredFontFamily,
                        Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)), // Gray color like original
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                        Margin = new Thickness(4, 2, 0, 0) // Small margin from edge
                    };

                    // Bind font size to window width with smaller ratio (14.0/140.0)
                    var binding = new Avalonia.Data.Binding("Bounds.Width")
                    {
                        Source = this,
                        Converter = new Q9CS_CrossPlatform.Converters.WidthToSmallFontSizeConverter()
                    };
                    textBlock.Bind(TextBlock.FontSizeProperty, binding);

                    grid.Children.Add(textBlock);

                    _candidateButtons[i].Content = grid;
                }
                else
                {
                    _candidateButtons[i].Content = "";
                }
            }

            _candidateButtons[9].Content = "標點"; // Cell0 - punctuation like original
            _candidateButtons[10].Content = "取消"; // Cell10 - cancel
        }

        private void SetStatusPrefix(string prefix = "")
        {
            _statusPrefix = prefix;
            RenewStatus();
        }

        private void UpdateStatus(string topText = "")
        {
            _statusBlock.Text = topText;
            RenewStatus();
        }

        private void RenewStatus()
        {
            this.Title = "九万 " + (_homo ? "[同音] " : "") + (_scOutput ? "[簡] " : "") + _statusPrefix + " " + _statusBlock.Text;
        }
        private void PressKey_WithHomoSupport(int inputInt)
        {
            string inputStr = inputInt.ToString();            
            
            if (_selectMode)
            {
                if (inputInt == 0)
                {
                    CommandInput(q9command.next);
                }
                else
                {
                    SelectWord(inputInt);
                }
            }
            else
            {
                _currCode += inputStr;
                SetStatusPrefix(_currCode);
                UpdateStatus();
                
                if (inputInt == 0)
                {
                    var results = KeyInput(Convert.ToInt32(_currCode));
                    
                    // If homo mode is active and we have results, show them for homo selection
                    if (_homo && results != null && results.Length > 0)
                    {
                        SetStatusPrefix($"同音選字[{_currCode}]");
                        StartSelectWord(results);
                    }
                    else
                    {
                        ProcessResult(results);
                    }
                }
                else
                {
                    if (_currCode.Length == 3)
                    {
                        var results = KeyInput(Convert.ToInt32(_currCode));
                        
                        // If homo mode is active, show candidates for homo selection
                        if (_homo && results != null && results.Length > 0)
                        {
                            SetStatusPrefix($"同音選字[{_currCode}]");
                            StartSelectWord(results);
                        }
                        else
                        {
                            ProcessResult(results);
                        }
                    }
                    else if (_currCode.Length == 1)
                    {
                        SetButtonImg(inputInt);
                    }
                    else
                    {
                        SetButtonImg(10);
                    }
                }
            }
        }

        private void SendText(string text)
        {
            try
            {
                string output = _scOutput ? Tcsc(text) : text;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _inputSimulator.Keyboard.TextEntry(output);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    System.Diagnostics.Process.Start("ydotool", $"type {output}").WaitForExit();
                }
            }
            catch (Exception ex)
            {
                _statusBlock.Text = $"輸出失敗：{ex.Message}";
                Log.Error(ex, $"輸出字符 {text} 失敗");
            }
        }

        private void SendOpenClose(string text)
        {
            try
            {
                string output = _scOutput ? Tcsc(text) : text;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _inputSimulator.Keyboard.TextEntry(output);
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.LEFT);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    System.Diagnostics.Process.Start("ydotool", $"type {output}").WaitForExit();
                    System.Diagnostics.Process.Start("ydotool", "key 105:1 105:0").WaitForExit();
                }
            }
            catch (Exception ex)
            {
                _statusBlock.Text = $"輸出失敗：{ex.Message}";
                Log.Error(ex, $"輸出開關字符 {text} 失敗");
            }
        }

        private string[] KeyInput(int code)
        {
            return _codeTable.GetCandidates(code);
        }

        private string[] GetRelate(string word)
        {
            return _codeTable.GetRelate(word);
        }

        private string[] GetHomo(string word)
        {
            return _codeTable.GetHomo(word);
        }

        private string Tcsc(string text)
        {
            return _codeTable.ConvertToSimplified(text);
        }

        // Cleanup method to dispose resources
        protected override void OnClosed(EventArgs e)
        {
            StopGlobalHotkeyMonitoring();

            // Cleanup global keyboard hook
            if (_globalHook != null)
            {
                _globalHook.Uninstall();
                _globalHook.KeyDown -= OnGlobalKeyDown;
                _globalHook = null;
            }

            // Dispose of images
            foreach (var bitmap in _images.Values)
            {
                bitmap?.Dispose();
            }
            _images.Clear();

            base.OnClosed(e);
        }
    }

    // Global Keyboard Hook Implementation for Windows
    public class GlobalKeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private LowLevelKeyboardProc _proc = HookCallback;
        private IntPtr _hookID = IntPtr.Zero;
        private static GlobalKeyboardHook _instance;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate bool KeyboardHookHandler(int vkCode, bool isKeyUp);
        public event KeyboardHookHandler KeyDown;

        public GlobalKeyboardHook()
        {
            _instance = this;
        }

        public void Install()
        {
            _hookID = SetHook(_proc);
        }

        public void Uninstall()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN;
                bool isKeyUp = wParam == (IntPtr)WM_KEYUP;

                if (isKeyDown) // Only process key down events
                {
                    int vkCode = Marshal.ReadInt32(lParam);

                    // Call the event handler
                    bool shouldBlock = _instance?.KeyDown?.Invoke(vkCode, isKeyUp) ?? false;

                    if (shouldBlock)
                    {
                        return (IntPtr)1; // Block the key
                    }
                }
            }

            return CallNextHookEx(_instance._hookID, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }

    public class CodeTable
    {
        private readonly string _connectionString;

        public CodeTable(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
            if (!File.Exists(dbPath))
                throw new FileNotFoundException("Database not found", dbPath);
        }

        public string[] GetCandidates(int code)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT characters FROM mapped_table WHERE id = $code";
                command.Parameters.AddWithValue("$code", code);

                var result = command.ExecuteScalar();
                if (result != null)
                {
                    string str = result.ToString();
                    var stru8 = new StringInfo(str);
                    var strs = new string[stru8.LengthInTextElements];
                    for (int i = 0; i < stru8.LengthInTextElements; i++)
                    {
                        strs[i] = stru8.SubstringByTextElements(i, 1);
                    }
                    return strs;
                }
                return new string[0];
            }
            catch (SqliteException ex)
            {
                Log.Error(ex, $"查詢編碼 {code} 失敗");
                return new string[0];
            }
        }

        public string[] GetRelate(string word)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT candidates FROM related_candidates_table WHERE character = $word";
                command.Parameters.AddWithValue("$word", word);

                var result = command.ExecuteScalar();
                if (result != null)
                {
                    string candidatesStr = result.ToString();
                    if (!string.IsNullOrEmpty(candidatesStr))
                    {
                        // Split by space and filter out empty entries
                        return candidatesStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    }
                }
                return new string[0];
            }
            catch (SqliteException ex)
            {
                Log.Error(ex, $"查詢關聯字 {word} 失敗");
                return new string[0];
            }
        }

        public string[] GetHomo(string word)
        {
            
            var words = new List<string>();
            
            // Only process single characters
            if (word.Length > 1) 
            {
                return words.ToArray();
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                Log.Information("Database connection opened successfully");

                // First, check if word_meta table exists
                var tableCheckCommand = connection.CreateCommand();
                tableCheckCommand.CommandText = @"
                    SELECT name FROM sqlite_master 
                    WHERE type='table' AND name='word_meta'";
                
                var tableExists = tableCheckCommand.ExecuteScalar();
                
                if (tableExists == null)
                {
                    Log.Error("word_meta table does not exist in database");
                    return words.ToArray();
                }
                
                Log.Information("word_meta table found");

                // Check table structure
                var schemaCommand = connection.CreateCommand();
                schemaCommand.CommandText = "PRAGMA table_info(word_meta)";
                
                var columns = new List<string>();
                using (var schemaReader = schemaCommand.ExecuteReader())
                {
                    while (schemaReader.Read())
                    {
                        string columnName = schemaReader["name"].ToString();
                        columns.Add(columnName);
                    }
                }
                Log.Information($"word_meta table columns: {string.Join(", ", columns)}");

                // Check if the input word exists in the table
                var checkWordCommand = connection.CreateCommand();
                checkWordCommand.CommandText = "SELECT COUNT(*) FROM word_meta WHERE char = $word";
                checkWordCommand.Parameters.AddWithValue("$word", word);
                
                var wordCount = Convert.ToInt32(checkWordCommand.ExecuteScalar());                
                
                if (wordCount == 0)
                {
                    Log.Warning($"Input word '{word}' not found in word_meta table");
                    return words.ToArray();
                }

                // Main homo query
                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT w1.char, w1.ping, w1.ping2, w2.ping as input_ping, w2.ping2 as input_ping2
                    FROM word_meta w1 
                    INNER JOIN word_meta w2 ON w1.ping = w2.ping 
                    WHERE w2.char = $word 
                    ORDER BY 
                        CASE WHEN w1.ping2 = w2.ping2 THEN 0 ELSE 1 END DESC";

                command.Parameters.AddWithValue("$word", word);
                
                using var reader = command.ExecuteReader();
                int resultCount = 0;
                while (reader.Read())
                {
                    string chr = reader["char"].ToString();
                    string ping = reader["ping"].ToString();
                    string ping2 = reader["ping2"].ToString();
                    string inputPing = reader["input_ping"].ToString();
                    string inputPing2 = reader["input_ping2"].ToString();
                    
                    resultCount++;                   
                    
                    if (!string.IsNullOrEmpty(chr))
                    {
                        words.Add(chr);
                    }
                }                
                return words.ToArray();
            }
            catch (SqliteException ex)
            {
                Log.Error(ex, $"Database error in GetHomo for word '{word}': {ex.Message}");
                return new string[0];
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"General error in GetHomo for word '{word}': {ex.Message}");
                return new string[0];
            }
        }

        public string ConvertToSimplified(string text)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                StringBuilder output = new StringBuilder();
                foreach (char c in text)
                {
                    string query = "SELECT `simplified` FROM `ts_chinese_table` WHERE `traditional` = $char LIMIT 1";
                    using var command = new SqliteCommand(query, connection);
                    command.Parameters.AddWithValue("$char", c.ToString());

                    object result = command.ExecuteScalar();
                    if (result != null)
                    {
                        string simplified = result.ToString();
                        output.Append(simplified);
                    }
                    else
                    {
                        output.Append(c); // Use original character if no match
                    }
                }

                return output.ToString();
            }
            catch (SqliteException ex)
            {
                Log.Error(ex, $"Failed to convert text to Simplified Chinese: {text}");
                return text; // Return original text on error
            }
        }
    }
}