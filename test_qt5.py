import sys
from PyQt5.QtWidgets import QApplication, QMainWindow, QPushButton
import keyboard

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Python PyQt5 UI with keyboard")
        self.setGeometry(100, 100, 400, 300)
        button = QPushButton("Click Me", self)
        button.setGeometry(150, 100, 100, 50)
        self.is_visible = True
        keyboard.hook(self.on_key_event)

    def on_key_event(self, event):
        if event.event_type == keyboard.KEY_DOWN:
            scan_code_map = {
                # Numpad 鍵（根據你的測試結果）
                82: (0x60, True, '0'),   # Numpad 0
                79: (0x61, True, '1'),   # Numpad 1
                80: (0x62, True, '2'),   # Numpad 2
                81: (0x63, True, '3'),   # Numpad 3
                75: (0x64, True, '4'),   # Numpad 4
                76: (0x65, True, '5'),   # Numpad 5
                77: (0x66, True, '6'),   # Numpad 6
                71: (0x67, True, '7'),   # Numpad 7
                72: (0x68, True, '8'),   # Numpad 8
                73: (0x69, True, '9'),   # Numpad 9
                98: (0x6F, True, '/'),   # Numpad /
                55: (0x6A, True, '*'),   # Numpad *
                74: (0x6D, True, '-'),   # Numpad -
                78: (0x6B, True, '+'),   # Numpad +
                83: (0x6E, True, '.'),   # Numpad .
                68: (0x79, False, 'f10')  # F10
            }
            key_info = scan_code_map.get(event.scan_code, (None, None, None))
            if key_info[0]:
                vk_code, is_numpad, key_name = key_info
                print(f"Key pressed: {key_name} (VK: {vk_code:02X}, Numpad: {is_numpad}, Scan: {event.scan_code})")
                if event.scan_code == 68:  # F10
                    self.is_visible = not self.is_visible
                    if self.is_visible:
                        self.show()
                        self.activateWindow()
                    else:
                        self.hide()
                elif self.is_visible and is_numpad:
                    if vk_code in range(0x60, 0x6A):  # Numpad 0-9
                        self.press_key(vk_code - 0x60)
                    elif vk_code in [0x6F, 0x6A, 0x6D, 0x6B, 0x6E]:  # Numpad /,*,+,-,.
                        self.command_input(key_name)

    def press_key(self, digit):
        print(f"Processing digit: {digit}")

    def command_input(self, operator):
        print(f"Processing operator: {operator}")

if __name__ == '__main__':
    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    sys.exit(app.exec_())