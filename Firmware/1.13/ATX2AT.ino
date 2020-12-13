/*********************************************************************
ATX2AT Firmware 1.13
*********************************************************************/

/*  -------------
 *  EEPROM Values
 *  -------------
 *  0x00 : Magic Byte (0xAD)
 *  0x01 : FW Revision (Major)
 *  0x02 : FW Revision (Minor)
 *  0x03 : Screensaver Duration (minutes)
 *  0x04 : SlowBlow Delay (ms * 10)
 *  0x05-0x0B : RESERVED
 *  0x0C : Cal Factor (5V VOLTAGE)
 *  0x0D : Cal Factor (5V CURRENT)
 *  0x0E : Cal Factor (12V VOLTAGE)
 *  0x0F : Cal Factor (12V CURRENT)
 *  0x10 : Slot 0x00 (5V Setting)
 *  0x11 : Slot 0x01 (5V Setting)
 *  ...
 *  0x2F : Slot 0x31 (5V Setting)
 *  0x30 : Slot 0x01 (12V Setting)
 *  ...
 *  0x4F : Slot 0x31 (12V Setting)
 *  
 *  ----------------------
 *  Communication protocol
 *  ----------------------
 *  Byte[0]  : 0x56 (Magic Header)
 *  Byte[1]  : 0x?? ('N' : Number of bytes in the whole trame, including checksum)
 *  Byte[2]  : 0x?? (Command Byte)
 *  Byte[3+] : 0x?? (Command Data - optional)
 *  Byte[N]  : 0x?? (Last Byte - XOR Checksum)
 *  
 *  Command Byte (Byte[2])
 *  ----------------------
 *  0xA0 : Get Status
 *  0xA1 : Get EEPROM from 0x00 to 0x3F
 *  0xA2 : Toggle Power
 *  0xB0 : Reset EEPROM to default Value
 *  0xB1 : Set EEPROM Byte (followed by Byte Address then Byte Data)
 *  0xB2 ; Set EEPROM 2-Bytes (to update current limit table)
 *  
 *  Reply to commands
 *  -----------------
 *  0xA0 : 0x56 0x0E 0xA0 PIN_STATUS[2] 5V_CURRENT[2] 12V_CURRENT[2] 5V_VOLT[2] 12V_VOLT[2] 0x(CHECKSUM)
 *  0xA1 : 0x56 0x06 0xA1 0x(BYTE ADR) 0x(BYTE VALUE) 0x(CHECKSUM)
 *  0xA2 : Same reply than 0xA0
 *  0xB0 : 0x56 0x05 0xB0 0x(CHECKSUM)
 *  0xB1 : 0x56 0x05 0xB1 0x(CHECKSUM)
 *  0xB2 : Same as 0xB1
 *    
 */
 
#include <EEPROM.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

#define REV_STR "ATX2AT CONVERTER 1.13"

// Minimum time between actions (in milliseconds)
#define TIMEBTACTIONS 500

// Display Refresh rate in fast blow mode
#define FB_REF_DELAY 2500

// Display Refresh rate in slow blow mode
#define SB_REF_DELAY 500

// Allowed Overshot in Slow-Blow
#define SBOS5V 1.75
#define SBOS12V 1.75

// Set ADC Smoothing period (ms)
#define ADC_SMOOTH 50

// PIN Assignements
#define OLED_RESET PD2

#define SW1 22 // PF1
#define SW2 21 // PF4
#define SW3 20 // PF5
#define SW4 19 // PF6
#define SW5 18 // PF7
#define SW6 8 // PB4

#define BTN 11 // PB7

#define PWRON 7 // PE6
#define PWROK 13 // PC7

#define TARGET_PWRON 30 // PD5

#define TARGET_CUSTOM 6 // PD6

#define MS5 0 // PD2
#define MS12 1 // PD3

#define MS12NEG 23 // PB0? Need check!

#define AMP_12V 9 // PB5
#define AMP_5V 10 // PB6

#define VOLT_12V A11 // PD6
#define VOLT_5V A6 // PD4

// Vars
unsigned int sb_delay = 150;
unsigned long ssaver_delay = 900000;
bool ssaver_enabled;

bool curpower = false;
bool ocurrent5 = false;
bool ocurrent12 = false;
bool slowblow = true;

