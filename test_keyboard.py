import keyboard

def print_key_event(event):
    print(f"Event: name={event.name}, scan_code={event.scan_code}, is_keypad={event.is_keypad}, event_type={event.event_type}")

keyboard.hook(print_key_event)
keyboard.wait('esc')  # 按 Esc 退出