using System.Collections.Generic;
using System.Linq;
using ThreeFingerDragEngine.utils;

namespace ThreeFingerDragOnWindows.touchpad;

// One instance per device. Emit only complete frames with real, unique IDs.
public sealed class ContactFrameAssembler {
    private uint target;
    private readonly Dictionary<int, TouchpadContact> pending = new();

    public List<TouchpadContact> Accept(uint count, IEnumerable<TouchpadContact> contacts) {
        if (count == 99 || count > 10) return null; // Unknown/malformed count.
        if (count > 0) {
            pending.Clear();
            target = count;
        }
        if (target == 0) return null; // Orphaned continuation.
        foreach (var contact in contacts) pending[contact.ContactId] = contact;
        if (pending.Count > target) {
            pending.Clear();
            target = 0;
            return null;
        }
        if (pending.Count != target) return null;
        var frame = pending.Values.ToList();
        pending.Clear();
        target = 0;
        return frame;
    }
}
