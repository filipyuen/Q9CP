import sys
import time
import subprocess
from PyQt5.QtWidgets import QApplication, QMainWindow, QPushButton, QTextEdit
from PyQt5.QtCore import Qt, QEvent
from evdev import InputDevice, ecodes
import threading
import os
import stat

class TextUpdateEvent(QEvent):
    EVENT_TYPE = QEvent.Type(QEvent.User + 1)
    def __init__(self, text):
        super().__init__(self.EVENT_TYPE)
        self.text = text

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Python PyQt5 UI with evdev and xdotool")
        self.setGeometry(100, 100, 400, 300)
        self.button = QPushButton("Clear Input", self)
        self.button.setGeometry(150, 200, 100, 50)
        self.button.clicked.connect(self.clear_input)
        self.display = QTextEdit(self)
        self.display.setGeometry(50, 50, 300, 100)
        self.display.setReadOnly(True)
        self.is_visible = True
        self.last_event_time = time.time()
        self.is_grabbed = False
        self.device = None
        self.find_keyboard_device()
        if not self.device:
            print("Error: No keyboard device found. Check /dev/input/event* permissions.")
            sys.exit(1)
        if not self.check_xdotool():
            print("Error: xdotool not found. Install with: sudo apt install xdotool")
            sys.exit(1)
        self.update_grab_state()
        self.listener_thread = threading.Thread(target=self.keyboard_listener, daemon=True)
        self.listener_thread.start()
        self.regrab_thread = threading.Thread(target=self.auto_regrab, daemon=True)
        self.regrab_thread.start()

    def find_keyboard_device(self):
        for dev in [f'/dev/input/event{i}' for i in range(32)]:
            try:
                device = InputDevice(dev)
                if 'keyboard' in device.name.lower():
                    self.device = device
                    print(f"Using keyboard device: {device.name} ({dev})")
                    return
            except (FileNotFoundError, PermissionError):
                continue

    def check_xdotool(self):
        try:
            subprocess.run(['xdotool', 'key', 'a'], check=True, capture_output=True)
            print("xdotool initialized successfully")
            return True
        except (subprocess.CalledProcessError, FileNotFoundError) as e:
            print(f"xdotool check failed: {e}")
            return False

    def customEvent(self, event):
        if event.type() == TextUpdateEvent.EVENT_TYPE:
            self.display.append(event.text)

    def update_grab_state(self):
        try:
            if self.is_visible and not self.is_grabbed:
                self.device.grab()
                self.is_grabbed = True
                print("Keyboard grabbed successfully")
            elif not self.is_visible and self.is_grabbed:
                self.device.ungrab()
                self.is_grabbed = False
                print("Keyboard ungrabbed successfully")
        except OSError as e:
            print(f"Update grab state failed: {e}")

    def keyboard_listener(self):
        scan_code_map = {
            82: (0x60, True, '0', 'KP_0'),
            79: (0x61, True, '1', 'KP_1'),
            80: (0x62, True, '2', 'KP_2'),
            81: (0x63, True, '3', 'KP_3'),
            75: (0x64, True, '4', 'KP_4'),
            76: (0x65, True, '5', 'KP_5'),
            77: (0x66, True, '6', 'KP_6'),
            71: (0x67, True, '7', 'KP_7'),
            72: (0x68, True, '8', 'KP_8'),
            73: (0x69, True, '9', 'KP_9'),
            98: (0x6F, True, '/', 'KP_Divide'),
            55: (0x6A, True, '*', 'KP_Multiply'),
            74: (0x6D, True, '-', 'KP_Minus'),
            78: (0x6B, True, '+', 'KP_Plus'),
            83: (0x6E, True, '.', 'KP_Period'),
            68: (0x79, False, 'f10', 'F10'),
            87: (0x7F, False, 'f12', 'F12')
        }
        xdotool_map = {
            30: 'a', 48: 'b', 46: 'c', 32: 'd', 18: 'e',
            33: 'f', 34: 'g', 35: 'h', 23: 'i', 36: 'j',
            37: 'k', 38: 'l', 50: 'm', 49: 'n', 24: 'o',
            25: 'p', 16: 'q', 19: 'r', 31: 's', 20: 't',
            22: 'u', 47: 'v', 17: 'w', 45: 'x', 21: 'y',
            44: 'z',
            11: '0', 2: '1', 3: '2', 4: '3', 5: '4',
            6: '5', 7: '6', 8: '7', 9: '8', 10: '9',
            59: 'F1', 60: 'F2', 61: 'F3', 62: 'F4', 63: 'F5',
            64: 'F6', 65: 'F7', 66: 'F8', 67: 'F9',
            29: 'Control_L', 97: 'Control_R', 42: 'Shift_L',
            54: 'Shift_R', 56: 'Alt_L', 100: 'Alt_R',
            125: 'Super_L', 126: 'Super_R',
            103: 'Up', 108: 'Down', 105: 'Left', 106: 'Right',
            102: 'Home', 107: 'End', 104: 'Page_Up', 109: 'Page_Down',
            28: 'Return', 14: 'BackSpace', 15: 'Tab', 57: 'space',
            12: 'minus', 13: 'equal', 26: 'bracketleft', 27: 'bracketright',
            43: 'backslash', 39: 'semicolon', 40: 'apostrophe',
            51: 'comma', 52: 'period', 53: 'slash', 1: 'Escape',
            58: 'Caps_Lock'
        }
        while True:
            try:
                for event in self.device.read_loop():
                    if event.type == ecodes.EV_KEY:
                        self.last_event_time = time.time()
                        scan_code = event.code
                        key_info = scan_code_map.get(scan_code, (None, None, None, None))
                        if key_info[0]:
                            vk_code, is_numpad, key_name, xdotool_code = key_info
                            if event.value == 1:
                                print(f"Key pressed: {key_name}")
                                if scan_code in (68, 87):
                                    try:
                                        if self.is_grabbed:
                                            self.device.ungrab()
                                            self.is_grabbed = False
                                            print(f"Ungrabbed for {key_name}")
                                        subprocess.run(['xdotool', 'key', xdotool_code], check=True)
                                        self.is_visible = not self.is_visible
                                        if self.is_visible:
                                            self.show()
                                            self.activateWindow()
                                            self.setFocus(Qt.OtherFocusReason)
                                            time.sleep(0.02)
                                        else:
                                            self.hide()
                                    except subprocess.CalledProcessError as e:
                                        print(f"{key_name} handling failed: {e}")
                                elif self.is_visible and is_numpad:
                                    print(f"Processing numpad key: {key_name}")
                                    if vk_code in range(0x60, 0x6A):
                                        self.press_key(vk_code - 0x60, key_name)
                                    elif vk_code in [0x6F, 0x6A, 0x6D, 0x6B, 0x6E]:
                                        self.command_input(key_name)
                        elif event.value in (0, 1):
                            xdotool_code = xdotool_map.get(scan_code)
                            if xdotool_code:
                                try:
                                    if self.is_grabbed:
                                        self.device.ungrab()
                                        self.is_grabbed = False
                                        print(f"Ungrabbed for non-numpad key: {xdotool_code}")
                                    if event.value == 1:
                                        subprocess.run(['xdotool', 'key', xdotool_code], check=True)
                                    elif event.value == 0:
                                        subprocess.run(['xdotool', 'key', '--clearmodifiers', xdotool_code], check=True)
                                except subprocess.CalledProcessError as e:
                                    print(f"xdotool emit failed for key {xdotool_code}: {e}")
                            else:
                                try:
                                    if self.is_grabbed:
                                        self.device.ungrab()
                                        self.is_grabbed = False
                                        print(f"Ungrabbed for unmapped key: scan_code {scan_code}")
                                except OSError as e:
                                    print(f"Unmapped key ungrab failed: {e}")
                        self.update_grab_state()
            except Exception as e:
                print(f"Error in keyboard listener: {e}")
                break
            finally:
                if self.is_grabbed:
                    try:
                        self.device.ungrab()
                        self.is_grabbed = False
                        print("Keyboard ungrabbed on exit")
                    except OSError:
                        pass

    def auto_regrab(self):
        while True:
            time.sleep(0.2)
            if self.is_visible and not self.is_grabbed and time.time() - self.last_event_time > 1.0:
                try:
                    self.device.grab()
                    self.is_grabbed = True
                    print("Auto-regrabbed keyboard")
                except OSError as e:
                    print(f"Auto-regrab failed: {e}")
                    time.sleep(1.0)

    def press_key(self, digit, key_name):
        print(f"Processing digit: {digit}")
        QApplication.postEvent(self, TextUpdateEvent(key_name))

    def command_input(self, operator):
        print(f"Processing operator: {operator}")
        QApplication.postEvent(self, TextUpdateEvent(operator))

    def clear_input(self):
        self.display.clear()

if __name__ == '__main__':
    runtime_dir = '/tmp/runtime-' + str(os.getuid())
    os.environ['XDG_RUNTIME_DIR'] = runtime_dir
    if not os.path.exists(runtime_dir):
        os.makedirs(runtime_dir)
    os.chmod(runtime_dir, stat.S_IRWXU)
    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    sys.exit(app.exec_())