bool extpwr = false;

bool display_enabled = true;

unsigned long lastaction;
unsigned long blowtimer5v, blowtimer12v;
unsigned long fblowtimer, sblowtimer, srdtimer;

unsigned int cal5v_amp, cal12v_amp, cal5v, cal12v;
float cur_5v, cur_12v;
  
byte SerCMD[20];
byte SerSend[16];
bool headerByteFound = false;
unsigned int RcdvBytes = 0;
byte Sendcsum = 0x00;

// Instantiate OLED Class
Adafruit_SSD1306 display(128, 64, &Wire, OLED_RESET);

// Default Max Current for the first 3 switches (Settings_5V) and for the next 2 switches (Settings_12V)
// If you change this, remember to reset the EEPROM to have the new value written in EEPROM.
float Settings_5V[] = { 4.00, 1.00, 2.25, 3.25, 4.75, 5.50, 6.75, 8.00 };
float Settings_12V[] = { 0.50, 1.50, 3.00, 4.75 };
byte Default_EEPROM[] = { 0xAD, 0x01, 0x0D, 0x0F, 0x0F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x80, 0x80, 0x80 };

// The big "ALERT" Bitmap displayed when overcurrent is detected
const PROGMEM unsigned char OC [] = {
  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 
  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 
  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xf1, 0xf3, 0xfc, 0xc0, 0x3c, 0x0f, 0xff, 0xff, 0xff, 
  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xe0, 0xf1, 0xf8, 0xc0, 0x3c, 0x07, 0xff, 0xff, 0xff, 
  0xff, 0xff, 0xc7, 0xff, 0xff, 0xff, 0xff, 0xc0, 0x79, 0xf9, 0xc0, 0x3c, 0x03, 0xff, 0xff, 0xff, 
  0xff, 0xff, 0x83, 0xff, 0xff, 0xff, 0xff, 0xce, 0x79, 0xf9, 0xcf, 0xfc, 0xf3, 0xff, 0xff, 0xff, 
  0xff, 0xff, 0x83, 0xff, 0xff, 0xff, 0xff, 0x9f, 0x38, 0xf1, 0xcf, 0xfc, 0xf3, 0xff, 0xff, 0xff, 
  0xff, 0xff, 0x39, 0xff, 0xff, 0xff, 0xff, 0x9f, 0x3c, 0xf3, 0xcf, 0xfc, 0xf3, 0xff, 0xff, 0xff, 
  0xff, 0xff, 0x7d, 0xff, 0xff, 0xff, 0xff, 0x9f, 0x3c, 0xf3, 0xc0, 0x7c, 0x03, 0xff, 0xff, 0xff, 
  0xff, 0xfe, 0x7c, 0xff, 0xff, 0xff, 0xff, 0x9f, 0x3c, 0xf3, 0xc0, 0x7c, 0x07, 0xff, 0xff, 0xff, 
  0xff, 0xfe, 0xfe, 0xff, 0xff, 0xff, 0xff, 0x9f, 0x3e, 0x67, 0xc0, 0x7c, 0x0f, 0xff, 0xff, 0xff, 
  0xff, 0xfc, 0xfe, 0x7f, 0xff, 0xff, 0xff, 0x9f, 0x3e, 0x67, 0xcf, 0xfc, 0xc7, 0xff, 0xff, 0xff, 
  0xff, 0xfd, 0xff, 0x7f, 0xff, 0xff, 0xff, 0x9f, 0x3e, 0x67, 0xcf, 0xfc, 0xe7, 0xff, 0xff, 0xff, 
  0xff, 0xf9, 0xef, 0x3f, 0xff, 0xff, 0xff, 0xce, 0x7e, 0x07, 0xcf, 0xfc, 0xe3, 0xff, 0xff, 0xff, 
  0xff, 0xf9, 0xc7, 0x3f, 0xff, 0xff, 0xff, 0xc0, 0x7f, 0x0f, 0xc0, 0x3c, 0xf3, 0xff, 0xff, 0xff, 
  0xff, 0xf3, 0xc7, 0x9f, 0xff, 0xff, 0xff, 0xe0, 0xff, 0x0f, 0xc0, 0x3c, 0xf3, 0xff, 0xff, 0xff, 
  0xff, 0xf3, 0xc7, 0x9f, 0xff, 0xff, 0xff, 0xf1, 0xff, 0x0f, 0xc0, 0x3c, 0xf9, 0xff, 0xff, 0xff, 
  0xff, 0xe7, 0xc7, 0xcf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 
  0xff, 0xe7, 0xc7, 0xcf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 
  0xff, 0xcf, 0xc7, 0xe7, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 
  0xff, 0xcf, 0xc7, 0xe7, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 
  0xff, 0x9f, 0xc7, 0xf3, 0xff, 0xc7, 0xe7, 0xcf, 0x01, 0xf0, 0x1f, 0x00, 0xf3, 0xe7, 0x00, 0x3f, 
  0xff, 0x9f, 0xc7, 0xf3, 0xff, 0x83, 0xe7, 0xcf, 0x01, 0xf0, 0x1f, 0x00, 0xf1, 0xe7, 0x00, 0x3f, 
  0xff, 0x3f, 0xef, 0xf9, 0xff, 0x03, 0xe7, 0xcf, 0x00, 0xf0, 0x0f, 0x00, 0xf1, 0xe7, 0x00, 0x3f, 
  0xff, 0x3f, 0xff, 0xf9, 0xff, 0x31, 0xe7, 0xcf, 0x3c, 0xf3, 0xcf, 0x3f, 0xf0, 0xe7, 0xf3, 0xff, 
  0xfe, 0x7f, 0xff, 0xfc, 0xfe, 0x7b, 0xe7, 0xcf, 0x3c, 0xf3, 0xcf, 0x3f, 0xf0, 0xe7, 0xf3, 0xff, 
  0xfe, 0x7f, 0xef, 0xfc, 0xfe, 0x7f, 0xe7, 0xcf, 0x3c, 0xf3, 0xcf, 0x3f, 0xf0, 0x67, 0xf3, 0xff, 
  0xfc, 0x7f, 0xc7, 0xfc, 0x7e, 0x7f, 0xe7, 0xcf, 0x00, 0xf0, 0x0f, 0x01, 0xf0, 0x67, 0xf3, 0xff, 
  0xfc, 0xff, 0xc7, 0xfe, 0x7e, 0x7f, 0xe7, 0xcf, 0x01, 0xf0, 0x1f, 0x01, 0xf3, 0x67, 0xf3, 0xff, 
  0xfc, 0xff, 0xef, 0xfe, 0x7e, 0x7f, 0xe7, 0xcf, 0x03, 0xf0, 0x3f, 0x01, 0xf3, 0x07, 0xf3, 0xff, 
  0xfc, 0xff, 0xff, 0xfe, 0x7e, 0x7f, 0xe7, 0xcf, 0x31, 0xf3, 0x1f, 0x3f, 0xf3, 0x07, 0xf3, 0xff, 
  0xfc, 0x7f, 0xff, 0xfc, 0x7e, 0x79, 0xe7, 0xcf, 0x39, 0xf3, 0x9f, 0x3f, 0xf3, 0x87, 0xf3, 0xff, 
  0xfc, 0x00, 0x00, 0x00, 0x7f, 0x31, 0xe3, 0x8f, 0x38, 0xf3, 0x8f, 0x3f, 0xf3, 0x87, 0xf3, 0xff, 
  0xfe, 0x00, 0x00, 0x00, 0xff, 0x03, 0xf0, 0x1f, 0x3c, 0xf3, 0xcf, 0x00, 0xf3, 0xc7, 0xf3, 0xff, 
  0xff, 0x00, 0x00, 0x01, 0xff, 0x83, 0xf0, 0x1f, 0x3c, 0xf3, 0xcf, 0x00, 0xf3, 0xc7, 0xf3, 0xff, 
  0xff, 0xff, 0xff, 0xff, 0xff, 0xc7, 0xf8, 0x3f, 0x3e, 0x73, 0xe7, 0x00, 0xf3, 0xe7, 0xf3, 0xff, 
  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 
  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 
};

