using System;

namespace Q9CS_CrossPlatform
{
    public interface IKeyboardHook : IDisposable
    {
        event KeyboardHookHandler KeyDown;
        void Install();
        void Uninstall();
    }

    public delegate bool KeyboardHookHandler(int vkCode, bool isKeyUp);
} 