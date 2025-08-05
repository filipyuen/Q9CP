import sys
from PyQt5.QtWidgets import QApplication, QMainWindow, QPushButton
from pynput import keyboard as pynput_keyboard

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Python PyQt5 UI with pynput")
        self.setGeometry(100, 100, 400, 300)
        button = QPushButton("Click Me", self)
        button.setGeometry(150, 100, 100, 50)
        self.is_visible = True
        self.listener = pynput_keyboard.Listener(on_press=self.on_key_press, suppress=False)
        self.listener.start()

    def on_key_press(self, key):
        vk_code_map = {
            # 根據 keyboard 測試掃描碼，使用 vk 模擬
            pynput_keyboard.KeyCode(vk=96): (0x60, True),   # Numpad 0
            pynput_keyboard.KeyCode(vk=97): (0x61, True),   # Numpad 1
            pynput_keyboard.KeyCode(vk=98): (0x62, True),   # Numpad 2
            pynput_keyboard.KeyCode(vk=99): (0x63, True),   # Numpad 3
            pynput_keyboard.KeyCode(vk=100): (0x64, True),  # Numpad 4
            pynput_keyboard.KeyCode(vk=101): (0x65, True),  # Numpad 5
            pynput_keyboard.KeyCode(vk=102): (0x66, True),  # Numpad 6
            pynput_keyboard.KeyCode(vk=103): (0x67, True),  # Numpad 7
            pynput_keyboard.KeyCode(vk=104): (0x68, True),  # Numpad 8
            pynput_keyboard.KeyCode(vk=105): (0x69, True),  # Numpad 9
            pynput_keyboard.KeyCode(vk=111): (0x6F, True),  # Numpad /
            pynput_keyboard.KeyCode(vk=106): (0x6A, True),  # Numpad *
            pynput_keyboard.KeyCode(vk=109): (0x6D, True),  # Numpad -
            pynput_keyboard.KeyCode(vk=107): (0x6B, True),  # Numpad +
            pynput_keyboard.KeyCode(vk=110): (0x6E, True),  # Numpad .
            pynput_keyboard.Key.f10: (0x79, False)         # F10
        }
        key_info = vk_code_map.get(key, (None, None))
        if key_info[0]:
            vk_code, is_numpad = key_info
            key_name = str(key).replace("Key.", "") if hasattr(key, 'name') else (key.char if hasattr(key, 'char') else str(key))
            print(f"Key pressed: {key_name} (VK: {vk_code:02X}, Numpad: {is_numpad})")
            if vk_code == 0x79:  # F10
                self.is_visible = not self.is_visible
                if self.is_visible:
                    self.show()
                    self.activateWindow()
                else:
                    self.hide()
                return False  # 不阻擋 F10
            elif self.is_visible and is_numpad:
                if vk_code in range(0x60, 0x6A):  # Numpad 0-9
                    self.press_key(vk_code - 0x60)
                elif vk_code in [0x6F, 0x6A, 0x6D, 0x6B, 0x6E]:  # Numpad /,*,+,-,.
                    operator = {0x6F: '/', 0x6A: '*', 0x6D: '-', 0x6B: '+', 0x6E: '.'}.get(vk_code)
                    self.command_input(operator)
                return False  # 阻擋 numpad 鍵
        return True  # 其他鍵允許傳遞

    def press_key(self, digit):
        print(f"Processing digit: {digit}")

    def command_input(self, operator):
        print(f"Processing operator: {operator}")

if __name__ == '__main__':
    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    sys.exit(app.exec_())