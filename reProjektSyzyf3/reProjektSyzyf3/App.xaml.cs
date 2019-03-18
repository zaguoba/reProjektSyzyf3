﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;

using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices;
using Windows.Devices.Gpio;

using Microsoft.IoT.Lightning.Providers;

namespace reProjektSyzyf3
{
    public class SimpleSerialProtocol : INotifyPropertyChanged
    {
        //NOTIFICATIONS
        private byte _initProgress = 0;
        private ushort _collectProgress = 0;

        public byte initProgress
        {
            get { return _initProgress;  }
            set
            {
                _initProgress = value;
                if(_initProgress == 0xff)
                    RaisePropertyChanged("initProgress");
            }
        }

        public ushort collectProgress
        {
            get { return _collectProgress; }
            set
            {
                _collectProgress = value;
                if(_collectProgress == 0x0)
                    RaisePropertyChanged("collectProgress");
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ushort? collectFrame { get; set; } = null;

        //GENERAL
        public GpioPin testPin; //BCM19
        public GpioPin _PD;     //BCM26
        //public GpioPin CLK192; //future clock source derived from RPi to drive ADC at full speed

        ////CHANNEL1
        //public GpioPin CLK1;    //BCM4
        //public GpioPin FSO1;    //BCM25
        //public GpioPin DOUT1;   //BCM6
        //public GpioPin SCLK1;   //BCM13
        //public GpioPin OTR1;    //BCM23
        //public GpioPin SYNC1;   //BCM18

        //CHANNEL2
        public GpioPin CLK2;    //BCM27
        public GpioPin FSO2;    //BCM24
        public GpioPin DOUT2;   //BCM5
        public GpioPin SCLK2;   //BCM12
        public GpioPin OTR2;    //BCM22
        public GpioPin SYNC2;   //BCM17

        //bit-bang SPI
        public GpioPin _CS0;    //BCM8
        public GpioPin aSCLK;    //BCM11
        public GpioPin DIO;     //BCM10  

        //AUX
        //public byte initProgress = 0;
        private const byte F_ = 0b11110000;
        private const byte _R = 0b00001111;
        private byte CLKedge = 0;
        private byte SCLKedge = 0;
        private byte FSOedge = 0;

        //STOPWATCH
        public long StoperOne = 0;
        public long[] StoperArr = new long[22];
        public uint StoperCnt = 0;

        public SimpleSerialProtocol()
        {

        }

        public void InitGPIO()
        {
            GpioController gpio = null;

            if (LightningProvider.IsLightningEnabled)
            {
                LowLevelDevicesController.DefaultProvider = LightningProvider.GetAggregateProvider();
                //gpio = (await GpioController.GetControllersAsync(LightningGpioProvider.GetGpioProvider()))[0];
                Debug.WriteLine("Lightning Provider ON and used");
            }
            else
            {
                Debug.WriteLine("Lightning Provider OFF or error, Inbox Driver used");
            }

            // Get the default GPIO controller on the system
            gpio = GpioController.GetDefault();
            if (gpio == null)
                return; // GPIO not available on this system

            // Open GPIO 27 - CLK listener
            CLK2 = gpio.OpenPin(27, GpioSharingMode.Exclusive);
            CLK2.SetDriveMode(GpioPinDriveMode.Input);

            // Open GPIO 24 - FSO input
            FSO2 = gpio.OpenPin(24);
            FSO2.SetDriveMode(GpioPinDriveMode.Input);

            // Open GPIO 5 - DOUT input
            DOUT2 = gpio.OpenPin(5);
            DOUT2.SetDriveMode(GpioPinDriveMode.Input);

            // Open GPIO 12 - SCLK input
            SCLK2 = gpio.OpenPin(12);
            SCLK2.SetDriveMode(GpioPinDriveMode.Input);

            // Open GPIO 22 - OTR input
            OTR2 = gpio.OpenPin(22);
            OTR2.SetDriveMode(GpioPinDriveMode.Input);

            // Open GPIO 17 - issue SYNC pulse to ADC
            SYNC2 = gpio.OpenPin(17);
            SYNC2.Write(GpioPinValue.Low);
            SYNC2.SetDriveMode(GpioPinDriveMode.Output);

            //Set PowerDown pin high to enable opamp and ADC
            _PD = gpio.OpenPin(26);
            _PD.Write(GpioPinValue.High);
            _PD.SetDriveMode(GpioPinDriveMode.Output);
            
        }

        //conversion / communication with ADC initialization
        public void EdgeDetCLKOnValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        {
            if (args.Edge.Equals(GpioPinEdge.FallingEdge))
            {
                if (initProgress == 0b00000000)
                {
                    SYNC2.Write(GpioPinValue.High);
                    initProgress |= 0b00000011;
                }
                else if (initProgress == 0b00000011)
                {
                    SYNC2.Write(GpioPinValue.Low);
                    if (FSO2.Read().Equals(GpioPinValue.Low))
                        initProgress = 0; //startOver
                    if (FSO2.Read().Equals(GpioPinValue.High))
                    {
                        CLK2.ValueChanged -= EdgeDetCLKOnValueChanged; //listen to edges on CLK
                        FSO2.ValueChanged += EdgeDetFSOOnValueChanged; //listen to edges on FSO
                        initProgress |= 0b00001100;
                    }
                }
            }
        }
        //ADC initialization check / frame capture start on FSO F_ edge
        private void EdgeDetFSOOnValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        {
            if (args.Edge.Equals(GpioPinEdge.FallingEdge))
            {
                if (initProgress == 0b00001111)
                    initProgress |= 0b00110000;
                //frame capture start
                if(collectProgress == 0xffff)
                {
                    collectProgress = 0x8000;
                    SCLK2.ValueChanged += EdgeDetSCLKOnValueChanged; //listen to edges on SCLK
                }
            }
            if (args.Edge.Equals(GpioPinEdge.RisingEdge))
            {
                if (initProgress == 0b00111111)
                {
                    initProgress |= 0b11000000;
                }
            }
        }

        private void EdgeDetSCLKOnValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        {
            if (args.Edge.Equals(GpioPinEdge.FallingEdge))
            {
                if (collectProgress < 0x8001)
                {
                    if (DOUT2.Read().Equals(GpioPinValue.High))
                    {
                        collectFrame |= collectProgress; //if data line is high, set current bit high
                    }
                    collectProgress >>= 1; //shift to next bit (less significant)
                }

            }
        }
    }

    /// <summary>
    /// Zapewnia zachowanie specyficzne dla aplikacji, aby uzupełnić domyślną klasę aplikacji.
    /// </summary>
    sealed partial class App : Application
    {
        /// <summary>
        /// Inicjuje pojedynczy obiekt aplikacji. Jest to pierwszy wiersz napisanego kodu
        /// wykonywanego i jest logicznym odpowiednikiem metod main() lub WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        /// <summary>
        /// Wywoływane, gdy aplikacja jest uruchamiana normalnie przez użytkownika końcowego. Inne punkty wejścia
        /// będą używane, kiedy aplikacja zostanie uruchomiona w celu otworzenia określonego pliku.
        /// </summary>
        /// <param name="e">Szczegóły dotyczące żądania uruchomienia i procesu.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            // Nie powtarzaj inicjowania aplikacji, gdy w oknie znajduje się już zawartość,
            // upewnij się tylko, że okno jest aktywne
            if (rootFrame == null)
            {
                // Utwórz ramkę, która będzie pełnić funkcję kontekstu nawigacji, i przejdź do pierwszej strony
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Załaduj stan z wstrzymanej wcześniej aplikacji
                }

                // Umieść ramkę w bieżącym oknie
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    // Kiedy stos nawigacji nie jest przywrócony, przejdź do pierwszej strony,
                    // konfigurując nową stronę przez przekazanie wymaganych informacji jako
                    // parametr
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                // Upewnij się, ze bieżące okno jest aktywne
                Window.Current.Activate();
            }
        }

        /// <summary>
        /// Wywoływane, gdy nawigacja do konkretnej strony nie powiedzie się
        /// </summary>
        /// <param name="sender">Ramka, do której nawigacja nie powiodła się</param>
        /// <param name="e">Szczegóły dotyczące niepowodzenia nawigacji</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Wywoływane, gdy wykonanie aplikacji jest wstrzymywane. Stan aplikacji jest zapisywany
        /// bez wiedzy o tym, czy aplikacja zostanie zakończona, czy wznowiona z niezmienioną zawartością
        /// pamięci.
        /// </summary>
        /// <param name="sender">Źródło żądania wstrzymania.</param>
        /// <param name="e">Szczegóły żądania wstrzymania.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Zapisz stan aplikacji i zatrzymaj wszelkie aktywności w tle
            deferral.Complete();
        }
    }
}
