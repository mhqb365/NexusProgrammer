# Nexus Programmer

![Nexus Programmer](NexusProgrammer.png)

## English

English | [Tiếng Việt](#tiếng-việt)

A modern BIOS programming tool for hardware technicians and repair professionals.

## Features

- Supports CH341, CH347, XGecu T48, RT809F and RT809H programmers.
- Automatic programmer detection, IC detection, IC catalog search and custom IC entries.
- Hex Editor with multi-buffer Memory tabs, hex/ASCII preview, copy/paste, replace selection, select block and fill selection.
- Search tools: find, go to offset, Hex Marker and Text Marker.
- BIOS buffer tools: compare, merge and split Memory buffers.
- OEM tools: find Windows OEM key and unlock ACER, ASUS, DELL and HP BIOS password regions.
- Intel Clear ME helper with ME Region/FIT selection, candidate retry and manual ME replacement fallback.
- Read, write, verify, erase, blank check and operation cancellation.
- Auto workflows for Read + Verify and Erase + Write + Verify.
- Theme support: Default, Arcade, Heritage, Violet and Matcha.

ME Region and FIT are not distributed with this project. You must prepare them yourself, or download here: [ME Region & FIT](https://drive.google.com/drive/folders/1ocp61oICeFGZuf-J59gpnLO88XGzKvPY?usp=sharing)

## Toolbar And Menus

- Main toolbar: New, Open, Save, Search IC, Detect, Read, Write, Verify, Erase, Auto and Stop.
- Hex Editor header:
  - Search: Find, Go to, Hex Marker and Text Marker.
  - Edit: Copy, Paste, Replace, Select block and Fill selection.
  - Tools: Merge buffer, Split buffer, Compare buffer, Find OEM key and BIOS unlock tools.
  - Clear ME: open the Intel Clear ME workflow.
- Hex Editor context menu: Copy, Paste, Replace, Select block, Fill selection and Clear buffer.

## Requirements

- Windows 10/11
- .NET 8
- CH341/CH347 Driver: [DriverCH341+CH347.zip](Drivers/DriverCH341+CH347.zip)
- XGecu T48 Driver: [DriverXGecuT48.zip](Drivers/DriverXGecuT48.zip)
- RT809F Driver: [DriverRT809F.zip](Drivers/DriverRT809F.zip)
- RT809H Driver: [DriverRT809H.zip](Drivers/DriverRT809H.zip)

## SDKs

This repository includes experimental .NET hardware SDKs which can be used outside the WPF application:

- [XGecu T48 SDK](https://github.com/mhqb365/T48.SDK): WinUSB-based SPI25 support for device discovery, JEDEC ID, read, blank check, erase, write, sparse write, transfer logging and protocol experiments.
- [RT809F SDK](https://github.com/mhqb365/RT809F.SDK): FTDI D2XX-based SPI-NOR support for discovery, JEDEC ID, read, blank check, erase, batched page program, verify, progress, cancellation and deterministic cleanup.
- [RT809H SDK](https://github.com/mhqb365/RT809H.SDK): FTDI D2XX-based SPI-NOR support using RT809H-specific initialization captured from vendor workflows.

These SDKs are unofficial community projects and are not endorsed by the device vendors. Treat erase and write operations as destructive and test new integrations with sacrificial flash chips.

## Build

Use the included build script to produce a versioned output folder:

```bat
Build.bat
```

The script builds the app and copies the build output to `build/NexusProgrammer_v<current version>`.

## Download

You can download the latest release from the [Releases page](https://github.com/mhqb365/NexusProgrammer/releases).

## Shortcuts

- Ctrl + Shift + N: Open a new application window
- Ctrl + N: Create a new ROM buffer
- Ctrl + O: Open a ROM file
- Ctrl + S: Save a ROM file
- Ctrl + F: Find
- Ctrl + G: Go to offset
- Ctrl + R: Replace selection
- Ctrl + C: Copy selection
- Ctrl + V: Paste at caret
- Ctrl + Q: Exit the application

## License

MIT License

Parts of the CH341/CH347 SPI NOR chip catalog are generated from flashrom chip definitions and remain subject to the flashrom GPL-2.0-or-later license.

See `THIRD_PARTY_NOTICES.md` and `flashrom-data/COPYING.rst` for more information.

## Discussion

Create a Discussion on GitHub to discuss features, report bugs and propose improvements. Or discuss on Telegram at [https://t.me/+O-3wSYgW95lkNThl](mhqb365's space).

## Tiếng Việt

[English](#english) | Tiếng Việt

Một công cụ nạp BIOS hiện đại dành cho kỹ thuật viên phần cứng và chuyên gia sửa chữa.

## Tính năng

- Hỗ trợ máy nạp CH341, CH347, XGecu T48, RT809F và RT809H.
- Tự động nhận dạng máy nạp, nhận dạng IC, tìm kiếm catalog IC và thêm IC tùy chỉnh.
- Hex Editor với nhiều tab Memory, xem hex/ASCII, copy/paste, replace selection, select block và fill selection.
- Công cụ tìm kiếm: Find, Go to offset, Hex Marker và Text Marker.
- Công cụ buffer BIOS: compare, merge và split các tab Memory.
- Công cụ OEM: tìm Windows OEM key và unlock vùng mật khẩu BIOS ACER, ASUS, DELL và HP.
- Hỗ trợ Clear ME Intel BIOS với chọn ME Region/FIT, retry candidate và fallback thay ME thủ công.
- Đọc, ghi, verify, xóa, blank check và hủy thao tác.
- Workflow Auto cho Read + Verify và Erase + Write + Verify.
- Hỗ trợ giao diện Default, Arcade, Heritage, Violet và Matcha.

ME Region và FIT không được phân phối kèm theo dự án này. Bạn phải tự chuẩn bị hoặc tải về tại đây: [ME Region & FIT](https://drive.google.com/drive/folders/1ocp61oICeFGZuf-J59gpnLO88XGzKvPY?usp=sharing)

## Thanh công cụ và menu

- Toolbar chính: New, Open, Save, Search IC, Detect, Read, Write, Verify, Erase, Auto và Stop.
- Header của Hex Editor:
  - Search: Find, Go to, Hex Marker và Text Marker.
  - Edit: Copy, Paste, Replace, Select block và Fill selection.
  - Tools: Merge buffer, Split buffer, Compare buffer, Find OEM key và các công cụ unlock BIOS.
  - Clear ME: mở workflow Clear ME Intel BIOS.
- Menu chuột phải trong Hex Editor: Copy, Paste, Replace, Select block, Fill selection và Clear buffer.

## Yêu cầu

- Windows 10/11
- .NET 8
- Driver CH341/CH347: [DriverCH341+CH347.zip](Drivers/DriverCH341+CH347.zip)
- Driver XGecu T48: [DriverXGecuT48.zip](Drivers/DriverXGecuT48.zip)
- Driver RT809F: [DriverRT809F.zip](Drivers/DriverRT809F.zip)
- Driver RT809H: [DriverRT809H.zip](Drivers/DriverRT809H.zip)

## SDK

Repository này có kèm các SDK phần cứng .NET thử nghiệm, có thể dùng độc lập với ứng dụng WPF:

- [XGecu T48 SDK](https://github.com/mhqb365/T48.SDK): hỗ trợ SPI25 qua WinUSB, gồm nhận dạng thiết bị, JEDEC ID, đọc, blank check, xóa, ghi, sparse write, log transfer và thử nghiệm protocol.
- [RT809F SDK](https://github.com/mhqb365/RT809F.SDK): hỗ trợ SPI-NOR qua FTDI D2XX, gồm nhận dạng thiết bị, JEDEC ID, đọc, blank check, xóa, ghi theo batch, verify, tiến trình, hủy thao tác và cleanup ổn định.
- [RT809H SDK](https://github.com/mhqb365/RT809H.SDK): hỗ trợ SPI-NOR qua FTDI D2XX với chuỗi khởi tạo riêng của RT809H được phân tích từ workflow của phần mềm hãng.

Các SDK này là dự án cộng đồng không chính thức và không được nhà sản xuất thiết bị xác nhận hay bảo trợ. Thao tác xóa và ghi có thể phá hủy dữ liệu trên chip, hãy thử nghiệm tích hợp mới bằng chip thử trước.

## Build

Dùng script có sẵn để tạo thư mục output theo version:

```bat
Build.bat
```

Script sẽ build app và copy output sang `build/NexusProgrammer_v<current version>`.

## Tải về

Bạn có thể tải bản phát hành mới nhất tại [trang Releases](https://github.com/mhqb365/NexusProgrammer/releases).

## Phím tắt

- Ctrl + Shift + N: Mở cửa sổ ứng dụng mới
- Ctrl + N: Tạo buffer ROM mới
- Ctrl + O: Mở file ROM
- Ctrl + S: Lưu file ROM
- Ctrl + F: Tìm kiếm
- Ctrl + G: Nhảy đến offset
- Ctrl + R: Replace selection
- Ctrl + C: Copy selection
- Ctrl + V: Paste tại con trỏ
- Ctrl + Q: Thoát ứng dụng

## Giấy phép

MIT License

Một số phần trong danh sách IC SPI NOR của CH341/CH347 được tạo từ định nghĩa chip của flashrom và vẫn tuân theo giấy phép flashrom GPL-2.0-or-later.

Xem `THIRD_PARTY_NOTICES.md` và `flashrom-data/COPYING.rst` để biết thêm thông tin.

## Thảo luận

Tạo Discussion trên GitHub để thảo luận về các tính năng, báo lỗi và đề xuất cải tiến. Hoặc thảo luận trên Telegram tại [https://t.me/+O-3wSYgW95lkNThl](mhqb365's space).
