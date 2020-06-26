# ATX2AT Smart Converter

*TL;DR – The ATX2AT Smart Converter is the ultimate tool to securely replace vintage power supplies with a standard PC ATX power supply. It’s open-source, open-hardware and firmware upgradable via USB.*

![enter image description here](https://x86.fr/wp-content/uploads/2019/11/intro-02s-1024x576.jpg)

Please check the project page for more information: [x86.fr/ATX2AT](https://x86.fr/atx2at)

## Content
This GIT repo is intended for hardware or software developers. 

 - BOM/ - Contains the Bill Of Material for the ATX2AT Smart Converter
 - Firmware/ - Contains the Firmware source & binary files
 - Gerbers/ - Contains the PCB Gerber (2-layers / 1.6mm / **2oz**)
 - Schematics/ - Contains the Circuit Schematic 
 - Tool/ - The companion the C# tool for live-monitoring (actually early Beta)

The companion tool only works on Windows 10 64-bit. 

The Firmware is compatible with the Arduino IDE 1.8.12+ configured for Arduino Micro. It only requires the Adafruit_GFX and Adafruit_SSD1306 libraries to drive the OLED Display. 

## EEPROM Value

Here are some tips about how the Internal EEPROM space is used on the ATX2AT Smart Converter.

    -------------
    EEPROM Values
    -------------
    0x00 : Magic Byte (0xAD)
    0x01 : FW Revision (Major)
    0x02 : FW Revision (Minor)
    0x03 : Screensaver Duration (minutes)
    0x04 : SlowBlow Delay (ms * 10)
    0x05-0x0B : RESERVED
    0x0C : Cal Factor (5V VOLTAGE)
    0x0D : Cal Factor (5V CURRENT)
    0x0E : Cal Factor (12V VOLTAGE)
    0x0F : Cal Factor (12V CURRENT)
    0x10 : Slot 0x00 (5V Setting)
    0x11 : Slot 0x01 (5V Setting)
    ...
    0x2F : Slot 0x31 (5V Setting)
    0x30 : Slot 0x01 (12V Setting)
    ...
    0x4F : Slot 0x31 (12V Setting)


## USB Protocol

And some quick tips about how data are exchanged between the Conf Tool and the ATX2AT Smart Converter

    ----------------------
    Communication protocol
    ----------------------
    Byte[0]  : 0x56 (Magic Header)
    Byte[1]  : 0x?? ('N' : Number of bytes in the whole trame, including checksum)
    Byte[2]  : 0x?? (Command Byte)
    Byte[3+] : 0x?? (Command Data - optional)
    Byte[N]  : 0x?? (Last Byte - XOR Checksum)
    
    Command Byte (Byte[2])
    ----------------------
    0xA0 : Get Status
    0xA1 : Get EEPROM from 0x00 to 0x3F
    0xA2 : Toggle Power
    0xB0 : Reset EEPROM to default Value
    0xB1 : Set EEPROM Byte (followed by Byte Address then Byte Data)
    0xB2 ; Set EEPROM 2-Bytes (to update current limit table)
    
    Reply to commands
    -----------------
    0xA0 : 0x56 0x0E 0xA0 PIN_STATUS[2] 5V_CURRENT[2] 12V_CURRENT[2] 5V_VOLT[2] 12V_VOLT[2] 0x(CHECKSUM)
    0xA1 : 0x56 0x06 0xA1 0x(BYTE ADR) 0x(BYTE VALUE) 0x(CHECKSUM)
    0xA2 : Same reply than 0xA0
    0xB0 : 0x56 0x05 0xB0 0x(CHECKSUM)
    0xB1 : 0x56 0x05 0xB1 0x(CHECKSUM)
    0xB2 : Same as 0xB1

## Author

Sam "Doc TB" DEMEULEMEESTER - [@d0cTB](https://twitter.com/d0cTB) 
Email : 01100100011001010111011001000000011110000011100000110110001011100110011001110010