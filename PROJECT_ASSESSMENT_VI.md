# Đánh giá tổng quan dự án Nexus Programmer

Ngày đánh giá: 2026-09-05

## Tóm tắt nhanh

Nexus Programmer là ứng dụng WPF chạy trên .NET 8 dành cho nạp và xử lý BIOS với các máy nạp CH341, CH347, XGecu T48, RT809F và RT809H. Dự án đã đi khá xa so với một tool nhỏ: có hex editor riêng, nhiều tab Memory, search/replace/go to, marker, fill selection, catalog IC, driver/SDK phần cứng, Clear ME Intel BIOS, unlock một số loại BIOS password, update app và theme.

Nhận định ngắn: nền tảng chức năng tốt, đi đúng hướng thực dụng, nhưng đang bắt đầu chịu sức nặng của việc phát triển nhanh. Điểm cần ưu tiên nhất là tách bớt `MainWindow.xaml.cs`, thêm test cho logic không cần phần cứng, và làm nút Stop thành hủy thao tác thật.

## Cấu trúc hiện tại

- `NexusProgrammer.csproj`: app WPF `net8.0-windows`, version hiện tại `1.1.3.19`, reference trực tiếp các SDK T48, RT809F, RT809H
- `Views/`: toàn bộ cửa sổ WPF như MainWindow, Clear ME, Search/Replace, Hex Marker, Fill Selection, Merge/Split BIOS, Settings, Search IC
- `Controls/HexEditorView.cs`: custom hex editor tự render bằng `FrameworkElement`
- `Programmers/`: adapter app-level cho CH34x, T48, RT809F, RT809H và mock programmer
- `SDK/`: SDK độc lập/experimental cho T48, RT809F, RT809H
- `Services/`: logic nghiệp vụ như MEAnalyzer, Clear ME, update, theme, catalog marker/fill, unlock, Windows key finder
- `Catalog/`: catalog SPI NOR và loader
- `MEAnalyzer/`: công cụ hỗ trợ phân tích Intel ME
- `Drivers/`: driver zip cho các programmer

## Điểm mạnh

- Abstraction phần cứng đã có nền tốt qua `IChipProgrammer` tại `Programmers/IChipProgrammer.cs:3-13`
- App hỗ trợ nhiều máy nạp nhưng vẫn gom thao tác chuẩn về cùng interface: detect, read ID, read, write, verify, unprotect, erase
- Hex editor tự viết khá đầy đủ: render nhanh, chọn vùng, copy/paste, fill selection, undo/redo, search highlight và theo dõi offset
- Clear ME đã tách thành service riêng, có retry nhiều ME Region/FIT, xử lý nhánh legacy ME 1-10, fallback replace ME thủ công, và xử lý lỗi MFS/EFS
- MEAnalyzer được gọi như tool hỗ trợ, có parser JSON/console output và thêm heuristic nhận diện Intel firmware
- UI có nhiều workflow thực tế cho thợ BIOS: nhiều Memory tab, merge/split, rename tab, marker, fill preset, Windows key, unlock Acer/Asus/HP/DELL 8FC8
- Catalog IC có cả nguồn mặc định và user-defined, giúp app tự mở rộng mà không cần sửa code
- Build hiện tại pass sạch: `dotnet build .\NexusProgrammer.csproj -o .\tmp\build-check` thành công với `0 Warning(s), 0 Error(s)`

## Rủi ro và technical debt

### 1. MainWindow đang quá lớn

`Views/MainWindow.xaml.cs` hiện khoảng 3359 dòng, còn `Views/MainWindow.xaml` khoảng 810 dòng. MainWindow đang chứa quá nhiều vai trò:

- điều phối phần cứng
- quản lý tab Memory
- thao tác file
- read/write/verify/erase
- search/replace/go to
- Clear ME orchestration
- unlock BIOS password
- update app
- log/progress/status
- theme/UI state

Điều này làm thay đổi nhỏ dễ ảnh hưởng chéo. Ví dụ sửa tên tab phải lần theo Merge/Split/Clear ME/log vì label được tạo ở nhiều nơi.

Khuyến nghị: tách dần theo cụm, không cần đại phẫu một lần. Ưu tiên tách `MemoryTabService`, `HexSearchService`, `ProgrammerWorkflowService`, `FileBufferService`.

### 2. Nút Stop chưa hủy thao tác thật [ ]

`Stop_Click` hiện chỉ log thông báo tại `Views/MainWindow.xaml.cs`, trong khi `IChipProgrammer` chưa nhận `CancellationToken`. Với thao tác read/write/erase trên chip thật, người dùng thấy có nút Stop nhưng kỳ vọng có thể dừng tác vụ.

Rủi ro: UX gây hiểu nhầm, nhất là khi write/erase bị treo hoặc người dùng chọn sai chip.

Khuyến nghị:

- thêm `CancellationToken` vào `IChipProgrammer`
- truyền token xuống từng SDK/adapter
- `Stop` gọi cancel source của operation hiện tại
- log rõ thao tác nào có thể dừng ngay, thao tác nào phải chờ hết page/block

### 3. Thiếu test project

Mi không thấy project test qua quét các pattern `*Tests*.csproj`, `*.Tests.csproj`, `*.Test.csproj`, `*test*.csproj`.

Các phần nên test trước vì không cần phần cứng:

- parse catalog IC trong `Catalog/IcCatalogLoader.cs`
- parse hex/text/offset cho Search/Replace
- `WindowsKeyFinder`
- `ClearMeCandidateFinder`
- `MeaAnalyzer.ParseInfo` và parser output MEA
- Clear ME region replacement, legacy ME 1-10, MFS/EFS fallback
- updater version parsing và chọn asset

