using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace QAssistant.Helpers
{
    internal static class DialogHelper
    {
        internal static void ApplyDarkTheme(ContentDialog dialog)
        {
            dialog.RequestedTheme = ElementTheme.Dark;

            // ── Dialog chrome ────────────────────────────────────────
            dialog.Resources["ContentDialogBackground"]               = Brush(19, 19, 26);
            dialog.Resources["ContentDialogForeground"]               = Brush(226, 232, 240);
            dialog.Resources["ContentDialogBorderBrush"]              = Brush(42, 42, 58);
            dialog.Resources["ContentDialogTitleForeground"]          = Brush(226, 232, 240);
            dialog.Resources["ContentDialogButtonAreaSeparatorBrush"] = Brush(42, 42, 58);

            // ── Default/accent button (whichever button is marked DefaultButton) ─
            //    Uses AccentButton* theme resources in the Button template.
            dialog.Resources["AccentButtonBackground"]             = Brush(167, 139, 250);  // #A78BFA
            dialog.Resources["AccentButtonBackgroundPointerOver"]  = Brush(139,  92, 246);  // #8B5CF6
            dialog.Resources["AccentButtonBackgroundPressed"]      = Brush(109,  40, 217);  // #6D28D9
            dialog.Resources["AccentButtonBackgroundDisabled"]     = Brush( 60,  50,  80);
            dialog.Resources["AccentButtonForeground"]             = Brush(255, 255, 255);
            dialog.Resources["AccentButtonForegroundPointerOver"]  = Brush(255, 255, 255);
            dialog.Resources["AccentButtonForegroundPressed"]      = Brush(255, 255, 255);
            dialog.Resources["AccentButtonBorderBrush"]            = Brush(167, 139, 250);
            dialog.Resources["AccentButtonBorderBrushPointerOver"] = Brush(139,  92, 246);
            dialog.Resources["AccentButtonBorderBrushPressed"]     = Brush(109,  40, 217);

            // ── Non-default buttons (Secondary & Close) ──────────────
            //    Uses Button* theme resources in the Button template.
            dialog.Resources["ButtonBackground"]             = Brush( 37,  37,  53);  // #252535
            dialog.Resources["ButtonBackgroundPointerOver"]  = Brush( 45,  45,  65);
            dialog.Resources["ButtonBackgroundPressed"]      = Brush( 35,  35,  50);
            dialog.Resources["ButtonBackgroundDisabled"]     = Brush( 30,  30,  40);
            dialog.Resources["ButtonForeground"]             = Brush(156, 163, 175);  // #9CA3AF
            dialog.Resources["ButtonForegroundPointerOver"]  = Brush(226, 232, 240);  // #E2E8F0
            dialog.Resources["ButtonForegroundPressed"]      = Brush(156, 163, 175);
            dialog.Resources["ButtonBorderBrush"]            = Brush( 42,  42,  58);  // #2A2A3A
            dialog.Resources["ButtonBorderBrushPointerOver"] = Brush( 55,  55,  75);
            dialog.Resources["ButtonBorderBrushPressed"]     = Brush( 42,  42,  58);
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
            => new(Color.FromArgb(255, r, g, b));
    }
}
