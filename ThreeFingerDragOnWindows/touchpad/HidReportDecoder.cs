using System.Collections.Generic;
using System.Linq;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.touchpad;

internal readonly record struct HidValueCapability(ushort Page, ushort Collection, ushort Usage);
internal delegate bool HidValueReader(HidValueCapability capability, int reportIndex, uint reportLength, out uint value);

internal static class HidReportDecoder {
    internal static List<(List<TouchpadContact> contacts, uint count)> Decode(
        uint reportCount, uint reportLength, IEnumerable<HidValueCapability> capabilities, HidValueReader read) {
        var results = new List<(List<TouchpadContact>, uint)>();
        var ordered = capabilities.OrderBy(c => c.Collection).ToArray();
        for(int index = 0; index < reportCount; index++) {
            uint count = 99;
            var contacts = new List<TouchpadContact>();
            var creators = new Dictionary<ushort, TouchpadContactCreator>();
            foreach(var cap in ordered) {
                if(!read(cap, index, reportLength, out var value)) continue;
                if(cap.Collection == 0) {
                    if(cap.Page == 0x0D && cap.Usage == 0x54) count = value;
                    continue;
                }
                if(!creators.TryGetValue(cap.Collection, out var creator))
                    creators[cap.Collection] = creator = new TouchpadContactCreator();
                switch(cap.Page, cap.Usage) {
                    case (0x0D, 0x51): creator.ContactId = (int)value; break;
                    case (0x01, 0x30): creator.X = (int)value; break;
                    case (0x01, 0x31): creator.Y = (int)value; break;
                }
                if(creator.TryCreate(out var contact)) {
                    contacts.Add(contact);
                    creator.Clear();
                    if(count != 0 && contacts.Count >= count) break;
                }
            }
            Logger.Log($"HID report {index + 1}/{reportCount}, bytes={reportLength}, cC={count}: " + string.Join(", ", contacts));
            results.Add((contacts, count));
        }
        return results;
    }
}
