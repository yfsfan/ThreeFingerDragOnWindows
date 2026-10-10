using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
internal static class HandleTests {
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
        [StructLayout(LayoutKind.Sequential)] private struct Registration { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [DllImport("user32.dll")] private static extern uint GetRegisteredRawInputDevices([Out] Registration[]? entries, ref uint count, uint size);
    private static IntPtr RegisteredTarget() {
        uint count = 0;
        var size = (uint)Marshal.SizeOf<Registration>();
        GetRegisteredRawInputDevices(null, ref count, size);
        var entries = new Registration[count];
        GetRegisteredRawInputDevices(entries, ref count, size);
        return entries.Single(e => e.Page == 13 && e.Usage == 5).Target;
    }
    [STAThread] static void Main() {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using(var old = new Form()) {
            var oldHandle = old.Handle;
            int destroyed = 0;
            old.HandleDestroyed += (_, _) => destroyed++;
            old.ShowInTaskbar = false;
            if(destroyed == 0) throw new Exception("Expected old code to recreate its handle");
            Console.WriteLine("PASS reproduced old taskbar change destroying registered window handle");
        }
        var assembly = Assembly.Load("ThreeFingerDrag-ThinkPadRepair");
        var type = assembly.GetType("ThreeFingerDragOnWindows.RepairWindow", true)!;
        using var form = (Form)Activator.CreateInstance(type, new object[] {false})!;
        var handle = form.Handle;
                var helper = assembly.GetType("ThreeFingerDragOnWindows.utils.TouchpadHelper", true)!;
        if(!(bool)helper.GetMethod("RegisterInput")!.Invoke(null, new object[] {handle})!) throw new Exception("Initial registration failed");
        int recreations = 0;
        form.HandleDestroyed += (_, _) => recreations++;
        var hide = type.GetMethod("HideToTray", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for(int i = 0; i < 20; i++) hide.Invoke(form, null);
        if(form.Handle != handle || recreations != 0 || !IsWindow(handle)) throw new Exception("Tray hiding invalidated input window");
        Console.WriteLine("PASS 20 tray hides keep the same live input window handle");
        if(!form.ShowInTaskbar || form.Visible) throw new Exception("Tray hide changed persistent window style or failed to hide");
                Console.WriteLine("PASS hidden form keeps taskbar style without showing a window");
        if(RegisteredTarget() != handle) throw new Exception("Input registration changed on tray hide");
        Console.WriteLine("PASS Windows raw input registration survives tray hiding");
        type.GetField("initialized", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
        type.GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        if(recreations == 0 || !IsWindow(form.Handle) || RegisteredTarget() != form.Handle) throw new Exception("Raw input not rebound after handle recreation");
        Console.WriteLine("PASS forced handle recreation rebinds Windows raw input to the current window");
        var overrides = type.GetMethod("OnHandleCreated", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if(overrides.DeclaringType != type) throw new Exception("Missing re-registration hook");
        Console.WriteLine("PASS handle recreation has an input re-registration hook");
        Console.WriteLine("No window was shown and no mouse input was sent.");
    }
}
