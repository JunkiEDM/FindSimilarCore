using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;

namespace CommonUtils
{
    /// <summary>
    /// The ImageUtils Class holds utility methods that apply to Bitmaps and Images
    /// </summary>
    public static class ImageUtils
    {
        /// <summary>
        /// Store color gradient to file
        /// </summary>
        /// <param name="directory">directory name</param>
        /// <param name="filename">filename to use (extension is ignored and replaced with png)</param>
        /// <param name="useHSL">bool whether to use HSL or HSB</param>
        public static void DrawColorGradient(string directory, string filename, bool useHSL)
        {

            string mode = useHSL ? "HSL" : "HSB";
            String filenameToSave = String.Format("{0}/{1}_{2}.png", directory, System.IO.Path.GetFileNameWithoutExtension(filename), mode);
            Console.Out.WriteLine("Writing " + filenameToSave);

            const int width = 360;
            const int height = 200;

            // Create the image for displaying the data.
            var png = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Graphics g = Graphics.FromImage(png);

            const float saturation = 1.0f;

            // http://en.wikipedia.org/wiki/HSL_and_HSV
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float brightness = 1 - ((float)y / height);

                    Color c = Color.White;
                    if (useHSL)
                    {
                        // HSL
                        c = ColorUtils.AhslToArgb(255, x, saturation, brightness);
                    }
                    else
                    {
                        // HSB
                        c = ColorUtils.AhsbToArgb(255, x, saturation, brightness);
                    }

                    png.SetPixel(x, y, c);
                }
            }