Không cần mock hardware ngay từ đầu. Chỉ cần test pure logic trước là đã giảm nhiều regression.

### 4. Một số I/O lớn còn chạy đồng bộ

Load BIOS đang dùng `File.ReadAllBytes` trong luồng UI tại `Views/MainWindow.xaml.cs`. Với BIOS 64 MB hoặc 128 MB, thao tác có thể làm UI khựng.

Khuyến nghị: chuyển load/save buffer lớn sang async hoặc `Task.Run`, kèm progress/log tối thiểu.

### 5. Updater cần thêm lớp an toàn

`Services/UpdateService.cs` có chức năng tải release zip và chuẩn bị update. Đây là tính năng tiện, nhưng là bề mặt supply-chain nhạy cảm.

Khuyến nghị:

- whitelist asset name thật chặt
- thêm checksum/signature nếu release flow hỗ trợ
- log rõ URL và version đang tải
- cân nhắc để update chỉ download/mở thư mục, còn overwrite app cần xác nhận rõ

### 6. Repo có dấu hiệu chứa output/build artifact

Trong repo có nhiều thư mục/file dạng `bin`, `obj`, `tmp` hoặc artifact tạm từ build WPF/SDK. Dù `.csproj` đã exclude `SDK` và `tmp` khỏi compile chính, repo vẫn nên sạch hơn để tránh commit nhầm.

Khuyến nghị:

- rà lại `.gitignore`
- xóa artifact khỏi git nếu đã bị track
- giữ `SDK/` source, nhưng không track `bin/obj`
- thêm checklist release/build

## Nhận định theo module

### UI và workflow

UI hiện rất giàu chức năng và phục vụ đúng workflow BIOS thực tế. Việc có nhiều tab Memory, merge/split, rename tab, marker, fill selection, search/replace riêng cửa sổ là hướng tốt.

Điểm cần để ý là MainWindow đang làm quá nhiều. Nếu tiếp tục thêm tính năng vào cùng file, tốc độ sửa ban đầu vẫn nhanh nhưng chi phí debug sẽ tăng mạnh.

### Hex Editor

`Controls/HexEditorView.cs` là một custom control khá có giá trị. Nó tránh phụ thuộc control nặng, tự render nên có thể tối ưu cho buffer lớn.

Điểm nên cải thiện:

- tách parsing copy/paste/fill ra helper để test được
- thêm test cho selection range, replace, fill, undo/redo
- cân nhắc expose command rõ ràng cho context menu thay vì logic nằm trong constructor

### Programmer layer

`IChipProgrammer` là hướng đúng. App-level adapter cho từng máy giúp MainWindow không gọi SDK trực tiếp quá sâu.

Điểm nên cải thiện:

- thêm cancellation
- chuẩn hóa progress stage thay vì chỉ `int`
- ghi rõ khả năng từng programmer: có/không có verify native, page write, erase mode, 1.8V flow
- detect nhiều thiết bị cùng lúc cần model rõ hơn nếu sau này hỗ trợ chọn serial/location

### Clear ME và MEAnalyzer

Clear ME hiện là module mạnh nhất về nghiệp vụ. Logic đã xử lý nhiều case thực tế: candidate ME/FIT, retry toàn bộ, legacy ME 1-10 không dùng FIT, MFS/EFS fallback, manual replace theo checkbox.

Điểm nên cải thiện:

- thêm test fixture bằng BIOS/ME sample nhỏ hoặc synthetic layout
- tách parser FIT output và detect MFS/EFS thành hàm public/internal testable
- lưu summary dạng structured result nhiều field hơn, thay vì ghép string nhiều nơi

### Catalog IC

Catalog TSV + user TSV là cách nhẹ và hợp lý. Có thể thêm IC không cần rebuild app.

Điểm nên cải thiện:

- validate duplicate JEDEC/device/page bytes
- backup `User_SPI_NOR.tsv` trước khi ghi
- thêm import/export user catalog nếu người dùng nhiều máy

### Documentation

README đã mô tả tính năng, driver, SDK và shortcut. Đây là đủ cho người dùng phổ thông bắt đầu.

Thiếu phần cho developer:

- cách build app
- cách build MEAnalyzer
- cách build từng SDK
- cách thêm programmer mới
- cách thêm IC mới
- quy trình release/update
- ma trận phần cứng đã test

## Ưu tiên cải thiện đề xuất

### P0 - Nên làm sớm

1. Làm nút Stop thành cancel thật bằng `CancellationToken`
2. Thêm test project cho logic không cần hardware
3. Tách bớt `MainWindow.xaml.cs` theo cụm chức năng nóng nhất

### P1 - Làm sau P0

1. Làm sạch repo khỏi build artifact
2. Tăng an toàn updater bằng checksum/signature/whitelist
3. Chuyển load/save BIOS lớn sang async có progress
4. Thêm docs developer setup và release checklist

### P2 - Cải thiện dần

1. Chuẩn hóa progress/log event thành model có stage/message/percent
2. Thêm metadata capability cho từng programmer
3. Thêm import/export cho marker, fill preset, user catalog
4. Tách theme/style chung cho các dialog nhỏ để UI đều hơn

## Kết luận

Dự án đang ở trạng thái chức năng tốt, build sạch, và đã có nhiều quyết định đúng: abstraction phần cứng, service riêng cho Clear ME/MEA, SDK tách riêng, catalog mở rộng được, UI bám workflow thật.

Vấn đề chính không phải là thiếu tính năng, mà là dự án đã lớn tới ngưỡng cần thêm kỷ luật kỹ thuật: test, cancellation thật, tách orchestration khỏi MainWindow, và làm sạch release/update flow.

Nếu làm theo thứ tự P0 trước, dự án sẽ dễ phát triển tiếp hơn nhiều mà không cần viết lại từ đầu.