// Set EEPROM Bytes.
// Fancy bit manipulation is here because physical DIP SWITCH are MSB while EEPROM Byte are LSB. 
void SetE2PROM(bool reset = false)
{  
  byte i = 0;
  byte slot = 0;

  // If EEPROM not set (first run) or Reset requested, then proceed
  if((EEPROM[0] != 0xAD) || reset)
  {    
    // Set the Default Bytes at ADR 0x00-0x0F (General Settings)
    for(i = 0; i < 0x10; i++)
    {
      if(reset && i > 9) { continue; } // If reset requested, don't reset cal factors and serial (0x0A-0x0F)
      EEPROM[i] = Default_EEPROM[i];
    }
  
    //Set Current Limit Value at ADR 0x10-0x2F (5V) & 0x30-0x4F (12V)
    for(i = 0x10; i < 0x30; i++)
    {
      slot = ~flipByte((i - 0x10) << 3) & 7;
      EEPROM[i] = (byte)(Settings_5V[slot] * 20.0);
      
      slot = ~flipByte((i - 0x10) << 6) & 3;
      EEPROM[i+0x20] = (byte)(Settings_12V[slot] * 20.0);       
    }  
  }
}

void reinit()
{
  // reset lastaction
  lastaction = millis();

  // Set SlowBlow Delay from EEPROM
  sb_delay = (unsigned int)EEPROM[4]*10;

  // Set Screensaver timeout from EEPROM
  if(EEPROM[3] > 0) { ssaver_delay = (unsigned long)EEPROM[3]*1000*60; }

  // Enable or Disable ScreenSaver
  ssaver_enabled = (EEPROM[3] == 0xFF) ? false : true;

  // Set Cal Factor
  cal5v = EEPROM[0x0C];
  cal5v_amp = EEPROM[0x0D];
  cal12v = EEPROM[0x0E];
  cal12v_amp = EEPROM[0x0F];
}

