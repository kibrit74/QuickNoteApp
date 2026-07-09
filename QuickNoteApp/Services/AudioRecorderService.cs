using System.IO;
using NAudio.Wave;

namespace QuickNoteApp.Services;

public sealed class AudioRecorderService : IDisposable
{
    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentPath;

    public bool IsRecording { get; private set; }

    public static string CreateRecordingPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "QuickNoteApp", "Recordings");
        Directory.CreateDirectory(root);

        return Path.Combine(root, $"dikte-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.wav");
    }

    public string Start()
    {
        lock (_gate)
        {
            if (IsRecording)
                throw new InvalidOperationException("Kayıt zaten devam ediyor.");

            _currentPath = CreateRecordingPath();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };
            _writer = new WaveFileWriter(_currentPath, _waveIn.WaveFormat);

            _waveIn.DataAvailable += (_, args) =>
            {
                lock (_gate)
                {
                    _writer?.Write(args.Buffer, 0, args.BytesRecorded);
                    _writer?.Flush();
                }
            };

            _waveIn.StartRecording();
            IsRecording = true;
            return _currentPath;
        }
    }

    public string? Stop()
    {
        lock (_gate)
        {
            if (!IsRecording)
                return _currentPath;

            try
            {
                _waveIn?.StopRecording();
            }
            finally
            {
                IsRecording = false;
                DisposeRecordingObjects();
            }

            return _currentPath;
        }
    }

    public void DiscardCurrentRecording()
    {
        var path = _currentPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (IsRecording)
            {
                try { _waveIn?.StopRecording(); } catch { }
                IsRecording = false;
            }

            DisposeRecordingObjects();
        }
    }

    private void DisposeRecordingObjects()
    {
        _waveIn?.Dispose();
        _waveIn = null;

        _writer?.Dispose();
        _writer = null;
    }
}
