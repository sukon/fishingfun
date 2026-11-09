using System;
using System.Windows;
using System.Windows.Controls;

namespace FishingFun
{
    /// <summary>
    /// Lets the user choose the key that casts the float. The choice is kept with the
    /// other settings, so that the console front end casts with the same key.
    /// </summary>
    public partial class KeyBindChooser : UserControl
    {
        public ConsoleKey CastKey
        {
            get { return settings.CastKey; }
            set { settings.CastKey = value; }
        }

        private readonly FishingFunSettings settings = FishingFunSettings.Load();

        public EventHandler CastKeyChanged;

        public KeyBindChooser()
        {
            CastKeyChanged += (s, e) => { };

            InitializeComponent();

            KeyBind.Text = GetCastKeyText(this.CastKey);
        }

        private void CastKey_Focus(object sender, RoutedEventArgs e)
        {
            KeyBind.Text = "";
        }

        private void KeyBind_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            string key = e.Key.ToString();
            ProcessKeybindText(key);
        }

        private void ProcessKeybindText(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                ConsoleKey ck;
                if (Enum.TryParse<ConsoleKey>(key, out ck))
                {
                    this.CastKey = ck;
                    settings.Save();
                    CastKeyChanged?.Invoke(this, null);
                    return;
                }
            }
            KeyBind.Text = "";
        }

        private string GetCastKeyText(ConsoleKey ck)
        {
            string keyText = ck.ToString();
            if (keyText.Length == 1) { return keyText; }
            if (keyText.StartsWith("D") && keyText.Length == 2)
            {
                return keyText.Substring(1, 1);
            }
            return "?";
        }
    }
}
