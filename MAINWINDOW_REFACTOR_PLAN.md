# MainWindow Refactor Plan

## Muc tieu

Giam kich thuoc va do roi cua `Views/MainWindow.xaml.cs` theo tung buoc nho, it rui ro, de app van build/test duoc sau moi phase.

Khong chuyen full MVVM ngay. Khong tach XAML truoc. Uu tien tach logic thuan va cac cum chuc nang co bien ro truoc.

## Hien trang

`Views/MainWindow.xaml.cs` hien khoang 3387 dong va dang dong vai tro vua la `Window`, vua la controller chinh cua app.

Cac nhom trach nhiem dang nam trong `MainWindow`:

- Khoi tao UI, command binding, theme, settings.
- Quan ly memory tab, active buffer, ten tab, source filename.
- Hex editor rows, scroll, selection status.
- Open/save/drag-drop BIOS file.
- Programmer detection va cac thao tac read/write/verify/erase/read ID.
- Operation runner, progress, busy state, stop/cancel, log/status.
- IC catalog search/add/apply.
- Search/replace/marker trong hex buffer.
- Clear ME workflow.
- Tools BIOS: find Windows key, merge/split BIOS, unlock OEM.

Services hien co da tach duoc mot phan logic nang:

- `ProgrammerDetectionService`
- `ClearMeSingleBiosService`
- `ClearMeCandidateFinder`
- `MeaAnalyzer`
- `WindowsKeyFinder`
- `Unlock8Fc8Service`
- `OemPasswordUnlockService`
- `LargeFileIo`
- Marker/preset stores

## Huong tach de xuat

### 1. Tach pure hex/search utilities truoc

Tao service static kieu `HexSearchService` hoac `HexPattern`.

Nen keo ra:

- Parse hex pattern.
- Format hex pattern.
- Parse offset hex.
- Find bytes forward/backward.
- Find all bytes.
- Text/ASCII search helper.
- Replace pattern validation neu phu hop.

Ly do:

- Logic thuan, it phu thuoc WPF.
- De test bang xUnit.
- Diff nho, rui ro thap.
- Loai duoc phu thuoc xau: dialog nhu `FillSelectionWindow` / `HexMarkerWindow` khong can goi `MainWindow.TryParseHexPattern`.

Trade-off:

- Giam so dong MainWindow chua qua nhieu ngay lap tuc.
- Doi lai tao nen bien ro cho cac phase sau.

### 2. Tach Memory tab / buffer orchestration

Tao `MemoryBufferService`, `MemoryDocumentService`, hoac `MemoryTabController`.

Nen keo ra:

- Model cho memory buffer/tab.
- Source filename/display name.
- Add/close/rename/select tab logic neu co the.
- Merge/split buffer logic thuan.
- Suggested file name / unique file name helper.

Ly do:

- Nhieu feature khac deu xoay quanh `_buffer` va `_activeMemoryTab`.
- Tach duoc lop nay se lam cac tool BIOS va file I/O gon hon.

Trade-off:

- Rui ro vua, vi hien dang mutate truc tiep `HexEditor`, `HexScrollBar`, `_buffer`, `_activeMemoryTab`.
- Nen tach logic thuan truoc, UI tab orchestration de sau.

### 3. Tach BIOS tools

Tao `BiosToolService` hoac cac service nho theo tung nhom.

Nen gom:

- Find Windows key orchestration.
- Merge BIOS.
- Split BIOS.
- Unlock ACER/ASUS/HP/DELL wrappers.

Ly do:

- Cac action nay co workflow giong nhau: lay buffer, chay service, tao tab moi, goi save.
- MainWindow nen chi con mo dialog, goi service, cap nhat UI.

Trade-off:

- Can can than voi MessageBox/log/save dialog.
- Nen lam sau khi buffer/file save logic da co bien ro.

### 4. Tach programmer workflow facade sau cung

Co the tao `ProgrammerOperationService` hoac `ProgrammerWorkflow`.

Nen gom:

- Detect/read ID/read/write/verify/erase.
- Script erase-write-verify.
- Cancellation/progress/log contract.

Ly do:

- Loi ich kien truc lon nhat, nhung cung rui ro cao nhat.

Trade-off:

- Dinh nhieu UI state: progress bar, busy state, Stop, MessageBox, hardware timing.
- Chi nen lam khi cac phan buffer/search/tools da gon lai.

## Thu tu thuc hien

### Phase 1: Hex search/pattern extraction

Deliverable:

- Them `Services/HexPattern.cs` hoac `Services/HexSearchService.cs`.
- Them test cho parse/format/search.
- Dialog khong con phu thuoc static method cua `MainWindow`.
- `MainWindow` chi goi service.

Verification:

- `dotnet test .\Tests\NexusProgrammer.Tests\NexusProgrammer.Tests.csproj -p:UseSharedCompilation=false`
- `dotnet build .\NexusProgrammer.csproj -p:UseSharedCompilation=false`

Status: [OK]

### Phase 2: Buffer/file helper extraction

Deliverable:

- Tach trim metadata, suggested filename, unique filename, merge/split buffer logic thuan.
- Them test cho merge/split/name/trim neu co case ro.

Verification:

- Test targeted cho service moi.
- Full test + build app.

Status: [OK]

### Phase 3: BIOS tools orchestration

Deliverable:

- Giam code trong cac handler Merge/Split/Unlock/Find key.
- MainWindow giu vai tro UI coordinator.
- Logic tinh toan nam trong service co test.

Verification:

- Unit test cho service.
- Manual smoke test cac dialog lien quan neu co UI.
- Full test + build app.

Status: [OK]

### Phase 4: Programmer workflow

Deliverable:

- Rut bot detect/read/write/verify/erase/script ra facade rieng.
- Giu ro contract progress/cancel/log.
- Khong thay doi hanh vi hardware neu khong can.

Verification:

- Unit test voi `MockProgrammer`.
- Full test + build app.
- Manual smoke test voi CH341/CH347/T48/RT809 neu co thiet bi.

Status: [OK]

## Nguyen tac khi lam

- Moi phase la mot commit rieng.
- Moi phase phai build/test xanh truoc khi sang phase tiep.
- Uu tien tach logic thuan truoc UI.
- Khong doi ten/hien thi UI neu khong can.
- Khong refactor dong thoi nhieu subsystem.
- Neu mot phase bat dau phinh diff qua lon, cat nho lai.

## Buoc tiep theo duoc de xuat

Bat dau voi Phase 1: tach `HexPattern` / `HexSearchService`.

Day la buoc it rui ro nhat vi logic thuan, de test, va giam phu thuoc sai giua cac dialog voi `MainWindow`.
