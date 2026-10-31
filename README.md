# AD7606 UART / USB FIFO 版本

这份版本在原有 UART 连接上增加了 `USB FIFO` 选项。

- UART：继续使用原来的 `AA 01 START COUNT`，读取 1 或 8 个通道。
- USB FIFO：通过 FT232H D2XX 同步 FIFO，打开接口 0 后发送固定命令 `BB 01 00 08`，读取 16 字节并更新 8 个通道。
- USB DLL：程序使用 64 位 D2XX 导入名 `ftd2xx64.dll`。请把官方驱动目录中的 `amd64\ftd2xx64.dll` 放到程序 exe 同目录，或加入系统 PATH。

把 `MainForm.cs` 和 `Ftd2xxUsb.cs` 放进原来的 WinForms 工程后重新生成即可。连接方式下拉框选择 UART 或 USB FIFO；USB 模式默认打开 FTDI 接口索引 0。

当前环境无法直接写入 `D:\Project Docs\Visual Studio PRJ\AD7606Monitor`，所以改好的文件保存在本目录，请手动覆盖工程中的同名文件。
