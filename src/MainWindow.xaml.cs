using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.Storage;
using Windows.System.UserProfile;

namespace BGPainter
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), "BGPainter");
        
        private BitmapImage? _originalBitmap;
        private string? _selectedSourcePath;
        private string? _tempColorImagePath;
        private string? _tempBwImagePath;

        private bool _isDragging = false;
        private Point _clickPosition;
        private double _aspectRatio = 16.0 / 9.0;

        private double _relCropX = 0.1;
        private double _relCropY = 0.1;
        private double _relCropW = 0.8;
        private double _relCropH = 0.8;

        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(_tempFolder);
        }

        private void PreviewCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            _aspectRatio = screenWidth / screenHeight;

            StatusText.Text = $"Monitor Aspect Ratio: {(_aspectRatio * 10):F0}:1";
        }

        private void SelectBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _selectedSourcePath = openFileDialog.FileName;
                    _originalBitmap = LoadBitmapFromPath(_selectedSourcePath);

                    SourceImage.Source = _originalBitmap;

                    InitRelativeCropCoordinates();
                    UpdateLayoutAndPositions();

                    CropWindow.Visibility = Visibility.Visible;
                    DimOverlay.Visibility = Visibility.Visible;
                    ApplyBtn.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void UpdateDimOverlay()
        {
            if (SourceImage.Width <= 0 || SourceImage.Height <= 0) return;

            double imgLeft = Canvas.GetLeft(SourceImage);
            double imgTop = Canvas.GetTop(SourceImage);
            double imgW = SourceImage.Width;
            double imgH = SourceImage.Height;

            double cropLeft = Canvas.GetLeft(CropWindow);
            double cropTop = Canvas.GetTop(CropWindow);
            double cropW = CropWindow.Width;
            double cropH = CropWindow.Height;

            FullImageGeo.Rect = new Rect(imgLeft, imgTop, imgW, imgH);
            CropCutoutGeo.Rect = new Rect(cropLeft, cropTop, cropW, cropH);
            
            UpdateLockscreenPreview();
        }

        private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_originalBitmap != null)
            {
                UpdateLayoutAndPositions();
            }
        }

        private void InitRelativeCropCoordinates()
        {
            if (_originalBitmap == null) return;

            double imgAspect = (double)_originalBitmap.PixelWidth / _originalBitmap.PixelHeight;

            if (imgAspect > _aspectRatio)
            {
                _relCropH = 0.8;
                _relCropW = (_relCropH / imgAspect) * _aspectRatio;
            }
            else
            {
                _relCropW = 0.8;
                _relCropH = (_relCropW * imgAspect) / _aspectRatio;
            }

            _relCropX = (1.0 - _relCropW) / 2.0;
            _relCropY = (1.0 - _relCropH) / 2.0;
        }

        private void UpdateLayoutAndPositions()
        {
            if (_originalBitmap == null || PreviewCanvas.ActualWidth < 10 || PreviewCanvas.ActualHeight < 10) 
                return;

            double canvasW = PreviewCanvas.ActualWidth;
            double canvasH = PreviewCanvas.ActualHeight;

            double imgRatio = (double)_originalBitmap.PixelWidth / _originalBitmap.PixelHeight;
            if (double.IsNaN(imgRatio) || imgRatio <= 0) return;

            double canvasRatio = canvasW / canvasH;

            double displayedImgWidth = imgRatio > canvasRatio ? canvasW : canvasH * imgRatio;
            double displayedImgHeight = imgRatio > canvasRatio ? canvasW / imgRatio : canvasH;

            displayedImgWidth = Math.Max(1.0, displayedImgWidth);
            displayedImgHeight = Math.Max(1.0, displayedImgHeight);

            SourceImage.Width = displayedImgWidth;
            SourceImage.Height = displayedImgHeight;

            double imgLeft = (canvasW - displayedImgWidth) / 2.0;
            double imgTop = (canvasH - displayedImgHeight) / 2.0;
            Canvas.SetLeft(SourceImage, imgLeft);
            Canvas.SetTop(SourceImage, imgTop);

            double cropW = Math.Max(10.0, _relCropW * displayedImgWidth);
            double cropH = Math.Max(10.0, _relCropH * displayedImgHeight);

            if (_aspectRatio > 0)
            {
                if (cropW / cropH > _aspectRatio)
                    cropW = cropH * _aspectRatio;
                else
                    cropH = cropW / _aspectRatio;
            }

            double cropX = Math.Clamp(imgLeft + (_relCropX * displayedImgWidth), imgLeft, Math.Max(imgLeft, imgLeft + displayedImgWidth - cropW));
            double cropY = Math.Clamp(imgTop + (_relCropY * displayedImgHeight), imgTop, Math.Max(imgTop, imgTop + displayedImgHeight - cropH));

            CropWindow.Width = cropW;
            CropWindow.Height = cropH;
            Canvas.SetLeft(CropWindow, cropX);
            Canvas.SetTop(CropWindow, cropY);

            UpdateDimOverlay();
        }

        #region Move Logic

        private void CropWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _clickPosition = e.GetPosition(CropWindow);
            CropWindow.CaptureMouse();
        }

        private void CropWindow_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            Point currentPos = e.GetPosition(PreviewCanvas);

            double newLeft = currentPos.X - _clickPosition.X;
            double newTop = currentPos.Y - _clickPosition.Y;

            double imgLeft = Canvas.GetLeft(SourceImage);
            double imgTop = Canvas.GetTop(SourceImage);
            double maxLeft = imgLeft + SourceImage.Width - CropWindow.Width;
            double maxTop = imgTop + SourceImage.Height - CropWindow.Height;

            newLeft = Math.Clamp(newLeft, imgLeft, maxLeft);
            newTop = Math.Clamp(newTop, imgTop, maxTop);

            Canvas.SetLeft(CropWindow, newLeft);
            Canvas.SetTop(CropWindow, newTop);

            _relCropX = (newLeft - imgLeft) / SourceImage.Width;
            _relCropY = (newTop - imgTop) / SourceImage.Height;

            UpdateDimOverlay();
        }

        private void CropWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            CropWindow.ReleaseMouseCapture();
        }

        #endregion

        #region Resize Handles (Thumb DragDelta)

        private void BottomRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (SourceImage.Width <= 0) return;

            double change = e.HorizontalChange;
            double newWidth = CropWindow.Width + change;
            double newHeight = newWidth / _aspectRatio;

            ApplyResize(newWidth, newHeight, anchorLeft: true, anchorTop: true);
        }

        private void BottomLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (SourceImage.Width <= 0) return;

            double change = -e.HorizontalChange;
            double newWidth = CropWindow.Width + change;
            double newHeight = newWidth / _aspectRatio;

            ApplyResize(newWidth, newHeight, anchorLeft: false, anchorTop: true);
        }

        private void TopRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (SourceImage.Width <= 0) return;

            double change = e.HorizontalChange;
            double newWidth = CropWindow.Width + change;
            double newHeight = newWidth / _aspectRatio;

            ApplyResize(newWidth, newHeight, anchorLeft: true, anchorTop: false);
        }

        private void TopLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (SourceImage.Width <= 0) return;

            double change = -e.HorizontalChange;
            double newWidth = CropWindow.Width + change;
            double newHeight = newWidth / _aspectRatio;

            ApplyResize(newWidth, newHeight, anchorLeft: false, anchorTop: false);
        }

        private void ApplyResize(double newW, double newH, bool anchorLeft, bool anchorTop)
        {
            double imgLeft = Canvas.GetLeft(SourceImage);
            double imgTop = Canvas.GetTop(SourceImage);
            double imgW = SourceImage.Width;
            double imgH = SourceImage.Height;

            if (imgW <= 0 || imgH <= 0) return;
            if (newW < 30 || newH < 30) return;

            double currentX = Canvas.GetLeft(CropWindow);
            double currentY = Canvas.GetTop(CropWindow);

            double maxW = anchorLeft ? (imgLeft + imgW - currentX) : (currentX + CropWindow.Width - imgLeft);
            double maxH = anchorTop ? (imgTop + imgH - currentY) : (currentY + CropWindow.Height - imgTop);

            if (newW > maxW || newH > maxH)
            {
                newW = Math.Min(maxW, maxH * _aspectRatio);
                newH = newW / _aspectRatio;
            }

            double newX = anchorLeft ? currentX : currentX + (CropWindow.Width - newW);
            double newY = anchorTop ? currentY : currentY + (CropWindow.Height - newH);

            newX = Math.Clamp(newX, imgLeft, imgLeft + imgW - newW);
            newY = Math.Clamp(newY, imgTop, imgTop + imgH - newH);

            CropWindow.Width = newW;
            CropWindow.Height = newH;
            Canvas.SetLeft(CropWindow, newX);
            Canvas.SetTop(CropWindow, newY);

            _relCropX = Math.Clamp((newX - imgLeft) / imgW, 0.0, 1.0);
            _relCropY = Math.Clamp((newY - imgTop) / imgH, 0.0, 1.0);
            _relCropW = Math.Clamp(newW / imgW, 0.0, 1.0 - _relCropX);
            _relCropH = Math.Clamp(newH / imgH, 0.0, 1.0 - _relCropY);

            UpdateDimOverlay();
        }

        #endregion

        private async void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_originalBitmap == null || string.IsNullOrEmpty(_selectedSourcePath)) return;

            try
            {
                StatusText.Text = "Cropping and processing images...";

                ClearTempFolder();

                _tempColorImagePath = Path.Combine(_tempFolder, "desktop_color.png");
                _tempBwImagePath = Path.Combine(_tempFolder, "lockscreen_bw.png");

                BitmapSource croppedColorBitmap = GetCroppedBitmap();

                SaveBitmapToFile(croppedColorBitmap, _tempColorImagePath);

                FormatConvertedBitmap bwBitmap = new FormatConvertedBitmap();
                bwBitmap.BeginInit();
                bwBitmap.Source = croppedColorBitmap;
                bwBitmap.DestinationFormat = PixelFormats.Gray8;
                bwBitmap.EndInit();
                SaveBitmapToFile(bwBitmap, _tempBwImagePath);

                SetDesktopWallpaper(_tempColorImagePath);
                bool lockSuccess = await SetLockScreenAsync(_tempBwImagePath);

                StatusText.Text = lockSuccess 
                    ? "Successfully applied cropped Desktop and Lockscreen wallpapers!" 
                    : "Desktop updated. Lock screen failed or unsupported.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying cropped image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private BitmapSource GetCroppedBitmap()
        {
            if (_originalBitmap == null)
                throw new InvalidOperationException("No source image loaded.");

            int origW = _originalBitmap.PixelWidth;
            int origH = _originalBitmap.PixelHeight;

            // 1. Convert normalized relative percentages to pixel values
            int pixelX = (int)Math.Floor(_relCropX * origW);
            int pixelY = (int)Math.Floor(_relCropY * origH);
            int pixelWidth = (int)Math.Ceiling(_relCropW * origW);
            int pixelHeight = (int)Math.Ceiling(_relCropH * origH);

            // 2. Strict lower boundary clamping (prevent negative values)
            pixelX = Math.Max(0, pixelX);
            pixelY = Math.Max(0, pixelY);

            // 3. Ensure width and height are at least 1 pixel
            pixelWidth = Math.Max(1, pixelWidth);
            pixelHeight = Math.Max(1, pixelHeight);

            // 4. Upper boundary clamping (prevent exceeding source bounds)
            if (pixelX + pixelWidth > origW)
            {
                pixelWidth = origW - pixelX;
            }

            if (pixelY + pixelHeight > origH)
            {
                pixelHeight = origH - pixelY;
            }

            // 5. Final fallback check: If image constraints forced width/height <= 0
            if (pixelWidth <= 0 || pixelHeight <= 0 || pixelX >= origW || pixelY >= origH)
            {
                // Fall back to entire original image if crop rect is somehow invalid
                return _originalBitmap;
            }

            Int32Rect cropRect = new Int32Rect(pixelX, pixelY, pixelWidth, pixelHeight);
            return new CroppedBitmap(_originalBitmap, cropRect);
        }

        private void UpdateLockscreenPreview()
        {
            if (_originalBitmap == null) return;

            try
            {
                BitmapSource croppedColor = GetCroppedBitmap();

                FormatConvertedBitmap bwBitmap = new FormatConvertedBitmap();
                bwBitmap.BeginInit();
                bwBitmap.Source = croppedColor;
                bwBitmap.DestinationFormat = PixelFormats.Gray8;
                bwBitmap.EndInit();
                bwBitmap.Freeze();

                LockscreenPreviewImage.Source = bwBitmap;
            }
            catch
            {
                // Ignore rapid updates during layout collapses
            }
        }

        private void ClearTempFolder()
        {
            if (Directory.Exists(_tempFolder))
            {
                foreach (FileInfo file in new DirectoryInfo(_tempFolder).GetFiles())
                {
                    try { file.Delete(); } catch { }
                }
            }
        }

        private BitmapImage LoadBitmapFromPath(string path)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void SaveBitmapToFile(BitmapSource bitmapSource, string destinationPath)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using FileStream stream = new FileStream(destinationPath, FileMode.Create);
            encoder.Save(stream);
        }

        private void SetDesktopWallpaper(string imagePath)
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        public async Task<bool> SetLockScreenAsync(string imagePath)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(imagePath);
            await LockScreen.SetImageFileAsync(file);
            return true;
        }
    }
}