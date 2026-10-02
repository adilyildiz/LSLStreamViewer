using System;
using System.Threading;
using LSL;

namespace LSLStreamViewer
{
    /// <summary>
    /// Generates a realistic synthetic 4-channel EEG-like stream (100 Hz)
    /// to test the LSLStreamViewer without needing external hardware or devices.
    /// </summary>
    public class TestStreamGenerator : IDisposable
    {
        private StreamOutlet _outlet;
        private StreamInfo _info;
        private Thread _thread;
        private volatile bool _running;

        public bool IsRunning => _running;
        public string StreamName { get; } = "Test-SineWave-EEG";

        public void Start()
        {
            if (_running) return;

            // 4 channels, 100 Hz, double64, unique source id
            _info = new StreamInfo(
                StreamName,
                "EEG",
                4,
                100.0,
                channel_format_t.cf_double64,
                "test-sim-outlet-001");

            // Add channel metadata
            var chs = _info.desc().append_child("channels");
            string[] names = { "Alpha (10Hz)", "Beta (20Hz)", "Theta (6Hz)", "Delta (2Hz)" };
            string[] units = { "uV", "uV", "uV", "uV" };

            for (int i = 0; i < names.Length; i++)
            {
                var ch = chs.append_child("channel");
                ch.append_child_value("label", names[i]);
                ch.append_child_value("unit", units[i]);
                ch.append_child_value("type", "EEG");
            }

            var acq = _info.desc().append_child("acquisition");
            acq.append_child_value("manufacturer", "LSLStreamViewer Simulator");
            acq.append_child_value("model", "Internal Synthesizer v1.0");

            _outlet = new StreamOutlet(_info);

            _running = true;
            _thread = new Thread(RunLoop) { IsBackground = true, Name = "LSL-TestGenerator" };
            _thread.Start();
        }

        private void RunLoop()
        {
            double[] sample = new double[4];
            double t = 0.0;
            var rand = new Random();

            while (_running)
            {
                // Channel 1: Alpha rhythm (~10 Hz) with amplitude modulation
                sample[0] = Math.Sin(2 * Math.PI * 10.0 * t) * (15.0 + 5.0 * Math.Sin(2 * Math.PI * 0.5 * t)) + (rand.NextDouble() - 0.5) * 2.0;

                // Channel 2: Beta rhythm (~20 Hz)
                sample[1] = Math.Sin(2 * Math.PI * 20.0 * t) * 8.0 + Math.Sin(2 * Math.PI * 22.0 * t) * 4.0 + (rand.NextDouble() - 0.5) * 1.5;

                // Channel 3: Theta rhythm (~6 Hz)
                sample[2] = Math.Sin(2 * Math.PI * 6.0 * t) * 22.0 + (rand.NextDouble() - 0.5) * 3.0;

                // Channel 4: Delta rhythm (~2 Hz) slow high-amplitude wave
                sample[3] = Math.Sin(2 * Math.PI * 2.0 * t) * 35.0 + Math.Cos(2 * Math.PI * 0.8 * t) * 10.0 + (rand.NextDouble() - 0.5) * 2.5;

                _outlet?.push_sample(sample);

                t += 0.01; // 100 Hz = 10 ms
                Thread.Sleep(10);
            }
        }

        public void Stop()
        {
            _running = false;
            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(1000);
                _thread = null;
            }

            try { _outlet?.Dispose(); } catch { }
            try { _info?.Dispose(); } catch { }
            _outlet = null;
            _info = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
