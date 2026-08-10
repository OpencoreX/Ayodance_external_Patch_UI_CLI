# AyodanceID

เครื่องมือ patch หน่วยความจำสำหรับเกม Ayodance (ARM32) ทำงานบน Windows ผ่าน
`OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory` พร้อม `VirtualProtectEx`
เพื่อเขียนลง code page ที่กันการเขียน

## Build

```powershell
dotnet build -c Release
# output: bin\Release\net8.0\AyodanceID.exe
```

## วิธีใช้ (Command Line)

รันเป็น Administrator

```powershell
AyodanceID.exe <PID>                     # เปิด patch ทั้งหมด
AyodanceID.exe 1234                      # ตัวอย่าง
AyodanceID.exe 1234 --lockperfect on --autokey off
AyodanceID.exe 1234 --all off            # คืนค่า original ทั้งหมด
AyodanceID.exe 1234 --grade on           # scan User struct + เขียน Max Grade 2610
AyodanceID.exe --help
```

- ใส่แค่ PID โดยไม่มี feature = apply ทุก patch
- `on` = แทนที่ bytes original ด้วย bytes patch
- `off` = เขียน bytes original กลับคืน
- AOB scan เจอทุก address ที่ match → เขียนทั้งหมด (โดยเฉพาะ Autokey)

### ตาราง Features

| key | ชื่อ | Description |
|-----|------|-------------|
| `--lockperfect` | Lock Perfect | Force perfect timing `MOV R9,#5` → `#1` |
| `--unlockep` | Unlock EP | ฟังก์ชัน EP return 0 |
| `--ismission` | IsMissionComplete | return true |
| `--beatup` | Auto Perfect Beatup | `MusicStation_Prop_BeatupTouchTempo$$FadeoutNote` |
| `--bubble` | Auto Bubble | `MusicStation_Bubble_Note$$Update` |
| `--autokey` | Autokey | `MusicStation_Prop_NoteBoard$$Show` |
| `--grade` | Max Grade | scan User struct + เขียน 2610 |

### Interactive Mode

รันโดยไม่ส่ง arg → ใส่ PID แล้วเลือก

```text
Features to ENABLE (comma list, Enter = all): 1,3
Features to RESTORE to original (prefix with !, e.g. !1,3): !6
```

## วิธีใช้จาก C#

### 1. เรียก exe จากโปรเจกต์ C#

```csharp
using System.Diagnostics;

static async Task RunPatcherAsync(int pid, bool lockPerfect, bool autoKey)
{
    var args = new List<string> { pid.ToString() };
    if (lockPerfect) args.Add("--lockperfect on");
    if (autoKey)     args.Add("--autokey on");

    var psi = new ProcessStartInfo
    {
        FileName = @"C:\Users\KALIUNAI_PC\Desktop\AyodanceID\bin\Release\net8.0\AyodanceID.exe",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = true,
    };
    foreach (var a in args) psi.ArgumentList.Add(a);

    using var p = Process.Start(psi)!;
    string output = await p.StandardOutput.ReadToEndAsync();
    await p.WaitForExitAsync();
    Console.WriteLine(output);
}
```

### 2. ใช้คลาสโดยตรง (reference โปรเจกต์)

```csharp
using AyodanceID;

// 1) เปิด process
using var mem = new MemoryReader(pid);
var patcher = new GamePatcher(mem);

// 2) สร้าง feature เองได้ (กำหนด AOB เอง)
var lockPerfect = new PatchFeature(
    key: "lockperfect",
    name: "Lock Perfect",
    description: "Force perfect timing",
    originalHex: "05 90 A0 E3 00 00 58 E3 67 00 00 1A 28 50 9A E5",
    patchedHex:  "01 90 A0 E3 00 00 58 E3 67 00 00 1A 28 50 9A E5");

// 3) apply / restore
PatchReport on  = patcher.Apply(lockPerfect, enable: true);
PatchReport off = patcher.Apply(lockPerfect, enable: false);

Console.WriteLine($"applied {on.Applied}  addr: {string.Join(", ", on.Addresses)}");
Console.WriteLine($"restored {off.Restored}");
```

- `PatchReport.Applied` / `Restored` / `AlreadyDone` / `Failed` = จำนวน address
- `PatchReport.Addresses` = list address ที่เขียนจริง

### 3. scan แบบ raw

```csharp
(List<nint> orig, List<nint> patched) = patcher.FindPatterns(lockPerfect.Original, lockPerfect.Patched);
```

## หมายเหตุ

- ต้องรันด้วยสิทธิ์ Administrator
- ให้ PID ของ process เกม (เช่น emulator ที่รัน ARM build) ตรงกับเวอร์ชันเกม
- `--grade` เป็น scan โครงสร้าง User (เขียนค่าข้อมูล ไม่ใช่ code patch)