void setup()   
{               

  // Set Reference to the External 3.00V Shunt.
  analogReference(EXTERNAL);

  // Set In/Out status and initial values
  // I tried to avoid direct Bit Manipulation for readability.
  pinMode(SW1, INPUT_PULLUP);
  pinMode(SW2, INPUT_PULLUP);
  pinMode(SW3, INPUT_PULLUP);
  pinMode(SW4, INPUT_PULLUP);
  pinMode(SW5, INPUT_PULLUP);
  pinMode(SW6, INPUT_PULLUP);
  pinMode(PWROK, INPUT);
    
  pinMode(BTN, INPUT_PULLUP);  
   
  pinMode(PWRON, OUTPUT);
  digitalWrite(PWRON, HIGH);   

  pinMode(MS5, OUTPUT);
  digitalWrite(MS5, HIGH);  

  pinMode(MS12, OUTPUT);
  digitalWrite(MS12, LOW);    

  pinMode(MS12NEG, OUTPUT);
  digitalWrite(MS12NEG, HIGH); 

  pinMode(AMP_5V, INPUT);
  pinMode(AMP_12V, INPUT);
  pinMode(VOLT_5V, INPUT);
  pinMode(VOLT_12V, INPUT);

  // Fail-safe bootloader if something goes wrong in FW dev
  // if(!digitalRead(BTN)) { delay(15000); }

  // Initialize OLED SCREEN at ADR 0x3C
  display.begin(SSD1306_SWITCHCAPVCC, 0x3C);

  delay(50);

  // Set EEPROM (first run after initial programming)
  SetE2PROM();
  
  // Finish init by setting EEPROM values
  reinit();
}


