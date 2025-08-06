#!/usr/bin/env python3
import sys
import os
import threading
import time
import subprocess
import argparse
from evdev import ecodes, InputDevice, KeyEvent, list_devices
from PyQt5.QtCore import pyqtSignal, QObject


class KeyboardListener:
    def __init__(self, device_path):
        self.device_path = device_path
        self.running = False
        self.original_device = None
        self.is_grabbing = False
        self.regrab_counter = -1
        self.pressed_keys_ungrab = set()
        self.keys_lock = threading.Lock()

        # A mapping from evdev key codes to the simple names our C# app will understand.
        self.intercepted_codes = {
            ecodes.KEY_F10,
            ecodes.KEY_KP0, ecodes.KEY_KP1, ecodes.KEY_KP2, ecodes.KEY_KP3, ecodes.KEY_KP4,
            ecodes.KEY_KP5, ecodes.KEY_KP6, ecodes.KEY_KP7, ecodes.KEY_KP8, ecodes.KEY_KP9,
            ecodes.KEY_KPSLASH, ecodes.KEY_KPASTERISK, ecodes.KEY_KPMINUS, ecodes.KEY_KPPLUS,
            ecodes.KEY_KPDOT,
        }
        
        self.key_map = {
            ecodes.KEY_A: 'a', ecodes.KEY_B: 'b', ecodes.KEY_C: 'c', ecodes.KEY_D: 'd',
            ecodes.KEY_E: 'e', ecodes.KEY_F: 'f', ecodes.KEY_G: 'g', ecodes.KEY_H: 'h',
            ecodes.KEY_I: 'i', ecodes.KEY_J: 'j', ecodes.KEY_K: 'k', ecodes.KEY_L: 'l',
            ecodes.KEY_M: 'm', ecodes.KEY_N: 'n', ecodes.KEY_O: 'o', ecodes.KEY_P: 'p',
            ecodes.KEY_Q: 'q', ecodes.KEY_R: 'r', ecodes.KEY_S: 's', ecodes.KEY_T: 't',
            ecodes.KEY_U: 'u', ecodes.KEY_V: 'v', ecodes.KEY_W: 'w', ecodes.KEY_X: 'x',
            ecodes.KEY_Y: 'y', ecodes.KEY_Z: 'z',
            ecodes.KEY_1: '1', ecodes.KEY_2: '2', ecodes.KEY_3: '3', ecodes.KEY_4: '4',
            ecodes.KEY_5: '5', ecodes.KEY_6: '6', ecodes.KEY_7: '7', ecodes.KEY_8: '8',
            ecodes.KEY_9: '9', ecodes.KEY_0: '0',ecodes.KEY_KPENTER: 'KP_Enter',
            ecodes.KEY_ENTER: 'Return', ecodes.KEY_SPACE: 'space',
            ecodes.KEY_BACKSPACE: 'BackSpace', ecodes.KEY_TAB: 'Tab',
            ecodes.KEY_LEFTSHIFT: 'shift', ecodes.KEY_RIGHTSHIFT: 'shift',
            ecodes.KEY_LEFTCTRL: 'ctrl', ecodes.KEY_RIGHTCTRL: 'ctrl',
            ecodes.KEY_LEFTALT: 'alt', ecodes.KEY_RIGHTALT: 'alt',
            ecodes.KEY_CAPSLOCK: 'Caps_Lock',
            ecodes.KEY_SEMICOLON: 'semicolon', ecodes.KEY_APOSTROPHE: 'apostrophe',
            ecodes.KEY_COMMA: 'comma', ecodes.KEY_DOT: 'period', ecodes.KEY_SLASH: 'slash',
            ecodes.KEY_MINUS: 'minus', ecodes.KEY_EQUAL: 'equal',
            ecodes.KEY_LEFTBRACE: 'bracketleft', ecodes.KEY_RIGHTBRACE: 'bracketright',
            ecodes.KEY_BACKSLASH: 'backslash', ecodes.KEY_GRAVE: 'grave',
            ecodes.KEY_KP0: 'KP_0', ecodes.KEY_KP1: 'KP_1', ecodes.KEY_KP2: 'KP_2',
            ecodes.KEY_KP3: 'KP_3', ecodes.KEY_KP4: 'KP_4', ecodes.KEY_KP5: 'KP_5',
            ecodes.KEY_KP6: 'KP_6', ecodes.KEY_KP7: 'KP_7', ecodes.KEY_KP8: 'KP_8',
            ecodes.KEY_KP9: 'KP_9', ecodes.KEY_KPSLASH: 'KP_Divide', 
            ecodes.KEY_KPASTERISK: 'KP_Multiply', ecodes.KEY_KPMINUS: 'KP_Subtract',
            ecodes.KEY_KPPLUS: 'KP_Add', ecodes.KEY_KPDOT: 'KP_Decimal',
            ecodes.KEY_LEFTMETA: 'Super_L',     # Win鍵 (左)
            ecodes.KEY_RIGHTMETA: 'Super_R',    # Win鍵 (右)
            ecodes.KEY_ESC: 'Escape',           # Esc
            ecodes.KEY_F1: 'F1',                # F1
            ecodes.KEY_F2: 'F2',                # F2
            ecodes.KEY_F3: 'F3',                # F3
            ecodes.KEY_F4: 'F4',                # F4
            ecodes.KEY_F5: 'F5',                # F5
            ecodes.KEY_F6: 'F6',                # F6
            ecodes.KEY_F7: 'F7',                # F7
            ecodes.KEY_F8: 'F8',                # F8
            ecodes.KEY_F9: 'F9',                # F9
            ecodes.KEY_F10: 'F10',
            ecodes.KEY_F11: 'F11',              # F11
            ecodes.KEY_F12: 'F12',              # F12
            ecodes.KEY_INSERT: 'Insert',        # Insert
            ecodes.KEY_SYSRQ: 'Print',          # Print Screen (PrtScn)
            ecodes.KEY_SCROLLLOCK: 'Scroll_Lock', # Scroll Lock
            ecodes.KEY_DELETE: 'Delete',
            ecodes.KEY_END: 'End',
            ecodes.KEY_HOME: 'Home',
            ecodes.KEY_PAGEUP: 'Page_Up',
            ecodes.KEY_PAGEDOWN: 'Page_Down',
        }
    
    def simulate_key_with_xdotool(self, key_code, key_value):
        #使用 xdotool 模擬按鍵事件。
        key_name = self.key_map.get(key_code)
        if not key_name:
            print(f"找不到 evdev 代碼 {key_code} 的xdotool對應name，無法模擬。")
            return
            
        action = ''
        if key_value == KeyEvent.key_down:
            action = 'keydown'
        elif key_value == KeyEvent.key_up:
            action = 'keyup'
        elif key_value == KeyEvent.key_hold:
            # 對於 hold 事件，我們只需確保 keydown 已經執行，不需要重複
            return

        if action:
            print(f"使用xdotool模擬 {action} {key_name}...")
            try:
                subprocess.run(['xdotool', action, key_name], check=False, timeout=0.1, 
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            except Exception as e:
                print(f"警告: xdotool {action} {key_name} 失敗: {e}")

    def start(self):
        """Starts the key listening and grabbing thread."""
        self.running = True
        try:
            self.original_device = InputDevice(self.device_path)
            print(f"Successfully opened device: {self.original_device.name}", flush=True)
        except Exception as e:
            print(f"Error: Failed to open device {self.device_path}. {e}", file=sys.stderr, flush=True)
            self.running = False
            return

        self.filter_thread = threading.Thread(target=self.event_loop, daemon=True)
        self.regrab_thread = threading.Thread(target=self.regrab_loop, daemon=True)
        self.filter_thread.start()
        self.regrab_thread.start()
        print("Key listener started. Grabbing device...", flush=True)
        self.grab_device()

    def stop(self):
        """Stops the listener and releases the device."""
        self.running = False
        if self.original_device and self.is_grabbing:
            try:
                self.original_device.ungrab()
                self.is_grabbing = False
                print("Device ungrabbed.", flush=True)
            except Exception as e:
                print(f"Error ungrabbing device: {e}", file=sys.stderr, flush=True)

    def grab_device(self):
        if not self.is_grabbing:
            try:
                self.original_device.grab()
                self.is_grabbing = True
            except Exception as e:
                print(f"Error: Failed to grab device. Are you running with sudo? {e}", file=sys.stderr, flush=True)

    def event_loop(self):
        """Main loop for reading and processing keyboard events."""
        while self.running:
            try:
                for event in self.original_device.read_loop():
                    if not self.running:
                        break

                    # We only care about key presses (down events)
                    if event.type != ecodes.EV_KEY or event.value != KeyEvent.key_down:
                        continue

                    if event.code not in self.intercepted_codes:
                        if not self.is_grabbing:
                            with self.keys_lock:
                                if event.value in [KeyEvent.key_down, KeyEvent.key_hold]:
                                    self.regrab_counter = 10
                                elif event.value == KeyEvent.key_up:
                                    self.pressed_keys_ungrab.discard(event.code)

                    if event.code == ecodes.KEY_F10 and event.value == KeyEvent.key_up:
                        if not self.is_grabbing:
                            try:
                                self.original_device.grab()
                                self.is_grabbing = True
                                print("鍵盤閒置超過1秒，已切換回 grab 狀態。")
                                #self.release_stuck_keys_with_xdotool()
                            except Exception as e:
                                print(f"警告: re-grab 失敗: {e}")
                            continue
                        elif self.is_grabbing:
                            self.original_device.ungrab()
                            self.is_grabbing = False
                            continue

                    if event.code in self.intercepted_codes:                        
                        if event.value == KeyEvent.key_down:
                            key_name = ecodes.KEY.get(event.code, "Unknown")
                            print(f"KEY:{key_name}", flush=True)
                        continue

                    if self.is_grabbing:
                        if event.value == KeyEvent.key_down or KeyEvent.key_hold:
                            try:
                                self.simulate_key_with_xdotool(event.code, event.value)#模擬輸入已阻斷的鍵                                
                                self.original_device.ungrab()
                                self.is_grabbing = False
                                self.simulate_key_with_xdotool(event.code, KeyEvent.key_up)#因為simulate_key_with_xdotool沒有處理Key_up，即是xdtotool會一直按著不放的bug，雖然Ctrl/Shift等起手用起來會很難受，只能習慣性連按2下ctrl起手
                                print("非攔截鍵被按下，已切換至 ungrab 狀態。")
                            except Exception as e:
                                print(f"警告: ungrab 失敗: {e}")

                            self.regrab_counter = 10
                        continue

            except Exception as e:
                pass

            except OSError as e:
                # This can happen if the device is disconnected.
                print(f"Error reading from device: {e}. Stopping.", file=sys.stderr, flush=True)
                self.running = False
            except Exception as e:
                print(f"An unexpected error occurred in event_loop: {e}", file=sys.stderr, flush=True)
                time.sleep(1) # Avoid spamming errors in a tight loop

    def regrab_loop(self):
        while self.running:
            time.sleep(0.1)
            
            if self.regrab_counter > -1:
                self.regrab_counter -= 1
                
                if self.regrab_counter <= 0:
                    if not self.is_grabbing:
                        try:
                            self.original_device.grab()
                            self.is_grabbing = True
                            print("鍵盤閒置超過1秒，已切換回 grab 狀態。")
                            #self.release_stuck_keys_with_xdotool()
                        except Exception as e:
                            print(f"警告: re-grab 失敗: {e}")
                    self.regrab_counter = -1
            #print(self.regrab_counter)
            
        print("regrab_loop 已停止。")
    
    def re_grab_device(self):
        if not self.is_grabbing:
            try:
                self.original_device.grab()
                self.is_grabbing = True
                print("鍵盤閒置超過1秒，已切換回 grab 狀態。")
            except Exception as e:
                print(f"警告: re-grab 失敗: {e}")

def main():
    parser = argparse.ArgumentParser(description="Headless keyboard hook for Q9CS on Linux.")
    parser.add_argument("device_path", help="Path to the keyboard event device (e.g., /dev/input/eventX)")
    args = parser.parse_args()

    if os.getuid() != 0:
        print("Error: This script must be run with root (sudo) privileges.", file=sys.stderr, flush=True)
        sys.exit(1)

    print("Starting Linux Keyboard Hook...", flush=True)
    listener = KeyboardListener(args.device_path)

    try:
        listener.start()
        # Keep the script alive while the listener is running
        while listener.running:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nInterrupt received, shutting down.", flush=True)
    finally:
        listener.stop()
        print("Shutdown complete.", flush=True)

if __name__ == '__main__':
    main()