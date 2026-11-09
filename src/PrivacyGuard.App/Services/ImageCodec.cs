using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrivacyGuard.Core;

namespace PrivacyGuard.App.Services;

public static class ImageCodec
{
    public static Task<PixelBuffer> ReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                131072, FileOptions.SequentialScan);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
                (long)frame.PixelWidth * frame.PixelHeight > 80_000_000)
                throw new NotSupportedException("画像が大きすぎます（最大80メガピクセル）。");
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = checked(frame.PixelWidth * 4);
            var bytes = new byte[checked(stride * frame.PixelHeight)];
            converted.CopyPixels(bytes, stride, 0);
            cancellationToken.ThrowIfCancellationRequested();
            return new PixelBuffer(frame.PixelWidth, frame.PixelHeight, bytes, copy: false);
        }, cancellationToken);

    public static PixelBuffer ReadEncodedBytes(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        using var stream = new MemoryStream(encoded, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
            (long)frame.PixelWidth * frame.PixelHeight > 80_000_000)
            throw new NotSupportedException("Preview frame exceeds 80 MP limit.");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int stride = checked(frame.PixelWidth * 4);
        var bytes = new byte[checked(stride * frame.PixelHeight)];
        converted.CopyPixels(bytes, stride, 0);
        return new PixelBuffer(frame.PixelWidth, frame.PixelHeight, bytes, copy: false);
    }

    public static BitmapSource ToBitmap(PixelBuffer buffer)
    {
        var bitmap = BitmapSource.Create(buffer.Width, buffer.Height, 96, 96, PixelFormats.Bgra32,
            null, buffer.Bgra, checked(buffer.Width * 4));
        bitmap.Freeze();
        return bitmap;
    }

    public static void WritePng(string path, PixelBuffer buffer, bool overwrite = true,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(ToBitmap(buffer)));
            using (var stream = File.Create(tempPath)) encoder.Save(stream);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, fullPath, overwrite: overwrite);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