void loop() {

  int cset;
  float max_5v, max_12v;

  // -------------------------------------------
  // Set Current Limit According to DIP Switches
  // -------------------------------------------
  
  cset = (digitalRead(SW1) << 4) | (digitalRead(SW2) << 3) | (digitalRead(SW3) << 2) | (digitalRead(SW4) << 1) | digitalRead(SW5);
  max_5v = (float)EEPROM[cset + 0x10] / 20.0;
  max_12v = (float)EEPROM[cset + 0x30] / 20.0;
  
  // Read Slow or Fast Blow
  slowblow = digitalRead(SW6);

  // ---------
  // Set Power
  // ---------
  
  if((!digitalRead(BTN) && (millis() - lastaction) > TIMEBTACTIONS) || extpwr)  
  { 
    if(!extpwr) { curpower = !curpower; }
    lastaction = millis();
    ocurrent5 = false;
    ocurrent12 = false;
    extpwr = false;
    blowtimer5v = 0;  
    blowtimer12v = 0;
    if(display_enabled == false) { 
      wakeDisplay(&display);
      display_enabled = true;
    }
    power_device(curpower);
  }

  // Forward PWROK Status from ATX PSU to AT
  if(digitalRead(PWROK)){
     digitalWrite(TARGET_PWRON, 1);
  } else {
     digitalWrite(TARGET_PWRON, 0);
  }
 

  // -----------------
  // Check overcurrent
  // -----------------
  
   cur_5v = (float)analogRead(AMP_5V) * 3.0 / 1024.0 / 50.0 / 0.005;
  //cur_5v = (float)ADC_0.getADCVal() * 3.0 / 1024.0 / 50.0 / 0.005;
  cur_5v *= (0.5 + (cal5v_amp / 256.0));  

  if(cur_5v > 9.0 || ((cur_5v > max_5v) && !slowblow) || ((cur_5v > (max_5v * SBOS5V)) && slowblow)) { 
      // if current > 9.0A OR fast blow mode and current limit exceeded OR slowblow and current limit + 75% => trigger immediatly 
      ocurrent5 = true; 
  } else if (cur_5v > max_5v && slowblow) {
      // slowblow and current limit exceeded. Allow SB_DELAY msec grace period before tripping
    if(blowtimer5v == 0) { 
      blowtimer5v = millis(); 
    } else if ((millis() - blowtimer5v) > sb_delay) {
      ocurrent5 = true; 
    }
  } 

  // ------------------------------------------------
  // If overcurrent detect, immediatly shutdown power
  // ------------------------------------------------
  
  if(ocurrent5) { power_device(false); }

   cur_12v = (float)analogRead(AMP_12V) * 3.0 / 1024.0 / 50.0 / 0.01;
  // cur_12v = (float)ADC_1.getADCVal() * 3.0 / 1024.0 / 50.0 / 0.01;
  cur_12v *= (0.5 + (cal12v_amp / 256.0));

  if(cur_12v > 5.0 || ((cur_12v > max_12v) && !slowblow) || ((cur_12v > (max_12v * SBOS12V)) && slowblow)) { 
      // if current > 5.0A OR fast blow mode and current limit exceeded OR slowblow and current limit + 75% => trigger immediatly 
      ocurrent12 = true; 
  } else if (cur_12v > max_12v && slowblow) {
      // slowblow and current limit exceeded. Allow SB_DELAY msec grace period before tripping
    if(blowtimer12v == 0) { 
      blowtimer12v = millis(); 
    } else if ((millis() - blowtimer12v) > sb_delay) {
      ocurrent12 = true; 
    }
  } 

  // ------------------------------------------------
  // If overcurrent detect, immediatly shutdown power
  // ------------------------------------------------
  
  if(ocurrent12) { power_device(false); }

  // --------------
  // Update Display
  // --------------
  
  if(display_enabled == true) {

    // Update Display
    if(ocurrent5 || ocurrent12) {
      // overcurrent => update immediately
      update_display(max_5v, max_12v);
    } else if (slowblow && (millis() - sblowtimer) > SB_REF_DELAY) {
      update_display(max_5v, max_12v); 
      sblowtimer = millis(); 
    } else if (!slowblow && (millis() - fblowtimer) > FB_REF_DELAY) {
      // fastblow => update only every FB_REF_DELAY msec
      update_display(max_5v, max_12v);
      fblowtimer = millis();  
    } 

    // Turn off display if no action since 'ssaver_delay' minutes AND Screensaver is enabled (EEPROM[3] not 0xFF)
    if((millis() - lastaction) > ssaver_delay && ssaver_enabled){
      sleepDisplay(&display);
      display_enabled = false;
    }
  }

  // ----------------------
  // Process Serial Command 
  // (TODO: Add Timeout)
  // ----------------------
  
  if (Serial.available() > 0) {
    // Detect the start of a trame (Begin with Magic Byte 0x56)
    if (headerByteFound == false) {
      SerCMD[0] = Serial.read();
      if (SerCMD[0] == 0x56) { 
        headerByteFound = true; 
        RcdvBytes++;
        } else {
          ResetSerial(); // Drop all the frame
        }
    }
    
    // We have a trame
    if(headerByteFound){
      while(Serial.available() > 0)
      {
        SerCMD[RcdvBytes] = Serial.read();
        RcdvBytes++;
        if(SerCMD[1] > 16 || RcdvBytes > 16) { ResetSerial(); break; }
        
        if(RcdvBytes > 2 && SerCMD[1] == RcdvBytes)
        {
          // We have a complete trame, so verify checksum
          if(checkSum_Rcvd()){  
            // Checksum is good. Process Command
            processCMD();          
          } else {
            // Checksum is wrong. Reset everything
            Serial.println(F("CHECKSUM FAILED"));
          }
          ResetSerial(); 
          break;
        } 
       }
     }
  }

}

