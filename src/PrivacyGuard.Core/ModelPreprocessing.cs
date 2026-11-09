namespace PrivacyGuard.Core;

/// <summary>
/// Reversible mapping from the model input grid back into the source image.
/// Values use actual integer-resized dimensions, not the approximate ideal scale.
/// </summary>
public readonly record struct ModelInputTransform(
    int SourceWidth, int SourceHeight, int InputWidth, int InputHeight,
    double ScaleX, double ScaleY, int OffsetX, int OffsetY)
{
    public NormalizedRect MapBox(double x1, double y1, double x2, double y2, string coordinates)
    {
        if (coordinates == "xyxy_normalized")
        {
            x1 *= InputWidth; x2 *= InputWidth;
            y1 *= InputHeight; y2 *= InputHeight;
        }
        else if (coordinates != "xyxy_input_pixels")
            throw new InvalidDataException("Unknown model coordinate convention.");
        if (!double.IsFinite(x1) || !double.IsFinite(y1) ||
            !double.IsFinite(x2) || !double.IsFinite(y2) ||
            SourceWidth <= 0 || SourceHeight <= 0 || ScaleX <= 0 || ScaleY <= 0)
            throw new InvalidDataException("Invalid model output geometry.");
        return new NormalizedRect(
            ((x1 - OffsetX) / ScaleX) / SourceWidth,
            ((y1 - OffsetY) / ScaleY) / SourceHeight,
            ((x2 - x1) / ScaleX) / SourceWidth,
            ((y2 - y1) / ScaleY) / SourceHeight).Clamp();
    }
}

public sealed record ModelInput(float[] Values, ModelInputTransform Transform);

public static class ModelPreprocessing
{
    public const string StretchNearest = "stretch_nearest";
    public const string LetterboxBilinear = "letterbox_bilinear";
    // NudeNet pads right and bottom with zero before OpenCV's linear resize.
    public const string NudeNetSquare = "nudenet_square";

    /// <summary>
    /// Produces RGB float32 NCHW and source-image geometry. Letterbox uses 114/255
    /// gray padding and bilinear scaling (half-pixel centers). Alpha is ignored.
    /// </summary>
    public static ModelInput Prepare(PixelBuffer image, int width, int height, string mode)
    {
        if (width < 1 || height < 1 || (long)width * height > 2048L * 2048)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (mode is not (StretchNearest or LetterboxBilinear or NudeNetSquare))
            throw new InvalidDataException("Unknown preprocessing mode.");

        if (mode == NudeNetSquare && width != height)
            throw new InvalidDataException("NudeNet requires a square input tensor.");
        int paddedSide = Math.Max(image.Width, image.Height);
        int resizedWidth = width, resizedHeight = height, offsetX = 0, offsetY = 0;
        if (mode == LetterboxBilinear)
        {
            double factor = Math.Min(width / (double)image.Width, height / (double)image.Height);
            resizedWidth = Math.Clamp((int)Math.Round(image.Width * factor, MidpointRounding.AwayFromZero), 1, width);
            resizedHeight = Math.Clamp((int)Math.Round(image.Height * factor, MidpointRounding.AwayFromZero), 1, height);
            offsetX = (width - resizedWidth) / 2;
            offsetY = (height - resizedHeight) / 2;
        }
        var mapping = mode == NudeNetSquare
            ? new ModelInputTransform(image.Width, image.Height, width, height,
                width / (double)paddedSide, height / (double)paddedSide, 0, 0)
            : new ModelInputTransform(image.Width, image.Height, width, height,
                resizedWidth / (double)image.Width, resizedHeight / (double)image.Height, offsetX, offsetY);
        int plane = checked(width * height);
        float[] rgb = new float[checked(plane * 3)];
        if (mode == LetterboxBilinear)
            Array.Fill(rgb, 114f / 255f);

        for (int y = 0; y < resizedHeight; y++)
        for (int x = 0; x < resizedWidth; x++)
        {
            int dest = (y + offsetY) * width + (x + offsetX);
            if (mode == NudeNetSquare)
            {
                // Linear resize of a zero-padded square, matching the NudeNet
                // OpenCV wrapper. RGB NCHW and [0,1] values.
                double fx = (x + 0.5) * paddedSide / (double)width - 0.5;
                double fy = (y + 0.5) * paddedSide / (double)height - 0.5;
                int squareX0 = (int)Math.Floor(fx), squareY0 = (int)Math.Floor(fy);
                double squareWx = fx - squareX0, squareWy = fy - squareY0;
                for (int c = 0; c < 3; c++)
                {
                    double v00 = Sample(image, squareX0, y0, 2 - c);
                    double v10 = Sample(image, squareX0 + 1, y0, 2 - c);
                    double v01 = Sample(image, squareX0, squareY0 + 1, 2 - c);
                    double v11 = Sample(image, squareX0 + 1, squareY0 + 1, 2 - c);
                    rgb[c * plane + dest] = (float)(
                        (((1 - squareWx) * v00 + wx * v10) * (1 - squareWy) +
                        ((1 - squareWx) * v01 + wx * v11) * squareWy) / 255.0);
                }
                continue;
            }
            if (mode == StretchNearest)
            {
                int sy = Math.Min(image.Height - 1, (int)((long)y * image.Height / resizedHeight));
                int sx = Math.Min(image.Width - 1, (int)((long)x * image.Width / resizedWidth));
                int index = (sy * image.Width + sx) * 4;
                rgb[dest] = image.Bgra[index + 2] / 255f;
                rgb[plane + dest] = image.Bgra[index + 1] / 255f;
                rgb[2 * plane + dest] = image.Bgra[index] / 255f;
                continue;
            }
            double xf = (x + 0.5) * image.Width / resizedWidth - 0.5;
            double yf = (y + 0.5) * image.Height / resizedHeight - 0.5;
            int x0 = Math.Clamp((int)Math.Floor(xf), 0, image.Width - 1);
            int x1 = Math.Clamp((int)Math.Floor(xf) + 1, 0, image.Width - 1);
            int y0 = Math.Clamp((int)Math.Floor(yf), 0, image.Height - 1);
            int y1 = Math.Clamp((int)Math.Floor(yf) + 1, 0, image.Height - 1);
            double wx = Math.Clamp(xf - Math.Floor(xf), 0, 1);
            double wy = Math.Clamp(yf - Math.Floor(yf), 0, 1);
            for (int channel = 0; channel < 3; channel++)
            {
                int bgraChannel = 2 - channel;
                double v00 = image.Bgra[(y0 * image.Width + x0) * 4 + bgraChannel];
                double v10 = image.Bgra[(y0 * image.Width + x1) * 4 + bgraChannel];
                double v01 = image.Bgra[(y1 * image.Width + x0) * 4 + bgraChannel];
                double v11 = image.Bgra[(y1 * image.Width + x1) * 4 + bgraChannel];
                rgb[channel * plane + dest] = (float)(
                    ((1 - wx) * v00 + wx * v10) * (1 - wy) +
                    ((1 - wx) * v01 + wx * v11) * wy) / 255f;
            }
        }
        return new ModelInput(rgb, mapping);
    }

    private static byte Sample(PixelBuffer image, int x, int y, int channel)
    {
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) return 0;
        return image.Bgra[(y * image.Width + x) * 4 + channel];
    }
}
