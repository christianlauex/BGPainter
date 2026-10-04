Building a Windows desktop app with this exact workflow is most straightforward using **C# with WPF (.NET 8/9)** or **WinUI 3**. Windows provides Win32 APIs via `SystemParametersInfo` and `UserProfilePersonalizationSettings` to set wallpapers and lock screen images natively.

Here is a step-by-step guide to building the application.

---

### Core Requirements & Architecture

1. **File Picker:** Allows the user to select an image (`.png`, `.jpg`, `.jpeg`).
2. **Image Processing:** Uses `System.Drawing` or `SixLabors.ImageSharp` (or native WPF `FormatConvertedBitmap`) to convert the image to grayscale and save it locally.
3. **Wallpaper Setting:** Uses the Win32 `SystemParametersInfo` API.
4. **Lock Screen Setting:** Uses `Windows.System.UserProfile.UserProfilePersonalizationSettings` (WinRT API).

---

### Step 1: Set Up the WPF (.NET 8+) Project

Create a new WPF Application in Visual Studio or via terminal:

```bash
dotnet new wpf -n Backgroundcolourist
cd Backgroundcolourist

```

In your project file (`Backgroundcolourist.csproj`), ensure target framework support for Windows 10/11 WinRT APIs:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>

```

---

### Step 2: UI Implementation (`MainWindow.xaml`)

Create a clean user interface with an **Upload Image** button and status updates:

```xml
<Window x:Class="Backgroundcolourist.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Wallpaper &amp; Lockscreen Setter" Height="350" Width="500">
    <Grid Margin="20">
        <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="15">
            <TextBlock Text="Select an image to apply as your color Desktop Wallpaper and B&amp;W Lock Screen." 
                       TextWrapping="Wrap" TextAlignment="Center" FontSize="14" Margin="0,0,0,10"/>
            
            <Button Name="UploadBtn" Content="Upload Image &amp; Apply" 
                    Width="220" Height="40" FontSize="14" FontWeight="Bold"
                    Click="UploadBtn_Click"/>
            
            <TextBlock Name="StatusText" Foreground="Green" TextAlignment="Center" Margin="0,10,0,0"/>
        </StackPanel>
    </Grid>
</Window>

```

---

### Step 3: Application Code (`MainWindow.xaml.cs`)

Replace your backend code with the following logic:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.Storage;
using Windows.System.UserProfile;

namespace Backgroundcolourist
{
    public partial class MainWindow : Window
    {
        // Win32 API declaration for Desktop Wallpaper
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void UploadBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    StatusText.Text = "Processing images...";
                    string originalPath = openFileDialog.FileName;

                    // 1. Set Desktop Wallpaper (Color)
                    SetDesktopWallpaper(originalPath);

                    // 2. Convert to Grayscale & Save
                    string bwImagePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                        "lockscreen_bw.png");

                    ConvertToBlackAndWhite(originalPath, bwImagePath);

                    // 3. Set Lock Screen (B&W)
                    bool lockScreenSuccess = await SetLockScreenAsync(bwImagePath);

                    if (lockScreenSuccess)
                    {
                        StatusText.Text = "Successfully updated Desktop and Lock Screen!";
                    }
                    else
                    {
                        StatusText.Text = "Desktop updated. Lock screen update not supported or failed.";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = "Operation failed.";
                }
            }
        }

        // Method to set Desktop Wallpaper via P/Invoke
        private void SetDesktopWallpaper(string imagePath)
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        // Method to convert image to Grayscale (Black & White) using WPF native classes
        private void ConvertToBlackAndWhite(string sourcePath, string destinationPath)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(sourcePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            // Native high-performance WPF grayscale conversion
            FormatConvertedBitmap bwBitmap = new FormatConvertedBitmap();
            bwBitmap.BeginInit();
            bwBitmap.Source = bitmap;
            bwBitmap.DestinationFormat = PixelFormats.Gray8;
            bwBitmap.EndInit();

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bwBitmap));

            using (FileStream stream = new FileStream(destinationPath, FileMode.Create))
            {
                encoder.Save(stream);
            }
        }

        // Method to set Lock Screen via Windows Runtime API
        private async Task<bool> SetLockScreenAsync(string imagePath)
        {
            if (!UserProfilePersonalizationSettings.IsSupported())
            {
                return false;
            }

            UserProfilePersonalizationSettings profileSettings = UserProfilePersonalizationSettings.Current;
            StorageFile file = await StorageFile.GetFileFromPathAsync(imagePath);

            return await profileSettings.TrySetLockScreenImageAsync(file);
        }
    }
}

```

---

### Important Technical Considerations

* **Lock Screen API Constraints:** `UserProfilePersonalizationSettings.TrySetLockScreenImageAsync` requires the app target framework to support WinRT APIs (`net8.0-windows10.0.19041.0` or higher).
* **Supported File Types:** Stick to `.png` or `.jpg` formats. `FormatConvertedBitmap` converts image data directly to an 8-bit grayscale bitmap without needing third-party libraries.
* **Permissions:** Changing wallpapers and lock screens through these standard APIs requires no elevated administrator rights on Windows 10/11.