using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows;
using ThreeFingerDragOnWindows.settings;
using ThreeFingerDragOnWindows.threefingerdrag;
using ThreeFingerDragOnWindows.touchpad;
using ThreeFingerDragOnWindows.utils;

static TouchpadContact C(int id, int x = 0) => new(id, x + id * 200, id * 100);
static void Check(bool ok, string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }

var caps = new[] { new HidValueCapability(0x0D, 0, 0x54), new HidValueCapability(0x0D, 1, 0x51), new HidValueCapability(1, 1, 0x30), new HidValueCapability(1, 1, 0x31) };
bool ReadBatch(HidValueCapability cap, int index, uint length, out uint value) {
    // Native HID parsers require the size of ONE report, not the whole batch.
    if(length != 32) { value = 0; return false; }
    value = (cap.Page, cap.Usage) switch {
        (0x0D, 0x54) => index == 0 ? 3u : index == 3 ? 4u : 0u,
        (0x0D, 0x51) => (uint)(index < 3 ? index : index - 3),
        (1, 0x30) => (uint)(index * 200 + 10),
        (1, 0x31) => (uint)(index * 100 + 20),
        _ => 0
    };
    return true;
}
var rejected = HidReportDecoder.Decode(7, 224, caps, ReadBatch);
Check(rejected.All(r => r.count == 99 && r.contacts.Count == 0), "reproduced original wrong batch-length parsing failure");
var decoded = HidReportDecoder.Decode(7, 32, caps, ReadBatch);
Check(decoded.Select(r => r.count).SequenceEqual(new uint[] {3,0,0,4,0,0,0}), "batched headers and continuation counts remain separate");
var batchFrames = new ContactFrameAssembler();
var emitted = decoded.Select(r => batchFrames.Accept(r.count, r.contacts)).Where(f => f != null).ToList();
Check(emitted.Select(f => f.Count).SequenceEqual(new[] {3,4}), "batched three- and four-finger frames assemble correctly");
Check(emitted[0][2].X == 410 && emitted[1][3].X == 1210, "each batched report retains its own coordinates");
// Two separate contact collections in a single report must not share incomplete values.
var mixedCaps = new[] { caps[0], new HidValueCapability(0x0D,1,0x51), new HidValueCapability(1,2,0x30), new HidValueCapability(1,2,0x31) };
Check(HidReportDecoder.Decode(1,32,mixedCaps,ReadBatch)[0].contacts.Count == 0, "incomplete collections cannot fabricate a combined contact");

// Reproduce the released version's changing-loop-bound bug.
foreach(var target in new[] { 3, 4 }) {
    var old = new List<TouchpadContact> { C(0) };
    for(int i = 1; i <= target - old.Count; i++) old.Add(C(i));
    Check(old.Count == target - 1, $"reproduced original {target}->{target-1} defect");
    var assembler = new ContactFrameAssembler();
    Check(assembler.Accept((uint)target, new[] { C(0) }) == null, $"{target} fingers waits for real contacts");
    List<TouchpadContact> result = null;
    for(int id = 1; id < target; id++) result = assembler.Accept(0, new[] { C(id) });
    Check(result.Count == target && result.SequenceEqual(Enumerable.Range(0, target).Select(id => C(id))), $"{target} fingers retains each real ID and coordinate");
}
var frames = new ContactFrameAssembler();
frames.Accept(3, new[] { C(0) });
Check(frames.Accept(4, new[] { C(0) }) == null, "new frame discards incomplete frame without fabricated contacts");
Check(frames.Accept(99, new[] { C(1) }) == null, "unknown parser sentinel does not become finger count");
frames.Accept(0, new[] { C(0, 30), C(1), C(2) });
var four = frames.Accept(0, new[] { C(3) });
Check(four.Count == 4 && four[0] == C(0, 30), "duplicate IDs update coordinates without increasing count");
Check(frames.Accept(0, new[] { C(4) }) == null, "orphan continuation ignored after completed frame");
Check(frames.Accept(2, new[] { C(0), C(1), C(2) }) == null, "overfull frame rejected instead of truncated");
Check(frames.Accept(1, new[] { C(0) }).Count == 1, "next valid frame recovers after malformed frame");
var deviceA = new ContactFrameAssembler();
var deviceB = new ContactFrameAssembler();
deviceA.Accept(3, new[] { C(0) });
Check(deviceB.Accept(0, new[] { C(1), C(2) }) == null, "devices do not combine contacts");
Check(deviceA.Accept(0, new[] { C(1), C(2) }).Count == 3, "device A completes independently");