// Process Serial Command
void processCMD()
{
  int tadc;
  byte tcksum;
  byte te2p;
  
  memset(SerSend, 0, sizeof(SerSend));
  SerSend[0] = 0x56; // Magic Byte
  SerSend[2] = SerCMD[2]; // Command Byte
  
  switch(SerCMD[2])
  {
    default:
      // Unknown command.
      Serial.print(F("Unknown Command: "));
      Serial.println(SerCMD[2], HEX);
      break;
     case 0xA0:
      // Get Status
      SerSend[1] = 0x0E;// 14 bytes reply
      SerSend[3] = PINF;
      SerSend[4] = (PINB & 0x90) | (PIND & 0x20) | (PINE & 0x40) | ((PINC >> 4) & 0x08);

      tadc = analogRead(AMP_5V); 
      SerSend[5] = (byte)(tadc >> 8);
      SerSend[6] = (byte)(tadc & 0xFF);
      tadc = analogRead(AMP_12V);
      SerSend[7] = (byte)(tadc >> 8);
      SerSend[8] = (byte)(tadc & 0xFF);
      
      tadc = analogRead(VOLT_5V); 
      SerSend[9] = (byte)(tadc >> 8);
      SerSend[10] = (byte)(tadc & 0xFF);
      tadc = analogRead(VOLT_12V);
      SerSend[11] = (byte)(tadc >> 8);
      SerSend[12] = (byte)(tadc & 0xFF);
      
      SerSend[13] = checkSum_Send();
      Serial.write(SerSend, SerSend[1]);
      break;
     case 0xA1:
      SerSend[1] = 0x54;
      tcksum = 0xA3;
      Serial.write(SerSend, 3);
      for(int i = 0; i < 0x50; i++)
      {
        te2p = EEPROM[i];
        Serial.write(te2p);
        tcksum ^= te2p;
      }
      Serial.write(tcksum);         
      break;
     case 0xA2:
      curpower = (SerCMD[3] != 0) ? true : false;
      extpwr = true;
      SerSend[1] = 0x04;
      SerSend[3] = checkSum_Send();     
      Serial.write(SerSend, SerSend[1]);
      break;
     case 0xB0:
      // Reset EEPROM
      SetE2PROM(true);
      reinit();
      SerSend[1] = 0x04;
      SerSend[3] = checkSum_Send();     
      Serial.write(SerSend, SerSend[1]);      
      break;
     case 0xB2:
      // Write two bytes at the specified adr. on the Current Limit table. (reuse 0xB1 appended)
      EEPROM[SerCMD[3]+0x20] = SerCMD[5];
     case 0xB1:
      // Write a byte at the specified adr.
      EEPROM[SerCMD[3]] = SerCMD[4];
      reinit();
      SerSend[1] = 0x04;
      SerSend[3] = checkSum_Send();     
      Serial.write(SerSend, SerSend[1]);
      break;
     // 0xEx : DEBUG 
  }

}

// Compute Checksum
byte checkSum_Send()
{
  byte ckret = 0x00;

  for(int i = 0; i < SerSend[1]; i++) { ckret ^= SerSend[i]; }

  return ckret;
}

void ResetSerial()
{
  // Flush Input Buffer, reset Array, restart initial sequence
  while(Serial.available() > 0) { Serial.read(); }
  memset(SerCMD, 0, sizeof(SerCMD));
  headerByteFound = false; 
  RcdvBytes = 0;
}

