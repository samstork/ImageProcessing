using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Input;

namespace ImageApp
{

    public partial class MainWindow : Window
    {
        private WriteableBitmap? _loadedBitmap; // the raw loaded image, kept in color, for display in OriginalImage
        private byte[,,]? _loadedColorPixels; // [x, y, channel] with channel 0=R, 1=G, 2=B -- extracted once at load time
        private byte[,,]? _loadedColorRef; // [x, y, channel] with channel 0=R, 1=G, 2=B -- extracted once at load time

        object? _processedResult; // Processed grayscale values (nullable)
        private byte[,]? _processedResultRef; // Processed grayscale values (nullable)

        public class Circle
        {
            public int x;
            public int y;
            public int r;// Radius
            public int c;// Confidence
            public Circle(int x_pos, int y_pos, int radius, byte confidence)
            {
                this.x = x_pos;
                this.y = y_pos;
                this.r = radius;
                this.c = confidence;
            }
        }


        // Simple fixed defaults used by the functions below until you add your own
        // GUI controls (TextBoxes, ComboBoxes, etc.) to let the user set these values.
        private static float[,] _horizontalKernel = new float[,]
        {
            {-1f, -2f, -1f},
            {  0,   0,   0},
            { 1f,  2f,  1f}
        };
        private static float[,] _verticalKernel = new float[,]
        {
            {-1f, 0, 1f},
            {-2f, 0, 2f},
            {-1f, 0, 1f}
        };
        private static bool[,] _plusStructElem = 
        {
            { false, true,  false },
            { true,  true,  true  },
            { false, true,  false }  
        };

        // Enum for operations. As you implement each function, add a case for it
        // in OnApply below; the dropdown is populated automatically from this list.
        //
        // NOTE: Task1, Task2, and Task3 (from the assignment text) are NOT listed here.
        // You need to add those dropdown entries yourself as part of implementing them.
        private enum ProcessingFunctions
        {
            ConvertToGrayscale,
            InvertImage,
            AdjustContrast,
            ConvolveImage,
            MedianFilter,
            EdgeMagnitude,
            ThresholdImage,
            BinaryErodeImage,
            BinaryDilateImage,
            BinaryOpenImage,
            BinaryCloseImage,
            GrayscaleErodeImage,
            GrayscaleDilateImage,
            Task1,
            Task2,
            Task3,
            EdgeSharpen,
            HistogramCheck,
            HistogramMatch,
            SequentialRegionLabeling,
            FloodFill,
            HoughTransform,
            HoughPeaks,
            HoughLineSegments,
            BinaryPipeline,
            GrayscalePipeline,
            NormalizeAccumulator,
            HoughCircleTransform,
            ObjectDetectionPipeline,
            CannyEdgeDetection,
            Downsample,
        }
        
        private enum FilterOptions
        {
            Gaussian,
            Median,
            None,
        }

        public MainWindow()
        {
            InitializeComponent();

            OperationBox.ItemsSource = Enum.GetValues<ProcessingFunctions>();
            OperationBox.SelectedIndex = 0; // Select first item by default

            FilterBox.ItemsSource = Enum.GetValues<FilterOptions>();
            FilterBox.SelectedIndex = 0;
            
            SigmaBox.Value = 1;
            ThresholdBox.Value = 120;

            MaxGapBox.Value = 25;
            SegmentBox.Value = 50;

            kBox.Value = 360;
            dBox.Value = 360;

            ThresholdSBox.Value = 150;
            ThresholdTBox.Value = 150;

            KernelBox.Value = 5;
            SharpenBox.Value = 1; 
        }

        private WriteableBitmap ColorArrayToBitmap(byte[,,] color)
        {
            int w = color.GetLength(0);
            int h = color.GetLength(1);
            var bmp = new WriteableBitmap(new PixelSize(w, h), new(96, 96), PixelFormat.Rgba8888,
            AlphaFormat.Opaque);
            using var fb = bmp.Lock();
            byte[] buffer = new byte[fb.RowBytes * h];
            for (int y = 0; y < h; y++)
                {
                int rowStart = y * fb.RowBytes;
                for (int x = 0; x < w; x++)
                {
                    int idx = rowStart + x * 4;
                    buffer[idx + 0] = color[x, y, 0]; // R
                    buffer[idx + 1] = color[x, y, 1]; // G
                    buffer[idx + 2] = color[x, y, 2]; // B
                    buffer[idx + 3] = 255;
                }
            }
            Marshal.Copy(buffer, 0, fb.Address, buffer.Length);
            return bmp;
        }


        /// <summary>
        /// Opens a file picker dialog, loads the selected image, and extracts its color pixel data.
        /// </summary>
        private async void OnLoadImage(object? sender, RoutedEventArgs e)
        {
            if (!StorageProvider.CanOpen)
            {
                StatusText.Text = "Opening files is not supported on this system.";
                return;
            }

            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(
                    new()
                    {
                        Title = "Open Image",
                        AllowMultiple = false,
                        FileTypeFilter = [FilePickerFileTypes.ImageAll]
                    }
                );

                var file = files.FirstOrDefault();
                if (file == null)
                    return;

                await using var stream = await file.OpenReadAsync();

                // Decode the file using Avalonia's own image loader
                using var decoded = new Bitmap(stream);
                var size = decoded.PixelSize;
                int width = size.Width;
                int height = size.Height;

                // Force a known, fixed pixel layout (RGBA, 8 bits per channel, unpremultiplied alpha)
                // so we can reliably read raw bytes regardless of the source file's own format.
                _loadedBitmap?.Dispose();
                _loadedBitmap = new(size, new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);

                _loadedColorPixels = new byte[width, height, 3];
                using (var fb = _loadedBitmap.Lock())
                {
                    decoded.CopyPixels(fb, AlphaFormat.Unpremul);
                    // ^ transcodes the decoded image into our WriteableBitmap's RGBA8888 layout

                    int totalBytes = fb.RowBytes * height;
                    byte[] buffer = new byte[totalBytes];
                    Marshal.Copy(fb.Address, buffer, 0, totalBytes);

                    for (int y = 0; y < height; y++)
                    {
                        int rowStart = y * fb.RowBytes;
                        for (int x = 0; x < width; x++)
                        {
                            int idx = rowStart + x * 4; // 4 bytes per pixel: R, G, B, A
                            _loadedColorPixels[x, y, 0] = buffer[idx + 0];
                            _loadedColorPixels[x, y, 1] = buffer[idx + 1];
                            _loadedColorPixels[x, y, 2] = buffer[idx + 2];
                        }
                    }
                }

                _processedResult = null;
                (OriginalImage.Source as IDisposable)?.Dispose();
                OriginalImage.Source = _loadedBitmap;
                (ProcessedImage.Source as IDisposable)?.Dispose();
                ProcessedImage.Source = null;
                StatusText.Text = $"Loaded image ({width} \u00d7 {height} px).";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load image: {ex.Message}";
            }
        }

