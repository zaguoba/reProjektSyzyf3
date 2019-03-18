using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

//Szablon elementu Pusta strona jest udokumentowany na stronie https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x415

namespace reProjektSyzyf3
{
    /// <summary>
    /// Pusta strona, która może być używana samodzielnie lub do której można nawigować wewnątrz ramki.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        SimpleSerialProtocol SSP = new SimpleSerialProtocol();

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            SSP.InitGPIO();

            SSP.PropertyChanged += vChangedNotification;

            //init connection to ADC thru interrupts
            SSP.CLK2.ValueChanged += SSP.EdgeDetCLKOnValueChanged; //listen to edges on CLK
        }

        private void vChangedNotification(object sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName.ToString() == "initProgress")
            {
                connectBTN.Background = new SolidColorBrush(Colors.MediumSpringGreen);
                connectBTN.Content = "ADC\n OK";
                if (!connectBTN.IsEnabled)
                    connectBTN.IsEnabled = true;
            }

        }

        private void ConnectBTN_Click(object sender, RoutedEventArgs e)
        {
            connectBTN.Background = new SolidColorBrush(Colors.Silver);
            connectBTN.Content = "...";
            connectBTN.IsEnabled = false;
            SSP.initProgress = 0;
            SSP.CLK2.ValueChanged += SSP.EdgeDetCLKOnValueChanged; //listen to edges on CLK
        }
    }
}