// Control Embedded MOSFETs to Power ON/OFF, and toggle main ATX PWRON. 
void power_device(bool status)
{
  if(status) {
    // POWER ON
    digitalWrite(MS5, LOW);  
    digitalWrite(MS12, LOW);  
    digitalWrite(MS12NEG, HIGH);      
    digitalWrite(PWRON, LOW);
    delay(50);
    //while(!digitalRead(PWROK)) { } // Add Timeout here
    digitalWrite(MS5, HIGH);  
    digitalWrite(MS12, HIGH);  
    digitalWrite(MS12NEG, LOW); 
    curpower = true;
    sblowtimer = millis();
    fblowtimer = millis();
    // This 2 ms delay is required to avoid catching the initial current spike when caps are charging
    delay(2); 
  } else {
    // POWER OFF
    digitalWrite(PWRON, HIGH);
    digitalWrite(MS5, LOW);  
    digitalWrite(MS12, LOW);  
    digitalWrite(MS12NEG, HIGH); 
    curpower = false; 
  }
}

// Update display (TODO : Switch to U8g2 to save FLASH Size?)
// Updating Display requires ~60 ms. Take that into consideration!

void update_display(float max_5v, float max_12v) {

  float rawadc;
  
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(WHITE);
  display.setCursor(0,0);
  display.print(F(REV_STR));
  display.drawLine(0,10,128,10,WHITE);

  if(ocurrent5 || ocurrent12)
  {
    display.drawBitmap(0, 11, OC, 128, 38, WHITE);
    display.fillRect(0,49,128,15,WHITE);
    display.setTextColor(BLACK);
    display.setCursor(20,52);
    if(ocurrent5){
      display.print(F("DETECTED ON +5V"));
    } else {
      display.print(F("DETECTED ON +12V"));     
    }
    display.display();
    return;
  }
   
  display.setCursor(5,17);
  display.print("+5V:  ");
  rawadc = cur_5v; 
  if(rawadc < 0) { rawadc = 0; } 
  display.print(rawadc, 2);   
  display.print("A (");
  rawadc = analogRead(VOLT_5V); // DBG  
  rawadc = rawadc / 1024.0 * 3.0 / 10.0 * 25.0;
  rawadc *= (0.5 + (cal5v / 256.0));
  display.print(rawadc, 2);    
  display.print("V)");
  
  display.setCursor(5,29);
  display.print("+12V: ");
  rawadc = cur_12v; 
  if(rawadc < 0) { rawadc = 0; } 
  display.print(rawadc, 2);   
  display.print("A (");
  rawadc = analogRead(VOLT_12V); // DBG  
  rawadc = rawadc / 1024.0 * 3.0 / 10.0 * 57.0;
  rawadc *= (0.5 + (cal12v / 256.0));
  display.print(rawadc, 2);    
  display.print("V)");  
  
  display.setCursor(0,45);
  display.print("5V Lim:  ");  
  display.print(max_5v, 2);
  display.print("A");    
  display.setCursor(0,56); 
  display.print("12V Lim: ");  
  display.print(max_12v, 2);
  display.print("A");  

  display.fillRect(90,42,38,22,WHITE);
  display.drawLine(0,41,128,41,WHITE);
  
  display.setTextSize(2);
  display.setTextColor(BLACK);

  if(digitalRead(PWROK)){
    display.setCursor(97,46);
    display.print("ON");   
  } else {   
    display.setCursor(92,46);
    display.print("OFF"); 
  }

  display.display();
 
}

void sleepDisplay(Adafruit_SSD1306* display) {
  display->ssd1306_command(SSD1306_DISPLAYOFF);
}

void wakeDisplay(Adafruit_SSD1306* display) {
  display->ssd1306_command(SSD1306_DISPLAYON);
}

bool checkSum_Rcvd()
{
  byte csum = 0x00;
  int i = 0;

  for(i = 0; i < SerCMD[1]-1; i++)
  {
    csum ^= SerCMD[i];  
  }

  if(csum == SerCMD[i])
    return true;
  else
    return false;  
}

// Reverse bit order (LSB <=> MSB). 
byte flipByte(byte c){
  char r=0;
  for(byte i = 0; i < 8; i++){
    r <<= 1;
    r |= c & 1;
    c >>= 1;
  }
  return r;
}
