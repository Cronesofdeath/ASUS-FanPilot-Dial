using System;
using System.Collections.Generic;
using System.Linq;

namespace FanPilot
{
    // Holds only currently pressed virtual keys. No text or keyboard history is recorded.
    public sealed class KeyChord
    {
        readonly object gate = new object ();
        readonly HashSet<int> down = new HashSet<int>(), capture = new HashSet<int>();
        int[] binding = new int[]
        {
            17,
            18,
            70
        };
        bool enabled = true, capturing, latched, tooMany;
        public event Action Activated;
        public event Action<int[]> Captured;
        public event Action CaptureRejected;
        public static int Normalize(int key)
        {
            if (key == 160 || key == 161)
                return 16;
            if (key == 162 || key == 163)
                return 17;
            if (key == 164 || key == 165)
                return 18;
            if (key == 92)
                return 91;
            return key;
        }

        public static int[] Validate(int[] keys)
        {
            if (keys == null || keys.Length < 1 || keys.Length > 3)
                throw new ArgumentException("Hold one, two or three keys together.");
            int[] result = keys.Select(Normalize).Distinct().OrderBy(x => x).ToArray();
            if (result.Length != keys.Length || result.Any(x => x < 8 || x > 254 || x == 229 || x == 231))
                throw new ArgumentException("Choose one to three distinct keyboard keys.");
            return result;
        }

        public int[] Binding
        {
            get
            {
                lock (gate)
                    return (int[])binding.Clone();
            }

            set
            {
                int[] checkedKeys = Validate(value);
                lock (gate)
                {
                    binding = checkedKeys;
                    down.Clear();
                    latched = false;
                }
            }
        }

        public bool Enabled
        {
            get
            {
                lock (gate)
                    return enabled;
            }

            set
            {
                lock (gate)
                {
                    enabled = value;
                    down.Clear();
                    latched = false;
                }
            }
        }

        public bool Capturing
        {
            get
            {
                lock (gate)
                    return capturing;
            }
        }

        public void BeginCapture()
        {
            lock (gate)
            {
                capture.Clear();
                down.Clear();
                tooMany = false;
                latched = false;
                capturing = true;
            }
        }

        public void CancelCapture()
        {
            lock (gate)
            {
                capture.Clear();
                down.Clear();
                capturing = false;
                latched = false;
            }
        }

        public void Input(int virtualKey, bool pressed)
        {
            int key = Normalize(virtualKey);
            if (key < 8 || key > 254 || key == 229 || key == 231)
                return;
            bool fire = false, rejected = false;
            int[] recorded = null;
            lock (gate)
            {
                if (pressed)
                {
                    if (!down.Add(key))
                        return;
                }
                else
                    down.Remove(key);
                if (capturing)
                {
                    if (pressed)
                    {
                        capture.Add(key);
                        if (capture.Count > 3)
                            tooMany = true;
                    }

                    if (!pressed && down.Count == 0 && capture.Count > 0)
                    {
                        if (tooMany)
                            rejected = true;
                        else
                            recorded = capture.OrderBy(x => x).ToArray();
                        capturing = false;
                        capture.Clear();
                    }
                }
                else
                {
                    bool matches = enabled && down.Count == binding.Length && binding.All(down.Contains);
                    if (pressed && matches && !latched)
                        fire = true;
                    latched = matches;
                }
            }

            if (recorded != null && Captured != null)
                Captured(recorded);
            if (rejected && CaptureRejected != null)
                CaptureRejected();
            if (fire && Activated != null)
                Activated();
        }
    }
}
