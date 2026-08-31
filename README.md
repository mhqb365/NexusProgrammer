# Nexus Programmer

![Nexus Programmer](NexusProgrammer.png)

## English

English | [Tiếng Việt](#tiếng-việt)

Flashing BIOS application for CH341, CH347, XGecu T48, RT809F and RT809H

## Features

- Supports CH341, CH347, XGecu T48, RT809F and RT809H programmers
- Automatic IC detection and search, add new ICs not currently in the list
- Hex-format buffer preview and editing
- Read, write, verify, erase, and execute workflow scripts
- Support Clear ME Intel BIOS

## Requirements

- Windows 10/11
- .NET 8
- CH341/CH347 Driver 👉 [DriverCH341+CH347.zip](Drivers/DriverCH341+CH347.zip)
- XGecu T48 Driver 👉 [DriverXGecuT48.zip](Drivers/DriverXGecuT48.zip)
- RT809F Driver 👉 [DriverRT809F.zip](Drivers/DriverRT809F.zip)
- RT809H Driver 👉 [DriverRT809H.zip](Drivers/DriverRT809H.zip)

## SDKs

This repository includes experimental .NET hardware SDKs which can be used
outside the WPF application:

- [XGecu T48 SDK](SDK/T48SDK/README.md): WinUSB-based SPI25 support for device
  discovery, JEDEC ID, read, blank check, erase, write, sparse write, transfer
  logging and protocol experiments.
- [RT809F SDK](SDK/RT809FSDK/README.md): FTDI D2XX-based SPI-NOR support for
  discovery, JEDEC ID, read, blank check, erase, batched page program, verify,
  progress, cancellation and deterministic cleanup.
- [RT809H SDK](SDK/RT809HSDK/README.md): FTDI D2XX-based SPI-NOR support using
  RT809H-specific initialization captured from vendor workflows.

These SDKs are unofficial community projects and are not endorsed by the device
vendors. Treat erase and write operations as destructive and test new
integrations with sacrificial flash chips.

## Download

You can download the latest release from the [Releases page](https://github.com/mhqb365/NexusProgrammer/releases)

## Shortcuts

- Ctrl + Shift + N: Open a new application window
- Ctrl + N: Create a new ROM buffer
- Ctrl + O: Open a ROM file
- Ctrl + S: Save a ROM file
- Ctrl + Q: Exit the application

## License

MIT License

Parts of the CH341/CH347 SPI NOR chip catalog are generated from flashrom chip definitions and remain subject to the flashrom GPL-2.0-or-later license

See `THIRD_PARTY_NOTICES.md` and `flashrom-data/COPYING.rst` for more information

## Tiếng Việt

[English](#english) | Tiếng Việt

Ứng dụng nạp BIOS cho CH341, CH347, XGecu T48 và RT809F

## Tính năng

- Nhận dạng máy nạp CH341, CH347, XGecu T48 và RT809F
- Tự động nhận dạng, tìm kiếm IC, thêm IC mới nếu chưa có trong danh sách
- Xem trước/chỉnh sửa buffer dạng hex
- Đọc, ghi, xác minh, xóa và chạy workflow script
- Hỗ trợ Clear ME BIOS Intel

## Yêu cầu

- Windows 10/11
- .NET 8
- Driver CH341/CH347 👉 [DriverCH341+CH347.zip](Drivers/DriverCH341+CH347.zip)
- Driver XGecu T48 👉 [DriverXGecuT48.zip](Drivers/DriverXGecuT48.zip)
- Driver RT809F 👉 [DriverRT809F.zip](Drivers/DriverRT809F.zip)

## Tải về`

Bạn có thể tải bản phát hành mới nhất tại [trang Releases](https://github.com/mhqb365/NexusProgrammer/releases)

## Phím tắt

- Ctrl + Shift + N: Mở cửa sổ ứng dụng mới
- Ctrl + N: Tạo buffer ROM mới
- Ctrl + O: Mở file ROM
- Ctrl + S: Lưu file ROM
- Ctrl + Q: Thoát ứng dụng

## Giấy phép

MIT License

Một số phần trong danh sách IC SPI NOR của CH341/CH347 được tạo từ định nghĩa chip của flashrom và vẫn tuân theo giấy phép flashrom GPL-2.0-or-later

See `THIRD_PARTY_NOTICES.md` và `flashrom-data/COPYING.rst` để biết thêm thông tin
