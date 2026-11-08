using System.Windows;

namespace FishingFun
{
    public partial class App : Application
    {
        public App()
        {
            FishingFun.LoggingSetup.Configure();
        }
    }
}