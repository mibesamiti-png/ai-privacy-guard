using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PrivacyGuard.Core;

namespace PrivacyGuard.AI;

/// <summary>
/// Experimental CPU-only adapter for an external, trusted, 6-column ONNX detector.
/// No model is bundled and no general-purpose object detector is assumed to
/// recognize exposure. Models with other output layouts are rejected.
/// </summary>
public sealed class OnnxExposureDetector : IExposureDetector
{
    private readonly InferenceSession _session;
    private readonly ModelManifest _manifest;
    private bool _disposed;
    public string ModelId => _manifest.ModelId;

    public OnnxExposureDetector(ModelManifest manifest, string modelPath)
    {
        _manifest = manifest;
        manifest.Validate();
        _session = new InferenceSession(modelPath);
        try
        {
            if (!_session.InputMetadata.TryGetValue(manifest.InputName, out var input) ||
                !input.IsTensor || input.ElementType != typeof(float))
                throw new InvalidDataException("Model input name/type is not float32 tensor.");
            int[] shape = input.Dimensions;
            if (shape.Length != 4 || (shape[0] != -1 && shape[0] != 1) ||
                shape[1] != 3 || (shape[2] != -1 && shape[2] != manifest.InputHeight) ||
                (shape[3] != -1 && shape[3] != manifest.InputWidth))
                throw new InvalidDataException("Only RGB NCHW [1,3,H,W] models are supported.");
            if (!_session.OutputMetadata.TryGetValue(manifest.OutputName, out var output) ||
                !output.IsTensor || output.ElementType != typeof(float))
                throw new InvalidDataException("Model output name/type is not float32 tensor.");
            int[] outputShape = output.Dimensions;
            if (!((outputShape.Length == 2 && (outputShape[1] == 6 || outputShape[1] == -1)) ||
                  (outputShape.Length == 3 && (outputShape[0] is 1 or -1) &&
                   (outputShape[2] is 6 or -1))))
                throw new InvalidDataException("Unsupported output shape; expected [N,6] or [1,N,6].");
        }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

    public Task<IReadOnlyList<DetectionCandidate>> DetectAsync(PixelBuffer image, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OnnxExposureDetector));
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preprocessed = ModelPreprocessing.Prepare(
                image, _manifest.InputWidth, _manifest.InputHeight, _manifest.Preprocessing);
            var input = new DenseTensor<float>(preprocessed.Values,
                new[] { 1, 3, _manifest.InputHeight, _manifest.InputWidth });
            var values = new[] { NamedOnnxValue.CreateFromTensor(_manifest.InputName, input) };
            // Ort Run() is synchronous. Cancellation prevents result use but may not interrupt native inference.
            using var outputs = _session.Run(values);
            cancellationToken.ThrowIfCancellationRequested();
            var output = outputs.FirstOrDefault(x => x.Name == _manifest.OutputName)
                ?? throw new InvalidDataException("Named ONNX output was not returned.");
            var tensor = output.AsTensor<float>();
            return ModelTensorProcessor.DecodeSixColumns(
                tensor.ToArray(), tensor.Dimensions.ToArray(), _manifest, transform: preprocessed.Transform);
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}
