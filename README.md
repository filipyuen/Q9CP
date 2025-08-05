# Q9CP
Just test

#test_evdev.py
使用py在Linux環境下測試和實現以下功能
1) 在程式UI窗口非Forus狀態下Keyboard hook 全域監聽numpad "0-9/*-+."和"F10"鍵key down
2) 程式在全域監聽項目1)並可運行程式內部function()
3) 同時阻截numpad "0-9/*-+."和"F10"原本keyboard input
4) 阻截的同時其他keyboard key如英文、符號、方向、ctrl+shift等使用如常不變，只有項目3)的按鍵阻截
5) 程式能輸出中文到forus的textbox

目前已實現和面對問題
已實現:
- 在程式UI窗口非Forus狀態下Keyboard hook 全域監聽numpad "0-9/*-+."和"F10"鍵key down
- 全鍵盤所有按鍵阻截(不符合項目4要求)

面對問題:
- 項目4停止阻截其他keyboard key未能成功
- 嘗試xdotool模擬輸出已阻截的key，但高延遲、key down按死、ctrl+不能用等等大量問題

你可以推翻xdotool方案使用其他方式，key hook方面已測試pyput和keyboard各有輸入、錯誤認鍵、監聽/阻截失敗問題。目前evdev最有機會實現功能。
你也可以推倒放棄兼用Windows，以Linux輸入法開發方式實現功能。