            png.Save(filenameToSave);
            g.Dispose();
        }

        #region Convert to Grayscale
        /// <summary>
        /// Slow grayscale conversion method from
        /// http://www.switchonthecode.com/tutorials/csharp-tutorial-convert-a-color-image-to-grayscale
        /// </summary>
        /// <param name="original">original bitmap to change</param>
        /// <returns>grayscaled version</returns>
        public static Bitmap MakeGrayscaleSlow(Bitmap original)
        {
            //make an empty bitmap the same size as original
            var newBitmap = new Bitmap(original.Width, original.Height);

            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    //get the pixel from the original image
                    Color originalColor = original.GetPixel(x, y);

                    //create the grayscale version of the pixel
                    int grayScale = (int)((originalColor.R * .3) + (originalColor.G * .59)
                                          + (originalColor.B * .11));

                    //create the color object
                    Color newColor = Color.FromArgb(grayScale, grayScale, grayScale);

                    //set the new image's pixel to the grayscale version
                    newBitmap.SetPixel(x, y, newColor);
                }
            }

            return newBitmap;
        }

        /// <summary>
        /// Slightly faster grayscale conversion method from
        /// http://www.switchonthecode.com/tutorials/csharp-tutorial-convert-a-color-image-to-grayscale
        /// </summary>
        /// <param name="original">original bitmap to change</param>
        /// <returns>grayscaled version</returns>
        public static Bitmap MakeGrayscaleFast(Bitmap original)
        {
            unsafe
            {
                //create an empty bitmap the same size as original
                var newBitmap = new Bitmap(original.Width, original.Height);

                //lock the original bitmap in memory
                BitmapData originalData = original.LockBits(
                    new Rectangle(0, 0, original.Width, original.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

                //lock the new bitmap in memory
                BitmapData newData = newBitmap.LockBits(
                    new Rectangle(0, 0, original.Width, original.Height),
                    ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                //set the number of bytes per pixel
                const int pixelSize = 3;

                for (int y = 0; y < original.Height; y++)
                {
                    //get the data from the original image
                    byte* oRow = (byte*)originalData.Scan0 + (y * originalData.Stride);

                    //get the data from the new image
                    byte* nRow = (byte*)newData.Scan0 + (y * newData.Stride);

                    for (int x = 0; x < original.Width; x++)
                    {
                        //create the grayscale version
                        byte grayScale =
                            (byte)((oRow[x * pixelSize] * .11) + //B
                                   (oRow[x * pixelSize + 1] * .59) +  //G
                                   (oRow[x * pixelSize + 2] * .3)); //R

                        //set the new image's pixel to the grayscale version
                        nRow[x * pixelSize] = grayScale; //B
                        nRow[x * pixelSize + 1] = grayScale; //G
                        nRow[x * pixelSize + 2] = grayScale; //R
                    }
                }

                //unlock the bitmaps
                newBitmap.UnlockBits(newData);
                original.UnlockBits(originalData);

                return newBitmap;
            }
        }

        /// <summary>
        /// Fastest grayscale conversion method from
        /// http://www.switchonthecode.com/tutorials/csharp-tutorial-convert-a-color-image-to-grayscale
        /// </summary>
        /// <param name="original">original bitmap to change</param>
        /// <returns>grayscaled version</returns>
        public static Bitmap MakeGrayscaleFastest(Bitmap original)
        {
            //create a blank bitmap the same size as original
            var newBitmap = new Bitmap(original.Width, original.Height);

            //get a graphics object from the new image
            Graphics g = Graphics.FromImage(newBitmap);

            //create the grayscale ColorMatrix
            var colorMatrix = new ColorMatrix(
                new float[][]
                {
                    new float[] {.3f, .3f, .3f, 0, 0},
                    new float[] {.59f, .59f, .59f, 0, 0},
                    new float[] {.11f, .11f, .11f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });

            //create some image attributes
            var attributes = new ImageAttributes();

            //set the color matrix attribute
            attributes.SetColorMatrix(colorMatrix);

            //draw the original image on the new image
            //using the grayscale color matrix
            g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                        0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);

            //dispose the Graphics object
            g.Dispose();
            return newBitmap;
        }

        /// <summary>
        /// Create a grayscaled version of the passed image
        /// </summary>
        /// <param name="original">original bitmap to change</param>
        /// <returns>grayscaled version</returns>
        public static Bitmap MakeGrayscale(Bitmap original)
        {
            return MakeGrayscaleFastest(original);
        }
        #endregion

        #region Resize
        /// <summary>
        /// Resize an image using high quality scaling (with smoothing and high quality bilinear interpolation)
        /// </summary>
        /// <param name="originalImage">original image</param>
        /// <param name="width">new width</param>
        /// <param name="height">new height</param>
        /// <returns></returns>
        public static Bitmap Resize(Image originalImage, int width, int height)
        {
            var newImage = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            using (Graphics canvas = Graphics.FromImage(newImage))
            {
                canvas.CompositingQuality = CompositingQuality.HighQuality;
                canvas.InterpolationMode = InterpolationMode.HighQualityBilinear;
                canvas.SmoothingMode = SmoothingMode.HighQuality;
                canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
                canvas.DrawImage(originalImage, 0, 0, width, height);
            }
            return newImage;
        }

        /// <summary>
        /// Resize an image using low quality scaling (no smoothing)
        /// </summary>
        /// <param name="originalImage">image</param>
        /// <param name="maxWidth">max width</param>
        /// <param name="maxHeight">max height</param>
        /// <param name="preserveRatio">bool whether to preserve ratio or force both width and height</param>
        /// <returns></returns>
        public static Image Resize(Image originalImage, int maxWidth, int maxHeight, bool preserveRatio)
        {
            double ratioX = (double)maxWidth / originalImage.Width;
            double ratioY = (double)maxHeight / originalImage.Height;
            double ratio = Math.Min(ratioX, ratioY);

            Bitmap newImage = null;
            int newWidth = 0;
            int newHeight = 0;
            if (preserveRatio)
            {
                newWidth = (int)(originalImage.Width * ratio);
                newHeight = (int)(originalImage.Height * ratio);
            }
            else
            {
                newWidth = maxWidth;
                newHeight = maxHeight;
            }
            newImage = new Bitmap(newWidth, newHeight);

            using (Graphics canvas = Graphics.FromImage(newImage))
            {
                canvas.InterpolationMode = InterpolationMode.NearestNeighbor;
                canvas.PixelOffsetMode = PixelOffsetMode.Half;
                canvas.DrawImage(originalImage, 0, 0, newWidth, newHeight);
            }
            return newImage;
        }
        #endregion

        #region Convert Back and Forth to Byte Arrays
        /// <summary>
        /// Get the image's color pixels as a byte array
        /// </summary>
        /// <param name="imageIn">image</param>
        /// <returns>byte array</returns>
        public static byte[] ImageToByteArray(Image imageIn)
        {
            var bmp = new Bitmap(imageIn);

            // Lock the bitmap's bits.
            var rect = new Rectangle(Point.Empty, bmp.Size);
            BitmapData bmpData =
                bmp.LockBits(rect, ImageLockMode.ReadOnly,
                             bmp.PixelFormat);

            // Get the address of the first line.
            IntPtr ptr = bmpData.Scan0;

            // Declare an array to hold the bytes of the bitmap.
            int colorDepthBitsPerPixel = Image.GetPixelFormatSize(bmp.PixelFormat);

            // This code is specific to a bitmap with 24 bits per pixels.
            // int bytes = bmp.Width * bmp.Height * 3 ;
            // therefore take into account the number of bits per pixel instead
            int bytes = bmp.Width * bmp.Height * (colorDepthBitsPerPixel / 8);
            var rgbValues = new byte[bytes];

            // Copy the RGB values into the array.
            System.Runtime.InteropServices.Marshal.Copy(ptr, rgbValues, 0, bytes);

            return rgbValues;
        }

        /// <summary>
        /// Take a byte array of color pixels and create a bitmap Image
        /// </summary>
        /// <param name="rgbValues">byte array of rgbValues</param>
        /// <param name="width">width</param>
        /// <param name="height">height</param>
        /// <param name="pixelFormat">pixelformat</param>
        /// <returns>image</returns>
        public static Image ByteArrayToImage(byte[] rgbValues, int width, int height, PixelFormat pixelFormat)
        {
            //Here create the Bitmap to the know height, width and format
            var bmp = new Bitmap(width, height, pixelFormat);

            //Create a BitmapData and Lock all pixels to be written
            BitmapData bmpData = bmp.LockBits(
                new Rectangle(Point.Empty, bmp.Size),
                ImageLockMode.WriteOnly, bmp.PixelFormat);

            //Copy the data from the byte array into BitmapData.Scan0
            System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, bmpData.Scan0, rgbValues.Length);

            //Unlock the pixels
            bmp.UnlockBits(bmpData);

            return bmp;
        }

        /// <summary>
        /// Convert a base64 encoded string to bytes
        /// E.g. data:image/png;base64,iVBORw0KGgoAA..
        /// </summary>
        /// <param name="base64String">base64 encoded string</param>
        /// <returns>byte array</returns>
        public static byte[] Base64ToByteArray(string base64String)
        {
            // remove the data:image/png;base64, part
            base64String = Regex.Replace(base64String, "^data:image/[a-zA-Z]+;base64,", string.Empty);
            return Convert.FromBase64String(base64String);
        }

        /// <summary>
        // Convert a base64 encoded string to Image
        /// E.g. data:image/png;base64,iVBORw0KGgoAA        
        /// </summary>
        /// <param name="base64String">base64 encoded image</param>
        /// <returns>an image</returns>
        public static System.Drawing.Image Base64ToImage(string base64String)
        {
            // remove the data:image/png;base64, part
            base64String = Regex.Replace(base64String, "^data:image/[a-zA-Z]+;base64,", string.Empty);
            byte[] imageBytes = Convert.FromBase64String(base64String);
            MemoryStream ms = new MemoryStream(imageBytes, 0, imageBytes.Length);

            ms.Write(imageBytes, 0, imageBytes.Length);
            return System.Drawing.Image.FromStream(ms, true);
        }

        /// <summary>
        /// Take a byte array of 8 bit greyscale pixels and create a bitmap Image
        /// </summary>
        /// <param name="grayscaleByteArray">byte array of 8bit grayscale values</param>
        /// <param name="width">width</param>
        /// <param name="height">height</param>
        /// <returns>image</returns>
        public static Image ByteArray8BitGrayscaleToImage(byte[] grayscaleByteArray, int width, int height)
        {
            //Here create the Bitmap to the know height, width and format
            var newBitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);

            // Declare an array to hold the bytes of the bitmap.
            uint colorDepthBitsPerPixel = (uint)Image.GetPixelFormatSize(newBitmap.PixelFormat);
            uint multiplicationFactor = colorDepthBitsPerPixel / 8;

            for (int y = 0; y < newBitmap.Height; y++)
            {
                for (int x = 0; x < newBitmap.Width; x++)
                {
                    byte gray = grayscaleByteArray[x + (y * newBitmap.Height)];

                    int grayScale = (int)(gray * multiplicationFactor);

                    //create the color object
                    Color newColor = Color.FromArgb(grayScale, grayScale, grayScale);

                    //set the new image's pixel to the grayscale version
                    newBitmap.SetPixel(x, y, newColor);
                }
            }
            return newBitmap;
        }

        /// <summary>
        /// Take a byte array of 8 bit greyscale pixels and create a bitmap Image
        /// </summary>
        /// <param name="grayscaleByteArray">byte array of 8bit grayscale values</param>
        /// <param name="width">width</param>
        /// <param name="height">height</param>
        /// <returns>image</returns>
        public static Image ByteArrayGrayscaleToImage(byte[] grayscaleByteArray, int width, int height)
        {
            //Here create the Bitmap to the know height, width and format
            var newBitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);

            // Declare an array to hold the bytes of the bitmap.
            for (int x = 0; x < newBitmap.Width; x++)
            {
                for (int y = 0; y < newBitmap.Height; y++)
                {
                    byte gray = grayscaleByteArray[x + (y * newBitmap.Width)];

                    //create the color object
                    Color newColor = Color.FromArgb(gray, gray, gray);

                    //set the new image's pixel to the grayscale version
                    newBitmap.SetPixel(x, y, newColor);
                }
            }
            return newBitmap;
        }

        /// <summary>
        /// Reduce colors to 8-bit grayscale and calculate average color value
        /// </summary>
        /// <param name="bmp">an image</param>
        /// <param name="averageValue">calculated average color value</param>
        /// <returns>byte array</returns>
        public static byte[] ImageToByteArray8BitGrayscale(Bitmap bmp, out uint averageValue)
        {

            // Declare an array to hold the bytes of the bitmap.
            uint colorDepthBitsPerPixel = (uint)Image.GetPixelFormatSize(bmp.PixelFormat);
            uint divisionFactor = colorDepthBitsPerPixel / 8;

            averageValue = 0;
            var grayscaleByteArray = new byte[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    //get the pixel from the original image
                    Color pixelColor = bmp.GetPixel(x, y);

                    //create the grayscale version of the pixel
                    //get average (sum all three and divide by three)
                    //uint gray = (uint)(pixelColor.R + pixelColor.G + pixelColor.B) / 3;

                    // better method that takes how the eyes determine gray
                    uint gray = (uint)((pixelColor.R * .3) + (pixelColor.G * .59)
                                       + (pixelColor.B * .11));

                    // reduce from colorDepthBitsPerPixel to 8 bit
                    // i.e. PixelFormat.Format32bppRgb (32 bit) to 8 bit = /4
                    gray /= divisionFactor;

                    // add to byte array
                    grayscaleByteArray[x + (y * bmp.Height)] = (byte)gray;
                    averageValue += gray;
                }
            }
            averageValue /= (uint)(bmp.Width * bmp.Height);
            return grayscaleByteArray;
        }
        #endregion

        /// <summary>
        /// Convert a double array with values between [0 - 1] to an image
        /// </summary>
        /// <param name="rawImage">double 2d array</param>
        /// <returns>an Image</returns>
        public unsafe static Image DoubleArrayToImage(double[][] rawImage)
        {
            //int width = rawImage.GetLength(1);
            //int height = rawImage.GetLength(0);
            int width = rawImage[0].Length;
            int height = rawImage.Length;

            var Image = new Bitmap(width, height);
            BitmapData bitmapData = Image.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb
            );
            ColorARGB* startingPosition = (ColorARGB*)bitmapData.Scan0;


            for (int i = 0; i < height; i++)
                for (int j = 0; j < width; j++)
                {
                    double color = rawImage[i][j];
                    byte rgb = (byte)(color * 255);

                    ColorARGB* position = startingPosition + j + i * width;
                    position->A = 255;
                    position->R = rgb;
                    position->G = rgb;
                    position->B = rgb;
                }

            Image.UnlockBits(bitmapData);
            return Image;
        }

        #region Read and Write BMP files to arrays
        public static double[][] ReadBMPGrayscale(string filePath)
        {
            double[][] image;
            var bmpFile = new BinaryFile(filePath);

            // "BM" format tag check
            if (!"BM".Equals(bmpFile.ReadString(2)))
            {
                Console.Error.WriteLine("This file is not in BMP format");
                return null;
            }

            bmpFile.Seek(8, SeekOrigin.Current); // skipping useless tags

            int offset = (int)bmpFile.ReadUInt32() - 54; // header offset

            bmpFile.Seek(4, SeekOrigin.Current); // skipping useless tags

            int x = (int)bmpFile.ReadUInt32();
            int y = (int)bmpFile.ReadUInt32();

            bmpFile.Seek(2, SeekOrigin.Current); // skipping useless tags

            int bitDepth = bmpFile.ReadUInt16();
            int numBytes = bitDepth / 8;

            bmpFile.Seek(24 + offset, SeekOrigin.Current); // skipping useless tags

            // image allocation
            image = new double[y][];

            // initialise jagged array
            for (int iy = 0; iy < y; iy++)
            {
                image[iy] = new double[x];
            }

            // calculate zero bytes (bytes to skip at the end of each bitmap line)
            int zerobytes = (byte)(4 - ((x * numBytes) & numBytes));
            if (zerobytes == 4)
            {
                zerobytes = 0;
            }

            // backwards reading
            for (int iy = y - 1; iy != -1; iy--)
            {
                for (int ix = 0; ix < x; ix++)
                {
                    for (int ic = numBytes - 1; ic != -1; ic--)
                    {
                        int val = bmpFile.ReadByte();
                        if (ic == 0 && numBytes == 4)
                        {
                            // if reading 32 bit, ignore the alpha bit
                        }
                        else
                        {
                            // Conversion to grey by averaging the three channels
                            image[iy][ix] += (double)val * (1.0 / (255.0 * 3.0));
                        }
                    }
                }

                bmpFile.Seek(zerobytes, SeekOrigin.Current); // skipping padding bytes
            }

            bmpFile.Close();
            return image;
        }

        public static void WriteBMPGrayscale(string filePath, double[][] image, int bitDepth = 24)
        {
            var bmpFile = new BinaryFile(filePath, BinaryFile.ByteOrder.LittleEndian, true);

            int y = image.Length;
            int x = image[0].Length;

            const byte zerobyte = 255; // what byte should we pad with
            int numBytes = bitDepth / 8;

            // calculate zero bytes (bytes to skip at the end of each bitmap line)
            int zerobytes = (byte)(4 - ((x * numBytes) & numBytes));
            if (zerobytes == 4)
            {
                zerobytes = 0;
            }

            #region Tags
            int filesize = 54 + ((x * numBytes) + zerobytes) * y;
            int imagesize = ((x * numBytes) + zerobytes) * y;

            bmpFile.Write("BM");

            bmpFile.Write((UInt32)filesize);    // filesize
            bmpFile.Write((UInt32)0);           // reserved
            bmpFile.Write((UInt32)54);          // off bits
            bmpFile.Write((UInt32)40);          // bitmap info header size
            bmpFile.Write((UInt32)x);           // width
            bmpFile.Write((UInt32)y);           // height
            bmpFile.Write((UInt16)1);           // planes
            bmpFile.Write((UInt32)bitDepth);    // bit depth
            bmpFile.Write((UInt16)0);           // compression

            bmpFile.Write((UInt32)imagesize);
            //bmpfile.Write((UInt32) 0);

            // There are at least three kind value of PelsPerMeter used for 96 DPI bitmap:
            //   0    - the bitmap just simply doesn't set this value
            //   2834 - 72 DPI
            //   3780 - 96 DPI
            const UInt32 pelsPerMeter = 0;//3780;
            bmpFile.Write((UInt32)pelsPerMeter);    // XPelsPerMeter
            bmpFile.Write((UInt32)pelsPerMeter);    // YPelsPerMeter
            bmpFile.Write((UInt32)0);   // clr used
            bmpFile.Write((UInt32)0);   // clr important
            #endregion Tags

            // backwards writing
            for (int iy = y - 1; iy != -1; iy--)
            {
                for (int ix = 0; ix < x; ix++)
                {

                    // define color (grayscale)
                    double vald = image[iy][ix] * 255.0;

                    if (vald > 255.0)
                    {
                        vald = 255.0;
                    }

                    if (vald < 0.0)
                    {
                        vald = 0.0;
                    }

                    byte val = Convert.ToByte(vald);

                    for (int ic = numBytes - 1; ic != -1; ic--)
                    {
                        if (ic == 0 && numBytes == 4)
                        {
                            bmpFile.Write((byte)0); // alpha bit
                        }
                        else
                        {
                            bmpFile.Write(val);
                        }
                    }
                }

                // write padding bytes
                for (int i = 0; i < zerobytes; i++)
                {
                    bmpFile.Write(zerobyte);
                }
            }

            //bmpfile.Write((UInt16)0);
            bmpFile.Close();

#if DEBUG
            Console.Write("Image size : {0:D}x{1:D}\n", x, y);
#endif
        }
        #endregion
    }

    /// <summary>
    /// Color struct used by the DoubleArrayToImage method
    /// </summary>
    public struct ColorARGB
    {
        public byte B;
        public byte G;
        public byte R;
        public byte A;

        public ColorARGB(Color color)
        {
            A = color.A;
            R = color.R;
            G = color.G;
            B = color.B;
        }

        public ColorARGB(byte a, byte r, byte g, byte b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public Color ToColor()
        {
            return Color.FromArgb(A, R, G, B);
        }
    }
}
