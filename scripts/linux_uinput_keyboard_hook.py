#!/usr/bin/env python3
import sys
import os
import threading
import time
import argparse
from evdev import ecodes, InputDevice, UInput, KeyEvent, list_devices

class KeyboardListener:
    def __init__(self, device_path):
        self.device_path = device_path
        self.running = False
        self.original_device = None
        self.virtual_keyboard = None
        self.listener_thread = None

        # A mapping from evdev key codes to the simple names our C# app will understand.
        self.intercepted_codes = {
            ecodes.KEY_F10,
            ecodes.KEY_KP0, ecodes.KEY_KP1, ecodes.KEY_KP2, ecodes.KEY_KP3, ecodes.KEY_KP4,
            ecodes.KEY_KP5, ecodes.KEY_KP6, ecodes.KEY_KP7, ecodes.KEY_KP8, ecodes.KEY_KP9,
            ecodes.KEY_KPSLASH, ecodes.KEY_KPASTERISK, ecodes.KEY_KPMINUS, ecodes.KEY_KPPLUS,
            ecodes.KEY_KPDOT,
        }
        
    def setup_devices(self):
        """
        尋找並打開原始鍵盤設備，同時創建一個虛擬鍵盤設備。
        """
        print(f"尋找鍵盤設備: {self.device_path}...", file=sys.stderr, flush=True)
        try:
            self.original_device = InputDevice(self.device_path)
        except FileNotFoundError:
            print(f"錯誤: 找不到設備 {self.device_path}。請確認路徑正確。", file=sys.stderr, flush=True)
            return False
            
        print(f"成功連接到原始鍵盤: {self.original_device.name}", file=sys.stderr, flush=True)
        
        try:
            # 創建一個新的虛擬鍵盤設備，並複製原始設備的按鍵能力。
            self.virtual_keyboard = UInput.from_device(self.original_device, name='Virtual Keyboard')
            print("成功創建虛擬鍵盤設備。", file=sys.stderr, flush=True)
            
            # 抓取原始設備，防止事件重複。這讓原始設備的事件只會被我們這個程式讀取。
            self.original_device.grab()
            print("已獨佔原始鍵盤設備。", file=sys.stderr, flush=True)

        except Exception as e:
            print(f"錯誤: 無法創建虛擬設備或獨佔原始設備。請確保以 root 權限運行。", file=sys.stderr, flush=True)
            print(e, file=sys.stderr, flush=True)
            if self.original_device:
                self.original_device.close()
            return False

        return True

    def start(self):
        if not self.setup_devices():
            return
            
        self.running = True
        self.listener_thread = threading.Thread(target=self.event_loop, daemon=True)
        self.listener_thread.start()
        print("服務已啟動，開始監聽鍵盤事件。", file=sys.stderr, flush=True)
        
    def stop(self):
        self.running = False
        if self.original_device:
            try:
                # 釋放設備獨佔
                self.original_device.ungrab()
            except Exception:
                pass
            finally:
                self.original_device.close()
        
        if self.virtual_keyboard:
            self.virtual_keyboard.close()
            
        print("服務已停止。", file=sys.stderr, flush=True)

    def event_loop(self):
        while self.running:
            try:
                for event in self.original_device.read_loop():
                    if event.type != ecodes.EV_KEY:
                        # 傳遞所有非按鍵事件，例如LED燈狀態等
                        self.virtual_keyboard.write(event.type, event.code, event.value)
                        self.virtual_keyboard.syn()
                        continue
                    
                    # 判斷是否為攔截鍵
                    if event.code in self.intercepted_codes:
                        if event.value == KeyEvent.key_down:
                             # 攔截按鍵，只輸出到 stdout，不傳遞給虛擬鍵盤
                             print(f"KEY_INTERCEPTED:{event.code}", flush=True)
                        continue # 阻擋此事件，不傳遞

                    # 對於非攔截鍵，直接寫入虛擬鍵盤，使其正常工作
                    self.virtual_keyboard.write(event.type, event.code, event.value)
                    self.virtual_keyboard.syn()
            
            except Exception as e:
                # 處理設備被移除等錯誤
                print(f"event_loop 錯誤: {e}", file=sys.stderr, flush=True)
                break
            
        print("事件循環已停止。", file=sys.stderr, flush=True)

def main():
    parser = argparse.ArgumentParser(description="Headless keyboard hook for Q9CS on Linux.")
    parser.add_argument("device_path", help="Path to the keyboard event device (e.g., /dev/input/eventX)")
    args = parser.parse_args()

    if os.getuid() != 0:
        print("錯誤: 此腳本必須以 root 權限運行。請使用 'sudo python3 your_script.py'。", file=sys.stderr, flush=True)
        sys.exit(1)
        
    listener = KeyboardListener(args.device_path)

    try:
        listener.start()
        while listener.running:
            time.sleep(1)
            
    except KeyboardInterrupt:
        print("\n程式被使用者中斷。", file=sys.stderr, flush=True)
    except Exception as e:
        print(f"主程式出錯: {e}", file=sys.stderr, flush=True)
    finally:
        listener.stop()
        sys.exit(0)

if __name__ == '__main__':
    main()