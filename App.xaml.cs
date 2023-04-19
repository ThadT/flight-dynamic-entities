using Esri.ArcGISRuntime;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Flights
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // TODO: Set your API key here
            // (Sign up at https://developers.arcgis.com/sign-up/ to get an API Key)
            ArcGISRuntimeEnvironment.ApiKey = "YOUR_API_KEY";

            if (ArcGISRuntimeEnvironment.ApiKey == "YOUR_API_KEY")
            {
                MessageBox.Show("Please provide an API Key (line 22 of App.xaml.cs)", 
                    "No API Key", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
        }
    }
}
