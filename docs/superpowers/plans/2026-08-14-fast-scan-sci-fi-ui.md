# Fast Scan and Sci-Fi UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ลดเวลาการแพตช์โดยอ่านหน่วยความจำหนึ่งรอบสำหรับทุก feature และทำหน้าคอนโซลให้มีธีม sci-fi ที่อ่านง่ายขึ้น

**Architecture:** เพิ่ม batch API ใน `GamePatcher` ที่รับรายการ feature และสแกน readable regions หนึ่งครั้ง โดยเก็บผล match แยกตาม feature จากนั้นให้ `Program` ใช้ API นี้เมื่อเปิด/ปิดหลาย featureพร้อมกัน ส่วนการใช้ feature เดี่ยวจะคง API เดิมไว้ และ UI จะเป็น ANSI-aware console presentation ที่ไม่เพิ่ม dependency

**Tech Stack:** C# / .NET 8 console app / Windows `ReadProcessMemory` APIs

## Global Constraints

- รักษาคำสั่ง CLI และรูปแบบ feature เดิม
- ไม่เพิ่ม NuGet dependency
- ไม่แก้ไขไฟล์ build output ใน `bin` และ `obj` โดยตรง
- ต้องรองรับ terminal ที่ไม่แสดง ANSI โดยยังใช้งานได้

---

### Task 1: Batch Memory Scan

**Files:**
- Modify: `GamePatcher.cs`

**Interfaces:**
- Add `ApplyMany(IReadOnlyList<PatchFeature> features, bool enable)` returning `IReadOnlyList<PatchReport>`.
- Keep `Apply(PatchFeature feature, bool enable)` behavior-compatible.

- [ ] สร้าง pattern descriptors และอ่านแต่ละ region เพียงครั้งเดียว
- [ ] ตรวจ original/patched pattern ของทุก feature ใน buffer เดียว
- [ ] เขียนผลตาม report เดิมและไม่เขียน address ซ้ำใน feature เดียว
- [ ] ให้ `Apply` เรียกกลไก batch เพื่อไม่ให้มี logic ซ้ำ

### Task 2: Reuse Batch Results

**Files:**
- Modify: `Program.cs`

**Interfaces:**
- Use `GamePatcher.ApplyMany` for the default `--all` and interactive multi-selection paths.

- [ ] รวม features ที่ต้อง enable/restore เป็นชุดก่อนสแกน
- [ ] แสดงผลราย feature ผ่าน helper เดิม
- [ ] คง `--feature on|off` แบบ feature เดียวไว้

### Task 3: Sci-Fi Console Presentation

**Files:**
- Modify: `Program.cs`
- Modify: `README.md`

- [ ] เพิ่มหัวโปรแกรมแบบ sci-fi และสถานะ connection ที่อ่านง่าย
- [ ] ใช้สี ANSI เฉพาะเมื่อ terminal รองรับ และ fallback เป็น plain text
- [ ] เพิ่ม progress/status ที่สื่อว่าใช้ FAST SCAN
- [ ] อัปเดตคู่มือให้สะท้อน UI และประสิทธิภาพใหม่

### Task 4: Verification

**Files:**
- No new test project; verify existing executable build and CLI help.

- [ ] รัน `dotnet build -c Release`
- [ ] รัน `dotnet run -- --help`
- [ ] ตรวจ `git diff --check` และดู diff เฉพาะ source/docs

