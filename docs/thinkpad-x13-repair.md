# ThinkPad X13 repair: investigation, fixes, and validation

This community repair was developed against upstream 2.0.7 (658e18dfdbf71827865a1051d07cabaef59c7fcc) on Windows 11, ThinkPad X13. It retains the upstream MIT license. Code assistance and regression harnesses were produced with OpenAI Codex; actual touchpad testing was performed by the device user.

## Symptoms and evidence

Windows three-finger swipes and taps were disabled; the application was enabled and already elevated. Two fingers were recognized, three did not drag, and four appeared as three. Changing the motion threshold did not resolve the issue.

Local logs reported Contact Count 3 or 4 while assembled contacts had length 2 or 3 with duplicated coordinates. Batched continuation reports yielded no parsed fields and the parser's default count 99. Count 99 is a sentinel, not a device reporting 99 fingers. Raw logs, device identifiers, personal paths, and settings are deliberately not published.

## Input corrections

1. HidP_GetUsageValue previously received a pointer to one HID report but the length of the entire batch. Use dwSizeHid for each report, not dwSizeHid * dwCount. Decode each report independently so a zero-count continuation cannot overwrite its preceding frame header.
2. Read bRawData after RAWINPUTHEADER and the two RAWHID DWORDs rather than inferring its offset from the allocation's end.
3. Replace incomplete-frame coordinate cloning. The old loop changed its own bound as List.Count grew: from one real contact, a target of 3 emitted 2; a target of 4 emitted 3. The new assembler emits only complete frames containing real, unique IDs. Incomplete frames are discarded when a new frame starts; orphan continuations and overfull frames are rejected. Assembly state is isolated per device.
4. Keep incomplete HID contact collections separate, preventing ID/X/Y fields from different collections being combined into a fabricated contact.
5. Add release timing immediately when drag starts, serialize engine access, and release on disposal. The portable frontend disposes its engine on pause or exit.

Microsoft references:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getusagevalue
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawhid

## Portable frontend and tray regression

ThinkPadRepair is an optional Windows Forms frontend using the patched upstream parser and drag engine. It supplies diagnostics, tray controls, per-user Startup-folder registration with --tray, and local preference persistence. It does not modify drivers or require elevation. Keep the original application exited while testing this alternative frontend.

Version 2.0.7.1 restored three-finger functionality according to the device user. Version 2.0.7.2 added tray and startup support but introduced a tray regression: changing ShowInTaskbar recreates a Form HWND, invalidating Raw Input registration on the previous HWND. Version 2.0.7.3 hides the window without changing that property and re-registers input in OnHandleCreated if recreation happens for another reason. This tray defect belongs to the experimental frontend, not the upstream WinUI application.

## Verification and remaining limits

- The first input repair built successfully in the upstream WinUI project.
- 24 regression checks exercised report decoding, real-contact assembly, device isolation, three-finger start, four-finger rejection, disposal release, and timeout release with mocked mouse output.
- 8 desktop checks exercised preference persistence/recovery and real shortcut creation/removal in a workspace test directory.
- 6 native-window checks reproduced the old handle destruction; hid the patched form 20 times; verified the actual Windows Raw Input registration remained intact; and forced handle recreation to verify rebinding. No window was shown and no mouse input was sent by that harness.
- Portable 2.0.7.3 compiled successfully. On 2026-10-10 the device user confirmed that the latest repaired version 2.0.7.3 works well in actual use, after reporting the tray regression in 2.0.7.2. This is user-reported validation, not an automated hardware test; next-login startup was not separately reported as tested.

These tests do not replay binary reports captured from the device. Synthetic report values exercise the decoder; native tests exercise window registration. This is an experimental device-specific repair, not a claim of compatibility with all precision touchpads. Unknown-count sentinel handling, a ten-contact guard, report-range usages, tip-switch handling, and broader device descriptors need maintainer review. The old heuristic is intentionally replaced with dropping incomplete data, which can affect devices that omit continuations.

## Reproduce

Use Windows and .NET SDK 10. From the repository root:

```powershell
dotnet run --project ThinkPadRepair.Tests -c Release
dotnet run --project ThinkPadRepair.HandleTests -c Release
dotnet publish ThinkPadRepair -c Release -r win-x64 --self-contained false -o publish/ThinkPadRepair
```

The portable build needs .NET 10 Desktop Runtime. Run publish/ThinkPadRepair/ThreeFingerDrag-ThinkPadRepair.exe. Enable dragging, test three moving fingers, then hide to tray and test again in a normal window. Double-click the tray icon to reopen; right-click to pause or exit. Enable startup through its checkbox; keep the published folder at a stable path. Closing the window hides it, while Exit actually stops the program.

The companion test projects are deliberately not added to the existing production solution. They can run independently. The upstream WinUI integration updates ParseInput and ContactsManager together; the portable frontend uses the same decoder and assembler. No executables, original diagnostic logs, credentials, or personal preference files are committed.
