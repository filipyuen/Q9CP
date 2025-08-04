# Linux Keyboard Hook Implementation

This document describes the Linux keyboard hook implementation for the Chinese input program, based on the [boppreh/keyboard](https://github.com/boppreh/keyboard) library approach.

## Overview

The Linux keyboard hook uses the `evdev` interface to capture global keyboard events directly from input devices. This approach is more reliable than X11-based methods and provides stable keycodes across sessions.

## Architecture

### Key Components

1. **`IKeyboardHook` Interface** - Common interface for both Windows and Linux implementations
2. **`LinuxKeyboardHook` Class** - Linux-specific implementation using evdev
3. **`GlobalKeyboardHook` Class** - Windows-specific implementation (existing)

### Implementation Details

#### Linux Implementation (`LinuxKeyboardHook.cs`)

- **Uses `evdev`** - Direct access to `/dev/input/event*` devices
- **Requires root privileges** - Uses `sudo` for global capture
- **Stable keycodes** - Linux keycodes are consistent across sessions
- **Device detection** - Automatically finds keyboard devices
- **Event parsing** - Parses raw evdev events into key events

#### Key Features

1. **Global Capture** - Captures all keyboard events system-wide
2. **Key Mapping** - Maps Linux keycodes to Windows VK codes
3. **Event Filtering** - Only processes key events (EV_KEY)
4. **Device Management** - Handles device grabbing and release
5. **Error Handling** - Robust error handling and logging

## Usage

### Prerequisites

1. **Linux system** with evdev support
2. **Root privileges** for global capture
3. **Input device access** to `/dev/input/event*`
4. **.NET 8.0** or later

### Installation

```bash
# Build the project
dotnet build Q9CS_CrossPlatform.csproj --configuration Release

# Run with root privileges for global capture
sudo dotnet run --project Q9CS_CrossPlatform.csproj --configuration Release
```

### Testing

```bash
# Run the test script
./test_linux_evdev.sh
```

## Key Mapping

The implementation maps Linux keycodes to Windows VK codes:

| Linux Keycode | Key | Windows VK Code |
|---------------|-----|-----------------|
| 82 | KP_0 | 0x60 (VK_NUMPAD0) |
| 79 | KP_1 | 0x61 (VK_NUMPAD1) |
| 80 | KP_2 | 0x62 (VK_NUMPAD2) |
| 81 | KP_3 | 0x63 (VK_NUMPAD3) |
| 75 | KP_4 | 0x64 (VK_NUMPAD4) |
| 76 | KP_5 | 0x65 (VK_NUMPAD5) |
| 77 | KP_6 | 0x66 (VK_NUMPAD6) |
| 71 | KP_7 | 0x67 (VK_NUMPAD7) |
| 72 | KP_8 | 0x68 (VK_NUMPAD8) |
| 73 | KP_9 | 0x69 (VK_NUMPAD9) |
| 68 | F10 | 0x79 (VK_F10) |

## Technical Details

### Event Structure

Linux evdev events have this structure:
```c
struct input_event {
    struct timeval time;
    __u16 type;
    __u16 code;
    __s32 value;
};
```

### Event Types

- `EV_KEY` (0x01) - Key events
- `EV_REL` (0x02) - Relative events
- `EV_ABS` (0x03) - Absolute events

### Event Values

- `0` - Key release
- `1` - Key press
- `2` - Key repeat

### Device Detection

The implementation automatically finds keyboard devices by:
1. Scanning `/dev/input/event*` devices
2. Checking device capabilities
3. Selecting the first available keyboard device

## Troubleshooting

### Common Issues

1. **Permission Denied**
   ```
   Error: Failed to open device: /dev/input/event0
   ```
   **Solution**: Run with `sudo`

2. **No Keyboard Device Found**
   ```
   Error: No keyboard device found
   ```
   **Solution**: Check `/dev/input/` directory and device permissions

3. **Wrong Keycodes**
   ```
   Unmapped Linux keycode: XXX
   ```
   **Solution**: Update the keycode mapping in `LinuxKeyboardHook.cs`

### Debugging

Enable debug logging to see detailed event information:

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();
```

## Comparison with Previous Implementation

| Feature | X11 Implementation | evdev Implementation |
|---------|-------------------|---------------------|
| **Global Capture** | Limited (window-focused) | True global capture |
| **Keycode Stability** | Unstable (varies by session) | Stable (consistent) |
| **Root Requirements** | No | Yes (for global capture) |
| **Reliability** | Low | High |
| **Performance** | Medium | High |
| **Compatibility** | X11 only | All Linux systems |

## References

- [boppreh/keyboard](https://github.com/boppreh/keyboard) - Python keyboard library
- [Linux Input Subsystem](https://www.kernel.org/doc/html/latest/input/) - Kernel documentation
- [evdev Interface](https://www.freedesktop.org/wiki/Software/libevdev/) - libevdev documentation

## Files

- `LinuxKeyboardHook.cs` - Linux keyboard hook implementation
- `IKeyboardHook.cs` - Common interface
- `MainWindow.axaml.cs` - Updated to use interface
- `test_linux_evdev.sh` - Test script
- `LINUX_KEYBOARD_HOOK_README.md` - This documentation 