        private async void OnLoadReference(object? sender, RoutedEventArgs e)
        {
            if (!StorageProvider.CanOpen)
            {
                StatusText.Text = "Opening files is not supported on this system.";
                return;
            }

            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(
                    new()
                    {
                        Title = "Open Image",
                        AllowMultiple = false,
                        FileTypeFilter = [FilePickerFileTypes.ImageAll]
                    }
                );

                var file = files.FirstOrDefault();
                if (file == null)
                    return;

                await using var stream = await file.OpenReadAsync();

                // Decode the file using Avalonia's own image loader
                using var decoded = new Bitmap(stream);
                var size = decoded.PixelSize;
                int width = size.Width;
                int height = size.Height;

                // Force a known, fixed pixel layout (RGBA, 8 bits per channel, unpremultiplied alpha)
                // so we can reliably read raw bytes regardless of the source file's own format.
                _loadedBitmap?.Dispose();
                _loadedBitmap = new(size, new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);

                _loadedColorRef = new byte[width, height, 3];
                using (var fb = _loadedBitmap.Lock())
                {
                    decoded.CopyPixels(fb, AlphaFormat.Unpremul);
                    // ^ transcodes the decoded image into our WriteableBitmap's RGBA8888 layout

                    int totalBytes = fb.RowBytes * height;
                    byte[] buffer = new byte[totalBytes];
                    Marshal.Copy(fb.Address, buffer, 0, totalBytes);

                    for (int y = 0; y < height; y++)
                    {
                        int rowStart = y * fb.RowBytes;
                        for (int x = 0; x < width; x++)
                        {
                            int idx = rowStart + x * 4; // 4 bytes per pixel: R, G, B, A
                            _loadedColorRef[x, y, 0] = buffer[idx + 0];
                            _loadedColorRef[x, y, 1] = buffer[idx + 1];
                            _loadedColorRef[x, y, 2] = buffer[idx + 2];
                        }
                    }
                }

                _processedResultRef = null;
                StatusText.Text = $"Loaded reference ({width} \u00d7 {height} px).";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load reference: {ex.Message}";
            }
        }

        /// <summary>
        /// Saves the current processed grayscale image to disk as a PNG file.
        /// </summary>
        private async void OnSaveImage(object? sender, RoutedEventArgs e)
        {
            if (_processedResult == null)
            {
                StatusText.Text = "No processed image to save. Apply an operation first.";
                return;
            }

            if (!StorageProvider.CanSave)
            {
                StatusText.Text = "Saving files is not supported on this system.";
                return;
            }

            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(
                    new()
                    {
                        Title = "Save Processed Image",
                        SuggestedFileName = "processed.png",
                        DefaultExtension = "png",
                        FileTypeChoices = [FilePickerFileTypes.ImagePng]
                    }
                );

                if (file == null)
                    return;

                using WriteableBitmap bmp = _processedResult switch
                {
                    byte[,,] colorImg => ColorArrayToBitmap(colorImg),
                    byte[,] grayImg => ByteArrayToBitmap(grayImg),
                    _ => throw new NotSupportedException()
                };

                await using var stream = await file.OpenWriteAsync();
                bmp.Save(stream);
                StatusText.Text = "Processed image saved successfully.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to save image: {ex.Message}";
            }
        }

        /// <summary>
        /// Dispatches the selected image processing operation on a background task
        /// to keep the UI responsive during heavy computations.
        /// </summary>
        private async void OnApply(object? sender, RoutedEventArgs e)
        {
            if (_loadedColorPixels == null)
            {
                StatusText.Text = "Please load an image first.";
                return;
            }

            if (OperationBox.SelectedItem is not ProcessingFunctions selected)
            {
                StatusText.Text = "Please select a valid operation.";
                return;
            }

            if (FilterBox.SelectedItem is not FilterOptions selectedFilter)
            {
                StatusText.Text = "Please select a valid Filter.";
                return;
            }

            float sigmaValue = 1;
            if (!(SigmaBox.Value==null)) sigmaValue = (float)SigmaBox.Value;

            byte thresholdValue = 120;
            if (!(ThresholdBox.Value==null)) thresholdValue = (byte)ThresholdBox.Value;
            
            bool normalise = NormBox.IsChecked == true;
            
            byte thresholdSValue = 120;
            if (!(ThresholdSBox.Value==null)) thresholdSValue = (byte)ThresholdSBox.Value;

            byte thresholdTValue = 120;
            if (!(ThresholdTBox.Value==null)) thresholdTValue = (byte)ThresholdTBox.Value;

            int kValue = 360;
            if (!(kBox.Value==null)) kValue = (int)kBox.Value;
            
            int dValue = 360;
            if (!(dBox.Value==null)) dValue = (int)dBox.Value;

            int gapValue = 25;
            if (!(MaxGapBox.Value==null)) gapValue = (int)MaxGapBox.Value;

            int segmentValue = 50;
            if (!(SegmentBox.Value==null)) segmentValue = (int)SegmentBox.Value;

            byte kernelSize = 3;
            if (!(KernelBox==null)) kernelSize = (byte)KernelBox.Value;
            
            float sharpenValue = 1f;
            if (!(SharpenBox.Value == null))sharpenValue = (float)SharpenBox.Value;

            ApplyButton.IsEnabled = false;
            StatusText.Text = "Processing...";
            string newStatusText = $"Completed {selected}.";

            byte[,,] colorPixels = _loadedColorPixels;
            try
            {
                // Run computation and bitmap generation on a background task
                // to keep the UI dispatcher thread responsive during heavy operations.
                var (resultGray, resultBmp) = await Task.Run(() =>
                {
                    // Grayscale conversion happens here on every Apply so every
                    // operation always starts from the original loaded image, never chained
                    // from a previous Apply's result.
                    byte[,] gray = ConvertToGrayscale(colorPixels);

                    // default: stays grayscale (ConvertToGrayscale case needs no reassignment)
                    object result = gray;

                    int image_width = gray.GetLength(0);
                    int image_height = gray.GetLength(1);

                        switch (selected)
                            {
                                case ProcessingFunctions.ObjectDetectionPipeline:
                                    result = objectDetectionPipeline(gray);
                                    break;
                                case ProcessingFunctions.ConvertToGrayscale:
                                    // Already fully working; gray already holds the grayscale
                                    // conversion result at this point (computed above, before this switch),
                                    // so nothing further is needed here.
                                    break;
                                case ProcessingFunctions.InvertImage:
                                    result = InvertImage(gray);
                                    break;
                                case ProcessingFunctions.AdjustContrast:
                                    result = AdjustContrast(gray, sigmaValue);
                                    break;
                                case ProcessingFunctions.ConvolveImage:
                                    result = ConvolveImage(gray, CreateGaussianFilter(kernelSize, sigmaValue));
                                    break;
                                case ProcessingFunctions.MedianFilter:
                                    result = MedianFilter(gray, 5);
                                    break;
                                case ProcessingFunctions.EdgeMagnitude:
                                {
                                    result = EdgeMagnitude(gray, _horizontalKernel, _verticalKernel);
                                    break;
                                }
                                case ProcessingFunctions.ThresholdImage:
                                    result = ThresholdImage(gray, thresholdValue);
                                    break;

                                case ProcessingFunctions.BinaryErodeImage:
                                {
                                    result = BinaryErodeImage(gray, _plusStructElem);
                                    break;
                                }

                                case ProcessingFunctions.BinaryDilateImage:
                                {
                                    result = BinaryDilateImage(gray, _plusStructElem);
                                    break;
                                }

                                case ProcessingFunctions.BinaryOpenImage:
                                {
                                    result = BinaryOpenImage(gray, _plusStructElem);
                                    break;
                                }

                                case ProcessingFunctions.BinaryCloseImage:
                                {
                                    result = BinaryCloseImage(gray, _plusStructElem);
                                    break;
                                }

                                case ProcessingFunctions.GrayscaleErodeImage:
                                {
                                    int[,] grayStructElem = new int[5,5];
                                    result = GrayscaleErodeImage(gray, grayStructElem);
                                    break;
                                }

                                case ProcessingFunctions.GrayscaleDilateImage:
                                {
                                    int[,] grayStructElem = new int[5,5];
                                    result = GrayscaleDilateImage(gray, grayStructElem);
                                    break;
                                }
                                case ProcessingFunctions.EdgeSharpen:
                                {
                                    byte[,] mask =  ConvolveImage(gray, CreateGaussianFilter(kernelSize, sigmaValue));

                                    result = Sharpen(gray, mask, thresholdValue, sharpenValue);
                                    break;
                                }
                                case ProcessingFunctions.Task1:
                                {
                                    if (selectedFilter == FilterOptions.Gaussian)
                                    {
                                        result = ConvolveImage(gray, CreateGaussianFilter(kernelSize, sigmaValue));

                                    } else if (selectedFilter == FilterOptions.Median)
                                    {
                                        result = MedianFilter(gray, kernelSize);
                                    } 
                                    result = Task1Pipeline(gray, thresholdValue);
                                    break;
                                }
                                case ProcessingFunctions.Task2:
                                {
                                    int[,] grayStructElem = new int[kernelSize,kernelSize];
                                    result = GrayscaleErodeImage(gray, grayStructElem);
                                    break;
                                }
                                case ProcessingFunctions.Task3:
                                {
                                    bool[,] structElem = new bool[kernelSize,kernelSize];
                                    for (byte i = 0; i < kernelSize; i++)
                                    for (byte j = 0; j < kernelSize; j++)
                                    {
                                        structElem[i,j] = true;
                                    }
                                    result = BinaryCloseImage(gray, structElem);
                                    break;
                                }
                                case ProcessingFunctions.HistogramCheck:
                                {  
                                    newStatusText = HistogramCheck(gray);
                                    break;
                                }
                                case ProcessingFunctions.HistogramMatch:
                                {
                                    if (_loadedColorRef == null)
                                    {
                                        newStatusText = "Please load a reference image.";
                                        break;
                                    }
                                    byte[,,] colorRef = _loadedColorRef;
                                    byte[,] grayRef = ConvertToGrayscale(colorRef);
                                    result = MatchHistograms(gray, grayRef);
                                    break;
                                }
                                case ProcessingFunctions.SequentialRegionLabeling:
                                {
                                    result = sequentialRegionLabeling(gray);
                                    break;
                                }
                                case ProcessingFunctions.FloodFill:
                                {
                                    result = floodFill(gray);
                                    break;
                                }
                                case ProcessingFunctions.HoughTransform:
                                {
                                    // TODO: add binary detection. Contrast adjustment for check but not for hough transform 
                                    bool isBinary = IsBinary(gray);
                                    int[,] output = HoughTransformer.HoughTransform(gray, isBinary, 360, 360);
                                    gray = IntToByte(output);
                                    result = AdjustContrast(gray);
                                    break; 
                                }
                                case ProcessingFunctions.HoughPeaks:
                                {
                                    result = HoughTransformer.HoughPeaks(gray, thresholdSValue);
                                    break;
                                }
                                case ProcessingFunctions.HoughLineSegments:
                                {
                                    bool isBinary = IsBinary(gray);
                                    int[,] accumulator = HoughTransformer.HoughTransform(gray, IsBinary(gray), kValue, dValue);
                                    // accumulator = HoughTransformer.NormaliseAccumulator(gray, accumulator, isBinary);
                                    byte[,] accumulatorB = IntToByte(accumulator);
                                    accumulatorB = AdjustContrast(accumulatorB);
                                    accumulatorB = HoughTransformer.HoughPeaks(accumulatorB, thresholdValue);
                                    List<List<(int x, int y)>> segments = HoughTransformer.HoughLineSegments(gray, accumulatorB, thresholdTValue, segmentValue, gapValue);
                                    result = HoughTransformer.VisualiseSegments(gray, segments);
                                    break;
                                }   
                                case ProcessingFunctions.BinaryPipeline:
                                {
                                    // Gaussion Smoothing
                                    gray = ConvolveImage(gray, CreateGaussianFilter(kernelSize, sigmaValue));
                                    // Detect edges
                                    gray = EdgeMagnitude(gray, _horizontalKernel, _verticalKernel);
                                    // Apply threshold (t_mag)
                                    gray = ThresholdImage(gray, thresholdValue);
                                    // Perform Hough transform (binary)
                                    int[,] accumulator = HoughTransformer.HoughTransform(gray, true, kValue, dValue);
                                    // Pick out the most salient line fragments
                                    byte[,] accumulatorB = IntToByte(accumulator);
                                    accumulatorB = AdjustContrast(accumulatorB);
                                    accumulatorB = HoughTransformer.HoughPeaks(accumulatorB, thresholdSValue);
                                    List<List<(int x, int y)>> segments = HoughTransformer.HoughLineSegments(gray, accumulatorB);
                                    // Visualize most salient line segments in red
                                    result = HoughTransformer.VisualiseSegments(gray, segments);
                                    break;
                                }
                                case ProcessingFunctions.GrayscalePipeline:
                                {
                                    // Gaussion Smoothing
                                    gray = ConvolveImage(gray, CreateGaussianFilter(kernelSize, sigmaValue));
                                    // Detect edges
                                    gray = EdgeMagnitude(gray, _horizontalKernel, _verticalKernel);
                                    // Perform Hough transform (grayscale)
                                    int[,] accumulator = HoughTransformer.HoughTransform(gray, false, kValue, dValue);
                                    if (normalise) {
                                        accumulator = HoughTransformer.NormaliseAccumulator(gray, accumulator, false);
                                    }
                                    byte[,] accumulatorB = IntToByte(accumulator);
                                    accumulatorB = AdjustContrast(accumulatorB);

                                    // Pick out the most salient line fragments
                                    accumulatorB = HoughTransformer.HoughPeaks(accumulatorB, thresholdSValue);
                                    List<List<(int x, int y)>> segments = HoughTransformer.HoughLineSegments(gray, accumulatorB, thresholdTValue, segmentValue, gapValue);
                                    // Visualize most salient line segments in red
                                    result = HoughTransformer.VisualiseSegments(gray, segments);
                                    break;
                                }
                                case ProcessingFunctions.NormalizeAccumulator:
                                {
                                    bool binary = IsBinary(gray);
                                    int[,] accumulator = HoughTransformer.HoughTransform(gray, binary, kValue, dValue);
                                    accumulator = HoughTransformer.NormaliseAccumulator(gray, accumulator, binary);
                                    result = AdjustContrast(IntToByte(accumulator));
                                    break;
                                }
                                case ProcessingFunctions.CannyEdgeDetection:
                                {
                                    result = cannyEdgeDetection(gray, _horizontalKernel, _verticalKernel, thresholdValue, thresholdSValue);
                                    break;
                                }
                                case ProcessingFunctions.HoughCircleTransform:
                                {
                                    bool binary = IsBinary(gray);
                                    int minRadius = thresholdValue;
                                    int maxRadius = thresholdTValue;
                                    byte[][,] accumulators = HoughTransformer.HoughCircleTransform(gray, binary, minRadius, maxRadius, sigmaValue);
                                    byte[][,] peakAccumulators = new byte[accumulators.GetLength(0)][,];
                                    for (int i = 0; i < accumulators.GetLength(0); i++)
                                    {
                                        byte[,] nms = HoughTransformer.NonMaxSuppression(accumulators[i], wrap: false);
                                        int w = nms.GetLength(0);
                                        int h = nms.GetLength(1);
                                        peakAccumulators[i] = new byte[w, h];
                                        for (int x = 0; x < w; x++)
                                        for (int y = 0; y < h; y++)
                                        {
                                            if (nms[x, y] == 0 || nms[x, y] < thresholdSValue) continue;
                                            if (i > 0 && nms[x, y] <= accumulators[i - 1][x, y]) continue;
                                            if (i < accumulators.GetLength(0) - 1 && nms[x, y] < accumulators[i + 1][x, y]) continue;
                                            peakAccumulators[i][x, y] = byte.MaxValue;
                                        }
                                    }

                                    result = HoughTransformer.VisualiseCircles(gray, peakAccumulators, minRadius);
                                    break;

                                }
                                case ProcessingFunctions.Downsample:
                                {
                                    result = Downsample(gray, 800);
                                    break;
                                }
                                default:
                                    throw new NotSupportedException($"Operation '{selected}' is not implemented in the OnApply switch.");
                            }

                    WriteableBitmap bmp = result switch
                    {
                        byte[,,] colorImg => ColorArrayToBitmap(colorImg),
                        byte[,] grayImg => ByteArrayToBitmap(grayImg),
                        _ => throw new NotSupportedException()
                    };

                    return (result, bmp);
                });

                _processedResult = resultGray;
                (ProcessedImage.Source as IDisposable)?.Dispose();
                ProcessedImage.Source = resultBmp;
                StatusText.Text = newStatusText;

            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error applying {selected}: {ex.Message}";
            }
            finally
            {
                ApplyButton.IsEnabled = true;
            }
        }

        // ====================================================================
        // ==================== GIVEN (already implemented) ==================
        // ====================================================================

        /// <summary>
        /// Converts loaded color pixel data (<c>[x, y, channel]</c> where channel 0=R, 1=G, 2=B)
        /// to single-channel grayscale by averaging RGB values.
        /// </summary>
        /// <param name="colorPixels">The 3D array of color pixels extracted at load time.</param>
        /// <returns>A 2D array of grayscale byte intensities with values in [0, 255].</returns>
        private static byte[,] ConvertToGrayscale(byte[,,] colorPixels)
        {
            int w = colorPixels.GetLength(0);
            int h = colorPixels.GetLength(1);
            byte[,] gray = new byte[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int r = colorPixels[x, y, 0];
                int g = colorPixels[x, y, 1];
                int b = colorPixels[x, y, 2];
                gray[x, y] = (byte)((r + g + b) / 3);
            }

            return gray;
        }

        // ====================================================================
        // ==================== FUNCTIONS TO IMPLEMENT =======================
        // ====================================================================

        // TODO : Figure out best parameters for the preprocessing.
        // Proportions of dutch electrical outlet:
        // Radius of outlet: 2cm
        // Radius of prong: 0.25cm
        // Distance between outer perimeter of prongs: 1.5cm
        // Distance from perimeter prong to outlet perimeter: 0.75cm
        private byte[,] objectDetectionPipeline (byte[,] gray)
        {
            int w = gray.GetLength(0);
            int h = gray.GetLength(1);
            byte[,] output = new byte[w,h];

            // -----------------------------------------------------------------
            // Pre-processing.
            // Parameters for the functions
            byte kernelSize = 5; // Guassian blur size; higher = more smoothing, less noise
            float sigmaValue = 0.5f; // Guassian standard deviation; Stronger smoothing
            byte thresholdValue = 100; // Sharpening threshold; higher = fewer contrasts get sharpened
            float sharpenValue = 0.5f; // Sharpening intensity; Stronger edges, but more noise

            // TODO : Figure out what kernel size and sigma works best.
            // Reduce noise in the image.
            output = ConvolveImage(gray, CreateGaussianFilter(kernelSize, sigmaValue));
            // output = MedianFilter(gray, kernelSize);

            // TODO : Maybe run the image through edge sharpening?
            byte[,] mask =  ConvolveImage(gray, CreateGaussianFilter(kernelSize, sigmaValue));
            output = Sharpen(output, mask, thresholdValue, sharpenValue);

            // TODO : Determine for what percentages it works best.
            // Adjust the contrast such that the object is easier to pick out.
            output = AutoAdjustContrast(output, 5, 95);

            // Run the image through Edge detection
            // output = EdgeMagnitude(output, _horizontalKernel, _verticalKernel);

            // Canny edge detection
            output = cannyEdgeDetection(output, _horizontalKernel, _verticalKernel, 50, 150);

            // -----------------------------------------------------------------
            // Recognizing the object.

            // Run hough circles
            int minRadius = 10;
            int maxRadius = 200;
            byte confidence_threshold = 80;
            byte[][,] accumulators = HoughTransformer.HoughCircleTransform(output, false, minRadius, maxRadius);

            // Step 1: Find pairs of circles within the circle, pairs should have
            //  A similar size.
            // Start by finding circles
            byte[][,] peakAccumulators = new byte[accumulators.GetLength(0)][,];
            for (int i = 0; i < accumulators.GetLength(0); i++)
            {
                byte[,] nms = HoughTransformer.NonMaxSuppression(accumulators[i], wrap: false);
                int width = nms.GetLength(0);
                int height = nms.GetLength(1);
                peakAccumulators[i] = new byte[width, height];
                for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (nms[x, y] == 0 || nms[x, y] < confidence_threshold) continue;
                    if (i > 0 && nms[x, y] <= accumulators[i - 1][x, y]) continue;
                    if (i < accumulators.GetLength(0) - 1 && nms[x, y] < accumulators[i + 1][x, y]) continue;
                    peakAccumulators[i][x, y] = byte.MaxValue;
                }
            }

            List<Circle> circles = new List<Circle>();
            for (int r = 0; r<peakAccumulators.Length; r++)
            {
                byte[,] temp = peakAccumulators[r];
                for (int x = 1; x < temp.GetLength(0)-1; x++)
                for (int y = 1; y < temp.GetLength(1)-1; y++)
                {
                    if(temp[x,y]==255)
                    {
                        circles.Add(new Circle(x,y,(r+minRadius),temp[x,y]));
                    }
                }
            }

            // Iterate over all circles and try to form pairs of potential plug holes.
            // HEURISTIC determining the similarity of the holes
            const float HEURISTIC = 0.90f; // Holes should be HEURISTIC*100 % similar
            List<(Circle ,Circle)> pairs = new List<(Circle,Circle)>();
            for (int i = 0; i < circles.Count; i++)
            for (int j = i+1; j < circles.Count; j++)
            {
                Circle c1 = circles[i];
                Circle c2 = circles[j];
                // The circles should have about the same radius.
                float circle_ratio = (float)Math.Min(c1.r, c2.r) / Math.Max(c1.r, c2.r);
                if (circle_ratio < HEURISTIC) continue;
                // The circles should at be about 6*average(c1.r+c2.r) apart.
                // double d = Math.Sqrt(Math.Pow((c1.x-c2.x), 2)+Math.Pow((c1.y-c2.y),2))-(c1.r+c2.r);
                double d = circleDistance(c1.x,c2.x,c1.y,c2.y,c1.r,c2.r);
                float average_r = (c1.r+c2.r)/2;
                float expected_d = 6*average_r;
                if (d < (HEURISTIC*expected_d)) continue;
                if (d > ((2f-HEURISTIC)*expected_d)) continue;
                pairs.Add((c1,c2));
            }

            // All potential pairs of circles should now have been found.
            // Step 2: Both circles should be contained within a big circle and be
            //  some distance (approximately 3*r of plughole) apart from the outer parameter. 
            // The big circle should be around a specific ratio larger. 
            // The small holes also should be on opposite sides of the big circle.
            // possible_outlets holds all possible tuples of outlets in the following order;
            //  Item1=outlet ; Item2=prong_hole_1 ; Item3=prong_hole_2.
            List<(Circle,Circle,Circle)> possible_outlets = new List<(Circle,Circle,Circle)>();
            for (int i = 0; i < pairs.Count; i++)
            {
                Circle c1 = pairs[i].Item1;
                Circle c2 = pairs[i].Item2;
                for (int j = 0; j < circles.Count; j++)
                {
                    Circle outlet = circles[j];
                    // The radius of the outlet circle should be about 8 times the
                    //  radius of the prong holes.
                    float average_r = (c1.r+c2.r)/2;
                    float expected_r_outlet = average_r*8.0f;
                    if (expected_r_outlet<outlet.r*HEURISTIC) continue;
                    if (expected_r_outlet>outlet.r*(2-HEURISTIC)) continue;

                    // Distance from the outlet to the holes should be negative and
                    //  be approximately 3*radius_of_prong_hole.
                    double d1 = circleDistance(c1.x,outlet.x,c1.y,outlet.y,c1.r,outlet.r);
                    double d2 = circleDistance(outlet.x,c2.x,outlet.y,c2.y,outlet.r,c2.r);
                    if ((d1>=0)||(d2>=0)) continue;
                    float expected_d = 3.0f*average_r;
                    if (Math.Abs(d1) < (HEURISTIC*expected_d)) continue;
                    if (Math.Abs(d1) > ((2f-HEURISTIC)*expected_d)) continue;
                    if (Math.Abs(d2) < (HEURISTIC*expected_d)) continue;
                    if (Math.Abs(d2) > ((2f-HEURISTIC)*expected_d)) continue;
                    possible_outlets.Add((outlet,c1,c2));
                }
            }

            // Step 3: Rank the pairs (triplets?) by confidence and give a definitive
            //  answer.
            
            // Step 4: Visualize the answer in red.

            return output;
        }// objectDetectionPipeline.

        // Returns the distance between 2 circles. Return vlaue can be negative.
        private double circleDistance(int x1, int x2, int y1, int y2, int r1, int r2)
        {
            return Math.Sqrt(Math.Pow((x1-x2), 2)+Math.Pow((y1-y2),2))-(r1+r2);
        }

        private static byte[,] cannyEdgeDetection (byte[,] inputImage,
                                            float[,] horizontalKernel,
                                            float[,] verticalKernel,
                                            byte lower_threshold = 50,
                                            byte upper_threshold = 250)
        {
            // create temporary grayscale image
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            byte[,] tempImage = new byte[w,h];

            // Apply Gaussian filter to smooth the image in order to remove the noise - Already done in preprocessing.
            short[,] tempImageH = SConvolveImage(inputImage, horizontalKernel);
            short[,] tempImageV = SConvolveImage(inputImage, verticalKernel);

            double[,] magnitude = new double[w,h];
            double[,] direction = new double[w,h]; 

            // Find the intensity gradients of the image
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                double gx = tempImageH[x,y];
                double gy = tempImageV[x,y];

                magnitude[x,y] = Math.Sqrt(
                    Math.Pow(gx, 2) +
                    Math.Pow(gy, 2)
                );

                // Compute the angle using the vector produced by gx and gy 
                direction[x,y] = Math.Atan2(gy,gx);
            }

            // Apply non-maximum suppression, this is used to remove multiple overlapping
            // bounding boxes that represent the same object.
            double[,] suppressed = NonMaximumSuppression(magnitude, direction);

            // TODO: Find good thresholding values.
            // Now apply double thresholding.
            byte weak_threshold = 75;
            byte strong_threshold = 255;

            byte[,] thresholded = new byte[w,h];

            for (int x = 0; x<w; x++)
            for (int y = 0; y<h; y++)
            {
                if (suppressed[x,y]>=upper_threshold){
                    thresholded[x,y] = strong_threshold;
                }
                else if (suppressed[x,y] >= lower_threshold){
                    thresholded[x,y] = weak_threshold;
                }
            }

            // Finally we use edge tracking by hysteresis
            byte[,] output = new byte[w, h];

            Queue<(int x, int y)> queue = new();

            // Find all strong edges
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (thresholded[x, y] == strong_threshold)
                {
                    output[x, y] = strong_threshold;
                    queue.Enqueue((x, y));
                }
            }

            // Eight neighborhood
            int[] dx = { -1, -1, -1, 0, 0, 1, 1, 1 };
            int[] dy = { -1, 0, 1, -1, 1, -1, 0, 1 };

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();

                for (int i = 0; i < 8; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];

                    if (nx < 0 || nx >= w ||
                        ny < 0 || ny >= h)
                        continue;

                    if (thresholded[nx, ny] == weak_threshold &&
                        output[nx, ny] == 0)
                    {
                        output[nx, ny] = strong_threshold;
                        queue.Enqueue((nx, ny));
                    }
                }
            }

            return output;
        }

        private static double[,] NonMaximumSuppression(double[,] magnitude, 
                                                     double[,] direction)
        {
            int w = magnitude.GetLength(0);
            int h = magnitude.GetLength(1);
            double[,] output = new double[w,h];

            for (int x = 1; x < (w-1); x++)
            for (int y = 1; y < (h-1); y++)
            {
                double angle = direction[x,y]*180.0/Math.PI;
                if (angle<0) angle += 180.0;
                double first;
                double second;

                if (angle < 22.5 || angle >= 157.5){
                    first = magnitude[x - 1, y];
                    second = magnitude[x + 1, y];
                }
                else if (angle < 67.5){
                    first = magnitude[x - 1, y - 1];
                    second = magnitude[x + 1, y + 1];
                }
                else if (angle < 112.5){
                    first = magnitude[x, y - 1];
                    second = magnitude[x, y + 1];
                }
                else{
                    first = magnitude[x - 1, y + 1];
                    second = magnitude[x + 1, y - 1];
                }

                if (magnitude[x, y] >= first &&
                    magnitude[x, y] >= second)
                {
                    output[x, y] = magnitude[x, y];
                }
            }

            return output;
        }

        private static bool IsBinary(byte[,] gray)
        {
            int w = gray.GetLength(0);
            int h = gray.GetLength(1);
            for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
            {
                if ((gray[i,j]!=0)&&(gray[i,j]!=255)) return false;
            }
            return true;
        }
        private static byte[,] Task1Pipeline(byte[,] gray, byte thresholdValue)
        {
            gray = EdgeMagnitude(gray, _horizontalKernel, _verticalKernel);
            gray = ThresholdImage(gray, thresholdValue);
            return gray;
        }

        public static void Print2DArray<T>(T[,] matrix)
        {
            for (int i = 0; i < matrix.GetLength(0); i++)
            {
                for (int j = 0; j < matrix.GetLength(1); j++)
                {
                    Console.Write(matrix[i,j] + "\t");
                }
                Console.WriteLine();
            }
        }


        /// <summary>
        /// Inverts the intensity values of the input grayscale image.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <returns>A new 2D grayscale image with inverted intensities.</returns>
        private byte[,] InvertImage(byte[,] inputImage)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            int w = tempImage.GetLength(0);
            int h = tempImage.GetLength(1);
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                tempImage[x, y] = (byte)(byte.MaxValue-inputImage[x, y]);
            }
            return tempImage;
        }
 
        /// <summary>
        /// Adjusts the contrast of the input grayscale image.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <returns>A new 2D grayscale image with adjusted contrast.</returns>
        private byte[,] AdjustContrast(byte[,] inputImage, float cutPercent = 0)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            byte cutPercentInt = 0;
            if (cutPercent > 0 && cutPercent < 0.5f) cutPercentInt = (byte)(cutPercent * byte.MaxValue);
            int w = tempImage.GetLength(0);
            int h = tempImage.GetLength(1);
            byte maxValue = 0;
            byte minValue = byte.MaxValue;
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                maxValue = inputImage[x, y] > maxValue && inputImage[x, y] <= byte.MaxValue - cutPercentInt ? inputImage[x, y] : maxValue;
                minValue = inputImage[x, y] < minValue && inputImage[x, y] >= cutPercentInt ? inputImage[x, y] : minValue;
            }
            float contrastCoefficient = 1f;
            if (maxValue - minValue > 0)
            {
                contrastCoefficient = byte.MaxValue / (maxValue - minValue);

            }

            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                tempImage[x, y] = (byte)Math.Clamp(Math.Floor((inputImage[x, y] - minValue) * contrastCoefficient), 0, byte.MaxValue);
            }
            return tempImage;
        }// AdjustContrast

        /// <summary>
        /// Adjusts the contrast of the input grayscale image.
        /// Values above max are mapped to 255, values below min are mapped to 0.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="min">The threshold % under which the pixels are mapped to 0.</param>
        /// <param name="max">The threshold % above which the pixels are mapped to 255.</param>
        /// <returns>A new 2D grayscale image with adjusted contrast.</returns>
        private byte[,] AutoAdjustContrast(byte[,] inputImage, int min = 5, int max = 95)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            int w = tempImage.GetLength(0);
            int h = tempImage.GetLength(1);

            // Determine the values of Alow and Ahigh
            // First calculate the accumulative pixel value
            int[] histogram = new int[256];
            int total_pixels = w*h;
            for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
            {
                histogram[inputImage[i,j]]++;
            }
            int[] accumulative_histogram = new int[256];
            accumulative_histogram[0] = histogram[0];
            for (int i = 1; i < 256; i++)
            {
                accumulative_histogram[i] = accumulative_histogram[i-1]+histogram[i];
            }

            // Now calculate the lower and upper threshold.
            int lower_threshold = (int)(total_pixels*(min/100.0));
            int upper_threshold = (int)(total_pixels*(max/100.0));

            // Calculate the values for alow and ahigh
            int a_low = -1;
            int a_high = -1;
            int iterator = 0;
            while (a_low == -1)
            {
                if (accumulative_histogram[iterator]>=lower_threshold) a_low = iterator;
                iterator++;
                if (iterator > 255)
                {
                    a_low = 0;
                    break;
                }
            }// while
            iterator = 0;
            while (a_high == -1)
            {
                if (accumulative_histogram[iterator] > upper_threshold) a_high = iterator ;
                iterator++;
                if (iterator > 255)
                {
                    a_high = 255;
                    break;
                }
            }// while

            float contrastCoefficient = 1f;
            if (a_high - a_low > 0)
            {
                contrastCoefficient = (float)byte.MaxValue / (a_high - a_low);
            }

            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (inputImage[x,y]>=a_high) tempImage[x,y] = 255;
                else if (inputImage[x,y]<=a_low) tempImage[x,y] = 0;
                else tempImage[x, y] = (byte)Math.Clamp((int)Math.Round((inputImage[x,y] - a_low) * contrastCoefficient),0, 255);
            }
            return tempImage;
        }// AutoAdjustContrast

        private byte[,] IntToByte(int[,] inputImage)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            int w = tempImage.GetLength(0);
            int h = tempImage.GetLength(1);
            int maxValue = 0;
            int minValue = int.MaxValue;
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                maxValue = inputImage[x, y] > maxValue ? inputImage[x, y] : maxValue;
                minValue = inputImage[x, y] < minValue ? inputImage[x, y] : minValue;
            }
            float contrastCoefficient = 1f;
            if (maxValue - minValue > 0 && maxValue > byte.MaxValue)
            {
                contrastCoefficient = (float)byte.MaxValue / maxValue;

            }

            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                tempImage[x, y] = (byte)Math.Round(inputImage[x, y] * contrastCoefficient);
            }
            return tempImage;
        }

        /// <summary>
        /// Generates a normalized 2D Gaussian filter kernel of the specified size and standard deviation.
        /// </summary>
        /// <param name="size">Kernel dimension (odd integer, e.g. 3, 5, 7).</param>
        /// <param name="sigma">Gaussian standard deviation parameter.</param>
        /// <returns>A 2D float array representing the normalized filter kernel.</returns>
        private float[,] CreateGaussianFilter(byte size, float sigma)
        {
            // create the filter
            float[,] filter = new float[size, size];
            const float pi = (float)Math.PI;
            const float E = (float)Math.E;
            float sigmaSquared = sigma*sigma;
            float denominator = 2*sigmaSquared;

            // Creation of the filter kernel
            for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                int xSquared = (int)Math.Pow(x - size/2,2);
                int ySquared = (int)Math.Pow(y - size/2,2);

                filter[x,y] = (1 / (pi*denominator))*
                            ((float)Math.Pow(E, -((xSquared+ySquared)/denominator)));
            }

            // Normalization of the filter kernel
            return NormalizeFilter(filter);

        }

        /// <summary>
        /// This is a function that normalizes all values in the given 2-Dimensional
        /// matrix to have a total value of 1.
        /// This is achieved by adding all elements to eachother, followed by  
        /// division of said elements by the total value.
        /// </summary>
        /// <param name="filter">The 2D filter kernel.</param>
        /// <returns>The filter normalized to a sum of 1.</param>
        private float[,] NormalizeFilter(float[,] filter)
        {
            int size = filter.GetLength(0);
            float totalValue = 0;
            for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                totalValue += filter[x,y];
            }
            for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                filter[x,y] = filter[x,y]/totalValue;
            }
        
            return filter;
        }

        /// <summary>
        /// Convolves a grayscale image with a given 2D filter kernel.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="filter">The 2D filter kernel to apply.</param>
        /// <returns>The convolved grayscale image.</returns>
        private byte[,] ConvolveImage(byte[,] inputImage, float[,] filter)
        {
            short[,] convolvedImage = SConvolveImage(inputImage, filter);
            int w = convolvedImage.GetLength(0);
            int h = convolvedImage.GetLength(1);
            byte[,] byteImage = new byte[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                byteImage[x, y] = (byte)Math.Clamp(convolvedImage[x, y], (short)0, byte.MaxValue);

            }
            return byteImage;
        }
        private static short[,] SConvolveImage(byte[,] inputImage, float[,] filter)
        {
            // create temporary grayscale image
            int fSize = filter.GetLength(0);
            byte[,] marginImage = AddMargin(inputImage, fSize/2);
            short[,] tempImage = new short[inputImage.GetLength(0), inputImage.GetLength(1)];
    
            int w = tempImage.GetLength(0);
            int h = tempImage.GetLength(1);
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float newValue = 0;
                for (int fX = 0; fX < fSize; fX++)
                for (int fY = 0; fY < fSize; fY++)
                {
                    newValue += marginImage[x + fX, y + fY] * filter[fSize - 1 - fX, fSize - 1 - fY];
                }
                tempImage[x, y] = (short)newValue;
            }
            return tempImage;
        }

        private static byte[,] AddMargin(byte[,] inputImage, int margin)
        {
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);
            int iX;
            int iY;
            byte[,] tempImage = new byte[iW + 2 * margin, iH + 2 * margin];
            for (int x = 0; x < iW + margin * 2; x++)
            for (int y = 0; y < iH + margin * 2; y++)
            {
                iX = Math.Min(Math.Max(x - margin, 0), iW - 1);
                iY = Math.Min(Math.Max(y - margin, 0), iH - 1);
                tempImage[x, y] = inputImage[iX, iY];
            }
            return tempImage;

        }

        /// <summary>
        /// Applies a median filter of the given kernel size to reduce noise.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="kernelSize">The size of the local neighborhood window (odd integer).</param>
        /// <returns>The filtered grayscale image.</returns>
        private byte[,] MedianFilter(byte[,] inputImage, byte kernelSize)
        {
            // create temporary grayscale image

            int fSize = (int)kernelSize;
            byte[,] marginImage = AddMargin(inputImage, fSize/2);
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            
            int vLen = (int)Math.Pow(kernelSize, 2);

            int w = tempImage.GetLength(0);
            int h = tempImage.GetLength(1);
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                byte[] valueArray = new byte[vLen];
                for (int fX = 0; fX < fSize; fX++)
                for (int fY = 0; fY < fSize; fY++)
                {
                    valueArray[fX + fY * fSize] = marginImage[x + fX, y + fY];
                }
                Array.Sort(valueArray);
                tempImage[x, y] = valueArray[(int)Math.Ceiling((float)vLen/2)];
            }
            return tempImage;
        }

        /// <summary>
        /// Computes edge magnitude from horizontal and vertical derivative kernels.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="horizontalKernel">Horizontal gradient kernel.</param>
        /// <param name="verticalKernel">Vertical gradient kernel.</param>
        /// <returns>The edge gradient magnitude image.</returns>
        private static byte[,] EdgeMagnitude(
            byte[,] inputImage,
            float[,] horizontalKernel,
            float[,] verticalKernel
        )
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            short[,] tempImageH = SConvolveImage(inputImage, horizontalKernel);
            short[,] tempImageV = SConvolveImage(inputImage, verticalKernel);
            for (int x = 0; x < inputImage.GetLength(0); x++)
            for (int y = 0; y < inputImage.GetLength(1); y++)
            {
                double magnitude = Math.Sqrt(
                    Math.Pow(tempImageH[x, y], 2) +
                    Math.Pow(tempImageV[x, y], 2)
                );

                tempImage[x, y] = (byte)Math.Clamp(
                    magnitude,
                    0,
                    255
                );            
            }

            return tempImage;

        }

        /// <summary>
        /// Thresholds a grayscale image into a binary representation based on a cutoff value.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="threshold">Intensity threshold cutoff value in [0, 255].</param>
        /// <returns>A binary image represented as byte intensities (e.g. 0 and 255).</returns>
        private static byte[,] ThresholdImage(byte[,] inputImage, byte threshold)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            for (int x = 0; x < inputImage.GetLength(0); x++)
            for (int y = 0; y < inputImage.GetLength(1); y++)
            {
                if(inputImage[x,y]<threshold) tempImage[x,y] = 0b00000000;
                else tempImage[x,y] = 0b11111111;
            }

            return tempImage;
        }

        /// <summary>
        /// Performs morphological binary erosion using the provided structuring element.
        /// </summary>
        /// <param name="inputImage">The binary input image.</param>
        /// <param name="structElem">2D boolean structuring element (true = foreground).</param>
        /// <returns>The eroded binary image.</returns>
        private byte[,] BinaryErodeImage(byte[,] inputImage, bool[,] structElem)
        {
            int x = inputImage.GetLength(0);
            int y = inputImage.GetLength(1);
            int x_struct = structElem.GetLength(0);
            int y_struct = structElem.GetLength(1);
            byte[,] output = new byte[x,y];

            // For all pixels in the input image.
            for (int i = 0; i<x; i++)
            for (int j = 0; j<y; j++)
            {
                if (inputImage[i,j] == 0) continue;
                
                output[i,j] = 0;
                bool make_white = true;
                // For all pixels in the structuring element.
                for (int u = 0; u < x_struct; u++)
                for (int v = 0; v < y_struct; v++)
                {
                    // Calculate the offset
                    int x_offset = i+(u-(x_struct/2));
                    int y_offset = j+(v-(y_struct/2));
                    if (!structElem[u,v]) continue; 
                    if ((x_offset>=0) && (x_offset<x) && (y_offset>=0) && (y_offset<y))
                    {
                        if (inputImage[x_offset, y_offset]==255) continue;
                    }
                    make_white = false;
                }
                if (make_white)
                {
                    output[i,j] = 255;
                }
            }

            return output;
        }

        /// <summary>
        /// Performs morphological binary dilation using the provided structuring element.
        /// </summary>
        /// <param name="inputImage">The binary input image.</param>
        /// <param name="structElem">2D boolean structuring element (true = foreground).</param>
        /// <returns>The dilated binary image.</returns>
        private byte[,] BinaryDilateImage(byte[,] inputImage, bool[,] structElem)
        {
            int x = inputImage.GetLength(0);
            int y = inputImage.GetLength(1);
            int x_struct = structElem.GetLength(0);
            int y_struct = structElem.GetLength(1);
            byte[,] output = new byte[x,y];

            // For all pixels in the input image.
            for (int i = 0; i<x; i++)
            for (int j = 0; j<y; j++)
            {
                if (inputImage[i,j] == 0) continue;
                output[i,j] = inputImage[i,j];
                // For all pixels in the structuring element.
                for (int u = 0; u < x_struct; u++)
                for (int v = 0; v < y_struct; v++)
                {
                    // Calculate the offset
                    int x_offset = i+(u-(x_struct/2));
                    int y_offset = j+(v-(y_struct/2));

                    // Check if the offset element is within the bounds of the image.
                    if ((x_offset>=0) && (x_offset<x) && (y_offset>=0) && (y_offset<y))
            {
                        if((inputImage[i, j] != 0) && structElem[(u),(v)])
                        {
                            output[x_offset,y_offset] = 255; 
                        }
                    }
                }
            }

            return output;
        }

        /// <summary>
        /// Performs morphological binary opening (erosion followed by dilation).
        /// </summary>
        /// <param name="inputImage">The binary input image.</param>
        /// <param name="structElem">2D boolean structuring element (true = foreground).</param>
        /// <returns>The opened binary image.</returns>
        private byte[,] BinaryOpenImage(byte[,] inputImage, bool[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            // Perform erosion
            output = BinaryErodeImage(inputImage, structElem);
            // Perform dilation on erosion output
            output = BinaryDilateImage(output, structElem);
            return output;
        }

        /// <summary>
        /// Performs morphological binary closing (dilation followed by erosion).
        /// </summary>
        /// <param name="inputImage">The binary input image.</param>
        /// <param name="structElem">2D boolean structuring element (true = foreground).</param>
        /// <returns>The closed binary image.</returns>
        private byte[,] BinaryCloseImage(byte[,] inputImage, bool[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            // Perform dilation
            output = BinaryDilateImage(inputImage, structElem);
            // Perform erosion
            output = BinaryErodeImage(output, structElem);
            return output;
        }

        /// <summary>
        /// Performs morphological grayscale erosion using the provided structuring element.
        /// </summary>
        /// <param name="inputImage">The 2D grayscale input image.</param>
        /// <param name="structElem">2D integer structuring element defining neighborhood offsets.</param>
        /// <returns>The eroded grayscale image.</returns>
        private byte[,] GrayscaleErodeImage(byte[,] inputImage, int[,] structElem)
        {
            int x = inputImage.GetLength(0);
            int y = inputImage.GetLength(1);
            int x_struct = structElem.GetLength(0);
            int y_struct = structElem.GetLength(1);
            byte[,] output = new byte[x,y];

            // For all pixels in the input image.
            for (int i = 0; i<x; i++)
            for (int j = 0; j<y; j++)
            {
                byte min = 255;
                // For all pixels in the structuring element, find the minimum value.
                for (int u = 0; u < x_struct; u++)
                for (int v = 0; v < y_struct; v++)
                {
                    // Calculate the offset
                    int x_offset = i+(u-(x_struct/2));
                    int y_offset = j+(v-(y_struct/2));

                    // Check if the offset element is within the bounds of the image.
                    if ((x_offset>=0) && (x_offset<x) && (y_offset>=0) && (y_offset<y))
                    {
                        if (Convert.ToByte(inputImage[x_offset, y_offset]-Convert.ToByte(structElem[u,v]))<min)
                        {
                            min = Convert.ToByte(inputImage[x_offset, y_offset]-Convert.ToByte(structElem[u,v]));
                            if (min<0) min = 0;
                        }
                    }
                }
                output[i,j] = min;
            }
            return output;
        }

        /// <summary>
        /// Performs morphological grayscale dilation using the provided structuring element.
        /// </summary>
        /// <param name="inputImage">The 2D grayscale input image.</param>
        /// <param name="structElem">2D integer structuring element defining neighborhood offsets.</param>
        /// <returns>The dilated grayscale image.</returns>
        private byte[,] GrayscaleDilateImage(byte[,] inputImage, int[,] structElem)
        {
            int x = inputImage.GetLength(0);
            int y = inputImage.GetLength(1);
            int x_struct = structElem.GetLength(0);
            int y_struct = structElem.GetLength(1);
            byte[,] output = new byte[x,y];

            // For all pixels in the input image.
            for (int i = 0; i<x; i++)
            for (int j = 0; j<y; j++)
            {
                byte max = 0;
                // For all pixels in the structuring element, find the minimum value.
                for (int u = 0; u < x_struct; u++)
                for (int v = 0; v < y_struct; v++)
                {
                    // Calculate the offset
                    int x_offset = i+(u-(x_struct/2));
                    int y_offset = j+(v-(y_struct/2));

                    // Check if the offset element is within the bounds of the image.
                    if ((x_offset>=0) && (x_offset<x) && (y_offset>=0) && (y_offset<y))
                    {
                        if (Convert.ToByte(inputImage[x_offset, y_offset]+Convert.ToByte(structElem[u,v]))>max)
                        {
                            max = Convert.ToByte(inputImage[x_offset, y_offset]+Convert.ToByte(structElem[u,v]));
                            if (max>255) max = 255;
                        }
                    }
                }
                output[i,j] = max;
            }
            return output;
        }

        private byte[,] Sharpen(byte[,] inputImage, byte[,] mask, byte threshold, float amount = 0.3f)
        {
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            float result;
            byte[,] output = new byte[w,h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                result = inputImage[x,y] - mask[x,y];
                if (Math.Abs(result) > threshold) {
                    output[x, y] = (byte)Math.Clamp(inputImage[x,y] + result * amount, 0, 255);
                } else {
                    output[x, y] = inputImage[x,y];
                } 
                // Debug.WriteLine("Input: {0}, Mask: {1}, Output: {2}", inputImage[x,y], mask[x,y], result);
            }
            return output;
        }

        private List<(int x, int y)>[] GenerateHistogram(byte[,] inputImage)
        {
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            List<(int x, int y)>[] output = new List<(int x, int y)>[256];
            for (int i = 0; i < 256; i++)
            {
                output[i] = new List<(int x, int y)>();
            }
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                (int x, int y) coords = (x, y);
                output[inputImage[x,y]].Add(coords);
            }
            return output;
        }

        private string HistogramCheck(byte[,] gray)
        {
            List<(int x, int y)>[] hist = GenerateHistogram(gray);
            byte min = 0;
            byte max = 0;
            for (short i = 0; i <= 255; i++)
            {
                if (hist[i].Count > 0)
                {
                    min = (byte)i;
                    break;
                }
            }
            for (short i = 255; i >= 0; i--)
            {
                if (hist[i].Count > 0)
                {
                    max = (byte)i;
                    break;
                }
            }
            bool fullContrast = max == 255 && min == 0;
            bool fullDynamicRange = true;
            for (short i = min; i < max; i++)
            {
                if (!(hist[i].Count > 0))
                {
                    fullDynamicRange = false;
                    break;
                }
            }
            return $"This image {(fullContrast ? "has" : "does not have")} maximum contrast and {(fullDynamicRange ? "has" : "does not have")} maximum dynamic range.";
        }

        private byte[,] MatchHistograms(byte[,] inputImage, byte[,] referenceImage)
        {
            List<(int x, int y)>[] hist = GenerateHistogram(inputImage);
            List<(int x, int y)>[] histRef = GenerateHistogram(referenceImage);


            long inputCoefficient = inputImage.GetLength(0) * (long)inputImage.GetLength(1); 
            long referenceCoefficient = referenceImage.GetLength(0) * (long)referenceImage.GetLength(1); 

            long cumulative = 0;
            long cumulativeRef = 0;
            long cumulativePotential;
            byte[] intensities = new byte[256];
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            int j = 0;
            for(int i = 0; i<=255; i++)
            {
                cumulativeRef += histRef[i].Count * inputCoefficient;
                Debug.WriteLine(cumulativeRef);
                Debug.WriteLine(cumulative);

                while (j < 256)
                {
                    cumulativePotential = cumulative + hist[j].Count * referenceCoefficient;
                    if (cumulativePotential > cumulativeRef) break;
                    cumulative = cumulativePotential;
                    intensities[j] = (byte)i;
                    j++;
                }
            }
            for(int u = 0; u <  inputImage.GetLength(0); u++)
            for(int v = 0; v < inputImage.GetLength(1); v++)
            {
                output[u, v] = intensities[inputImage[u, v]];
            }
            return output;
        }

        /// <summary>
        ///  Performs sequential region labeling on the binary input image.
        /// </summary>
        /// <param name="inputImage">The 2D input binary image.</param>
        /// <returns>A new 2D grayscale image with labeled regions.</returns>
        private byte[,] sequentialRegionLabeling(byte[,] inputImage)
        {
            // Labeling
            int x = inputImage.GetLength(0);
            int y = inputImage.GetLength(1);
            byte[,] output = new byte[x,y];
            
            byte label = 2;

            // For all pixels in the input image.
            for (int i = 0; i<x; i++)
            for (int j = 0; j<y; j++)
            {
                // Skip non-foreground pixels
                if (inputImage[i,j]!=255) continue;
                output[i,j] = 255;// Set the output pixel to foreground
                byte temp_label = 255;
                // Look at the neighbouring pixels.
                // Top Left
                if ((i>0)&&(j>0))
                {
                    if (output[i-1,j-1]!=0)
                    {
                        if (output[i-1,j-1] < temp_label) temp_label = output[i-1,j-1];
                        output[i,j] = temp_label;
                    }
                }//[-1,-1]
                // Top Middle
                if (j>0)
                {
                    if (output[i,j-1]!=0)
                    {
                        if (output[i,j-1] < temp_label) temp_label = output[i,j-1];
                        output[i,j] = temp_label;
                    } 
                }//[0,-1]
                // Top Right
                if ((i+1<x)&&(j>0))
                {
                    if (output[i+1,j-1]!=0){
                        if (output[i+1,j-1] < temp_label) temp_label = output[i+1,j-1];
                        output[i,j] = temp_label;
                    }
                }//[+1,-1]
                // Centre Left
                if (i>0)
                {
                    if (output[i-1,j]!=0){
                        if (output[i-1,j] < temp_label) temp_label = output[i-1,j];
                        output[i,j] = temp_label;
                    }
                }//[-1,0]

                // If the pixel hasn't been labelled, label it here.
                if(output[i,j]==255)
                {
                    output[i,j]=label;
                    label+=1;
                } 
            }// for loop over all pixels.

            // Make an adjecency matrix containing all region values
            bool[,] collision_matrix = new bool[label,label];
            for (int i = 0; i<x; i++)
            for (int j = 0; j<y; j++)
            {
                if (output[i,j] == 0) continue;
                foreach (int row in new[] {i-1, i, i+1})
                foreach (int column in new[] {j-1,j,j+1})
                {
                    if ((row==i)&&(column==j)) continue;
                    if ((row<0)||(row>=x)||(column<0)||(column>=y)) continue;
                    if (output[row,column]==0) continue;
                    if (output[row,column]==output[i,j]) continue;
                    collision_matrix[output[row,column],output[i,j]] = true;
                }
            }

            // Resolving Collisions
            for (byte i = 0; i<label; i++)
            for (byte j = 0; j<label; j++)
            {
                if (!collision_matrix[i,j]) continue;
                byte smallest = (i<j) ? i:j;
                byte biggest = (i>j) ? i:j;
                for (int u = 0; u<x; u++)
                for (int v = 0; v<y; v++)
                {
                    if (output[u,v]==biggest) output[u,v] = smallest;
                }
                // set values in adjecency matrix to false
                collision_matrix[i,j] = false;
                collision_matrix[j,i] = false;
            }


            // Make the image visible using contrast adjustment.
            // TODO: remove this for final project, should be done somewhere else.
            output = AdjustContrast(output);

            return output;
        }// Sequential region labeling.

        /// <summary>
        ///  Performs flood fill on the binary input image.
        /// </summary>
        /// <param name="inputImage">The 2D input binary image.</param>
        /// <returns>A new 2D grayscale image with labeled regions.</returns>
        private byte[,] floodFill(byte[,] inputImage)
        {
            int x = inputImage.GetLength(0);
            int y = inputImage.GetLength(1);
            byte[,] output = new byte[x,y];

            byte colour = 2;

            // For all pixels.
            for (int i = 0; i<x; i++)
            for (int j = 0; j<y; j++)
            {
                if (inputImage[i,j]!=255) continue;
                if (output[i,j]!=0) continue;

                // Use recursion to fill out the entire object.
                floodFillRecursive(inputImage, output, i, j, colour);
                colour++;   
            }

            output = AdjustContrast(output);
            return output;
        }// Flood Fill

        /// <summary>
        /// Fill all connected foreground pixels using recursion.
        /// </summary>
        private void floodFillRecursive(byte[,] inputImage, byte[,] output, 
                                        int i, int j, byte colour)
        {
            int x = inputImage.GetLength(0);
            int y = inputImage.GetLength(1);
            if ((i<0)||(i>=x)||(j<0)||j>=y) return;// Skip if out of bounds
            if (inputImage[i,j]==0) return;// Skip if its a background pixel.
            if (output[i,j]!=0) return;// Skip if pixel is already labeled

            output[i,j] = colour;

            // Visit all neighbouring pixels
            foreach (int row in new[] {i-1, i, i+1})
            foreach (int column in new[] {j-1,j,j+1})
            {
                if ((row==i)&&(column==j)) continue;
                floodFillRecursive(inputImage, output, row, column, colour);
            }
        }// floodFillRecursive

        private byte[,] Downsample(byte[,] inputImage, int targetResolution = 800)
        {
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            if (w <= targetResolution && h <= targetResolution) return inputImage;

            int downsampledW = w/2;
            int downsampledH = h/2;
            byte[,] downsampledImage = new byte[downsampledW, downsampledH];
            for(int x = 0; x < downsampledW; x ++)
            for(int y = 0; y < downsampledH; y++)
            {
                int u = 2 * x;
                int v = 2 * y;
                downsampledImage[x, y] = (byte)Math.Round((double)(inputImage[u, v] + 
                                                                    inputImage[u + 1, v] + 
                                                                    inputImage[u, v + 1] + 
                                                                    inputImage[u + 1, v + 1])/4);
            }
            return Downsample(downsampledImage, targetResolution);
        }

        // ====================================================================
        // ==================== IMAGE <-> BITMAP HELPERS (given) =============
        // ====================================================================

        /// <summary>
        /// Builds a displayable and savable <see cref="WriteableBitmap"/> from a grayscale byte[,] array
        /// (replicated into R, G, B; alpha fully opaque), using Avalonia's native imaging APIs.
        /// </summary>
        /// <param name="gray">The 2D grayscale byte array.</param>
        /// <returns>A displayable and savable <see cref="WriteableBitmap"/>.</returns>
        private WriteableBitmap ByteArrayToBitmap(byte[,] gray)
        {
            int w = gray.GetLength(0);
            int h = gray.GetLength(1);
            var size = new PixelSize(w, h);
            var bmp = new WriteableBitmap(
                size,
                new(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Opaque
            );

            using var fb = bmp.Lock();

            int totalBytes = fb.RowBytes * h;
            byte[] buffer = new byte[totalBytes];
            for (int y = 0; y < h; y++)
            {
                int rowStart = y * fb.RowBytes;
                for (int x = 0; x < w; x++)
                {
                    byte val = gray[x, y];
                    int idx = rowStart + x * 4;
                    buffer[idx + 0] = val; // R
                    buffer[idx + 1] = val; // G
                    buffer[idx + 2] = val; // B
                    buffer[idx + 3] = 255; // A (fully opaque)
                }
            }

            Marshal.Copy(buffer, 0, fb.Address, totalBytes);

            return bmp;
        }
    }
}