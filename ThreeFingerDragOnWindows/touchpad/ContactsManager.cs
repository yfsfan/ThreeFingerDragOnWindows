using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;
using WinRT.Interop;
using WinUICommunity;

namespace ThreeFingerDragOnWindows.touchpad;

public class ContactsManager{
    private readonly HandlerWindow _source;

    private readonly IntPtr _hwnd;
    private IntPtr _oldWndProc;

    public ContactsManager(HandlerWindow source){
        _source = source;

        _hwnd = WindowNative.GetWindowHandle(_source);
        _oldWndProc = Interop.SetWndProc(_hwnd, WindowProcess);
    }

    public void InitializeSource(){
        var touchpadExists = TouchpadHelper.Exists();
        var inputReceiverInstalled = TouchpadHelper.RegisterInput(_hwnd);

        _source.OnTouchpadInitialized(touchpadExists, inputReceiverInstalled);
    }

    // WindowProc Listener
    private IntPtr WindowProcess(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam){
        switch(message){
            case TouchpadHelper.WM_INPUT:
                foreach(var (currentDevice, contacts, count) in TouchpadHelper.ParseInput(lParam))
                ReceiveTouchpadContacts(currentDevice, contacts, count);
                break;
            case TouchpadHelper.WM_INPUT_DEVICE_CHANGE:
                _source.OnTouchpadInitialized(TouchpadHelper.Exists(lParam), true);
                break;
        }

        return Interop.CallWindowProc(_oldWndProc, hwnd, message, wParam, lParam);
    }


    private readonly Dictionary<IntPtr, ContactFrameAssembler> frames = new();
    private void ReceiveTouchpadContacts(IntPtr currentDevice, List<TouchpadContact> contacts, uint count){
        if (!frames.TryGetValue(currentDevice, out var assembler))
            frames[currentDevice] = assembler = new ContactFrameAssembler();
        var complete = assembler.Accept(count, contacts);
        if (complete != null) {
            Logger.Log("Complete real contact frame: " + string.Join(", ", complete));
            _source.OnTouchpadContact(currentDevice, complete);
        }
    }
}