App.SettingsData.ThreeFingerDeviceDragCursorConfigs["test"] = new SettingsData.ThreeFingerDragConfig();
static void Move(ThreeFingerDrag engine, int count) {
    var previous = Array.Empty<TouchpadContact>();
    for(int step = 0; step < 20; step++) {
        var next = Enumerable.Range(0, count).Select(id => new TouchpadContact(id, id * 200 + step * 100, id * 100)).ToArray();
        engine.OnTouchpadContact(IntPtr.Zero, previous, next, step == 0 ? 100 : 8);
        previous = next;
        Thread.Sleep(8);
    }
}
MouseOperations.Reset();
using(var engine = new ThreeFingerDrag()) Move(engine, 4);
Check(MouseOperations.Downs == 0, "four actual fingers never trigger three-finger dragging");
MouseOperations.Reset();
using(var engine = new ThreeFingerDrag()) {
    Move(engine, 3);
    Check(MouseOperations.Downs == 1 && MouseOperations.Moves > 0, "three real moving fingers start drag and move cursor");
}
Check(MouseOperations.Ups == 1, "closing or disabling releases active drag exactly once");
MouseOperations.Reset();
using(var engine = new ThreeFingerDrag()) {
    Move(engine, 3);
    Thread.Sleep(650);
    Check(MouseOperations.Ups == 1, "lost input releases drag by timeout");
}
Check(MouseOperations.Ups == 1, "shutdown after timeout does not release twice");
Console.WriteLine("All regression checks passed. Mouse output was mocked; no real mouse input was sent.");

var desktopTestDir = Path.Combine(Directory.GetCurrentDirectory(), "desktop-test " + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(desktopTestDir);
Check(DesktopPreferences.Load(desktopTestDir).DragEnabled, "first launch enables working drag by default");
new DesktopPreferences { DragEnabled = false }.Save(desktopTestDir);
Check(!DesktopPreferences.Load(desktopTestDir).DragEnabled, "paused drag persists across restart");
new DesktopPreferences { DragEnabled = true }.Save(desktopTestDir);
Check(DesktopPreferences.Load(desktopTestDir).DragEnabled, "enabled drag persists across restart");
File.WriteAllText(DesktopPreferences.PathIn(desktopTestDir), "broken json");
Check(DesktopPreferences.Load(desktopTestDir).DragEnabled, "corrupted preferences recover without crashing");
var shortcutPath = Path.Combine(desktopTestDir, StartupRegistration.ShortcutName);
var startupTarget = Path.Combine(desktopTestDir, "app folder", "ThreeFingerDrag-ThinkPadRepair.exe");
StartupRegistration.SetEnabled(true, shortcutPath, startupTarget);
Check(StartupRegistration.IsEnabled(shortcutPath, startupTarget), "startup shortcut preserves executable path with spaces and tray arguments");
StartupRegistration.SetEnabled(true, shortcutPath, startupTarget);
Check(Directory.GetFiles(desktopTestDir, "*.lnk").Length == 1, "enabling startup twice does not create duplicate shortcuts");
StartupRegistration.SetEnabled(false, shortcutPath, startupTarget + "other.exe");
Check(File.Exists(shortcutPath), "disabling does not remove another target's shortcut");
StartupRegistration.SetEnabled(false, shortcutPath, startupTarget);
Check(!File.Exists(shortcutPath), "disabling startup removes only the matching shortcut");
Console.WriteLine("Desktop checks passed. Startup shortcuts were tested only inside the workspace.");

namespace ThreeFingerDragOnWindows.utils {
    public struct IntMousePoint { public int x; public int y; }
    internal static class TouchpadHelper {
        public static TouchpadDeviceInfo GetDeivceInfo(IntPtr device) => new() { deviceId = "test" };
    }
    internal static class MouseOperations {
        public static int Downs, Ups, Moves;
        public static void Reset() => Downs = Ups = Moves = 0;
        public static void ThreeFingersDragMouseDown() => Downs++;
        public static void ThreeFingersDragMouseUp() => Ups++;
        public static void ShiftCursorPosition(float x, float y) => Moves++;
    }
}
