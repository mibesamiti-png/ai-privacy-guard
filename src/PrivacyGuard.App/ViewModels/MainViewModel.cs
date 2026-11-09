using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using PrivacyGuard.Core;

namespace PrivacyGuard.App.ViewModels;

public sealed class ReviewItem : INotifyPropertyChanged
{
    private DetectionRecord _data;
    private BitmapSource? _thumbnail;
    public ReviewItem(DetectionRecord data) => _data = data;
    public DetectionRecord Data => _data;
    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; Notify(nameof(Thumbnail)); }
    }
    public string Label => $"{(_data.Origin == "mock" ? "[模擬] " : _data.Origin.StartsWith("onnx:", StringComparison.Ordinal) ? "[AI実験] " : "")}{_data.Category}  |  {DecisionText}";
    public string ConfidenceText => _data.Confidence is { } c ? $"スコア {c:0.000}（未校正）" : "手動指定";
    public string RegionText => $"位置 X:{_data.Bounds.X:P0} Y:{_data.Bounds.Y:P0}  幅:{_data.Bounds.Width:P0} 高:{_data.Bounds.Height:P0}";
    public string DecisionText => _data.Decision switch
    {
        ReviewDecision.Approved => "隠す",
        ReviewDecision.Rejected => "無視",
        ReviewDecision.Deferred => "保留",
        _ => "未確認"
    };
    public void SetDecision(ReviewDecision decision)
    {
        _data = _data with { Decision = decision };
        Notify(nameof(Label));
        Notify(nameof(DecisionText));
        Notify(nameof(Data));
    }
    public void SetBounds(NormalizedRect bounds)
    {
        _data = _data with { Bounds = bounds.Clamp() };
        Notify(nameof(RegionText));
        Notify(nameof(Data));
    }
    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _status = "画像を読み込んでください。専用AIモデルは同梱していません。";
    private string _sourcePath = "未選択";
    private string _modelStatus = "AIモデル: 未選択（自動検出オフ）";
    private BitmapSource? _mainImage;
    private BitmapSource? _processedImage;
    private BitmapSource? _cropImage;
    private ReviewItem? _selected;
    private int _zoom = 1;
    public ObservableCollection<ReviewItem> Candidates { get; } = new();
    public string Status { get => _status; set => Set(ref _status, value); }
    public string SourcePath { get => _sourcePath; set => Set(ref _sourcePath, value); }
    public string ModelStatus { get => _modelStatus; set => Set(ref _modelStatus, value); }
    public BitmapSource? MainImage { get => _mainImage; set => Set(ref _mainImage, value); }
    public BitmapSource? ProcessedImage { get => _processedImage; set => Set(ref _processedImage, value); }
    public BitmapSource? CropImage { get => _cropImage; set => Set(ref _cropImage, value); }
    public ReviewItem? Selected { get => _selected; set => Set(ref _selected, value); }
    public int Zoom { get => _zoom; set => Set(ref _zoom, value); }
    public string ZoomText => $"{Zoom}x";
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(Zoom)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZoomText)));
    }
}
