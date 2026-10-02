using System;
using System.Threading;
using LSL;

namespace LSLStreamViewer
{
    /// <summary>
    /// Background worker that reads samples from an LSL stream inlet.
    /// Supports direct chart feeding for zero-lag high-frequency data (e.g. 500-1000 Hz)
    /// and synchronized events for text logging and UI stats.
    /// </summary>
    public class StreamReaderWorker : IDisposable
    {
        private StreamInlet _inlet;
        private StreamInfo _resolvedInfo;
        private Thread _thread;
        private volatile bool _running;
        private readonly SynchronizationContext _syncContext;

        public event Action<double[], double> NumericSampleReceived;
        public event Action<string[], double> StringSampleReceived;
        public event Action<string> ErrorOccurred;
        public event Action Disconnected;

        public Action<double[]> DirectChartSink { get; set; }

        public int ChannelCount { get; private set; }
        public double NominalSampleRate { get; private set; }
        public string ChannelFormat { get; private set; }
        public bool IsRunning => _running;

        private long _samplesReceived;
        public long SamplesReceived => Interlocked.Read(ref _samplesReceived);

        public StreamReaderWorker()
        {
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        public void Start(StreamInfo info)
        {
            Stop();

            _resolvedInfo = info;
            ChannelCount = info.channel_count();
            NominalSampleRate = info.nominal_srate();
            ChannelFormat = info.channel_format().ToString();
            Interlocked.Exchange(ref _samplesReceived, 0);

            _running = true;
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "LSL-Reader" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(1500);
                _thread = null;
            }

            try { _inlet?.Dispose(); } catch { }
            _inlet = null;
        }

        private void ReadLoop()
        {
            try
            {
                _inlet = new StreamInlet(_resolvedInfo);
                _inlet.open_stream(3.0);

                var fmt = _resolvedInfo.channel_format();
                bool isString = (fmt == channel_format_t.cf_string);

                long lastUiLogSample = 0;
                int logStride = NominalSampleRate > 20 ? (int)(NominalSampleRate / 20) : 1;

                if (isString)
                {
                    string[] sample = new string[ChannelCount];
                    while (_running)
                    {
                        double ts = _inlet.pull_sample(sample, 0.2);
                        if (ts > 0)
                        {
                            Interlocked.Increment(ref _samplesReceived);
                            var copy = (string[])sample.Clone();
                            _syncContext.Post(_ => StringSampleReceived?.Invoke(copy, ts), null);
                        }
                    }
                }
                else
                {
                    double[] sample = new double[ChannelCount];
                    while (_running)
                    {
                        double ts = _inlet.pull_sample(sample, 0.2);
                        if (ts > 0)
                        {
                            long total = Interlocked.Increment(ref _samplesReceived);

                            // Direct push to chart (sub-microsecond thread-safe lock, zero UI thread overhead)
                            DirectChartSink?.Invoke(sample);

                            // Rate limit text log dispatch to UI thread (~20 per second max)
                            if (NumericSampleReceived != null && (total - lastUiLogSample >= logStride))
                            {
                                lastUiLogSample = total;
                                var copy = (double[])sample.Clone();
                                _syncContext.Post(_ => NumericSampleReceived?.Invoke(copy, ts), null);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_running)
                    _syncContext.Post(_ => ErrorOccurred?.Invoke(ex.Message), null);
            }
            finally
            {
                try { _inlet?.Dispose(); } catch { }
                _inlet = null;
                _syncContext.Post(_ => Disconnected?.Invoke(), null);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
