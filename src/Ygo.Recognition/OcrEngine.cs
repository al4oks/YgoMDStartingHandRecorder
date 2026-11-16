using System.Drawing.Imaging;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace Ygo.Recognition;

public sealed class OcrEngine : IDisposable
{
    private readonly PaddleOcrAll _all = new(LocalFullModels.ChineseV5);
    private readonly object _gate = new();

    public OcrEngine()
    {
        // v3 returns region coords in detector-resized space; avoid resize so coords match the source Mat.
        _all.Detector.MaxSize = 4096;
        _all.AllowRotateDetection = false;
        _all.Enable180Classification = false;
    }

    public PaddleOcrResult Run(Bitmap bmp)
    {
        using var mat = ToMat(bmp);
        using var pre = Preprocess(mat);
        lock (_gate) return _all.Run(pre);
    }

    public PaddleOcrResult Run(Mat mat)
    {
        using var pre = Preprocess(mat);
        lock (_gate) return _all.Run(pre);
    }

    public RotatedRect[] Detect(Mat mat)
    {
        lock (_gate) return _all.Detector.Run(mat);
    }

    public Mat CropRotated(Mat src, RotatedRect rect) => PaddleOcrAll.GetRotateCropImage(src, rect);

    public PaddleOcrRecognizerResult[] RecognizeBatch(Mat[] crops)
    {
        lock (_gate) return _all.Recognizer.Run(crops, 8);
    }

    private static Mat Preprocess(Mat src)
    {
        switch (Environment.GetEnvironmentVariable("YGO_PRE"))
        {
            case "clahe":
            {
                using var lab = new Mat();
                Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
                var ch = Cv2.Split(lab);
                using var clahe = Cv2.CreateCLAHE(2.0, new OpenCvSharp.Size(8, 8));
                clahe.Apply(ch[0], ch[0]);
                using var merged = new Mat();
                Cv2.Merge(ch, merged);
                var outp = new Mat();
                Cv2.CvtColor(merged, outp, ColorConversionCodes.Lab2BGR);
                foreach (var c in ch) c.Dispose();
                return outp;
            }
            case "sharpen":
            {
                using var blur = new Mat();
                Cv2.GaussianBlur(src, blur, new OpenCvSharp.Size(0, 0), 3);
                var outp = new Mat();
                Cv2.AddWeighted(src, 1.5, blur, -0.5, 0, outp);
                return outp;
            }
            default:
                return src.Clone();
        }
    }

    public static Mat ToMat(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var bgra = Mat.FromPixelData(bmp.Height, bmp.Width, MatType.CV_8UC4, data.Scan0);
            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public void Dispose() => _all.Dispose();
}